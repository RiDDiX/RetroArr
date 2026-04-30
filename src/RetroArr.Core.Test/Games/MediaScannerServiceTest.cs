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
    }
}
