using NUnit.Framework;
using RetroArr.Core.Games;
using RetroArr.Core.Linux;
using System.Collections.Generic;

namespace RetroArr.Core.Test.Linux
{
    [TestFixture]
    public class SteamShortcutExportServiceTest
    {
        private SteamShortcutExportService _sut = null!;

        [SetUp]
        public void Setup()
        {
            _sut = new SteamShortcutExportService();
        }

        [Test]
        public void GenerateShortcut_BasicGame_HasRequiredFields()
        {
            var game = new Game
            {
                Id = 1,
                Title = "Test Game",
                ExecutablePath = "/home/user/games/test/game.exe",
                Platform = new Platform { Name = "PC" }
            };

            var shortcut = _sut.GenerateShortcut(game);

            Assert.That(shortcut.AppName, Is.EqualTo("Test Game"));
            Assert.That(shortcut.Exe, Does.Contain("game.exe"));
            Assert.That(shortcut.StartDir, Does.Contain("/home/user/games/test"));
            Assert.That(shortcut.Tags, Does.Contain("RetroArr"));
            Assert.That(shortcut.Tags, Does.Contain("PC"));
            Assert.That(shortcut.AllowDesktopConfig, Is.True);
            Assert.That(shortcut.AllowOverlay, Is.True);
        }

        [Test]
        public void GenerateShortcut_WindowsExe_SuggestsProtonLaunchOptions()
        {
            var game = new Game
            {
                Id = 2,
                Title = "Windows Game",
                ExecutablePath = "/home/user/games/wingame/game.exe"
            };

            var shortcut = _sut.GenerateShortcut(game);

            Assert.That(shortcut.LaunchOptions, Is.Not.Empty);
        }

        [Test]
        public void GenerateShortcuts_BulkExport_SkipsGamesWithoutExe()
        {
            var games = new List<Game>
            {
                new Game { Id = 1, Title = "Has Exe", ExecutablePath = "/path/game" },
                new Game { Id = 2, Title = "No Exe", ExecutablePath = null },
                new Game { Id = 3, Title = "Empty Exe", ExecutablePath = "" },
                new Game { Id = 4, Title = "Also Has Exe", ExecutablePath = "/path/other" }
            };

            var shortcuts = _sut.GenerateShortcuts(games);

            Assert.That(shortcuts, Has.Count.EqualTo(2));
            Assert.That(shortcuts[0].AppName, Is.EqualTo("Has Exe"));
            Assert.That(shortcuts[1].AppName, Is.EqualTo("Also Has Exe"));
        }

        [Test]
        public void GetSteamShortcutPaths_ReturnsNonEmpty()
        {
            var paths = SteamShortcutExportService.GetSteamShortcutPaths();
            Assert.That(paths, Is.Not.Empty);
        }
    }
}
