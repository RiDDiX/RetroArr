using System;
using System.IO;
using NUnit.Framework;
using RetroArr.Core.Download;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class DownloadPlatformTrackerTest
    {
        private string _tempDir = null!;
        private DownloadPlatformTracker _tracker = null!;

        [SetUp]
        public void Setup()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), $"retroarr_test_{Guid.NewGuid():N}");
            Directory.CreateDirectory(_tempDir);
            _tracker = new DownloadPlatformTracker(_tempDir);
        }

        [TearDown]
        public void TearDown()
        {
            try { Directory.Delete(_tempDir, true); } catch { }
        }

        [Test]
        public void SetAndLookup_ReturnsPlatform()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            Assert.That(_tracker.LookupByName("MyGame.nsp"), Is.EqualTo("switch"));
        }

        [Test]
        public void SetAndLookup_CaseInsensitive()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            Assert.That(_tracker.LookupByName("mygame.nsp"), Is.EqualTo("switch"));
        }

        [Test]
        public void LookupByName_NotFound_ReturnsNull()
        {
            Assert.That(_tracker.LookupByName("nonexistent"), Is.Null);
        }

        [Test]
        public void SetPlatform_WithGameId_CanLookupGameId()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch", gameId: 42);
            Assert.That(_tracker.LookupGameId("MyGame.nsp"), Is.EqualTo(42));
        }

        [Test]
        public void SetPlatform_WithImportSubfolder_CanLookupSubfolder()
        {
            _tracker.SetPlatformForDownload("MyUpdate.nsp", "switch", importSubfolder: "Patches");
            Assert.That(_tracker.LookupImportSubfolder("MyUpdate.nsp"), Is.EqualTo("Patches"));
        }

        [Test]
        public void SetPlatform_Overwrites_PreviousEntry()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            _tracker.SetPlatformForDownload("MyGame.nsp", "ps4");
            Assert.That(_tracker.LookupByName("MyGame.nsp"), Is.EqualTo("ps4"));
        }

        [Test]
        public void MarkProcessed_RemovesEntry()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch");
            _tracker.MarkProcessed("MyGame.nsp");
            Assert.That(_tracker.LookupByName("MyGame.nsp"), Is.Null);
        }

        [Test]
        public void Persistence_SurvivesReload()
        {
            _tracker.SetPlatformForDownload("MyGame.nsp", "switch", gameId: 7, importSubfolder: "DLC");

            // Create a new tracker from the same directory
            var tracker2 = new DownloadPlatformTracker(_tempDir);
            Assert.That(tracker2.LookupByName("MyGame.nsp"), Is.EqualTo("switch"));
            Assert.That(tracker2.LookupGameId("MyGame.nsp"), Is.EqualTo(7));
            Assert.That(tracker2.LookupImportSubfolder("MyGame.nsp"), Is.EqualTo("DLC"));
        }

        private const string ProwlarrGrab = "http://prowlarr:9696/3/download?apikey=x&link=abc&file=Chrono+Trigger+(USA)";

        [Test]
        public void ProwlarrUrl_MapsByTheReleaseNameInFile()
        {
            _tracker.Track(ProwlarrGrab, "snes", 42);

            Assert.That(_tracker.LookupByName("Chrono Trigger (USA)"), Is.EqualTo("snes"));
            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(42));
            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA) [!]"), Is.EqualTo(42));
        }

        // Every NZBGet job added before jobs were named after the release is called RetroArr_download
        [TestCase("RetroArr_download")]
        [TestCase("RetroArr_download.nzb")]
        [TestCase("download")]
        [TestCase("api")]
        [TestCase("get")]
        public void GenericNames_DoNotInheritAnUnrelatedGrab(string downloadName)
        {
            _tracker.Track(ProwlarrGrab, "snes", 42);
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&link=def", "gba", 43);
            _tracker.Track("http://indexer/api?t=get&id=1", "psx", 44);

            Assert.That(_tracker.LookupByName(downloadName), Is.Null);
            Assert.That(_tracker.LookupGameId(downloadName), Is.Null);
        }

        [Test]
        public void ShortNames_MatchOnlyExactly()
        {
            _tracker.Track("http://indexer/api?t=get&id=1", "psx", 44);
            _tracker.Track("http://prowlarr:9696/3/download?file=Ico", "ps2", 45);

            Assert.That(_tracker.LookupByName("Rapid Racer"), Is.Null);
            Assert.That(_tracker.LookupByName("Picori"), Is.Null);
            Assert.That(_tracker.LookupByName("ICO"), Is.EqualTo("ps2"));
        }

        [Test]
        public void GenericName_MappedManually_StillMatchesExactly()
        {
            _tracker.Track(ProwlarrGrab, "snes", 42);
            _tracker.SetPlatformForDownload("RetroArr_download", "n64", gameId: 7);

            Assert.That(_tracker.LookupByName("RetroArr_download"), Is.EqualTo("n64"));
            Assert.That(_tracker.LookupGameId("RetroArr_download"), Is.EqualTo(7));
        }

        [Test]
        public void DottedNames_AreNotCutAtTheLastDot()
        {
            _tracker.SetPlatformForDownload("Retro.Game.QF-SVF", "snes");

            Assert.That(_tracker.LookupByName("Retro.Game.EU-XYZ"), Is.Null);
            Assert.That(_tracker.LookupByName("Retro.Game.QF-SVF"), Is.EqualTo("snes"));
        }

        [Test]
        public void ContainerExtensions_AreIgnored()
        {
            _tracker.Track("http://tracker/dl/Metroid.Fusion.GBA.torrent", "gba");
            _tracker.SetPlatformForDownload("Golden.Sun.GBA.nzb", "gba", gameId: 3);

            Assert.That(_tracker.LookupByName("Metroid.Fusion.GBA"), Is.EqualTo("gba"));
            Assert.That(_tracker.LookupGameId("Golden.Sun.GBA"), Is.EqualTo(3));
        }

        [TestCase("magnet:?xt=urn:btih:0123456789abcdef&dn=Super+Metroid+(USA)&tr=udp%3A%2F%2Ftracker%3A1337%2Fannounce", "Super Metroid (USA)")]
        [TestCase("magnet:?xt=urn:btih:0123456789abcdef&dn=Super.Metroid.USA-GRP&tr=udp://tracker:1337/announce", "Super.Metroid.USA-GRP")]
        [TestCase("magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&tr=udp%3A%2F%2Ftracker%3A1337%2Fannounce", "0123456789abcdef0123456789abcdef01234567")]
        public void Magnet_MapsByDisplayName(string magnet, string torrentName)
        {
            _tracker.Track(magnet, "snes", 5);

            Assert.That(_tracker.LookupByName(torrentName), Is.EqualTo("snes"));
            Assert.That(_tracker.LookupGameId(torrentName), Is.EqualTo(5));
        }

        [Test]
        public void MarkProcessed_RemovesOnlyTheEntriesThisDownloadResolved()
        {
            _tracker.SetPlatformForDownload("Retro.Game.QF-SVF", "snes");
            _tracker.SetPlatformForDownload("Retro.Game", "psx");
            _tracker.Track("http://prowlarr:9696/3/download?apikey=x&file=Retro.Game.EU-XYZ", "n64", 9);

            _tracker.MarkProcessed("Retro.Game.EU-XYZ");

            Assert.That(_tracker.LookupGameId("Retro.Game.EU-XYZ"), Is.Null);
            Assert.That(_tracker.LookupByName("Retro.Game"), Is.EqualTo("psx"));
            Assert.That(_tracker.LookupByName("Retro.Game.QF-SVF"), Is.EqualTo("snes"));
        }

        // The file keeps its format: entries saved by earlier versions still resolve
        [Test]
        public void StoredEntries_FromEarlierVersions_StillResolve()
        {
            File.WriteAllText(Path.Combine(_tempDir, "download_platform_map.json"), $$"""
                [
                  { "Url": "{{ProwlarrGrab}}", "PlatformFolder": "snes", "GameId": 42, "ImportSubfolder": null, "AddedAt": "{{DateTime.UtcNow:O}}" },
                  { "Url": "Retro.Game.QF-SVF", "PlatformFolder": "psx", "GameId": null, "ImportSubfolder": "DLC", "AddedAt": "{{DateTime.UtcNow:O}}" }
                ]
                """);
            var tracker = new DownloadPlatformTracker(_tempDir);

            Assert.That(tracker.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(42));
            Assert.That(tracker.LookupImportSubfolder("Retro.Game.QF-SVF"), Is.EqualTo("DLC"));
            Assert.That(tracker.LookupByName("RetroArr_download"), Is.Null);
        }
    }
}
