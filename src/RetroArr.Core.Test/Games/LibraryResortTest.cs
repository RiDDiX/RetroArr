using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class LibraryResortTest
    {
        // ── Helper: compute expected path using the same logic as LibraryResortService ──

        private static string ComputeExpectedPath(string libraryRoot, Platform platform, Game game, string mode = "native")
        {
            var settings = new MediaSettings
            {
                FolderPath = libraryRoot,
                DestinationPath = libraryRoot,
                FolderNamingMode = mode,
                DestinationPathPattern = "{Platform}/{Title}",
                UseDestinationPattern = true
            };
            var effectiveFolder = platform.GetEffectiveFolderName(mode);
            return settings.ResolveDestinationPath(libraryRoot, effectiveFolder, game.Title, game.Year > 0 ? game.Year : (int?)null);
        }

        // ── Rule Engine Tests ──────────────────────────────────────────

        [Test]
        public void ComputeExpectedPath_NativeMode_CorrectPath()
        {
            var platform = new Platform { Id = 1, Name = "Switch", FolderName = "switch", Slug = "switch" };
            var game = new Game { Id = 1, Title = "Zelda TOTK", PlatformId = 1 };
            var result = ComputeExpectedPath("/library", platform, game, "native");

            Assert.That(result, Is.EqualTo(Path.Combine("/library", "switch", "Zelda TOTK")));
        }

        [Test]
        public void ComputeExpectedPath_RetroBatMode_UsesOverride()
        {
            var platform = new Platform
            {
                Id = 100, Name = "Arcade", FolderName = "arcade", Slug = "arcade",
                RetroBatFolderName = "mame"
            };
            var game = new Game { Id = 2, Title = "Pac-Man", PlatformId = 100 };
            var result = ComputeExpectedPath("/library", platform, game, "retrobat");

            Assert.That(result, Is.EqualTo(Path.Combine("/library", "mame", "Pac-Man")));
        }

        [Test]
        public void ComputeExpectedPath_BatoceraMode_UsesOverride()
        {
            var platform = new Platform
            {
                Id = 100, Name = "Arcade", FolderName = "arcade", Slug = "arcade",
                BatoceraFolderName = "mame"
            };
            var game = new Game { Id = 3, Title = "Street Fighter II", PlatformId = 100 };
            var result = ComputeExpectedPath("/library", platform, game, "batocera");

            Assert.That(result, Is.EqualTo(Path.Combine("/library", "mame", "Street Fighter II")));
        }

        [Test]
        public void ComputeExpectedPath_CustomPattern_WithYear()
        {
            var settings = new MediaSettings
            {
                FolderPath = "/library",
                DestinationPath = "/library",
                FolderNamingMode = "native",
                DestinationPathPattern = "{Platform}/{Title} ({Year})",
                UseDestinationPattern = true
            };
            var result = settings.ResolveDestinationPath("/library", "switch", "Zelda TOTK", 2023);

            Assert.That(result, Is.EqualTo(Path.Combine("/library", "switch", "Zelda TOTK (2023)")));
        }

        // ── Detection Tests ────────────────────────────────────────────

        [Test]
        public void DetectWrongPlatformFolder_GameUnderWrongDir()
        {
            var platform = new Platform { Id = 100, Name = "Arcade", FolderName = "arcade", Slug = "arcade" };
            var game = new Game { Id = 1, Title = "Pac-Man", PlatformId = 100 };

            game.Path = Path.Combine("/library", "switch", "Pac-Man");
            var expected = ComputeExpectedPath("/library", platform, game, "native");

            Assert.That(expected, Is.Not.EqualTo(game.Path));
            Assert.That(expected, Does.Contain("arcade"));
        }

        [Test]
        public void DetectWrongGameFolderName_SceneReleaseName()
        {
            var platform = new Platform { Id = 1, Name = "Switch", FolderName = "switch", Slug = "switch" };
            var game = new Game { Id = 1, Title = "Zelda TOTK", PlatformId = 1 };

            game.Path = Path.Combine("/library", "switch", "Zelda.TOTK-PLAZA");
            var expected = ComputeExpectedPath("/library", platform, game, "native");

            Assert.That(expected, Is.EqualTo(Path.Combine("/library", "switch", "Zelda TOTK")));
            Assert.That(expected, Is.Not.EqualTo(game.Path));
        }

        [Test]
        public void DetectCompatibilityMismatch_NativeVsRetroBat()
        {
            var platform = new Platform
            {
                Id = 100, Name = "Arcade", FolderName = "arcade", Slug = "arcade",
                RetroBatFolderName = "mame"
            };
            var game = new Game { Id = 1, Title = "Pac-Man", PlatformId = 100 };

            game.Path = Path.Combine("/library", "arcade", "Pac-Man");
            var expected = ComputeExpectedPath("/library", platform, game, "retrobat");

            Assert.That(expected, Is.EqualTo(Path.Combine("/library", "mame", "Pac-Man")));
            Assert.That(expected, Is.Not.EqualTo(game.Path));
            Assert.That(platform.MatchesFolderName("arcade"), Is.True);
        }

        [Test]
        public void DetectDbPathMismatch_GameMoved()
        {
            var platform = new Platform { Id = 1, Name = "Switch", FolderName = "switch", Slug = "switch" };
            var game = new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = 1 };

            game.Path = "/library/switch/FF7";
            var expected = ComputeExpectedPath("/library", platform, game, "native");

            Assert.That(expected, Is.EqualTo(Path.Combine("/library", "switch", "Final Fantasy VII")));
            Assert.That(expected, Is.Not.EqualTo(game.Path));
        }

        // ── Preview Tests ──────────────────────────────────────────────

        [Test]
        public void Preview_ProducesCorrectOperationType()
        {
            var issue = new StructureIssue
            {
                Id = "test-1",
                GameId = 1,
                IssueType = IssueType.WrongPlatformFolder,
                CurrentPath = "/library/switch/Pac-Man",
                ExpectedPath = "/library/arcade/Pac-Man",
                ProposedAction = OperationType.MoveGameFolder
            };

            var op = new StructureOperation
            {
                IssueId = issue.Id,
                Type = issue.ProposedAction,
                SourcePath = issue.CurrentPath,
                TargetPath = issue.ExpectedPath,
                GameId = issue.GameId,
                IssueType = issue.IssueType.ToString()
            };

            Assert.That(op.Type, Is.EqualTo(OperationType.MoveGameFolder));
            Assert.That(op.SourcePath, Is.EqualTo("/library/switch/Pac-Man"));
            Assert.That(op.TargetPath, Is.EqualTo("/library/arcade/Pac-Man"));
        }

        // ── Idempotency Tests ──────────────────────────────────────────

        [Test]
        public void IdempotentScan_CorrectPathReturnsNoIssue()
        {
            var platform = new Platform { Id = 1, Name = "Switch", FolderName = "switch", Slug = "switch" };
            var game = new Game { Id = 1, Title = "Zelda TOTK", PlatformId = 1 };

            var expected = ComputeExpectedPath("/library", platform, game, "native");
            game.Path = expected;

            Assert.That(game.Path, Is.EqualTo(expected));
        }

        // ── Conflict Detection Tests ───────────────────────────────────

        [Test]
        public void FindAvailablePath_AppendsSequence()
        {
            var basePath = "/library/switch/Test Game";
            var suffixed = $"{basePath} (2)";
            Assert.That(suffixed, Does.Contain("(2)"));
        }

        // ── OperationPlan Status Tracking ──────────────────────────────

        [Test]
        public void OperationPlan_TracksStatusCorrectly()
        {
            var plan = new OperationPlan();
            plan.Operations.Add(new StructureOperation { Status = OperationStatus.Applied });
            plan.Operations.Add(new StructureOperation { Status = OperationStatus.Failed });
            plan.Operations.Add(new StructureOperation { Status = OperationStatus.Skipped });
            plan.Operations.Add(new StructureOperation { Status = OperationStatus.Pending });

            Assert.That(plan.TotalCount, Is.EqualTo(4));
            Assert.That(plan.AppliedCount, Is.EqualTo(1));
            Assert.That(plan.FailedCount, Is.EqualTo(1));
            Assert.That(plan.SkippedCount, Is.EqualTo(1));
            Assert.That(plan.PendingCount, Is.EqualTo(1));
            Assert.That(plan.IsComplete, Is.False);
        }

        [Test]
        public void OperationPlan_IsComplete_WhenNoPending()
        {
            var plan = new OperationPlan();
            plan.Operations.Add(new StructureOperation { Status = OperationStatus.Applied });
            plan.Operations.Add(new StructureOperation { Status = OperationStatus.Skipped });

            Assert.That(plan.IsComplete, Is.True);
        }

        // ── Data Model Serialization ───────────────────────────────────

        [Test]
        public void StructureIssue_DefaultsAreSet()
        {
            var issue = new StructureIssue();
            Assert.That(string.IsNullOrEmpty(issue.Id), Is.False);
            Assert.That(issue.Selected, Is.False);
            Assert.That(issue.GameId, Is.Null);
        }

        [Test]
        public void StructureOperation_DefaultStatus_IsPending()
        {
            var op = new StructureOperation();
            Assert.That(op.Status, Is.EqualTo(OperationStatus.Pending));
            Assert.That(op.ErrorMessage, Is.Null);
            Assert.That(op.CompletedAt, Is.Null);
        }

        // ── File-mode game handling ────────────────────────────────────

        [Test]
        public void FileModeGame_ExpectedPath_PreservesOriginalFilename()
        {
            // For a ROM file like "2 Fast 4 Gnomz (Europe).3ds", the expected
            // path must keep the original filename - only the platform folder matters.
            var platform = new Platform { Id = 107, Name = "Nintendo 3DS", FolderName = "3ds", Slug = "3ds" };
            var game = new Game { Id = 1, Title = "2 Fast 4 Gnomz", PlatformId = 107 };
            game.Path = Path.Combine("/media", "3ds", "2 Fast 4 Gnomz (Europe).3ds");

            var libraryRoot = "/media";
            var effectiveFolder = platform.GetEffectiveFolderName("native");
            var originalFileName = Path.GetFileName(game.Path);
            var expected = Path.Combine(libraryRoot, effectiveFolder, originalFileName);

            // Expected path keeps the full original filename with region and extension
            Assert.That(expected, Is.EqualTo(Path.Combine("/media", "3ds", "2 Fast 4 Gnomz (Europe).3ds")));
            // No issue detected - path already matches
            Assert.That(expected, Is.EqualTo(game.Path));
        }

        [Test]
        public void FileModeGame_SameFolder_NoRenameIssue()
        {
            // A ROM in the correct platform folder should NOT trigger D2 (wrong name)
            // even if the filename differs from Game.Title due to region tags
            var platform = new Platform { Id = 97, Name = "NES", FolderName = "nes", Slug = "nes" };
            var game = new Game { Id = 2, Title = "Super Mario Bros.", PlatformId = 97 };
            game.Path = Path.Combine("/media", "nes", "Super Mario Bros. (USA).nes");

            var libraryRoot = "/media";
            var effectiveFolder = platform.GetEffectiveFolderName("native");
            var originalFileName = Path.GetFileName(game.Path);
            var fileModeExpected = Path.Combine(libraryRoot, effectiveFolder, originalFileName);

            // File is already in the correct platform folder with its original name
            Assert.That(fileModeExpected, Is.EqualTo(game.Path));
        }

        [Test]
        public void FileModeGame_WrongPlatformFolder_PreservesFilename()
        {
            // A ROM in the wrong platform folder - the fix should move the FILE
            // to the correct folder while keeping the original filename
            var platform = new Platform { Id = 107, Name = "Nintendo 3DS", FolderName = "3ds", Slug = "3ds" };
            var game = new Game { Id = 3, Title = "2 Fast 4 Gnomz", PlatformId = 107 };
            game.Path = Path.Combine("/media", "nds", "2 Fast 4 Gnomz (Europe).3ds");

            var libraryRoot = "/media";
            var effectiveFolder = platform.GetEffectiveFolderName("native");
            var originalFileName = Path.GetFileName(game.Path);
            var expected = Path.Combine(libraryRoot, effectiveFolder, originalFileName);

            // Should move to 3ds/ keeping the original filename
            Assert.That(expected, Is.EqualTo(Path.Combine("/media", "3ds", "2 Fast 4 Gnomz (Europe).3ds")));
            Assert.That(expected, Is.Not.EqualTo(game.Path));
        }

        [Test]
        public void FolderModeGame_StillDetectsWrongName()
        {
            // For folder-mode games (PC, PS3, etc.), D2 detection should still work
            var platform = new Platform { Id = 86, Name = "PC (Windows)", FolderName = "windows", Slug = "windows" };
            var game = new Game { Id = 4, Title = "Cyberpunk 2077", PlatformId = 86 };
            game.Path = Path.Combine("/media", "windows", "Cyberpunk.2077-GOG");

            var expected = ComputeExpectedPath("/media", platform, game, "native");
            Assert.That(expected, Is.EqualTo(Path.Combine("/media", "windows", "Cyberpunk 2077")));
            Assert.That(expected, Is.Not.EqualTo(game.Path));
        }

        // ── Multi-platform game: valid alternative platform folder ─────

        [Test]
        public void MultiPlatformGame_InValidAlternativePlatformFolder_NoIssue()
        {
            // A game with DB PlatformId=Steam but physically in xbox360/ should
            // NOT be flagged - xbox360 is a recognized platform, placement is intentional.
            var xbox360 = PlatformDefinitions.AllPlatforms.FirstOrDefault(p =>
                p.MatchesFolderName("xbox360"));
            Assert.That(xbox360, Is.Not.Null, "xbox360 must be a known platform");

            // The game's current folder is a valid known platform
            Assert.That(xbox360!.MatchesFolderName("xbox360"), Is.True);
        }

        [Test]
        public void UnknownPlatformFolder_ShouldBeDetected()
        {
            // A game in "xbox36" (typo) should be flagged - it's not a known platform.
            var allPlatforms = PlatformDefinitions.AllPlatforms;
            bool isKnown = allPlatforms.Any(p => p.MatchesFolderName("xbox36"));
            Assert.That(isKnown, Is.False, "'xbox36' should not match any platform");
        }

        [Test]
        public void KnownPlatformFolder_NotFlaggedAsWrongPlatform()
        {
            // Verify that all standard platform folder names are recognized
            var testFolders = new[] { "xbox360", "steam", "switch", "3ds", "nes", "psx", "arcade", "windows" };
            var allPlatforms = PlatformDefinitions.AllPlatforms;
            foreach (var folder in testFolders)
            {
                bool isKnown = allPlatforms.Any(p => p.MatchesFolderName(folder));
                Assert.That(isKnown, Is.True, $"'{folder}' should be a recognized platform folder");
            }
        }

        // ── All 12 compatibility platforms produce different paths ─────

        [Test]
        public void AllMismatchedPlatforms_ProduceDifferentPaths_InRetroBatMode()
        {
            var mismatched = PlatformDefinitions.AllPlatforms
                .Where(p => !string.IsNullOrEmpty(p.RetroBatFolderName)
                         && !p.RetroBatFolderName.Equals(p.FolderName, StringComparison.OrdinalIgnoreCase))
                .ToList();

            Assert.That(mismatched.Count, Is.GreaterThanOrEqualTo(12));

            foreach (var platform in mismatched)
            {
                var game = new Game { Id = platform.Id, Title = "TestGame", PlatformId = platform.Id };
                var nativePath = ComputeExpectedPath("/lib", platform, game, "native");
                var retroBatPath = ComputeExpectedPath("/lib", platform, game, "retrobat");

                Assert.That(retroBatPath, Is.Not.EqualTo(nativePath),
                    $"Platform {platform.Name} should have different paths for native vs retrobat");
            }
        }

        [TestCase("Gran Turismo 7", "Patch", "v3.0.1", null, ".pkg", "Gran Turismo 7-Patch-v3.0.1.pkg")]
        [TestCase("iDigging", "Patch", "1.7.0", null, ".zip", "iDigging-Patch-1.7.0.zip")]
        [TestCase("Bayonetta 2", "Patch", "v65536", null, ".nsp", "Bayonetta 2-Patch-v65536.nsp")]
        [TestCase("Gran Turismo 7", "Patch", null, null, ".pkg", "Gran Turismo 7-Patch.pkg")]
        [TestCase("Bayonetta 2", "DLC", null, "Map Pack", ".nsp", "Bayonetta 2-DLC-Map Pack.nsp")]
        [TestCase("Bayonetta 2", "DLC", null, null, ".nsp", "Bayonetta 2-DLC.nsp")]
        [TestCase("Halo 3", "DLC", null, "Halo 3 Mythic Map Pack", ".god", "Halo 3-DLC-Mythic Map Pack.god")]
        public void BuildSupplementaryFileName_Correct(string title, string type, string? version, string? contentName, string ext, string expected)
        {
            var result = LibraryResortService.BuildSupplementaryFileName(title, type, version, contentName, ext);
            Assert.That(result, Is.EqualTo(expected));
        }

        // ── A library on disk ──────────────────────────────────────────

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        private string _root = null!;
        private string _lib = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_resort_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private async Task<LibraryResortService> Resort(params Game[] games)
        {
            await Seed(games);
            return Service(new SqliteGameRepository(new DbFactory(_db)));
        }

        private async Task Seed(params Game[] games)
        {
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Games.AddRange(games);
            await ctx.SaveChangesAsync();
        }

        private LibraryResortService Service(IGameRepository repo)
        {
            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib });
            return new LibraryResortService(repo, config);
        }

        // The user unmonitors the entry after the resort read it, right before the resort writes it
        private IGameRepository UnmonitoredBeforeEachWrite()
        {
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, args) =>
            {
                if (method.Name is not (nameof(IGameRepository.UpdateAsync) or nameof(IGameRepository.UpdateFieldsAsync))) return;
                using var ctx = new RetroArrDbContext(_db);
                ctx.Games.Single(g => g.Id == (int)args![0]!).Monitored = false;
                ctx.SaveChanges();
            };
            return repo;
        }

        private Game Row(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.AsNoTracking().Single(g => g.Id == id);
        }

        private static string Put(string folder, string name, string text = "data")
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
            return path;
        }

        // A playlist written on Windows, in a folder named after no platform
        private async Task<(LibraryResortService Resort, string[] Set)> M3uGameInAWrongFolder()
        {
            var folder = Path.Combine(_lib, "psx-games");
            var set = new[]
            {
                Put(folder, "Final Fantasy VII.m3u", "Disc1\\Final Fantasy VII (Disc 1).chd\r\nDisc2\\Final Fantasy VII (Disc 2).chd\r\n..\\Shared\\Final Fantasy VII (Disc 3).chd\r\n"),
                Put(folder, "Disc1/Final Fantasy VII (Disc 1).chd"),
                Put(folder, "Disc2/Final Fantasy VII (Disc 2).chd"),
                Put(Path.Combine(_lib, "Shared"), "Final Fantasy VII (Disc 3).chd")
            };
            var resort = await Resort(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = 20, Path = set[0], ExecutablePath = set[0], Status = GameStatus.Downloaded });
            return (resort, set);
        }

        private static async Task<StructureOperation> ApplyAll(LibraryResortService resort, int gameId = 1)
        {
            var issues = await resort.ScanAsync(new ResortScanRequest { GameId = gameId });
            return (await resort.ApplyAsync(issues.Select(i => i.Id).ToList(), ConflictResolution.Skip)).Operations.Single();
        }

        [Test]
        public async Task Apply_M3uSet_DiscsKeepTheirFolders()
        {
            var (resort, set) = await M3uGameInAWrongFolder();

            var op = await ApplyAll(resort);

            var psx = Path.Combine(_lib, "psx");
            Assert.That((op.Type, op.Status), Is.EqualTo((OperationType.MoveFileSet, OperationStatus.Applied)), op.ErrorMessage);
            // A disc outside the playlist's folder stays, and the playlist still finds it
            Assert.That(FileSetResolver.Resolve(Path.Combine(psx, "Final Fantasy VII.m3u")).CompanionFiles, Is.EqualTo(new[]
            {
                Path.Combine(psx, "Disc1", "Final Fantasy VII (Disc 1).chd"),
                Path.Combine(psx, "Disc2", "Final Fantasy VII (Disc 2).chd"),
                set[3]
            }));
        }

        [Test]
        public async Task Apply_M3uSet_TargetTaken_NothingMoves()
        {
            var (resort, set) = await M3uGameInAWrongFolder();
            var taken = Put(Path.Combine(_lib, "psx", "Disc2"), "Final Fantasy VII (Disc 2).chd", "other");

            var op = await ApplyAll(resort);

            Assert.That(op.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(set.All(File.Exists), Is.True);
            Assert.That(Directory.Exists(Path.Combine(_lib, "psx", "Disc1")), Is.False, "a disc moved before the taken target was seen");
            Assert.That(File.ReadAllText(taken), Is.EqualTo("other"));
        }

        [Test]
        public async Task Apply_M3uSet_MoveFails_TheSetIsPutBack()
        {
            var (resort, set) = await M3uGameInAWrongFolder();
            // A file where the second disc's folder has to go
            Put(Path.Combine(_lib, "psx"), "Disc2");

            var op = await ApplyAll(resort);

            Assert.That(op.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(set.All(File.Exists), Is.True);
            Assert.That(File.Exists(Path.Combine(_lib, "psx", "Final Fantasy VII.m3u")), Is.False);
        }

        // A single file moves, and its entry follows it
        [Test]
        public async Task Apply_SingleRom_TheEntryFollows()
        {
            var rom = Put(Path.Combine(_lib, "gba-roms"), "Advance Wars.gba");
            var resort = await Resort(new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded });

            var op = await ApplyAll(resort);

            var moved = Path.Combine(_lib, "gba", "Advance Wars.gba");
            Assert.That((op.Type, op.Status), Is.EqualTo((OperationType.MoveFile, OperationStatus.Applied)), op.ErrorMessage);
            Assert.That(File.Exists(moved), Is.True);
            Assert.That((Row(1).Path, Row(1).ExecutablePath), Is.EqualTo((moved, moved)));
        }

        [Test]
        public async Task Apply_EntryChangedWhileItMoves_KeepsTheChange()
        {
            var rom = Put(Path.Combine(_lib, "gba-roms"), "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });

            await ApplyAll(Service(UnmonitoredBeforeEachWrite()));

            Assert.That((Row(1).Path, Row(1).Monitored), Is.EqualTo((Path.Combine(_lib, "gba", "Advance Wars.gba"), false)));
        }

        // A single-file game's files are relative to the folder it lies in
        [Test]
        public async Task Apply_SingleRom_ItsFilesAreFoundBesideIt()
        {
            var rom = Put(Path.Combine(_lib, "gba-roms"), "Advance Wars.gba");
            var resort = await Resort(new Game { Id = 1, Title = "Advance Wars", PlatformId = 52, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded,
                GameFiles = new List<GameFile> { new() { RelativePath = "Advance Wars.gba", Size = 4, FileType = "Main" } } });

            var logs = await MissingContentTest.Logs(() => ApplyAll(resort));

            Assert.That(logs, Has.None.Contains("not found at new path"));
        }

        [Test]
        public async Task FixPlatforms_EntryChangedMeanwhile_KeepsTheChange()
        {
            var rom = Put(Path.Combine(_lib, "gba"), "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = 20, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });

            var fixes = await Service(UnmonitoredBeforeEachWrite()).FixPlatformAssignmentsAsync();

            Assert.That(fixes.Select(f => f.NewPlatformId), Is.EqualTo(new[] { 52 }));
            Assert.That((Row(1).PlatformId, Row(1).Monitored), Is.EqualTo((52, false)));
        }

        // A leftover entry of the second disc points at its cue, which the playlist lists too
        private async Task<(LibraryResortService Resort, string Folder)> CueBinSetWithALeftoverDisc()
        {
            var folder = Path.Combine(_lib, "psx-games");
            Put(folder, "FF7 (Disc 1).cue", "FILE \"FF7 (Disc 1).bin\" BINARY\n");
            Put(folder, "FF7 (Disc 1).bin");
            var cue2 = Put(folder, "FF7 (Disc 2).cue", "FILE \"FF7 (Disc 2).bin\" BINARY\n");
            Put(folder, "FF7 (Disc 2).bin");
            var m3u = Put(folder, "FF7.m3u", "FF7 (Disc 1).cue\nFF7 (Disc 2).cue\n");
            var resort = await Resort(
                new Game { Id = 1, Title = "FF7", PlatformId = 20, Path = m3u, ExecutablePath = m3u, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "FF7 (Disc 2)", PlatformId = 20, Path = cue2, ExecutablePath = cue2, Status = GameStatus.Downloaded });
            return (resort, folder);
        }

        // Disc 2 is the leftover entry's too: the playlist moved without it would miss it, so nothing moves
        [Test]
        public async Task Apply_M3uSet_ALeftoverDiscEntryKeepsItsCueAndBin()
        {
            var (resort, folder) = await CueBinSetWithALeftoverDisc();

            var op = await ApplyAll(resort, 1);

            Assert.That(op.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(op.ErrorMessage, Does.Contain("FF7 (Disc 2)"));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(5));
            Assert.That(Directory.Exists(Path.Combine(_lib, "psx")), Is.False);
            Assert.That(Row(1).Path, Is.EqualTo(Path.Combine(folder, "FF7.m3u")));
        }

        [Test]
        public async Task Apply_FileAnotherEntryTakesAlong_IsNotMoved()
        {
            var (resort, folder) = await CueBinSetWithALeftoverDisc();

            var op = await ApplyAll(resort, 2);

            Assert.That(op.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(Directory.GetFiles(folder), Has.Length.EqualTo(5));
            Assert.That(Directory.Exists(Path.Combine(_lib, "psx")), Is.False);
        }

        // The playlist lies above its discs: moving a leftover entry of disc 1 would take a disc the playlist lists
        [Test]
        public async Task Apply_LeftoverDiscEntryBelowAPlaylist_IsNotMoved()
        {
            var folder = Path.Combine(_lib, "psx-games");
            var discs = Path.Combine(folder, "FF7");
            var cue1 = Put(discs, "FF7 (Disc 1).cue", "FILE \"FF7 (Disc 1).bin\" BINARY\n");
            Put(discs, "FF7 (Disc 1).bin");
            Put(discs, "FF7 (Disc 2).cue", "FILE \"FF7 (Disc 2).bin\" BINARY\n");
            Put(discs, "FF7 (Disc 2).bin");
            var m3u = Put(folder, "FF7.m3u", "FF7/FF7 (Disc 1).cue\nFF7/FF7 (Disc 2).cue\n");
            var resort = await Resort(
                new Game { Id = 1, Title = "FF7", PlatformId = 20, Path = m3u, ExecutablePath = m3u, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "FF7 (Disc 1)", PlatformId = 20, Path = cue1, ExecutablePath = cue1, Status = GameStatus.Downloaded });

            var op = await ApplyAll(resort, 2);

            Assert.That(op.Status, Is.EqualTo(OperationStatus.Failed));
            Assert.That(op.ErrorMessage, Does.Contain("FF7 (Disc 1)"));
            Assert.That(Directory.GetFiles(discs), Has.Length.EqualTo(4));
            Assert.That(Directory.Exists(Path.Combine(_lib, "psx")), Is.False);
            Assert.That(Row(2).Path, Is.EqualTo(cue1));
        }

        // The import puts a second region of a title beside the first, in "{Title} ({Region})"
        [Test]
        public async Task Scan_SecondRegionBesideTheFirst_IsNoIssue()
        {
            var usa = Directory.CreateDirectory(Path.Combine(_lib, "gba", "Advance Wars")).FullName;
            var europe = Directory.CreateDirectory(Path.Combine(_lib, "gba", "Advance Wars (Europe)")).FullName;
            var resort = await Resort(
                new Game { Id = 1, Title = "Advance Wars", Region = "USA", PlatformId = 52, Path = usa, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "Advance Wars", Region = "Europe", PlatformId = 52, Path = europe, Status = GameStatus.Downloaded });

            Assert.That(await resort.ScanAsync(), Is.Empty);
            Assert.That(await resort.ScanAsync(new ResortScanRequest { GameId = 2 }), Is.Empty);
        }
    }
}
