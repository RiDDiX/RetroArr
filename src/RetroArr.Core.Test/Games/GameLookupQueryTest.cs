using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // The lookups the Status page and the queue use, on SQLite and, when a connection string is set
    // (see ProviderIntegrationTest), on PostgreSQL and MariaDB
    [TestFixture]
    public class GameLookupQueryTest
    {
        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task Lookups_TranslateAndMatch(DatabaseType type, string? variable)
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            var builder = new DbContextOptionsBuilder<RetroArrDbContext>();
            if (type == DatabaseType.SQLite)
            {
                connection.Open();
                builder.UseSqlite(connection);
            }
            else
            {
                var connectionString = Environment.GetEnvironmentVariable(variable!);
                if (string.IsNullOrEmpty(connectionString)) Assert.Ignore($"{variable} is not set");
                if (type == DatabaseType.PostgreSQL) builder.UseNpgsql(connectionString);
                else builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString));
            }
            var options = builder.Options;
            using (var ctx = new RetroArrDbContext(options))
            {
                if (type == DatabaseType.SQLite)
                {
                    ctx.Database.EnsureCreated();
                    ctx.Platforms.AddRange(
                        new Platform { Id = 41, Name = "Super Nintendo", Slug = "snes", FolderName = "snes" },
                        new Platform { Id = 52, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
                }
                else
                {
                    ctx.Database.EnsureDeleted();
                    DatabaseMigrator.EnsureSchema(ctx);
                    DatabaseMigrator.ApplyMigrations(ctx, type);
                }
                ctx.Games.AddRange(
                    new Game { Title = "Ökoland", PlatformId = 41 },
                    new Game { Title = "Super Mario World", PlatformId = 41 },
                    new Game { Title = "Super Metroid", PlatformId = 41 },
                    new Game { Title = "Super Mario Advance", PlatformId = 52 });
                await ctx.SaveChangesAsync();
            }
            var repository = new SqliteGameRepository(new PooledDbContextFactory<RetroArrDbContext>(options));
            var all = (await repository.GetAllPagedAsync(1, 50)).Items.ToDictionary(g => g.Title, g => g.Id);

            // Typed as the library has it, a title with a non-ASCII capital is found: SQLite's lower() leaves "Ö" as it is
            Assert.That((await repository.GetAllPagedAsync(1, 50, 41, "Ökoland")).Items.Select(g => g.Title), Is.EqualTo(new[] { "Ökoland" }));
            Assert.That((await repository.GetAllPagedAsync(1, 50, 41, "super MARIO")).Items.Select(g => g.Title), Is.EqualTo(new[] { "Super Mario World" }));

            var rows = await repository.GetListItemsAsync(new[] { all["Super Metroid"], all["Ökoland"], all["Super Metroid"], -1 });
            Assert.That(rows.OrderBy(r => r.Id).Select(r => $"{r.Title} {r.PlatformId}"), Is.EqualTo(new[] { "Ökoland 41", "Super Metroid 41" }));
            Assert.That(await repository.GetListItemsAsync(Array.Empty<int>()), Is.Empty);
        }
    }
}
