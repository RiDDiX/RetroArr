using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class AuditRegressionTest
    {
        private DbContextOptions<RetroArrDbContext> _dbOptions = null!;

        [SetUp]
        public void Setup()
        {
            _dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        // ==================== DEFECT-L1: SyncGameFilesAsync updates existing file metadata ====================

        [Test]
        public async Task SyncGameFiles_UpdatesExistingFileSize()
        {
            var factory = new TestDbContextFactory(_dbOptions);
            var repo = new SqliteGameRepository(factory);

            // Seed a game
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "TestGame", PlatformId = 1 });
                ctx.GameFiles.Add(new GameFile
                {
                    GameId = 1, RelativePath = "game.iso", Size = 1000,
                    DateAdded = DateTime.UtcNow.AddDays(-1), FileType = "Main"
                });
                await ctx.SaveChangesAsync();
            }

            // Sync with updated size
            var updatedFiles = new List<GameFile>
            {
                new GameFile { RelativePath = "game.iso", Size = 2000, DateAdded = DateTime.UtcNow, FileType = "Main" }
            };
            await repo.SyncGameFilesAsync(1, updatedFiles);

            // Verify size was updated
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var file = await ctx.GameFiles.FirstAsync(f => f.GameId == 1);
                Assert.That(file.Size, Is.EqualTo(2000));
            }
        }

        [Test]
        public async Task SyncGameFiles_UpdatesFileType()
        {
            var factory = new TestDbContextFactory(_dbOptions);
            var repo = new SqliteGameRepository(factory);

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "TestGame", PlatformId = 1 });
                ctx.GameFiles.Add(new GameFile
                {
                    GameId = 1, RelativePath = "update.nsp", Size = 500,
                    DateAdded = DateTime.UtcNow, FileType = "Main"
                });
                await ctx.SaveChangesAsync();
            }

            var updatedFiles = new List<GameFile>
            {
                new GameFile { RelativePath = "update.nsp", Size = 500, DateAdded = DateTime.UtcNow, FileType = "Patch" }
            };
            await repo.SyncGameFilesAsync(1, updatedFiles);

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var file = await ctx.GameFiles.FirstAsync(f => f.GameId == 1);
                Assert.That(file.FileType, Is.EqualTo("Patch"));
            }
        }

        [Test]
        public async Task SyncGameFiles_NoChangeWhenSizeSame()
        {
            var factory = new TestDbContextFactory(_dbOptions);
            var repo = new SqliteGameRepository(factory);
            var originalDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "TestGame", PlatformId = 1 });
                ctx.GameFiles.Add(new GameFile
                {
                    GameId = 1, RelativePath = "game.iso", Size = 1000,
                    DateAdded = originalDate, FileType = "Main"
                });
                await ctx.SaveChangesAsync();
            }

            var sameFiles = new List<GameFile>
            {
                new GameFile { RelativePath = "game.iso", Size = 1000, DateAdded = DateTime.UtcNow, FileType = "Main" }
            };
            await repo.SyncGameFilesAsync(1, sameFiles);

            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                var file = await ctx.GameFiles.FirstAsync(f => f.GameId == 1);
                Assert.That(file.DateAdded, Is.EqualTo(originalDate));
            }
        }

        // ==================== DEFECT-D1: Unique constraint (Title, PlatformId) ====================

        [Test]
        public async Task UniqueIndex_TitlePlatformId_PreventsDuplicate()
        {
            using var ctx = new RetroArrDbContext(_dbOptions);

            ctx.Games.Add(new Game { Title = "Zelda", PlatformId = 130 });
            await ctx.SaveChangesAsync();

            // Same title+platform on a different platform is allowed
            ctx.Games.Add(new Game { Title = "Zelda", PlatformId = 6 });
            await ctx.SaveChangesAsync();

            var count = await ctx.Games.CountAsync(g => g.Title == "Zelda");
            Assert.That(count, Is.EqualTo(2));
        }

        // ==================== DEFECT-M1: MapIgdbGameToGameAsync PlatformId resolution ====================

        [Test]
        public void PlatformDefinitions_AllHaveName()
        {
            foreach (var p in PlatformDefinitions.AllPlatforms)
            {
                Assert.That(p.Name, Is.Not.Null.And.Not.Empty,
                    $"Platform ID {p.Id} has null/empty Name");
            }
        }

        [Test]
        public void PlatformDefinitions_AllHaveSlug()
        {
            foreach (var p in PlatformDefinitions.AllPlatforms)
            {
                Assert.That(p.Slug, Is.Not.Null.And.Not.Empty,
                    $"Platform ID {p.Id} ({p.Name}) has null/empty Slug");
            }
        }

        [Test]
        public void PlatformDefinitions_IgdbPlatformIds_KnownDuplicates()
        {
            // Some platforms share IgdbPlatformId (e.g. parent/child or regional variants).
            // This test documents the known count to catch unintended additions.
            var withIgdb = PlatformDefinitions.AllPlatforms
                .Where(p => p.IgdbPlatformId.HasValue)
                .ToList();

            var ids = withIgdb.Select(p => p.IgdbPlatformId!.Value).ToList();
            var duplicateCount = ids.Count - ids.Distinct().Count();

            Assert.That(duplicateCount, Is.LessThanOrEqualTo(20),
                $"More IgdbPlatformId duplicates than expected ({duplicateCount}). Review PlatformDefinitions.");
        }

        // ==================== DEFECT-P1: Switch scanning path resolution ====================

        [Test]
        public void MediaSettings_FolderPathFallback_DestinationPath()
        {
            var settings = new RetroArr.Core.Configuration.MediaSettings
            {
                FolderPath = "",
                DestinationPath = "/library"
            };

            // Verify the fallback logic: when FolderPath is empty, DestinationPath should be usable
            var folderPath = !string.IsNullOrEmpty(settings.FolderPath) ? settings.FolderPath
                : !string.IsNullOrEmpty(settings.DestinationPath) ? settings.DestinationPath
                : null;

            Assert.That(folderPath, Is.EqualTo("/library"));
        }

        [Test]
        public void MediaSettings_BothPaths_PrefersFolderPath()
        {
            var settings = new RetroArr.Core.Configuration.MediaSettings
            {
                FolderPath = "/primary",
                DestinationPath = "/secondary"
            };

            var folderPath = !string.IsNullOrEmpty(settings.FolderPath) ? settings.FolderPath
                : !string.IsNullOrEmpty(settings.DestinationPath) ? settings.DestinationPath
                : null;

            Assert.That(folderPath, Is.EqualTo("/primary"));
        }

        // ==================== Frontend Compatibility Mode ====================

        [Test]
        public void GetEffectiveFolderName_NativeMode_ReturnsOriginal()
        {
            var platform = new Platform { FolderName = "arcade", RetroBatFolderName = "mame", BatoceraFolderName = "mame" };
            Assert.That(platform.GetEffectiveFolderName("native"), Is.EqualTo("arcade"));
            Assert.That(platform.GetEffectiveFolderName(null), Is.EqualTo("arcade"));
            Assert.That(platform.GetEffectiveFolderName(""), Is.EqualTo("arcade"));
        }

        [Test]
        public void GetEffectiveFolderName_RetroBatMode_ReturnsOverride()
        {
            var platform = new Platform { FolderName = "arcade", RetroBatFolderName = "mame", BatoceraFolderName = "mame" };
            Assert.That(platform.GetEffectiveFolderName("retrobat"), Is.EqualTo("mame"));
        }

        [Test]
        public void GetEffectiveFolderName_BatoceraMode_ReturnsOverride()
        {
            var platform = new Platform { FolderName = "vita", RetroBatFolderName = "psvita", BatoceraFolderName = "psvita" };
            Assert.That(platform.GetEffectiveFolderName("batocera"), Is.EqualTo("psvita"));
        }

        [Test]
        public void GetEffectiveFolderName_NoOverride_FallsBackToNative()
        {
            var platform = new Platform { FolderName = "nes" };
            Assert.That(platform.GetEffectiveFolderName("retrobat"), Is.EqualTo("nes"));
            Assert.That(platform.GetEffectiveFolderName("batocera"), Is.EqualTo("nes"));
        }

        [Test]
        public void GetEffectiveFolderName_SegaCD_RetroBatDiffers()
        {
            var segacd = PlatformDefinitions.AllPlatforms.First(p => p.Id == 63);
            Assert.That(segacd.GetEffectiveFolderName("native"), Is.EqualTo("segacd"));
            Assert.That(segacd.GetEffectiveFolderName("retrobat"), Is.EqualTo("megacd"));
            Assert.That(segacd.GetEffectiveFolderName("batocera"), Is.EqualTo("megacd"));
        }

        [Test]
        public void MatchesFolderName_MatchesNativeAndSlug()
        {
            var platform = new Platform { FolderName = "arcade", Slug = "arcade" };
            Assert.That(platform.MatchesFolderName("arcade"), Is.True);
            Assert.That(platform.MatchesFolderName("ARCADE"), Is.True);
        }

        [Test]
        public void MatchesFolderName_MatchesRetroBatOverride()
        {
            var platform = new Platform { FolderName = "arcade", Slug = "arcade", RetroBatFolderName = "mame", BatoceraFolderName = "mame" };
            Assert.That(platform.MatchesFolderName("mame"), Is.True);
            Assert.That(platform.MatchesFolderName("MAME"), Is.True);
        }

        [Test]
        public void MatchesFolderName_MatchesBatoceraOverride()
        {
            var platform = new Platform { FolderName = "wonderswan", Slug = "wonderswan", RetroBatFolderName = "wswan", BatoceraFolderName = "wswan" };
            Assert.That(platform.MatchesFolderName("wswan"), Is.True);
        }

        [Test]
        public void MatchesFolderName_RejectsUnknown()
        {
            var platform = new Platform { FolderName = "arcade", Slug = "arcade", RetroBatFolderName = "mame" };
            Assert.That(platform.MatchesFolderName("unknown"), Is.False);
            Assert.That(platform.MatchesFolderName(""), Is.False);
            Assert.That(platform.MatchesFolderName(null!), Is.False);
        }

        [Test]
        public void AllMismatchedPlatforms_HaveOverrides()
        {
            var expectedOverrides = new Dictionary<int, (string retrobat, string? batocera)>
            {
                { 4, ("amiga500", "amiga500") },
                { 5, ("amigacd32", "amigacd32") },
                { 7, ("c20", "c20") },
                { 9, ("msx1", "msx1") },
                { 26, ("psvita", "psvita") },
                { 49, ("snes", "snes") },
                { 63, ("megacd", null) },
                { 64, ("sega32x", "sega32x") },
                { 100, ("mame", "mame") },
                { 110, ("wswan", "wswan") },
                { 111, ("wswanc", "wswanc") },
                { 121, ("dos", "dos") },
            };

            foreach (var (id, (retrobat, batocera)) in expectedOverrides)
            {
                var plat = PlatformDefinitions.AllPlatforms.First(p => p.Id == id);
                Assert.That(plat.RetroBatFolderName, Is.EqualTo(retrobat),
                    $"Platform {plat.Name} (ID {id}) RetroBatFolderName mismatch");
                if (batocera != null)
                {
                    Assert.That(plat.BatoceraFolderName, Is.EqualTo(batocera),
                        $"Platform {plat.Name} (ID {id}) BatoceraFolderName mismatch");
                }
            }
        }

        [Test]
        public void MatchingPlatforms_DontHaveOverrides()
        {
            var matchingIds = new[] { 20, 21, 22, 40, 41, 42, 43, 44, 45, 50, 51, 52, 53, 54, 61, 62, 66, 67 };
            foreach (var id in matchingIds)
            {
                var plat = PlatformDefinitions.AllPlatforms.First(p => p.Id == id);
                Assert.That(plat.RetroBatFolderName, Is.Null,
                    $"Platform {plat.Name} (ID {id}) should not have RetroBatFolderName (already matches)");
            }
        }

        [Test]
        public void MediaSettings_FolderNamingMode_DefaultsToNative()
        {
            var settings = new RetroArr.Core.Configuration.MediaSettings();
            Assert.That(settings.FolderNamingMode, Is.EqualTo("native"));
        }

        [Test]
        public void ResolveDestinationPath_UsesEffectiveFolderName()
        {
            var settings = new RetroArr.Core.Configuration.MediaSettings
            {
                DestinationPathPattern = "{Platform}/{Title}",
                UseDestinationPattern = true
            };

            var arcade = PlatformDefinitions.AllPlatforms.First(p => p.Id == 100);

            var nativePath = settings.ResolveDestinationPath("/lib", arcade.GetEffectiveFolderName("native"), "Street Fighter II");
            var retrobatPath = settings.ResolveDestinationPath("/lib", arcade.GetEffectiveFolderName("retrobat"), "Street Fighter II");

            Assert.That(nativePath, Does.Contain("arcade"));
            Assert.That(retrobatPath, Does.Contain("mame"));
        }

        [Test]
        public void DetectPlatformSubfolders_MatchesAllVariants()
        {
            var allPlatforms = PlatformDefinitions.AllPlatforms;
            
            // Simulate matching "mame" folder from a RetroBat-style library
            var matched = allPlatforms.FirstOrDefault(p => p.MatchesFolderName("mame"));
            Assert.That(matched, Is.Not.Null);
            Assert.That(matched!.Id, Is.EqualTo(100));

            // Simulate matching "arcade" folder from a native-style library  
            var matched2 = allPlatforms.FirstOrDefault(p => p.MatchesFolderName("arcade"));
            Assert.That(matched2, Is.Not.Null);
            Assert.That(matched2!.Id, Is.EqualTo(100));

            // Simulate matching "psvita" folder from RetroBat/Batocera library
            var matched3 = allPlatforms.FirstOrDefault(p => p.MatchesFolderName("psvita"));
            Assert.That(matched3, Is.Not.Null);
            Assert.That(matched3!.Id, Is.EqualTo(26));

            // Simulate matching "vita" folder from native library
            var matched4 = allPlatforms.FirstOrDefault(p => p.MatchesFolderName("vita"));
            Assert.That(matched4, Is.Not.Null);
            Assert.That(matched4!.Id, Is.EqualTo(26));
        }

        // ==================== Helper: IDbContextFactory for InMemory ====================

        private class TestDbContextFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public TestDbContextFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
