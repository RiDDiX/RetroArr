using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RetroArr.Core.Download;
using RetroArr.Core.Download.TrackedDownloads;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class TrackedDownloadServiceTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_tracked_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        private static DownloadStatus Status(string id, string name, DownloadState state = DownloadState.Completed, long size = 0) => new()
        {
            Id = id,
            Name = name,
            DownloadPath = "/downloads/" + name,
            State = state,
            Size = size,
        };

        // NZBGet and other clients count their ids from 1: the same number from two clients is two downloads
        [Test]
        public void SameIdFromTwoClients_IsTrackedApart()
        {
            var service = new TrackedDownloadService(_root);

            var usenet = service.TrackDownload(Status("5", "Game A"), 1, "NZBGet");
            usenet.PlatformFolder = "snes";
            usenet.GameId = 7;
            var torrent = service.TrackDownload(Status("5", "Game B"), 2, "Other client");

            Assert.That(torrent, Is.Not.SameAs(usenet));
            Assert.That(service.GetTrackedDownloads(), Has.Count.EqualTo(2));
            Assert.That(service.Find(1, "5"), Is.SameAs(usenet));
            Assert.That(service.Find(2, "5"), Is.SameAs(torrent));
            Assert.That((usenet.DownloadClientId, usenet.Title, usenet.OutputPath), Is.EqualTo((1, "Game A", "/downloads/Game A")));
            Assert.That((torrent.DownloadClientId, torrent.Title, torrent.PlatformFolder, torrent.GameId), Is.EqualTo((2, "Game B", (string?)null, (int?)null)));
        }

        [Test]
        public void Reconcile_OnlyDropsWhatItsOwnClientStoppedReporting()
        {
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Status("5", "Game A", DownloadState.Downloading), 1, "NZBGet");
            service.TrackDownload(Status("5", "Game B", DownloadState.Downloading), 2, "Other client");

            service.ReconcileWithClientIds(new HashSet<(int, string)> { (2, "5") });

            Assert.That(service.Find(1, "5"), Is.Null);
            Assert.That(service.Find(2, "5")?.Title, Is.EqualTo("Game B"));
        }

        // tracked_downloads.json as the earlier builds wrote it
        [TestCase(TrackedDownloadState.Downloading, null)]
        [TestCase(TrackedDownloadState.ImportPending, null)]
        [TestCase(TrackedDownloadState.ImportBlocked, null)]
        [TestCase(TrackedDownloadState.ImportFailed, null)]
        [TestCase(TrackedDownloadState.Imported, "snes")]
        [TestCase(TrackedDownloadState.Ignored, "snes")]
        public void Load_OldNzbgetJobName_ForgetsItsGuessedPlatform(TrackedDownloadState state, string? expectedPlatform)
        {
            File.WriteAllText(Path.Combine(_root, "tracked_downloads.json"),
                "[{\"DownloadId\":\"7\",\"DownloadClientId\":1,\"Title\":\"RetroArr_download\",\"PlatformFolder\":\"snes\",\"GameId\":42,\"ImportSubfolder\":\"DLC\",\"State\":" + (int)state + "}]");

            var loaded = new TrackedDownloadService(_root).Find(1, "7");

            Assert.That(loaded!.PlatformFolder, Is.EqualTo(expectedPlatform));
            Assert.That(loaded.GameId, expectedPlatform == null ? Is.Null : Is.EqualTo(42));
            Assert.That(loaded.ImportSubfolder, expectedPlatform == null ? Is.Null : Is.EqualTo("DLC"));
        }

        [Test]
        public void Load_EntryWithoutTitle_KeepsLoadingTheRest()
        {
            File.WriteAllText(Path.Combine(_root, "tracked_downloads.json"),
                "[{\"DownloadId\":\"1\",\"DownloadClientId\":1,\"Title\":null},{\"DownloadId\":\"2\",\"DownloadClientId\":1,\"Title\":\"Game\"}]");

            Assert.That(new TrackedDownloadService(_root).GetTrackedDownloads(), Has.Count.EqualTo(2));
        }

        [Test]
        public void Load_EarlierFile_KeepsEachEntryUnderItsClient()
        {
            File.WriteAllText(Path.Combine(_root, "tracked_downloads.json"), @"[
  {
    ""DownloadId"": ""5"",
    ""DownloadClientId"": 1,
    ""DownloadClientName"": ""NZBGet"",
    ""Title"": ""Game A"",
    ""OutputPath"": ""/usenet/Game A"",
    ""Category"": ""RetroArr"",
    ""PlatformFolder"": ""snes"",
    ""GameId"": 7,
    ""ImportSubfolder"": null,
    ""Size"": 3,
    ""Progress"": 100,
    ""State"": 1,
    ""Status"": 0,
    ""StatusMessages"": [],
    ""Added"": ""2026-09-27T10:00:00Z"",
    ""ImportedAt"": null,
    ""CanBeRemoved"": false,
    ""IsUnmapped"": false
  }
]");
            var service = new TrackedDownloadService(_root);

            var loaded = service.Find(1, "5");
            Assert.That(loaded, Is.Not.Null);
            Assert.That((loaded!.State, loaded.PlatformFolder, loaded.GameId), Is.EqualTo((TrackedDownloadState.ImportPending, "snes", (int?)7)));
            Assert.That(service.Find(2, "5"), Is.Null);

            service.TrackDownload(Status("5", "Game B"), 2, "Other client");
            Assert.That(service.Find(1, "5")!.Title, Is.EqualTo("Game A"));
            Assert.That(new TrackedDownloadService(_root).GetTrackedDownloads(), Has.Count.EqualTo(2));
        }

        private const string Hash = "0123456789abcdef0123456789abcdef01234567";
        private const string OtherHash = "fedcba9876543210fedcba9876543210fedcba98";

        private void WriteEntry(string id, int clientId, string title, TrackedDownloadState state, long size = 0) =>
            File.WriteAllText(Path.Combine(_root, "tracked_downloads.json"),
                $"[{{\"DownloadId\":\"{id}\",\"DownloadClientId\":{clientId},\"Title\":\"{title}\",\"Size\":{size},\"State\":{(int)state}," +
                "\"PlatformFolder\":\"snes\",\"StatusMessages\":[\"Import failed: disk full\"],\"Added\":\"2026-09-27T10:00:00Z\"}]");

        // Transmission entries from before its ids became the hash carry its per-session number
        [Test]
        public void Track_TransmissionEntryUnderItsOldNumber_MovesToTheHash()
        {
            WriteEntry("1", 2, "Game A", TrackedDownloadState.ImportFailed);
            var service = new TrackedDownloadService(_root);

            var tracked = service.TrackDownload(Status(Hash, "Game A"), 2, "Transmission");

            Assert.That((tracked.DownloadId, tracked.State, tracked.PlatformFolder), Is.EqualTo((Hash, TrackedDownloadState.ImportFailed, "snes")));
            Assert.That(tracked.StatusMessages, Is.EqualTo(new[] { "Import failed: disk full" }));
            Assert.That(service.GetTrackedDownloads(), Is.EqualTo(new[] { tracked }));
            Assert.That(service.Find(2, Hash), Is.SameAs(tracked));
            Assert.That(service.Find(2, "1"), Is.Null);
            Assert.That(new TrackedDownloadService(_root).Find(2, Hash)?.State, Is.EqualTo(TrackedDownloadState.ImportFailed));
        }

        [TestCase("1", 2, "Other Game", Hash)]
        [TestCase("1", 3, "Game A", Hash)]
        [TestCase("SABnzbd_nzo_x", 2, "Game A", Hash)]
        [TestCase("1", 2, "Game A", "7")]
        [TestCase("1", 2, "Game A", Hash, 999999)]
        public void Track_OtherEntries_StayWhereTheyAre(string oldId, int oldClient, string oldTitle, string newId, long oldSize = 0)
        {
            WriteEntry(oldId, oldClient, oldTitle, TrackedDownloadState.ImportFailed, oldSize);
            var service = new TrackedDownloadService(_root);

            var tracked = service.TrackDownload(Status(newId, "Game A"), 2, "Transmission");

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.ImportPending));
            Assert.That(service.Find(oldClient, oldId)?.DownloadId, Is.EqualTo(oldId));
            Assert.That(service.GetTrackedDownloads(), Has.Count.EqualTo(2));
        }

        // Two torrents with the same name from before the upgrade: each hash takes over the entry of its size
        [TestCase(true)]
        [TestCase(false)]
        public void Track_TwoOldEntriesWithTheSameName_EachHashTakesOverItsOwn(bool seedingFirst)
        {
            File.WriteAllText(Path.Combine(_root, "tracked_downloads.json"),
                $"[{{\"DownloadId\":\"1\",\"DownloadClientId\":2,\"Title\":\"Game X\",\"Size\":100,\"State\":{(int)TrackedDownloadState.Imported}}}," +
                $"{{\"DownloadId\":\"2\",\"DownloadClientId\":2,\"Title\":\"Game X\",\"Size\":200,\"State\":{(int)TrackedDownloadState.Downloading}}}]");
            var service = new TrackedDownloadService(_root);
            var seeding = Status(Hash, "Game X", DownloadState.Completed, 100);
            var downloading = Status(OtherHash, "Game X", DownloadState.Downloading, 200);

            if (seedingFirst) service.TrackDownload(seeding, 2, "Transmission");
            service.TrackDownload(downloading, 2, "Transmission");
            if (!seedingFirst) service.TrackDownload(seeding, 2, "Transmission");

            Assert.That(service.Find(2, Hash)?.State, Is.EqualTo(TrackedDownloadState.Imported));
            Assert.That(service.Find(2, OtherHash)?.State, Is.EqualTo(TrackedDownloadState.Downloading));
            Assert.That(service.GetTrackedDownloads(), Has.Count.EqualTo(2));
        }

        [Test]
        public void ImportStatus_SameIdFromAnotherClient_IsNotImporting()
        {
            var status = new ImportStatusService();
            status.MarkImporting(1, "5");

            Assert.That(status.IsImporting(1, "5"), Is.True);
            Assert.That(status.IsImporting(2, "5"), Is.False);
        }

        [Test]
        public void ClientMessage_FollowsWhatTheClientReportsNow()
        {
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Status("hash", "Game A"), 1, "qBittorrent");
            Assert.That(tracked.ClientMessage, Is.Null);

            var withReason = Status("hash", "Game A");
            withReason.StatusMessages.Add("saved straight into '/downloads'");
            service.TrackDownload(withReason, 1, "qBittorrent");
            Assert.That(tracked.ClientMessage, Is.EqualTo("saved straight into '/downloads'"));

            service.TrackDownload(Status("hash", "Game A"), 1, "qBittorrent");
            Assert.That(tracked.ClientMessage, Is.Null);
        }
    }
}
