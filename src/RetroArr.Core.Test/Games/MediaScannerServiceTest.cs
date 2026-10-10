using NUnit.Framework;
using RetroArr.Core.Games;
using System.IO;

namespace RetroArr.Core.Test.Games
{
    // Covers the public folder-resolution contract MediaScannerService relies on for
    // folder-absolute platform binding. The scanner's own ResolvePlatformFromPath is
    // private; this fixture pins the PlatformDefinitions side that drives the heal
    // path (DatabaseController) and mirrors the rules the scanner inherits.
    [TestFixture]
    public class MediaScannerServiceTest
    {
        [Test]
        public void ResolvePlatformFromPath_NoMatch_ReturnsNull()
        {
            // Path with no platform-named segment must NOT fall back to a concrete platform.
            var path = $"{Path.DirectorySeparatorChar}games{Path.DirectorySeparatorChar}misc{Path.DirectorySeparatorChar}rom.bin";
            var hit = PlatformDefinitions.ResolvePlatformFromPath(path);
            Assert.That(hit, Is.Null);
        }

        [Test]
        public void ResolvePlatformFromPath_FolderMatch_ReturnsThatPlatform()
        {
            var path = Path.Combine(Path.GetTempPath(), "snes", "Mario.smc");
            var hit = PlatformDefinitions.ResolvePlatformFromPath(path);
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.Slug, Is.EqualTo("snes"));
        }

        [Test]
        public void ResolvePlatformFromPath_NestedPlatformFolder_PrefersDeepest()
        {
            // /lib/retrobat/nds/Game.nds must resolve to nds, not whatever the
            // shallow segments collide with.
            var path = Path.Combine(Path.GetTempPath(), "retrobat", "nds", "Game.nds");
            var hit = PlatformDefinitions.ResolvePlatformFromPath(path);
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.Slug, Is.EqualTo("nds"));
        }

        [Test]
        public void ResolvePlatformFromPath_IsCaseInsensitive()
        {
            var path = Path.Combine(Path.GetTempPath(), "SNES", "rom.smc");
            var hit = PlatformDefinitions.ResolvePlatformFromPath(path);
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.Slug, Is.EqualTo("snes"));
        }

        [Test]
        public void ResolvePlatformFromPath_PrefixOnly_DoesNotMatch()
        {
            // "snes_archive" must NOT match the snes platform (exact-equals contract).
            var path = Path.Combine(Path.GetTempPath(), "snes_archive", "rom.bin");
            var hit = PlatformDefinitions.ResolvePlatformFromPath(path);
            Assert.That(hit, Is.Null);
        }

        [Test]
        public void ResolvePlatformFromPath_PsxFolderAlias_HitsPs1()
        {
            // PlayStation 1 has Slug=ps1 but FolderName=psx. Both names should match.
            var pathPsx = Path.Combine(Path.GetTempPath(), "psx", "game.cue");
            var pathPs1 = Path.Combine(Path.GetTempPath(), "ps1", "game.cue");
            Assert.That(PlatformDefinitions.ResolvePlatformFromPath(pathPsx)?.Slug, Is.EqualTo("ps1"));
            Assert.That(PlatformDefinitions.ResolvePlatformFromPath(pathPs1)?.Slug, Is.EqualTo("ps1"));
        }

        [Test]
        public void ResolvePlatformFromPath_LiteralUnknown_ReturnsUnknownSentinel()
        {
            // A folder literally named "unknown" maps to the sentinel by design.
            var path = Path.Combine(Path.GetTempPath(), "unknown", "blob.iso");
            var hit = PlatformDefinitions.ResolvePlatformFromPath(path);
            Assert.That(hit, Is.Not.Null);
            Assert.That(hit!.Id, Is.EqualTo(PlatformDefinitions.UnknownPlatformId));
        }

        // Junk the scanner skips when it looks for ROMs and executables. Long names of junk count anywhere, short ones only as a
        // word of their own; updates and DLC are skipped, a game that comes with them is not.
        [TestCase("gba_bios.bin", true)]
        [TestCase("bios7.bin", true)]
        [TestCase("[BIOS] Sony PlayStation (USA) (v4.1).bin", true)]
        [TestCase("firmware.bin", true)]
        [TestCase("Mono.Posix.dll", true)]
        [TestCase("mono-2.0-bdwgc.dll", true)]
        [TestCase("FNA.dll", true)]
        [TestCase("FNA3D.dll", true)]
        [TestCase("unins000.exe", true)]
        [TestCase("unins000.dat", true)]
        [TestCase("UnityCrashHandler32.exe", true)]
        [TestCase("crashpad_handler.exe", true)]
        [TestCase("vcredist_x64.exe", true)]
        [TestCase("VC_redist.x64.exe", true)]
        [TestCase("DXSETUP.exe", true)]
        [TestCase("steam_api64.dll", true)]
        [TestCase("retroarch.exe", true)]
        [TestCase("crt-royale.slangp", true)]
        [TestCase("Zelda [Update v1.0.1].nsp", true)]
        [TestCase("Zelda [DLC] Master Trials.nsp", true)]
        [TestCase("Mario Kart 8 Deluxe (DLCs).nsp", true)]
        [TestCase("Mario Kart 8 Deluxe - All DLCs.nsp", true)]
        [TestCase("Mario Kart 8 Deluxe Updates.nsp", true)]
        [TestCase("Mario Kart 8 Deluxe [0100152000023800][v1376256].nsp", true)]
        [TestCase("Mario Kart 8 Deluxe [0100152000023001][v0].nsp", true)]
        [TestCase("Uninstaller.exe", true)]
        [TestCase("GameUpdater.exe", true)]
        [TestCase("UpdateChecker.exe", true)]
        [TestCase("GameOverlayUI.exe", true)]
        [TestCase("Firmwares", true)]
        [TestCase("MonoGame.Framework.dll", true)]
        [TestCase("Mario Kart 8 Deluxe + All DLCs.nsp", false)]
        [TestCase("Mario Kart 8 Deluxe (incl. Update 2.0).nsp", false)]
        [TestCase("Mario Kart 8 Deluxe [0100152000022000][v0].nsp", false)]
        [TestCase("Monopoly (USA).nes", false)]
        [TestCase("Symbiosis.adf", false)]
        [TestCase("BioShock.exe", false)]
        [TestCase("Jeremy McGrath Supercross 98 (USA).cue", false)]
        [TestCase("FNAF World.exe", false)]
        public void IsBlacklistedFile_SkipsJunkUpdatesAndDlc_NotGames(string fileName, bool skipped)
        {
            Assert.That(MediaScannerService.IsBlacklistedFile(fileName, isExternal: false), Is.EqualTo(skipped));
        }
    }
}
