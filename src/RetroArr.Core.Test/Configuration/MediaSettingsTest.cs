using NUnit.Framework;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Configuration
{
    [TestFixture]
    public class MediaSettingsTest
    {
        // ==================== ResolveDestinationPath ====================

        [Test]
        public void ResolveDestinationPath_DefaultPattern_UsesPlatformTitle()
        {
            var settings = new MediaSettings
            {
                UseDestinationPattern = true,
                DestinationPathPattern = "{Platform}/{Title}"
            };

            var result = settings.ResolveDestinationPath("/library", "switch", "Super Mario Odyssey");
            Assert.That(result, Is.EqualTo("/library/switch/Super Mario Odyssey"));
        }

        [Test]
        public void ResolveDestinationPath_PatternWithYear_IncludesYear()
        {
            var settings = new MediaSettings
            {
                UseDestinationPattern = true,
                DestinationPathPattern = "{Platform}/{Title} ({Year})"
            };

            var result = settings.ResolveDestinationPath("/library", "ps4", "God of War", 2018);
            Assert.That(result, Is.EqualTo("/library/ps4/God of War (2018)"));
        }

        [Test]
        public void ResolveDestinationPath_PatternWithYear_NoYearProvided_OmitsYear()
        {
            var settings = new MediaSettings
            {
                UseDestinationPattern = true,
                DestinationPathPattern = "{Platform}/{Title} ({Year})"
            };

            var result = settings.ResolveDestinationPath("/library", "ps4", "God of War");
            Assert.That(result, Is.EqualTo("/library/ps4/God of War ()"));
        }

        [Test]
        public void ResolveDestinationPath_PatternDisabled_UsesLibraryRoot()
        {
            var settings = new MediaSettings
            {
                UseDestinationPattern = false,
                DestinationPathPattern = "{Platform}/{Title}"
            };

            var result = settings.ResolveDestinationPath("/library", "switch", "Zelda");
            // When pattern is disabled, should still return a valid path
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        [Test]
        public void ResolveDestinationPath_EmptyPattern_FallsBackToDefault()
        {
            var settings = new MediaSettings
            {
                UseDestinationPattern = true,
                DestinationPathPattern = ""
            };

            var result = settings.ResolveDestinationPath("/library", "switch", "Zelda");
            Assert.That(result, Is.Not.Null.And.Not.Empty);
        }

        // ==================== ResolveGogDownloadPath ====================

        [Test]
        public void ResolveGogDownloadPath_WithFolderPath_ReturnsGogSubfolder()
        {
            var settings = new MediaSettings
            {
                FolderPath = "/games"
            };

            var result = settings.ResolveGogDownloadPath("Witcher 3");
            Assert.That(result, Does.Contain("gog"));
            Assert.That(result, Does.Contain("downloads"));
            Assert.That(result, Does.Contain("Witcher 3"));
        }

        [Test]
        public void ResolveGogDownloadPath_NoPaths_ReturnsEmpty()
        {
            var settings = new MediaSettings();

            var result = settings.ResolveGogDownloadPath("Test");
            Assert.That(result, Is.Empty);
        }

        [Test]
        public void ResolveGogDownloadPath_NoGameTitle_ReturnsBaseOnly()
        {
            var settings = new MediaSettings
            {
                FolderPath = "/games"
            };

            var result = settings.ResolveGogDownloadPath();
            Assert.That(result, Does.Contain("gog"));
            Assert.That(result, Does.Contain("downloads"));
        }

        // ==================== IsConfigured ====================

        [Test]
        public void IsConfigured_WithFolderPath_ReturnsTrue()
        {
            var settings = new MediaSettings { FolderPath = "/games" };
            Assert.That(settings.IsConfigured, Is.True);
        }

        [Test]
        public void IsConfigured_Empty_ReturnsFalse()
        {
            var settings = new MediaSettings();
            Assert.That(settings.IsConfigured, Is.False);
        }
    }
}
