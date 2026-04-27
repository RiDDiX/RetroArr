using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
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
