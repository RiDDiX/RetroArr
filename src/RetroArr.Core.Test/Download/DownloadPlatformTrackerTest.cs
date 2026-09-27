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
        [TestCase("Download (Japan)")]
        [TestCase("Torrent (USA)")]
        public void GenericNames_DoNotInheritAnUnrelatedGrab(string downloadName)
        {
            _tracker.Track(ProwlarrGrab, "snes", 42);
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&link=def", "gba", 43);
            _tracker.Track("http://indexer/api?t=get&id=1", "psx", 44);
            _tracker.Track("https://tracker.example/torrent?id=9", "psx", 45);

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
            Assert.That(_tracker.LookupByName("RetroArr_download.nzb"), Is.Null);
        }

        [Test]
        public void OtherRegionOrRelease_DoesNotMatch()
        {
            _tracker.SetPlatformForDownload("Chrono Trigger (USA)", "snes", gameId: 42);
            _tracker.SetPlatformForDownload("Tetris (USA)", "nes", gameId: 5);

            Assert.That(_tracker.LookupGameId("Chrono Trigger (Japan)"), Is.Null);
            Assert.That(_tracker.LookupByName("Tetris (World)"), Is.Null);
            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA) [!].sfc"), Is.EqualTo(42));
        }

        [Test]
        public void OldNzbgetNameMapping_IsDroppedOnLoad()
        {
            _tracker.SetPlatformForDownload("RetroArr_download", "n64", gameId: 7);
            _tracker.SetPlatformForDownload("Chrono Trigger (USA)", "snes", gameId: 42);

            var reloaded = new DownloadPlatformTracker(_tempDir);

            Assert.That(reloaded.LookupByName("RetroArr_download"), Is.Null);
            Assert.That(reloaded.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(42));
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

        [Test]
        public void ContainerExtensions_AreIgnored_ForShortNames()
        {
            _tracker.Track("http://tracker/dl/Ico.torrent", "ps2");
            _tracker.SetPlatformForDownload("Rez.nzb", "dc");

            Assert.That(_tracker.LookupByName("ICO"), Is.EqualTo("ps2"));
            Assert.That(_tracker.LookupByName("Rez"), Is.EqualTo("dc"));
        }

        // A name never matches inside another one: that picks the wrong game
        [Test]
        public void ReleaseNames_MatchOnlyAsAWhole()
        {
            _tracker.Track("http://prowlarr:9696/3/download?apikey=x&file=Doom", "pc", 1);
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&file=Doom+64+(USA)", "n64", 2);
            _tracker.Track("http://prowlarr:9696/5/download?apikey=x&file=Halo", "xbox", 3);
            _tracker.Track("http://prowlarr:9696/6/download?apikey=x&file=Super+Metroid+(USA)", "snes", 4);
            _tracker.Track("http://prowlarr:9696/7/download?apikey=x&file=Final+Fantasy", "nes", 5);
            _tracker.Track("http://prowlarr:9696/8/download?apikey=x&file=Kingdom+Hearts+2", "ps2", 6);

            Assert.That(_tracker.LookupByName("Doom 64 (USA).z64"), Is.EqualTo("n64"));
            Assert.That(_tracker.LookupGameId("Doom 64 (USA).z64"), Is.EqualTo(2));
            Assert.That(_tracker.LookupByName("Shalom (Japan)"), Is.Null);
            Assert.That(_tracker.LookupByName("Halo 2 (USA)"), Is.Null);
            Assert.That(_tracker.LookupByName("Metroid (USA)"), Is.Null);
            Assert.That(_tracker.LookupByName("Final.Fantasy.VII"), Is.Null);
            Assert.That(_tracker.LookupByName("Kingdom Hearts 2.5"), Is.Null);
        }

        // Tags in brackets and a file extension may differ, the rest of the name may not
        [Test]
        public void TorrentName_MatchesTheReleaseTitle_UpToTagsAndExtension()
        {
            _tracker.Track(ProwlarrGrab, "snes", 42);
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&file=Secret+of+Mana+(USA)+%5B!%5D", "snes", 43);

            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(42));
            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA) [!].sfc"), Is.EqualTo(42));
            Assert.That(_tracker.LookupGameId("Secret of Mana (USA)"), Is.EqualTo(43));
            Assert.That(_tracker.LookupByName("Chrono.Trigger.USA-GRP"), Is.Null);
        }

        [TestCase("magnet:?xt=urn:btih:0123456789abcdef&dn=Super+Metroid+(USA)&tr=udp%3A%2F%2Ftracker%3A1337%2Fannounce", "Super Metroid (USA)")]
        [TestCase("magnet:?xt=urn:btih:0123456789abcdef&dn=Super.Metroid.USA-GRP&tr=udp://tracker:1337/announce", "Super.Metroid.USA-GRP")]
        [TestCase("magnet:?xt=urn:btih:0123456789ABCDEF0123456789ABCDEF01234567&tr=udp%3A%2F%2Ftracker%3A1337%2Fannounce", "0123456789abcdef0123456789abcdef01234567")]
        [TestCase("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&xt=urn:btmh:1220fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210", "0123456789abcdef0123456789abcdef01234567")]
        [TestCase("magnet:?xt=urn:btmh:1220fedcba9876543210fedcba9876543210fedcba9876543210fedcba9876543210&xt=urn:btih:0123456789abcdef0123456789abcdef01234567", "0123456789abcdef0123456789abcdef01234567")]
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

        // Each lookup takes the entry that has what it looks for, MarkProcessed drops every one of them
        [Test]
        public void Lookups_UseTheEntryWithTheValue_AndMarkProcessedDropsEachOfThem()
        {
            _tracker.SetPlatformForDownload("Game X", "snes");
            _tracker.Track("http://prowlarr:9696/3/download?apikey=x&file=Game+X", "snes", 7);
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&file=Game+X", "snes", null, "DLC");

            Assert.That(_tracker.LookupGameId("Game X"), Is.EqualTo(7));
            Assert.That(_tracker.LookupImportSubfolder("Game X"), Is.EqualTo("DLC"));

            _tracker.MarkProcessed("Game X");

            Assert.That(_tracker.LookupByName("Game X"), Is.Null);
            Assert.That(_tracker.LookupGameId("Game X"), Is.Null);
            Assert.That(_tracker.LookupImportSubfolder("Game X"), Is.Null);
        }

        [Test]
        public void SameTarget_TheNewestEntryWins_AndMarkProcessedKeepsTheOlderOne()
        {
            _tracker.Track("http://prowlarr:9696/3/download?apikey=x&file=Game+X", "snes", 2, "Updates");
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&file=Game+X", "snes", 2, "DLC");

            Assert.That(_tracker.LookupImportSubfolder("Game X"), Is.EqualTo("DLC"));

            _tracker.MarkProcessed("Game X");

            Assert.That(_tracker.LookupImportSubfolder("Game X"), Is.EqualTo("Updates"));
        }

        // A title grabbed for several platforms or games: no guess, the download waits for manual mapping
        [Test]
        public void OneName_SeveralTargets_StaysUnmapped()
        {
            _tracker.Track("http://prowlarr:9696/3/download?apikey=x&file=Sonic+The+Hedgehog+(USA,+Europe)", "megadrive", 1);
            _tracker.Track("http://prowlarr:9696/4/download?apikey=x&file=Sonic+The+Hedgehog+(USA,+Europe)", "gamegear", 2);
            _tracker.Track(ProwlarrGrab, "snes", 42);
            _tracker.Track("http://prowlarr:9696/5/download?apikey=x&file=Chrono+Trigger+(Japan)", "sfc", 44);
            _tracker.Track("http://prowlarr:9696/6/download?apikey=x&file=Tetris+(World)", "gb", 5);
            _tracker.Track("http://prowlarr:9696/7/download?apikey=x&file=Tetris+(World)", "gb", 6);

            Assert.That(_tracker.LookupByName("Sonic The Hedgehog (USA, Europe)"), Is.Null);
            Assert.That(_tracker.LookupGameId("Sonic The Hedgehog (USA, Europe).gg"), Is.Null);
            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA)"), Is.EqualTo(42));
            // only the (USA) grab is the same release plus tags
            Assert.That(_tracker.LookupGameId("Chrono Trigger (USA) [!].sfc"), Is.EqualTo(42));
            Assert.That(_tracker.LookupByName("Tetris (World)"), Is.Null);
        }

        // Entries older than 7 days are only removed by the next MarkProcessed, lookups skip them before that
        [Test]
        public void ExpiredEntries_AreNotUsed()
        {
            File.WriteAllText(Path.Combine(_tempDir, "download_platform_map.json"), $$"""
                [
                  { "Url": "http://prowlarr:9696/3/download?apikey=x&file=Game+X", "PlatformFolder": "pc", "GameId": 1, "AddedAt": "{{DateTime.UtcNow.AddDays(-8):O}}" },
                  { "Url": "http://prowlarr:9696/4/download?apikey=x&file=Game+X", "PlatformFolder": "snes", "GameId": 2, "AddedAt": "{{DateTime.UtcNow.AddDays(-1):O}}" },
                  { "Url": "Old.Game", "PlatformFolder": "psx", "GameId": 3, "AddedAt": "{{DateTime.UtcNow.AddDays(-8):O}}" }
                ]
                """);
            var tracker = new DownloadPlatformTracker(_tempDir);

            Assert.That(tracker.LookupByName("Game X"), Is.EqualTo("snes"));
            Assert.That(tracker.LookupGameId("Game X"), Is.EqualTo(2));
            Assert.That(tracker.LookupByName("Old.Game"), Is.Null);
        }

        [Test]
        public void MarkProcessed_DropsExpiredEntries()
        {
            File.WriteAllText(Path.Combine(_tempDir, "download_platform_map.json"), $$"""
                [ { "Url": "Old.Game", "PlatformFolder": "psx", "AddedAt": "{{DateTime.UtcNow.AddDays(-8):O}}" } ]
                """);
            var tracker = new DownloadPlatformTracker(_tempDir);

            tracker.MarkProcessed("Something Else");

            Assert.That(tracker.GetAll(), Is.Empty);
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
