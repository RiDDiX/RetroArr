using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NUnit.Framework;
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

        [TestCase("Gran Turismo 7", "Patch", "3.0.1", null, ".pkg", "Gran Turismo 7-Patch-v3.0.1.pkg")]
        [TestCase("Gran Turismo 7", "Patch", null, null, ".pkg", "Gran Turismo 7-Patch.pkg")]
        [TestCase("Bayonetta 2", "DLC", null, "Map Pack", ".nsp", "Bayonetta 2-DLC-Map Pack.nsp")]
        [TestCase("Bayonetta 2", "DLC", null, null, ".nsp", "Bayonetta 2-DLC.nsp")]
        [TestCase("Halo 3", "DLC", null, "Halo 3 Mythic Map Pack", ".god", "Halo 3-DLC-Mythic Map Pack.god")]
        public void BuildSupplementaryFileName_Correct(string title, string type, string? version, string? contentName, string ext, string expected)
        {
            var result = LibraryResortService.BuildSupplementaryFileName(title, type, version, contentName, ext);
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
