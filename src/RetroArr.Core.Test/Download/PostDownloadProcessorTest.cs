using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Games;
using RetroArr.Core.IO;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class PostDownloadProcessorTest
    {
        // ── Import with the release detector's "unknown" platform ─────

        [Test]
        public async Task GameTargetedImport_WithUnknownPlatformHint_UsesTheGamesPlatform()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdunknown_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "config"));
            try
            {
                var config = new ConfigurationService(root);
                var library = Directory.CreateDirectory(Path.Combine(root, "library", "nds", "Pokemon Platinum")).FullName;
                config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(root, "library") });
                config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true });

                var source = Directory.CreateDirectory(Path.Combine(root, "downloads", "Pokemon.Platinum")).FullName;
                File.WriteAllText(Path.Combine(source, "Pokemon Platinum.nds"), "rom");

                var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
                using (var ctx = new RetroArrDbContext(dbOptions))
                {
                    ctx.Games.Add(new Game { Id = 1, Title = "Pokemon Platinum", PlatformId = 53, Path = library });
                    await ctx.SaveChangesAsync();
                }

                var processor = new PostDownloadProcessor(config, new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());
                await processor.ProcessCompletedDownloadAsync(new DownloadStatus
                {
                    Id = "x", Name = "Pokemon Platinum", DownloadPath = source, GameId = 1,
                    PlatformFolder = "unknown", State = DownloadState.Completed
                });

                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Count(), Is.EqualTo(1), "a second game was created on the Unknown platform");
                Assert.That(Directory.GetFiles(library), Has.Length.EqualTo(1));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // ── Import never replaces a file that's already there ─────────

        [Test]
        public async Task GameTargetedImport_OtherRegionOfSameGame_KeepsBothFiles()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdclash_" + Path.GetRandomFileName());
            try
            {
                var (processor, dbOptions, library) = await SetUpGameAsync(root, "Advance Wars", 52);

                var usa = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");
                var eur = await ImportFileAsync(processor, root, "Advance Wars (Europe).gba", "eur");
                Assert.That(usa.Success, Is.True, usa.Reason);
                Assert.That(eur.Success, Is.True, eur.Reason);
                Assert.That(File.ReadAllText(Path.Combine(library, "Advance Wars.gba")), Is.EqualTo("usa"));
                Assert.That(File.ReadAllText(Path.Combine(library, "Advance Wars (Europe).gba")), Is.EqualTo("eur"));

                var again = await ImportFileAsync(processor, root, "Advance Wars (Europe).gba", "eur rev 1");
                Assert.That(again.Success, Is.False);
                Assert.That(again.Reason, Does.Contain("not overwriting"));
                Assert.That(File.ReadAllText(Path.Combine(library, "Advance Wars.gba")), Is.EqualTo("usa"));
                Assert.That(File.ReadAllText(Path.Combine(library, "Advance Wars (Europe).gba")), Is.EqualTo("eur"));
                Assert.That(File.Exists(Path.Combine(root, "downloads", "Advance Wars (Europe).gba")), Is.True, "source was removed");
                Assert.That(Directory.GetFiles(library), Has.Length.EqualTo(2));

                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single().ExecutablePath, Is.EqualTo(Path.Combine(library, "Advance Wars.gba")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task GameTargetedImport_SameFileAgain_LeavesTheLibraryFileAlone()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdsame_" + Path.GetRandomFileName());
            try
            {
                var (processor, _, library) = await SetUpGameAsync(root, "Advance Wars", 52);

                await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");
                var libraryFile = Path.Combine(library, "Advance Wars.gba");
                var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
                File.SetLastWriteTimeUtc(libraryFile, stamp);
                var again = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");

                Assert.That(again.Success, Is.True, again.Reason);
                Assert.That(Directory.GetFiles(library).Select(Path.GetFileName), Is.EqualTo(new[] { "Advance Wars.gba" }));
                Assert.That(File.GetLastWriteTimeUtc(libraryFile), Is.EqualTo(stamp), "library file was replaced");
                Assert.That(File.Exists(Path.Combine(root, "downloads", "Advance Wars (USA).gba")), Is.False, "source was kept");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task GameTargetedImport_SourceAlreadyAtTarget_IsLeftAlone(bool asFolder)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdself_" + Path.GetRandomFileName());
            try
            {
                var (processor, dbOptions, library) = await SetUpGameAsync(root, "Advance Wars", 52);
                var file = Path.Combine(library, "Advance Wars.gba");
                File.WriteAllText(file, "usa");

                var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
                {
                    Id = "x", Name = "Advance Wars", DownloadPath = asFolder ? library : file, GameId = 1, State = DownloadState.Completed
                });

                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(file), Is.EqualTo("usa"));
                Assert.That(Directory.GetFiles(library), Has.Length.EqualTo(1));
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single().ExecutablePath, Is.EqualTo(file));
                Assert.That(check.Games.Single().Status, Is.EqualTo(GameStatus.Downloaded));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task GameTargetedImport_KeepsTheDiscToken()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pddisc_" + Path.GetRandomFileName());
            try
            {
                var (processor, dbOptions, library) = await SetUpGameAsync(root, "Final Fantasy IX", 20);

                var result = await ImportFolderAsync(processor, root, "Final Fantasy IX (USA) (Disc 2)", "Final Fantasy IX (USA) (Disc 2).chd", "disc2");

                var expected = Path.Combine(library, "Final Fantasy IX (Disc 2).chd");
                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(expected), Is.EqualTo("disc2"));
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single().ExecutablePath, Is.EqualTo(expected));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase("Final Fantasy IX (USA) (Disc 2).chd", "Final Fantasy IX (Europe) (Disc 2).chd")]
        [TestCase("ffix.chd", "ffix.chd")]
        public async Task GameTargetedImport_FolderOfOtherRegion_KeepsBothFiles(string usaName, string eurName)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdfolder_" + Path.GetRandomFileName());
            try
            {
                var (processor, _, library) = await SetUpGameAsync(root, "Final Fantasy IX", 20);
                var usaFile = Path.Combine(library, "Final Fantasy IX (Disc 2).chd");
                var eurFile = Path.Combine(library, "Final Fantasy IX (Europe) (Disc 2).chd");

                var usa = await ImportFolderAsync(processor, root, "Final Fantasy IX (USA) (Disc 2)", usaName, "usa");
                var eur = await ImportFolderAsync(processor, root, "Final Fantasy IX (Europe) (Disc 2)", eurName, "eur");
                Assert.That(usa.Success, Is.True, usa.Reason);
                Assert.That(eur.Success, Is.True, eur.Reason);
                Assert.That(File.ReadAllText(usaFile), Is.EqualTo("usa"));
                Assert.That(File.ReadAllText(eurFile), Is.EqualTo("eur"));

                var again = await ImportFolderAsync(processor, root, "Final Fantasy IX (Europe) (Disc 2)", eurName, "eur rev 1");
                Assert.That(again.Success, Is.False);
                Assert.That(again.Reason, Does.Contain("not overwriting"));
                Assert.That(File.ReadAllText(usaFile), Is.EqualTo("usa"));
                Assert.That(File.ReadAllText(eurFile), Is.EqualTo("eur"));
                Assert.That(File.Exists(Path.Combine(root, "downloads", "Final Fantasy IX (Europe) (Disc 2)", eurName)), Is.True, "source was removed");
                Assert.That(Directory.GetFiles(library), Has.Length.EqualTo(2));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        [Platform(Exclude = "Win")]
        public async Task GameTargetedImport_SymlinkToTheSource_IsReplacedByTheFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdlink_" + Path.GetRandomFileName());
            try
            {
                var (processor, _, library) = await SetUpGameAsync(root, "Advance Wars", 52);
                var link = Path.Combine(library, "Advance Wars.gba");
                File.CreateSymbolicLink(link, Path.Combine(root, "downloads", "Advance Wars (USA).gba"));

                var result = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");

                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(new FileInfo(link).LinkTarget, Is.Null);
                Assert.That(File.ReadAllText(link), Is.EqualTo("usa"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // ── Only a main game import marks the game downloaded ─────────

        [TestCase("Advance Wars (USA).gba", GameStatus.Released, GameStatus.Downloaded)]
        [TestCase("Advance Wars Update v1.1.gba", GameStatus.Released, GameStatus.Released)]
        [TestCase("Advance Wars (USA).gba", GameStatus.InstallerDetected, GameStatus.InstallerDetected)]
        public async Task GameTargetedImport_MainGameMarksTheGameDownloaded(string fileName, GameStatus before, GameStatus expected)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdstatus_" + Path.GetRandomFileName());
            try
            {
                var (processor, dbOptions, _) = await SetUpGameAsync(root, "Advance Wars", 52);
                using (var ctx = new RetroArrDbContext(dbOptions))
                {
                    ctx.Games.Single().Status = before;
                    await ctx.SaveChangesAsync();
                }

                var result = await ImportFileAsync(processor, root, fileName, "rom");

                Assert.That(result.Success, Is.True, result.Reason);
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single().Status, Is.EqualTo(expected));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [Test]
        public async Task GameTargetedImport_StatusThatCannotBeSaved_StillImports()
        {
            // The repository refuses to save a game on the unknown platform, so only the status write fails
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdstatusfail_" + Path.GetRandomFileName());
            try
            {
                var (processor, dbOptions, library) = await SetUpGameAsync(root, "Advance Wars", 52);
                using (var ctx = new RetroArrDbContext(dbOptions))
                {
                    var game = ctx.Games.Single();
                    game.PlatformId = 0;
                    game.ExecutablePath = Path.Combine(library, "Advance Wars.gba");
                    await ctx.SaveChangesAsync();
                }

                var result = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");

                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(Path.Combine(library, "Advance Wars.gba")), Is.EqualTo("usa"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private static async Task<(PostDownloadProcessor Processor, DbContextOptions<RetroArrDbContext> DbOptions, string Library)> SetUpGameAsync(string root, string title, int platformId)
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            var config = new ConfigurationService(root);
            var platform = PlatformDefinitions.AllPlatforms.Single(p => p.Id == platformId);
            var library = Directory.CreateDirectory(Path.Combine(root, "library", platform.FolderName, title)).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(root, "library") });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true });

            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = title, PlatformId = platformId, Path = library });
                await ctx.SaveChangesAsync();
            }

            var processor = new PostDownloadProcessor(config, new FileMoverService(),
                new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());
            return (processor, dbOptions, library);
        }

        private static Task<PostDownloadResult> ImportFileAsync(PostDownloadProcessor processor, string root, string fileName, string content)
        {
            var file = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName, fileName);
            File.WriteAllText(file, content);
            return processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = fileName, Name = Path.GetFileNameWithoutExtension(fileName), DownloadPath = file, GameId = 1, State = DownloadState.Completed
            });
        }

        private static Task<PostDownloadResult> ImportFolderAsync(PostDownloadProcessor processor, string root, string release, string fileName, string content)
        {
            var folder = Directory.CreateDirectory(Path.Combine(root, "downloads", release)).FullName;
            File.WriteAllText(Path.Combine(folder, fileName), content);
            return processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = release, Name = release, DownloadPath = folder, GameId = 1, State = DownloadState.Completed
            });
        }

        // ── DetectContentType: Patch with version ─────────────────────

        [TestCase("Street Fighter x Tekken Update v1.02 PS3", "1.02")]
        [TestCase("Game.Title.Patch.1.05.PS3", "1.05")]
        [TestCase("MyGame Hotfix 2.01", "2.01")]
        [TestCase("SomeGame Update v3.0.1", "3.0.1")]
        [TestCase("GameTitle Fix 1.0.2", "1.0.2")]
        public void DetectContentType_Patch_WithVersion(string input, string expectedVersion)
        {
            var (type, version, _) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.Patch));
            Assert.That(version, Is.EqualTo(expectedVersion));
        }

        [TestCase("GameTitle Update PS3")]
        [TestCase("SomeGame Patch")]
        [TestCase("MyGame Hotfix")]
        public void DetectContentType_Patch_WithoutVersion(string input)
        {
            var (type, version, _) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.Patch));
            Assert.That(version, Is.Null);
        }

        // ── DetectContentType: DLC ────────────────────────────────────

        [TestCase("GameTitle DLC Season Pass", "Season Pass")]
        [TestCase("SomeGame DLC Map Pack", "Map Pack")]
        [TestCase("MyGame Expansion The Frozen North", "The Frozen North")]
        [TestCase("Game Add-on Extra Content", "Extra Content")]
        public void DetectContentType_DLC(string input, string expectedName)
        {
            var (type, _, dlcName) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.DLC));
            Assert.That(dlcName, Is.EqualTo(expectedName));
        }

        // ── DetectContentType: MainGame ───────────────────────────────

        [TestCase("Street Fighter x Tekken PS3")]
        [TestCase("Gran Turismo 7")]
        [TestCase("The Last of Us Part II")]
        [TestCase("")]
        public void DetectContentType_MainGame(string input)
        {
            var (type, version, dlcName) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.MainGame));
            Assert.That(version, Is.Null);
            Assert.That(dlcName, Is.Null);
        }

        // ── BuildPatchFileName ────────────────────────────────────────

        [TestCase("Street Fighter x Tekken", "1.02", ".pkg", "Street Fighter x Tekken-Patch-v1.02.pkg")]
        [TestCase("Gran Turismo 7", "3.0.1", ".pkg", "Gran Turismo 7-Patch-v3.0.1.pkg")]
        [TestCase("MyGame", null, ".iso", "MyGame-Patch.iso")]
        public void BuildPatchFileName_Correct(string gameTitle, string? version, string ext, string expected)
        {
            var result = PostDownloadProcessor.BuildPatchFileName(gameTitle, version, ext);
            Assert.That(result, Is.EqualTo(expected));
        }

        // ── BuildDlcFileName ──────────────────────────────────────────

        [TestCase("Street Fighter x Tekken", "Season Pass", ".pkg", "Street Fighter x Tekken-DLC-Season Pass.pkg")]
        [TestCase("MyGame", "Map Pack", ".iso", "MyGame-DLC-Map Pack.iso")]
        [TestCase("MyGame", null, ".pkg", "MyGame-DLC.pkg")]
        public void BuildDlcFileName_Correct(string gameTitle, string? dlcName, string ext, string expected)
        {
            var result = PostDownloadProcessor.BuildDlcFileName(gameTitle, dlcName, ext);
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
