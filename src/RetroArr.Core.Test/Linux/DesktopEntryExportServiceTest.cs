using NUnit.Framework;
using RetroArr.Core.Games;
using RetroArr.Core.Linux;

namespace RetroArr.Core.Test.Linux
{
    [TestFixture]
    public class DesktopEntryExportServiceTest
    {
        private DesktopEntryExportService _sut = null!;

        [SetUp]
        public void Setup()
        {
            _sut = new DesktopEntryExportService();
        }

        [Test]
        public void GenerateDesktopEntry_NativeGame_HasRequiredFields()
        {
            var game = new Game
            {
                Id = 1,
                Title = "SuperTux",
                ExecutablePath = "/home/user/games/supertux/supertux2"
            };

            var entry = _sut.GenerateDesktopEntry(game);

            Assert.That(entry, Does.Contain("[Desktop Entry]"));
            Assert.That(entry, Does.Contain("Type=Application"));
            Assert.That(entry, Does.Contain("Name=SuperTux"));
            Assert.That(entry, Does.Contain("Exec=\"/home/user/games/supertux/supertux2\""));
            Assert.That(entry, Does.Contain("Terminal=false"));
            Assert.That(entry, Does.Contain("Categories=Game;"));
        }

        [Test]
        public void GenerateDesktopEntry_WindowsExe_UsesWinePrefix()
        {
            var game = new Game
            {
                Id = 2,
                Title = "Windows Game",
                ExecutablePath = "/home/user/games/wingame/game.exe"
            };

            var entry = _sut.GenerateDesktopEntry(game);

            Assert.That(entry, Does.Contain("Exec=wine"));
            Assert.That(entry, Does.Contain("game.exe"));
        }

        [Test]
        public void GenerateDesktopEntry_CustomRunner_UsesRunnerPrefix()
        {
            var game = new Game
            {
                Id = 3,
                Title = "Custom Runner Game",
                ExecutablePath = "/home/user/games/test/game.exe"
            };

            var entry = _sut.GenerateDesktopEntry(game, runnerPrefix: "gamescope wine");

            Assert.That(entry, Does.Contain("Exec=gamescope wine"));
        }

        [Test]
        public void GenerateDesktopEntry_WithCustomIcon_UsesIcon()
        {
            var game = new Game
            {
                Id = 4,
                Title = "Icon Game",
                ExecutablePath = "/path/game"
            };

            var entry = _sut.GenerateDesktopEntry(game, iconPath: "/path/to/icon.png");

            Assert.That(entry, Does.Contain("Icon=/path/to/icon.png"));
        }

        [Test]
        public void BuildExecLine_EmptyPath_ReturnsEmpty()
        {
            Assert.That(DesktopEntryExportService.BuildExecLine("", null), Is.EqualTo(""));
        }
    }
}
