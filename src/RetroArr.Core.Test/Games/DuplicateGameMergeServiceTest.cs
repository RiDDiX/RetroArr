using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class DuplicateGameMergeServiceTest
    {
        private DbContextOptions<RetroArrDbContext> _dbOptions = null!;

        [SetUp]
        public void Setup()
        {
            _dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        [Test]
        public async Task MergeAsync_CueBinPair_KeepsCueRowDropsBinRow()
        {
            // Reproduce the user's case: SLES_***.bin and SLES_***.cue both
            // sit in /media/psx and end up as two rows. The cue carries the
            // IGDB id; merging should keep that one and drop the bin row.
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.AddRange(
                    new Game
                    {
                        Id = 1,
                        Title = "SLES 040 93 Beyblade",
                        PlatformId = 20,
                        Path = Path.Combine("/media", "psx", "SLES_040.93.Beyblade (EU).bin"),
                        IgdbId = null
                    },
                    new Game
                    {
                        Id = 2,
                        Title = "Beyblade",
                        PlatformId = 20,
                        Path = Path.Combine("/media", "psx", "SLES_040.93.Beyblade (EU).cue"),
                        IgdbId = 9999
                    }
                );
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);

                Assert.That(result.RowsMerged, Is.EqualTo(1));
                Assert.That(result.ClustersFound, Is.GreaterThan(0));
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var games = await ctx.Games.ToListAsync();
                Assert.That(games.Count, Is.EqualTo(1));
                Assert.That(games[0].Id, Is.EqualTo(2));
                Assert.That(games[0].Title, Is.EqualTo("Beyblade"));
            }
        }

        [Test]
        public async Task MergeAsync_GameFilesReattachedToWinner()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.AddRange(
                    new Game { Id = 10, Title = "A", PlatformId = 20, Path = "/x/Game.bin" },
                    new Game { Id = 11, Title = "A", PlatformId = 20, Path = "/x/Game.cue", IgdbId = 1 }
                );
                ctx.GameFiles.AddRange(
                    new GameFile { Id = 100, GameId = 10, RelativePath = "/x/Game.bin", Size = 1, FileType = "Main" },
                    new GameFile { Id = 101, GameId = 10, RelativePath = "/x/Game.lrg", Size = 2, FileType = "Main" }
                );
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                await DuplicateGameMergeService.MergeAsync(ctx);
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var files = await ctx.GameFiles.ToListAsync();
                Assert.That(files.Count, Is.EqualTo(2));
                Assert.That(files.All(f => f.GameId == 11), Is.True);
            }
        }

        // x takes over y (the same disc), then y and z are one title: z goes to the row that holds y by now.
        // SQLite, so a second review on one row would break its unique index.
        [TestCase(9, null, 1)]
        [TestCase(null, 5, 3)]
        public async Task MergeAsync_ChainOfClusters_EverythingEndsUpOnTheSurvivor(int? xIgdbId, int? zIgdbId, int survivor)
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options;
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Database.EnsureCreated();
                ctx.Platforms.Add(new Platform { Id = 20, Name = "PlayStation 1", Slug = "ps1", FolderName = "psx" });
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Alpha", PlatformId = 20, Path = "/psx/Game.cue", IgdbId = xIgdbId },
                    new Game { Id = 2, Title = "Beta", PlatformId = 20, Path = "/psx/Game.bin" },
                    new Game { Id = 3, Title = "Beta", PlatformId = 20, Path = "/other/Beta.iso", IgdbId = zIgdbId });
                ctx.GameFiles.AddRange(
                    new GameFile { Id = 100, GameId = 2, RelativePath = "Game.bin", Size = 1, FileType = "Main" },
                    new GameFile { Id = 101, GameId = 3, RelativePath = "Beta.iso", Size = 1, FileType = "Main" });
                ctx.GameReviews.AddRange(new GameReview { GameId = 2, Notes = "y" }, new GameReview { GameId = 3, Notes = "z" });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(2));
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(ctx.Games.Select(g => g.Id), Is.EqualTo(new[] { survivor }));
                Assert.That(ctx.GameFiles.AsEnumerable().Select(f => (f.Id, f.GameId)), Is.EquivalentTo(new[] { (100, survivor), (101, survivor) }));
                Assert.That(ctx.GameReviews.Select(r => r.GameId), Is.EqualTo(new[] { survivor }));
            }
        }

        // A database made before the unique IGDB index can hold both
        [Test]
        public async Task MergeAsync_TwoRegionsSharingAnIgdbId_BothStay()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Region = "USA", Path = "/gba/Advance Wars", IgdbId = 7 },
                    new Game { Id = 2, Title = "Advance Wars", PlatformId = 52, Region = "Europe", Path = "/gba/Advance Wars (Europe)", IgdbId = 7 });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(0));
                Assert.That(await ctx.Games.CountAsync(), Is.EqualTo(2));
            }
        }

        [Test]
        public async Task MergeAsync_NoDuplicates_NoChange()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Mario", PlatformId = 41, Path = "/snes/Mario.sfc" });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var result = await DuplicateGameMergeService.MergeAsync(ctx);
                Assert.That(result.RowsMerged, Is.EqualTo(0));
            }
        }
    }
}
