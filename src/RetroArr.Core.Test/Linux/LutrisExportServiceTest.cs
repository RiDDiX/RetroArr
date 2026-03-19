using NUnit.Framework;
using RetroArr.Core.Games;
using RetroArr.Core.Linux;

namespace RetroArr.Core.Test.Linux
{
    [TestFixture]
    public class LutrisExportServiceTest
    {
        private LutrisExportService _sut = null!;

        [SetUp]
        public void Setup()
        {
            _sut = new LutrisExportService();
        }

        [Test]
        public void GenerateInstallerYaml_NativeLinuxGame_UsesLinuxRunner()
        {
            var game = new Game
            {
                Id = 1,
                Title = "SuperTux",
                ExecutablePath = "/home/user/games/supertux/supertux2"
            };

            var yaml = _sut.GenerateInstallerYaml(game);

            Assert.That(yaml, Does.Contain("runner: linux"));
            Assert.That(yaml, Does.Contain("name: SuperTux"));
            Assert.That(yaml, Does.Contain("exe: /home/user/games/supertux/supertux2"));
            Assert.That(yaml, Does.Contain("game_slug: supertux"));
        }

        [Test]
        public void GenerateInstallerYaml_WindowsExe_UsesWineRunner()
        {
            var game = new Game
            {
                Id = 2,
                Title = "Test Game",
                ExecutablePath = "/home/user/games/testgame/game.exe"
            };

            var yaml = _sut.GenerateInstallerYaml(game);

            Assert.That(yaml, Does.Contain("runner: wine"));
            Assert.That(yaml, Does.Contain("exe: /home/user/games/testgame/game.exe"));
        }

        [Test]
        public void GenerateInstallerYaml_WithRunnerOverride_UsesOverride()
        {
            var game = new Game
            {
                Id = 3,
                Title = "Overridden Game",
                ExecutablePath = "/home/user/games/test/game.exe"
            };

            var yaml = _sut.GenerateInstallerYaml(game, "proton");

            Assert.That(yaml, Does.Contain("runner: steam"));
        }

        [Test]
        public void GenerateInstallerYaml_SteamGame_UsesSteamRunner()
        {
            var game = new Game
            {
                Id = 4,
                Title = "Portal 2",
                SteamId = 620,
                ExecutablePath = ""
            };

            var yaml = _sut.GenerateInstallerYaml(game, "steam");

            Assert.That(yaml, Does.Contain("runner: steam"));
            Assert.That(yaml, Does.Contain("appid: 620"));
        }

        [Test]
        public void GenerateSlug_VariousTitles_ReturnsValidSlugs()
        {
            Assert.That(LutrisExportService.GenerateSlug("Super Mario Bros"), Is.EqualTo("super-mario-bros"));
            Assert.That(LutrisExportService.GenerateSlug("Half-Life 2"), Is.EqualTo("half-life-2"));
            Assert.That(LutrisExportService.GenerateSlug("Game: The Sequel"), Is.EqualTo("game-the-sequel"));
            Assert.That(LutrisExportService.GenerateSlug(""), Is.EqualTo("unknown"));
        }

        [Test]
        public void DetermineRunner_AutoDetectsFromExtension()
        {
            Assert.That(LutrisExportService.DetermineRunner("/path/game.exe", null), Is.EqualTo("wine"));
            Assert.That(LutrisExportService.DetermineRunner("/path/game", null), Is.EqualTo("linux"));
            Assert.That(LutrisExportService.DetermineRunner("", null), Is.EqualTo("linux"));
        }
    }
}
