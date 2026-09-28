using System;
using System.IO;
using NUnit.Framework;
using RetroArr.Core.Download;
using RetroArr.Core.Logging;

namespace RetroArr.Core.Test.Security
{
    [TestFixture]
    public class DownloadLinkRedactionTest
    {
        private const string ProwlarrLink = "http://prowlarr:9696/3/download?apikey=PROWLARRKEY123&link=bG9uZ2xpbms&file=Chrono+Trigger+(USA)";
        private const string JackettLink = "http://jackett:9117/dl/nyaa/?jackett_apikey=JACKETTKEY456&path=Q2g&file=Mother+3+%5BT-En%5D.torrent";
        private const string MagnetLink = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Super+Metroid+(JU)&tr=https%3A%2F%2Ftracker.example%2Fannounce%2FPASSKEY789";
        private const string PathKeyLink = "https://tracker.example/download.php/12345/PASSKEY789/Earthbound.torrent";

        private string _dir = null!;

        [SetUp]
        public void SetUp()
        {
            _dir = Path.Combine(Path.GetTempPath(), "retroarr_linkredact_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_dir);
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
        }

        private string MapFile => File.ReadAllText(Path.Combine(_dir, "download_platform_map.json"));

        [TestCase(ProwlarrLink, "'Chrono Trigger (USA)' from prowlarr")]
        [TestCase(JackettLink, "'Mother 3 [T-En].torrent' from jackett")]
        [TestCase(MagnetLink, "'Super Metroid (JU)' from magnet link")]
        [TestCase(PathKeyLink, "tracker.example")]
        public void LogDescription_HasNoCredentials(string link, string expected)
        {
            var described = LogRedactor.DescribeDownloadUrl(link);
            Assert.That(described, Is.EqualTo(expected));
            Assert.That(described, Does.Not.Contain("KEY").And.Not.Contain("PASSKEY"));
        }

        [Test]
        public void TrackedLinks_AreStoredWithoutCredentials_AndStillMatch()
        {
            var tracker = new DownloadPlatformTracker(_dir);
            tracker.Track(ProwlarrLink, "snes", 1);
            tracker.Track(JackettLink, "gba", 2);
            tracker.Track(MagnetLink, "snes", 3);
            tracker.Track(PathKeyLink, "snes", 4);

            Assert.That(MapFile, Does.Not.Contain("PROWLARRKEY").And.Not.Contain("JACKETTKEY").And.Not.Contain("PASSKEY"));
            Assert.That(tracker.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(1));
            Assert.That(tracker.LookupGameId("Mother 3 [T-En]"), Is.EqualTo(2));
            Assert.That(tracker.LookupGameId("Super Metroid (JU)"), Is.EqualTo(3));
            Assert.That(tracker.LookupGameId("Earthbound"), Is.EqualTo(4));
        }

        [Test]
        public void OldMapFile_IsCleanedOnLoad()
        {
            var old = $@"[
  {{ ""Url"": ""{ProwlarrLink}"", ""PlatformFolder"": ""snes"", ""GameId"": 1, ""AddedAt"": ""{DateTime.UtcNow:O}"" }},
  {{ ""Url"": ""{MagnetLink}"", ""PlatformFolder"": ""snes"", ""GameId"": 3, ""AddedAt"": ""{DateTime.UtcNow:O}"" }},
  {{ ""Url"": ""Zelda (USA)"", ""PlatformFolder"": ""nes"", ""GameId"": 5, ""AddedAt"": ""{DateTime.UtcNow:O}"" }}
]";
            File.WriteAllText(Path.Combine(_dir, "download_platform_map.json"), old);

            var tracker = new DownloadPlatformTracker(_dir);

            Assert.That(MapFile, Does.Not.Contain("PROWLARRKEY").And.Not.Contain("PASSKEY"));
            Assert.That(MapFile, Does.Contain("Zelda (USA)"));
            Assert.That(tracker.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(1));
            Assert.That(tracker.LookupGameId("Super Metroid (JU)"), Is.EqualTo(3));
            Assert.That(tracker.LookupGameId("Zelda (USA)"), Is.EqualTo(5));
        }

        [TestCase("https://nzb.example/getnzb/0f3c9a1b2c3d4e5f.nzb&i=12&r=NEWZNABKEY0123456789abcd", "NEWZNABKEY")]
        [TestCase("https://dognzb.example/fetch/0f3c9a1b2c3d4e5f/DOGKEY0123456789abcdef01", "DOGKEY")]
        public void KeysInThePath_AreDroppedToo(string link, string secret)
        {
            var stripped = DownloadPlatformTracker.StripCredentials(link);
            Assert.That(stripped, Does.Not.Contain(secret));
            Assert.That(DownloadPlatformTracker.StripCredentials(stripped), Is.EqualTo(stripped));
        }

        [Test]
        public void LogRedaction_CoversSearchResultsSignedLinksAndNewznab()
        {
            var preview = "[{\"title\":\"Chrono Trigger\",\"downloadUrl\":\"http://prowlarr:9696/3/download?apikey=PROWLARRKEY123\",\"x\":1}]";
            var redacted = LogRedactor.Redact(preview);
            Assert.That(redacted, Does.Not.Contain("PROWLARRKEY123"));
            Assert.That(redacted, Does.EndWith("[REDACTED]\",\"x\":1}]"), "the JSON around the key stays intact");

            Assert.That(LogRedactor.RedactUrl("/api/v3/emulator/5/rom?exp=1&sig=abcDEF-_"), Does.Not.Contain("abcDEF"));
            Assert.That(LogRedactor.RedactUrl("https://nzb.example/api?t=get&id=1&r=NEWZNABKEY"), Does.Not.Contain("NEWZNABKEY"));
            Assert.That(LogRedactor.RedactUrl("/api/v3/game/5/local-media/file?folder=images&user=bob"), Does.Contain("folder=images").And.Contain("user=bob"));
        }

        [Test]
        public void StrippedLink_IsStable()
        {
            foreach (var link in new[] { ProwlarrLink, JackettLink, MagnetLink, PathKeyLink })
            {
                var once = DownloadPlatformTracker.StripCredentials(link);
                Assert.That(DownloadPlatformTracker.StripCredentials(once), Is.EqualTo(once), link);
            }
            Assert.That(DownloadPlatformTracker.StripCredentials("Some Release Name"), Is.EqualTo("Some Release Name"));
        }
    }
}
