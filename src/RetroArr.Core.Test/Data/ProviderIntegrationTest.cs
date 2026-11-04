using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.Test.Games;

namespace RetroArr.Core.Test.Data
{
    // Runs against a real server only when CI (or you) hands over a connection string,
    // e.g. RETROARR_TEST_PG="Host=127.0.0.1;Database=retroarr_test;Username=postgres;Password=postgres".
    // The database in it is dropped and created again.
    [TestFixture]
    public class ProviderIntegrationTest
    {
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task FreshDatabase_GetsSchemaAndStoresData(DatabaseType type, string variable)
        {
            var connectionString = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrEmpty(connectionString)) Assert.Ignore($"{variable} is not set");

            var builder = new DbContextOptionsBuilder<RetroArrDbContext>();
            if (type == DatabaseType.PostgreSQL) builder.UseNpgsql(connectionString);
            else builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            var options = builder.Options;

            using (var ctx = new RetroArrDbContext(options))
                ctx.Database.EnsureDeleted();

            // Twice, like two app starts: the second run has to find everything in place
            var logs = await MissingContentTest.Logs(() =>
            {
                for (var start = 0; start < 2; start++)
                {
                    using var ctx = new RetroArrDbContext(options);
                    DatabaseMigrator.EnsureSchema(ctx);
                    DatabaseMigrator.ApplyMigrations(ctx, type);
                }
                return Task.CompletedTask;
            });
            Assert.That(logs.Count(l => l.Contains("schema migrations applied")), Is.EqualTo(2));
            Assert.That(logs.Where(l => l.StartsWith("Error|") || l.StartsWith("Warn|") || l.Contains(" skipped: ")), Is.Empty);

            var local = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Local);
            var unspecified = new DateTime(1987, 2, 20, 0, 0, 0, DateTimeKind.Unspecified);
            int gameId;
            using (var ctx = new RetroArrDbContext(options))
            {
                var game = new Game { Title = "Contra", PlatformId = 40, Region = "", Added = local, ReleaseDate = unspecified, Genres = { "Shooter" }, Overview = " ",
                    NeedsMetadataReview = true, MetadataConfirmedByUser = true };
                ctx.Games.Add(game);
                // byte-wise like SQLite, not a case-insensitive collision
                ctx.Games.Add(new Game { Title = "CONTRA", PlatformId = 40, Region = "", Added = local, Overview = "Run and gun",
                    NeedsMetadataReview = true, Images = { CoverUrl = "/cover.jpg" } });
                ctx.Tags.Add(new Tag { Name = "Favs", GameTags = { new GameTag { Game = game } } });
                ctx.Collections.Add(new Collection { Name = "Run and gun", Color = "var(--ctp-sapphire)", CollectionGames = { new CollectionGame { Game = game } } });
                await ctx.SaveChangesAsync();
                gameId = game.Id;
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var game = ctx.Games.Single(g => g.Id == gameId);
                Assert.That(game.Added, Is.EqualTo(local.ToUniversalTime()));
                Assert.That(game.Added.Kind, Is.EqualTo(DateTimeKind.Utc));
                Assert.That(game.ReleaseDate, Is.EqualTo(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)));
                Assert.That(game.Genres, Is.EqualTo(new[] { "Shooter" }));
                Assert.That(ctx.Games.Count(g => g.Added >= local), Is.EqualTo(2));
                Assert.That(ctx.Tags.Include(t => t.GameTags).Single().GameTags.Single().GameId, Is.EqualTo(gameId));
                var collection = ctx.Collections.Include(c => c.CollectionGames).Single();
                Assert.That(collection.Color, Is.EqualTo("var(--ctp-sapphire)"));
                Assert.That(collection.CollectionGames.Single().GameId, Is.EqualTo(gameId));
            }

            var repository = new SqliteGameRepository(new PooledDbContextFactory<RetroArrDbContext>(options));
            // The review queue reads the cover from the owned Images columns
            Assert.That((await repository.GetReviewRowsAsync()).Select(r => (r.Title, r.CoverUrl)), Is.EqualTo(new[] { ("CONTRA", "/cover.jpg") }));
            var rows = await repository.GetProblemRowsAsync();
            // MariaDB pads on compare (" " = ''), so a blank description must still count as one
            Assert.That(rows.Where(r => !r.NoMetadata).Select(r => r.Title), Is.EquivalentTo(new[] { "Contra", "CONTRA" }));
            var other = await repository.AddAsync(new Game { Title = "Contra Force", PlatformId = 40 });
            other.Title = "Contra";
            var ex = Assert.ThrowsAsync<DuplicateGameException>(() => repository.UpdateAsync(other.Id, other));
            Assert.That(ex!.ConflictField, Is.EqualTo("Title+PlatformId"));

            await repository.UpdateFieldsAsync(gameId, g => g.ProblemResolvedAt = DateTime.UtcNow);
            Assert.That((await repository.GetProblemRowsAsync()).Single(r => r.Id == gameId).ProblemResolvedAt, Is.Not.Null);
            // A blank description is one for the metadata rescan's pick too; the download monitor reads only the missing games it asks for
            await repository.UpdateFieldsAsync(gameId, g => { g.IgdbId = 1; g.MissingSince = DateTime.UtcNow; });
            Assert.That((await repository.GetMetadataRowsAsync(40)).Select(r => (r.Title, r.Incomplete)),
                Is.EqualTo(new[] { ("Contra", false), ("CONTRA", true), ("Contra Force", true) }));
            Assert.That((await repository.GetMissingAsync(new[] { gameId, other.Id })).Select(g => g.Id), Is.EqualTo(new[] { gameId }));
            // The tag and collection rows go with the game
            Assert.That(await repository.DeleteManyAsync(new[] { gameId, other.Id, -1 }), Is.EquivalentTo(new[] { gameId, other.Id }));
            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(ctx.Games.Select(g => g.Title), Is.EqualTo(new[] { "CONTRA" }));
                Assert.That(ctx.GameTags.Count() + ctx.CollectionGames.Count(), Is.Zero);
            }

            // Counted and cleared by the database
            Assert.That(await repository.CountByPlatformAsync(), Is.EquivalentTo(new Dictionary<int, int> { [40] = 1 }));
            await repository.DeleteAllAsync();
            using (var ctx = new RetroArrDbContext(options))
                Assert.That(ctx.Games.Count(), Is.Zero);

            // Discarding a selection of the whole pool is one statement, as large as the pool
            var discovered = new DiscoveredGameRepository(new PooledDbContextFactory<RetroArrDbContext>(options));
            await discovered.AddAsync(new DiscoveredGame { Title = "Contra", Path = "/roms/Contra.nes" });
            await discovered.AddAsync(new DiscoveredGame { Title = "Gradius", Path = "/roms/Gradius.nes" });
            var pool = (await discovered.GetAllAsync()).Select(d => d.Id);
            Assert.That(await discovered.DeleteManyAsync(pool.Concat(Enumerable.Range(1_000_000, 100_000))), Is.EqualTo(2));
            Assert.That(await discovered.CountAsync(), Is.Zero);
        }
    }
}
