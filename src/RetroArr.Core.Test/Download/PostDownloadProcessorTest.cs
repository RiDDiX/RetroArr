using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Games;
using RetroArr.Core.IO;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;
using RetroArr.Core.Test.Games;

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
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdstatusfail_" + Path.GetRandomFileName());
            try
            {
                var (_, dbOptions, library) = await SetUpGameAsync(root, "Advance Wars", 52);
                // Only the status write fails
                var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
                var hook = (MissingContentTest.HookedRepository)(object)repo;
                hook.Inner = new SqliteGameRepository(new DbFactory(dbOptions));
                hook.Before = (method, _) =>
                {
                    if (method.Name == nameof(IGameRepository.ApplyContentStateAsync)) throw new InvalidOperationException("database is locked");
                };
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(), repo, null!, new ArchiveService(), new TitleCleanerService());

                var result = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");

                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(Path.Combine(library, "Advance Wars.gba")), Is.EqualTo("usa"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // The game's paths are saved before the source goes: a failed save leaves the source for a retry
        [Test]
        public async Task GameTargetedImport_PathThatCannotBeSaved_KeepsTheSourceForARetry()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdpathfail_" + Path.GetRandomFileName());
            try
            {
                var (_, dbOptions, library) = await SetUpGameAsync(root, "Advance Wars", 52);
                var locked = true;
                var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
                var hook = (MissingContentTest.HookedRepository)(object)repo;
                hook.Inner = new SqliteGameRepository(new DbFactory(dbOptions));
                hook.Before = (method, _) =>
                {
                    if (locked && method.Name == nameof(IGameRepository.UpdateFieldsAsync)) throw new InvalidOperationException("database is locked");
                };
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(), repo, null!, new ArchiveService(), new TitleCleanerService());
                var source = Path.Combine(root, "downloads", "Advance Wars (USA).gba");

                var failed = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");

                Assert.That(failed.Success, Is.False);
                Assert.That(failed.Reason, Does.Contain("database is locked"));
                Assert.That(File.Exists(source), Is.True, "usenet source was removed");

                locked = false;
                var retry = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "usa");

                Assert.That(retry.Success, Is.True, retry.Reason);
                Assert.That(File.Exists(source), Is.False, "usenet source was kept");
                Assert.That(Directory.GetFiles(library).Select(Path.GetFileName), Is.EqualTo(new[] { "Advance Wars.gba" }));
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single().ExecutablePath, Is.EqualTo(Path.Combine(library, "Advance Wars.gba")));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Two region rows of one title would both go to {platform}/{Title}, the second one gets a folder of its own
        [Test]
        public async Task GameTargetedImport_SecondRegionOfATitle_GetsAFolderOfItsOwn()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdregion_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var (processor, dbOptions, folder) = SetUpRegionRows(root, connection, "USA", "Europe");

                var result = await ImportFileAsync(processor, root, "Advance Wars (Europe).gba", "eur", gameId: 2);

                var europe = Path.Combine(root, "library", "gba", "Advance Wars (Europe)");
                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(Path.Combine(europe, "Advance Wars.gba")), Is.EqualTo("eur"));
                Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Advance Wars.gba" }));
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single(g => g.Id == 2).Path, Is.EqualTo(europe));
                Assert.That(check.Games.Single(g => g.Id == 1).Path, Is.EqualTo(folder));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Without a region to tell the rows apart the files go into the other row's folder, and the game's path is its file there
        [Test]
        public async Task GameTargetedImport_FolderOfAnotherEntry_PathIsTheGamesFile()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdtaken_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var (processor, dbOptions, folder) = SetUpRegionRows(root, connection, null, null);

                var result = await ImportFileAsync(processor, root, "Advance Wars (Europe).gba", "eur", gameId: 2);

                var file = Path.Combine(folder, "Advance Wars (Europe).gba");
                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(file), Is.EqualTo("eur"));
                using var check = new RetroArrDbContext(dbOptions);
                var second = check.Games.Single(g => g.Id == 2);
                Assert.That(second.Path, Is.EqualTo(file));
                Assert.That(second.ExecutablePath, Is.EqualTo(file));
                Assert.That(check.Games.Single(g => g.Id == 1).Path, Is.EqualTo(folder));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Another entry gets the folder while the files land: they go again, and no retry is promised, it would end the same way
        [Test]
        public async Task GameTargetedImport_FolderTakenMeanwhile_UndoesTheImport()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdrace_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var (_, dbOptions, folder) = SetUpRegionRows(root, connection, null, null);
                using (var ctx = new RetroArrDbContext(dbOptions))
                {
                    ctx.Games.Single(g => g.Id == 1).Path = null;
                    ctx.SaveChanges();
                }
                var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
                var hook = (MissingContentTest.HookedRepository)(object)repo;
                hook.Inner = new SqliteGameRepository(new DbFactory(dbOptions));
                hook.Before = (method, _) =>
                {
                    if (method.Name != nameof(IGameRepository.UpdateFieldsAsync)) return;
                    using var ctx = new RetroArrDbContext(dbOptions);
                    ctx.Games.Single(g => g.Id == 1).Path = folder;
                    ctx.SaveChanges();
                };
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(), repo, null!, new ArchiveService(), new TitleCleanerService());

                var result = await ImportFileAsync(processor, root, "Advance Wars (Europe).gba", "eur", gameId: 2);

                Assert.That(result.Success, Is.False);
                Assert.That(result.Reason, Does.Contain("another library entry").And.Not.Contain("retry"));
                Assert.That(Directory.GetFiles(folder).Select(Path.GetFileName), Is.EqualTo(new[] { "Advance Wars.gba" }));
                Assert.That(File.Exists(Path.Combine(root, "downloads", "Advance Wars (Europe).gba")), Is.True, "source was removed");
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single(g => g.Id == 2).Path, Is.Null);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Without a folder per game (no pattern, or one without {Title}) the games share the folder their files land in.
        // Each one's path is its file there, as the scan has it for loose ROMs: the content check finds it, and no two
        // games share a path.
        [TestCase(false, "{Platform}/{Title}", "")]
        [TestCase(true, "{Platform}", "gba")]
        [TestCase(true, "{Platform}/{Year}", "gba/2001")]
        public async Task GameTargetedImport_FolderOfManyGames_PathIsTheGamesFile(bool usePattern, string pattern, string folder)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdshared_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var library = SetUpLibrary(root, new MediaSettings { UseDestinationPattern = usePattern, DestinationPathPattern = pattern });
                var dbOptions = SqliteDb(connection,
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Year = 2001, Monitored = true },
                    new Game { Id = 2, Title = "Golden Sun", PlatformId = 52, Year = 2001, Monitored = true });
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());

                var first = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "aw", gameId: 1);
                var second = await ImportFileAsync(processor, root, "Golden Sun (USA).gba", "gs", gameId: 2);

                Assert.That(first.Success && second.Success, Is.True, first.Reason + second.Reason);
                AssertPathsAreFiles(dbOptions, library, In(library, folder, "Advance Wars.gba"), In(library, folder, "Golden Sun.gba"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // An entry whose path is a library root or a platform folder gets a path of its own with its next import
        [TestCase("", false, "Advance Wars.gba")]
        [TestCase("", true, "gba/Advance Wars")]
        [TestCase("gba", true, "gba/Advance Wars")]
        public async Task GameTargetedImport_PathOfManyGames_IsReplaced(string current, bool usePattern, string expected)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdrepair_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var library = SetUpLibrary(root, new MediaSettings { UseDestinationPattern = usePattern });
                var dbOptions = SqliteDb(connection,
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Path = Directory.CreateDirectory(In(library, current)).FullName, Monitored = true });
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());

                var result = await ImportFileAsync(processor, root, "Advance Wars (USA).gba", "aw");

                Assert.That(result.Success, Is.True, result.Reason);
                using var check = new RetroArrDbContext(dbOptions);
                var game = check.Games.Single();
                Assert.That(game.Path, Is.EqualTo(In(library, expected)));
                Assert.That(MediaScannerService.CheckContent(game, new[] { library }), Is.EqualTo(GameContent.Present));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // An entry the old import left with the library root as its path: its path becomes the file it runs from,
        // not the other release that lands beside it
        [Test]
        public async Task GameTargetedImport_FolderOfManyGames_PathIsTheFileItRunsFrom()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdruns_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var library = SetUpLibrary(root, new MediaSettings { UseDestinationPattern = false });
                var rom = In(library, "Advance Wars.gba");
                File.WriteAllText(rom, "usa");
                var dbOptions = SqliteDb(connection,
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Path = library, ExecutablePath = rom, Status = GameStatus.Downloaded });
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());

                var result = await ImportFileAsync(processor, root, "Advance Wars (Europe).gba", "eur");

                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(In(library, "Advance Wars (Europe).gba")), Is.EqualTo("eur"));
                AssertPathsAreFiles(dbOptions, library, rom);
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // The generic import names the path the same way, for a wanted game it finds and for a new entry with or without metadata
        [TestCase(0, false)]
        [TestCase(7, false)]
        [TestCase(0, true)]
        public async Task GenericImport_FolderOfManyGames_PathIsTheGamesFile(int igdbId, bool wanted)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdgeneric_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var library = SetUpLibrary(root, new MediaSettings { UseDestinationPattern = false });
                var dbOptions = wanted
                    ? SqliteDb(connection, new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Monitored = true })
                    : SqliteDb(connection);
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), new FakeIgdb("Advance Wars", igdbId), new ArchiveService(), new TitleCleanerService());

                var first = await ImportGenericAsync(processor, root, "Advance Wars.gba");
                var second = await ImportGenericAsync(processor, root, "Golden Sun.gba");

                Assert.That(first.Success && second.Success, Is.True, first.Reason + second.Reason);
                AssertPathsAreFiles(dbOptions, library, In(library, "Advance Wars.gba"), In(library, "Golden Sun.gba"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // A release folder nested in the flat library is the game's own, the library isn't. An installer is added first.
        [TestCase(0, "Advance Wars.gba", "Advance Wars.gba")]
        [TestCase(7, "Advance Wars.gba", "Advance.Wars-GRP")]
        [TestCase(0, "setup.exe", "setup.exe")]
        public async Task GenericImport_ReleaseFolder_OnlyANestedOneIsTheGamesPath(int igdbId, string file, string expected)
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdnest_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var library = SetUpLibrary(root, new MediaSettings { UseDestinationPattern = false });
                var dbOptions = SqliteDb(connection);
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), new FakeIgdb("Advance Wars", igdbId), new ArchiveService(), new TitleCleanerService());
                var release = Directory.CreateDirectory(Path.Combine(root, "downloads", "Advance.Wars-GRP")).FullName;
                File.WriteAllText(Path.Combine(release, file), "rom");

                var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
                {
                    Id = "x", Name = "Advance Wars", PlatformFolder = "gba", DownloadPath = release, State = DownloadState.Completed
                });

                Assert.That(result.Success, Is.True, result.Reason);
                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Single().Path, Is.EqualTo(In(library, expected)));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // A client that completes into the very folder the import picks: the import lands inside the download, so
        // deleting the download would take the game with it
        [Test]
        public async Task GenericImport_DownloadThatIsTheGameFolder_IsKept()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdself_" + Path.GetRandomFileName());
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            try
            {
                var library = SetUpLibrary(root, new MediaSettings());
                var dbOptions = SqliteDb(connection);
                var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), new FakeIgdb("Advance Wars", 7), new ArchiveService(), new TitleCleanerService());
                var download = Directory.CreateDirectory(In(library, "gba/Advance Wars")).FullName;
                var rom = Path.Combine(download, "Advance Wars.gba");
                File.WriteAllText(rom, "the only copy");

                var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
                {
                    Id = "x", Name = "Advance Wars", PlatformFolder = "gba", DownloadPath = download, State = DownloadState.Completed
                });

                Assert.That(result.Success, Is.True, result.Reason);
                Assert.That(File.ReadAllText(rom), Is.EqualTo("the only copy"));
                using var check = new RetroArrDbContext(dbOptions);
                var game = check.Games.Single();
                Assert.That(File.Exists(game.ExecutablePath), Is.True, game.ExecutablePath);
                Assert.That(MediaScannerService.CheckContent(game, new[] { library }), Is.EqualTo(GameContent.Present));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        // Each game's path is its file, the one it runs from, and the content check finds it
        private static void AssertPathsAreFiles(DbContextOptions<RetroArrDbContext> dbOptions, string library, params string[] files)
        {
            using var check = new RetroArrDbContext(dbOptions);
            var games = check.Games.AsNoTracking().ToList();
            Assert.That(games.Select(g => g.Path), Is.EquivalentTo(files));
            foreach (var game in games)
            {
                Assert.That(game.ExecutablePath, Is.EqualTo(game.Path), game.Title);
                Assert.That(MediaScannerService.CheckContent(game, new[] { library }), Is.EqualTo(GameContent.Present), game.Title);
            }
        }

        private static string In(string library, params string[] parts) =>
            Path.Combine(new[] { library }.Concat(parts.SelectMany(p => p.Split('/', StringSplitOptions.RemoveEmptyEntries))).ToArray());

        // IGDB that knows one game (an id above 0), found by its title; every other search finds nothing
        private sealed class FakeIgdb : HttpMessageHandler, IGameMetadataServiceFactory
        {
            private readonly string _title;
            private readonly int _id;
            public FakeIgdb(string title, int id) { _title = title; _id = id; }

            public GameMetadataService CreateService()
            {
                var igdb = new IgdbClient("id", "secret");
                typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(this, false));
                return new GameMetadataService(igdb, new SteamClient());
            }

            public void RefreshConfiguration() { }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = "{\"access_token\":\"t\",\"expires_in\":3600}";
                if (request.RequestUri!.Host == "api.igdb.com")
                {
                    var query = await request.Content!.ReadAsStringAsync(ct);
                    body = _id > 0 && (query.Contains($"search \"{_title}\"") || query.Contains($"where id = ({_id})"))
                        ? $"[{{\"id\":{_id},\"name\":\"{_title}\",\"cover\":{{\"image_id\":\"cover{_id}\"}}}}]"
                        : "[]";
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        }

        private static Task<PostDownloadResult> ImportGenericAsync(PostDownloadProcessor processor, string root, string fileName)
        {
            var file = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName, fileName);
            File.WriteAllText(file, fileName);
            return processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = fileName, Name = Path.GetFileNameWithoutExtension(fileName), PlatformFolder = "gba", DownloadPath = file, State = DownloadState.Completed
            });
        }

        private static string SetUpLibrary(string root, MediaSettings settings)
        {
            Directory.CreateDirectory(Path.Combine(root, "config"));
            var library = Directory.CreateDirectory(Path.Combine(root, "library")).FullName;
            settings.FolderPath = library;
            settings.DestinationPath = library;
            var config = new ConfigurationService(root);
            config.SaveMediaSettings(settings);
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true });
            return library;
        }

        // SQLite, so the unique index on Path holds as it does on an install
        private static DbContextOptions<RetroArrDbContext> SqliteDb(SqliteConnection connection, params Game[] games)
        {
            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options;
            using var ctx = new RetroArrDbContext(dbOptions);
            ctx.Database.EnsureCreated();
            ctx.Platforms.Add(new Platform { Id = 52, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
            ctx.Games.AddRange(games);
            ctx.SaveChanges();
            return dbOptions;
        }

        // Row 1 has the title's folder, row 2 has none yet
        private static (PostDownloadProcessor Processor, DbContextOptions<RetroArrDbContext> DbOptions, string Folder) SetUpRegionRows(
            string root, SqliteConnection connection, string? firstRegion, string? secondRegion)
        {
            var library = SetUpLibrary(root, new MediaSettings());
            var folder = Directory.CreateDirectory(Path.Combine(library, "gba", "Advance Wars")).FullName;
            var rom = Path.Combine(folder, "Advance Wars.gba");
            File.WriteAllText(rom, "usa");
            var dbOptions = SqliteDb(connection,
                new Game { Id = 1, Title = "Advance Wars", Region = firstRegion, PlatformId = 52, Path = folder, ExecutablePath = rom, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "Advance Wars", Region = secondRegion, PlatformId = 52, Status = GameStatus.Released, Monitored = true });
            var processor = new PostDownloadProcessor(new ConfigurationService(root), new FileMoverService(),
                new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());
            return (processor, dbOptions, folder);
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

        private static Task<PostDownloadResult> ImportFileAsync(PostDownloadProcessor processor, string root, string fileName, string content, int gameId = 1)
        {
            var file = Path.Combine(Directory.CreateDirectory(Path.Combine(root, "downloads")).FullName, fileName);
            File.WriteAllText(file, content);
            return processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = fileName, Name = Path.GetFileNameWithoutExtension(fileName), DownloadPath = file, GameId = gameId, State = DownloadState.Completed
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

        // ── DetectContentType: with the game's title ──────────────────

        // Bundles of the game with its update or DLC, Switch base markers, keywords in the game's title
        [TestCase("Zelda TotK [NSP] + Update 1.2.1", "The Legend of Zelda: Tears of the Kingdom")]
        [TestCase("Cyberpunk 2077 v2.1 + All DLCs + Bonus Content", "Cyberpunk 2077")]
        [TestCase("Elden Ring [v1.0] + DLC", "Elden Ring")]
        [TestCase("Die Sims 4 inkl. Update 1.2 GERMAN", "Die Sims 4")]
        [TestCase("Die Sims 4 inklusive Update 1.2", "Die Sims 4")]
        [TestCase("Final Fantasy X-2 HD Remaster (Update 1.01 Included)", "Final Fantasy X-2")]
        [TestCase("Spider-Man Remastered (v1.817 Patch Applied)", "Marvel's Spider-Man Remastered")]
        [TestCase("Cyberpunk 2077 + DLC + Update v2.1", "Cyberpunk 2077")]
        [TestCase("Pokemon Sword [v0] [Update v1.3.2 + DLC]", "Pokemon Sword")]
        [TestCase("Patch Quest v1.0.3", "Patch Quest")]
        [TestCase("Fix-It Felix Jr. (USA)", "Fix-It Felix Jr.")]
        public void DetectContentType_WithTitle_MainGame(string input, string title)
        {
            Assert.That(PostDownloadProcessor.DetectContentType(input, title).Type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.MainGame));
        }

        [TestCase("patch_hades_1.37_to_1.38", "Hades", "1.37")]
        [TestCase("Hades_Update_v1.2", "Hades", "1.2")]
        [TestCase("Splatoon 3 Inkling Pack Update 1.1", "Splatoon 3", "1.1")]
        public void DetectContentType_WithTitle_Patch(string input, string title, string? expectedVersion)
        {
            var (type, version, _) = PostDownloadProcessor.DetectContentType(input, title);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.Patch));
            Assert.That(version, Is.EqualTo(expectedVersion));
        }

        [TestCase("Hades_DLC_Soundtrack", "Hades", "Soundtrack")]
        // No brackets, extension or release group in the name, and no name if nothing is left
        [TestCase("Mario Kart 8 Deluxe Booster Course Pass [v0] (DLC).nsp", "Mario Kart 8 Deluxe", null)]
        [TestCase("Hades (DLC) - Soundtrack.zip", "Hades", "Soundtrack")]
        [TestCase("Hades.DLC.Soundtrack-RUNE", "Hades", "Soundtrack")]
        [TestCase("Borderlands.2.Season.Pass-RELOADED", "Borderlands 2", "Season Pass")]
        // The update came with the DLC and is no part of its name. "Inkling" is no "incl".
        [TestCase("Monster Hunter Rise Sunbreak DLC incl. Update v10.0.2", "Monster Hunter Rise", null)]
        [TestCase("Borderlands 2 Season Pass + Update 1.8", "Borderlands 2", "Season Pass")]
        [TestCase("The Witcher 3 Hearts of Stone Expansion incl Update 1.10", "The Witcher 3", "Expansion")]
        [TestCase("Splatoon 3 Inkling Hairstyle Pack DLC", "Splatoon 3", null)]
        public void DetectContentType_WithTitle_DLC(string input, string title, string? expectedName)
        {
            var (type, _, dlcName) = PostDownloadProcessor.DetectContentType(input, title);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.DLC));
            Assert.That(dlcName, Is.EqualTo(expectedName));
        }

        // No bundle: "incl" after the marker, "Plus", a real marker beside the title
        [TestCase("Cyberpunk 2077 Update v2.12 incl DLC-RUNE", "Cyberpunk 2077", "2.12")]
        [TestCase("Sonic Mania Plus Update v1.1", "Sonic Mania", "1.1")]
        [TestCase("Patch Quest Patch v1.1", "Patch Quest", "1.1")]
        public void DetectContentType_WithTitle_StaysPatch(string input, string title, string expectedVersion)
        {
            var (type, version, _) = PostDownloadProcessor.DetectContentType(input, title);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.Patch));
            Assert.That(version, Is.EqualTo(expectedVersion));
        }

        // A title id counts in brackets, for a Switch game or a release that says Switch (tag or extension).
        // Anywhere else a hex id starting with 01 is none.
        [TestCase("Pokemon Sword [0100ABF008968000] [Update v1.3.2]", "Pokemon Sword", 46, "MainGame")]
        [TestCase("Metroid Dread [010093801237C800][v131072]", "Metroid Dread", 47, "Patch")]
        [TestCase("Mario Kart 8 Deluxe Booster Course Pass [0100152000023001][v0]", "Mario Kart 8 Deluxe", 46, "DLC")]
        [TestCase("Metroid Dread [010093801237C800][v131072].nsp", "Metroid Dread", 0, "Patch")]
        [TestCase("Metroid Dread [010093801237C800] NSW", "Metroid Dread", 0, "Patch")]
        [TestCase("Metroid Dread [010093801237C800][v131072]", "Metroid Dread", 0, "MainGame")]
        [TestCase("Metroid Dread 010093801237C800", "Metroid Dread", 46, "MainGame")]
        [TestCase("Hades v1.0 [01ABCDEF12345678]", "Hades", 1, "MainGame")]
        public void DetectContentType_SwitchTitleId(string input, string title, int platformId, string expected)
        {
            var platform = PlatformDefinitions.AllPlatforms.SingleOrDefault(p => p.Id == platformId);
            Assert.That(PostDownloadProcessor.DetectContentType(input, title, platform).Type.ToString(), Is.EqualTo(expected));
        }

        // ── Generic import: the name the game is looked up by ─────────

        [TestCase("Fix-It Felix Jr.", "Fix It Felix Jr")]
        [TestCase("Patch Quest", "Patch Quest")]
        [TestCase("Patch Quest v1.0.3", "Patch Quest")]
        [TestCase("Hades Hotfix 2", "Hades")]
        [TestCase("Hades Patch v1.2", "Hades")]
        [TestCase("Hades_Patch_1.02", "Hades")]
        // At the end, alone or with only a group or tags after it, the word is the release's
        [TestCase("Cyberpunk.2077.Hotfix-RUNE", "Cyberpunk 2077")]
        [TestCase("Hades Hotfix", "Hades")]
        [TestCase("Hades.Build-GRP", "Hades")]
        [TestCase("Hades Hotfix [FitGirl Repack]", "Hades")]
        [TestCase("Patch", "Patch")]
        public void CleanReleaseName_TakesPatchWordsButNotTitles(string release, string expected)
        {
            var processor = new PostDownloadProcessor(null!, null!, null!, null!, null!, new TitleCleanerService());
            Assert.That(processor.CleanReleaseName(release), Is.EqualTo(expected));
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
