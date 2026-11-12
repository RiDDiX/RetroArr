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

        // An NZBGet that lost its queue and history gives job number 1 to another release
        [Test]
        public void SameNumberUnderAnotherName_IsANewDownload()
        {
            var service = new TrackedDownloadService(_root);
            var earlier = service.TrackDownload(Status("1", "Game A"), 1, "NZBGet");
            earlier.GameId = 7;
            earlier.MarkImported();
            Assert.That(service.TrackDownload(Status("1", "Game A"), 1, "NZBGet"), Is.SameAs(earlier));

            var next = service.TrackDownload(Status("1", "Game B"), 1, "NZBGet");

            Assert.That(next, Is.Not.SameAs(earlier));
            Assert.That((next.Title, next.State, next.GameId), Is.EqualTo(("Game B", TrackedDownloadState.ImportPending, (int?)null)));
            Assert.That(service.GetTrackedDownloads(), Is.EqualTo(new[] { next }));
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
        [TestCase(TrackedDownloadState.ImportPending, "snes")]
        [TestCase(TrackedDownloadState.ImportBlocked, "snes")]
        [TestCase(TrackedDownloadState.ImportFailed, "snes")]
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

        private static DownloadStatus Failed(string id, string name, string? reason = null)
        {
            var status = Status(id, name, DownloadState.Error);
            if (reason != null) status.StatusMessages.Add(reason);
            return status;
        }

        // SABnzbd and NZBGet keep a failed job in their history, so the client keeps reporting it
        [Test]
        public void ClientReportsFailure_DownloadFailedWithTheClientsReason()
        {
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd");

            service.TrackDownload(Failed("nzo_1", "Game A", "Aborted, cannot be completed"), 1, "SABnzbd");

            Assert.That((tracked.State, tracked.Status), Is.EqualTo((TrackedDownloadState.DownloadFailed, TrackedDownloadStatus.Error)));
            Assert.That(tracked.StatusMessages, Is.EqualTo(new[] { "Aborted, cannot be completed" }));
            Assert.That(new TrackedDownloadService(_root).Find(1, "nzo_1")?.State, Is.EqualTo(TrackedDownloadState.DownloadFailed));
        }

        [Test]
        public void FirstSeenFailed_WithoutReason_DownloadFailed()
        {
            var tracked = new TrackedDownloadService(_root).TrackDownload(Failed("7", "Game A"), 1, "NZBGet");

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.DownloadFailed));
            Assert.That(tracked.StatusMessages, Is.EqualTo(new[] { "The download client reports this download as failed." }));
        }

        // A client error later on, such as qBittorrent's missingfiles once the import moved the files, changes nothing
        [TestCase(TrackedDownloadState.ImportPending)]
        [TestCase(TrackedDownloadState.ImportBlocked)]
        [TestCase(TrackedDownloadState.ImportFailed)]
        [TestCase(TrackedDownloadState.Imported)]
        [TestCase(TrackedDownloadState.Ignored)]
        public void ClientReportsFailure_AfterTheDownload_KeepsTheState(TrackedDownloadState state)
        {
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Status("hash", "Game A"), 1, "qBittorrent");
            tracked.State = state;

            service.TrackDownload(Failed("hash", "Game A", "missing files"), 1, "qBittorrent");

            Assert.That(tracked.State, Is.EqualTo(state));
        }

        // Retried or resumed in the client
        [TestCase(DownloadState.Downloading, TrackedDownloadState.Downloading)]
        [TestCase(DownloadState.Queued, TrackedDownloadState.Downloading)]
        [TestCase(DownloadState.Paused, TrackedDownloadState.Downloading)]
        [TestCase(DownloadState.Checking, TrackedDownloadState.Downloading)]
        [TestCase(DownloadState.Completed, TrackedDownloadState.ImportPending)]
        public void FailedDownload_ClientWorksOnItAgain_PicksUp(DownloadState clientState, TrackedDownloadState expected)
        {
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Failed("nzo_1", "Game A", "Aborted, cannot be completed"), 1, "SABnzbd");

            service.TrackDownload(Status("nzo_1", "Game A", clientState), 1, "SABnzbd");

            Assert.That((tracked.State, tracked.Status), Is.EqualTo((expected, TrackedDownloadStatus.Ok)));
            Assert.That(tracked.StatusMessages, Is.Empty);
        }

        [Test]
        public void Reconcile_DropsAFailedDownloadTheClientNoLongerReports()
        {
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd");
            service.TrackDownload(Failed("nzo_2", "Game B"), 1, "SABnzbd");

            service.ReconcileWithClientIds(new HashSet<(int, string)> { (1, "nzo_2") });

            Assert.That(service.Find(1, "nzo_1"), Is.Null);
            Assert.That(service.Find(1, "nzo_2")?.State, Is.EqualTo(TrackedDownloadState.DownloadFailed));
        }

        // NZBGet's DELETED history entries and statuses no client mapping knows are no retry
        [TestCase(DownloadState.Deleted)]
        [TestCase(DownloadState.Unknown)]
        public void FailedDownload_DeletedOrUnknown_StaysFailed(DownloadState clientState)
        {
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Failed("7", "Game A", "Unpack failed"), 1, "NZBGet");

            service.TrackDownload(Status("7", "Game A", clientState), 1, "NZBGet");

            Assert.That((tracked.State, tracked.Status), Is.EqualTo((TrackedDownloadState.DownloadFailed, TrackedDownloadStatus.Error)));
            Assert.That(tracked.StatusMessages, Is.EqualTo(new[] { "Unpack failed" }));
        }

        [Test]
        public void FailedDownload_ClientGivesANewReason_KeepsTheNewOne()
        {
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Failed("nzo_1", "Game A", "Aborted, cannot be completed"), 1, "SABnzbd");

            service.TrackDownload(Failed("nzo_1", "Game A", "Repair failed, not enough repair blocks"), 1, "SABnzbd");
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd");

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.DownloadFailed));
            Assert.That(tracked.StatusMessages, Is.EqualTo(new[] { "Repair failed, not enough repair blocks" }));
            Assert.That(new TrackedDownloadService(_root).Find(1, "nzo_1")?.StatusMessages, Is.EqualTo(new[] { "Repair failed, not enough repair blocks" }));
        }

        // Otherwise the monitored search skips the game until the grab expires
        [Test]
        public void ClientReportsFailure_ReleasesTheGrabForTheGame()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);

            var tracked = service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            Assert.That(grabs.HasPendingGrab(7), Is.True);

            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.False);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.True, "the monitor would send the failed release again");
            Assert.That((tracked.PlatformFolder, tracked.GameId), Is.EqualTo(("switch", (int?)7)), "a retry still knows its game");
        }

        // Retried in the client: the grab holds back the next search again, so the game is not grabbed twice
        [Test]
        public void FailedDownload_RetriedInTheClient_IsAPendingGrabAgain()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            var tracked = service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.Downloading));
            Assert.That(grabs.HasPendingGrab(7), Is.True);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.False);
        }

        // SABnzbd's retry is a new job under the same name; the failed one left the client's list
        [TestCase(DownloadState.Downloading, true)]
        [TestCase(DownloadState.Queued, true)]
        [TestCase(DownloadState.Deleted, false)]
        [TestCase(DownloadState.Unknown, false)]
        public void FailedDownload_RetriedAsANewJob_IsAPendingGrabAgain(DownloadState clientState, bool pending)
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            service.ReconcileWithClientIds(new HashSet<(int, string)>());

            service.TrackDownload(Status("nzo_2", "Game A", clientState), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.EqualTo(pending));
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.EqualTo(!pending));
        }

        // An NZBGet that lost its queue gives the number of a job it knew to a new one that fails at once
        [Test]
        public void SameNumberUnderAnotherName_FailedAtOnce_FailsItsGrab()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game B", "switch", 8);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("1", "Game A"), 1, "NZBGet", grabs);

            service.TrackDownload(Failed("1", "Game B"), 1, "NZBGet", grabs);

            Assert.That(grabs.HasPendingGrab(8), Is.False);
            Assert.That(grabs.IsFailedGrab("Game B", null, 8), Is.True);
        }

        // A retry under a new id, or a second grab of the release, still depends on the grab
        [Test]
        public void FailedDownload_SameNameStillDownloading_KeepsTheGrabPending()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            service.TrackDownload(Status("hash_b", "game a", DownloadState.Downloading), 2, "qBittorrent", grabs);

            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.True);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.False);

            service.TrackDownload(Failed("hash_b", "game a"), 2, "qBittorrent", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.False, "no job of the name is left");
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.True);
        }

        [Test]
        public void FailedDownload_ThenSameNameAsANewJob_KeepsTheGrabPending()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            service.TrackDownload(Status("nzo_2", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.True);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.False);
        }

        // The other job was deleted in the client after the failure: nothing holds the grab any more
        [Test]
        public void FailedDownload_SameNameGoneLater_FailsTheGrabOnTheNextPoll()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            service.TrackDownload(Status("hash_b", "Game A", DownloadState.Downloading), 2, "qBittorrent", grabs);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            Assert.That(grabs.HasPendingGrab(7), Is.True);

            service.ReconcileWithClientIds(new HashSet<(int, string)> { (1, "nzo_1") });
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.False);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.True);
        }

        // The client reported it Completed, so it waits for its import
        [Test]
        public void FailedDownload_SameNameWaitingForImport_KeepsTheGrabPending()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            var other = service.TrackDownload(Status("hash_b", "Game A", DownloadState.Completed), 2, "qBittorrent", grabs);

            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(other.State, Is.EqualTo(TrackedDownloadState.ImportPending));
            Assert.That(grabs.HasPendingGrab(7), Is.True);
        }

        // 'Tetris (USA)' for two systems is two games: the other one's download does not hold this grab
        [Test]
        public void FailedDownload_SameNameOfAnotherGame_FailsTheGrab()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("http://prowlarr:9696/1/download?apikey=x&file=Tetris+(USA)", "gb", 1, null, "Tetris (USA)");
            grabs.Track("http://prowlarr:9696/2/download?apikey=x&file=Tetris+(USA)", "nes", 2, null, "Tetris (USA)");
            var service = new TrackedDownloadService(_root);
            var gameBoy = service.TrackDownload(Status("nzo_1", "Tetris (USA)", DownloadState.Downloading), 1, "SABnzbd", grabs);
            var nes = service.TrackDownload(Status("nzo_2", "Tetris (USA)", DownloadState.Downloading), 1, "SABnzbd", grabs);
            (gameBoy.GameId, gameBoy.GameSetByHand) = (1, true);
            (nes.GameId, nes.GameSetByHand) = (2, true);

            service.TrackDownload(Failed("nzo_2", "Tetris (USA)"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(2), Is.False);
            Assert.That(grabs.HasPendingGrab(1), Is.True);
        }

        // With no grab stored since its mark, polling a failed download again leaves the grabs file alone
        [Test]
        public void FailedDownload_PolledAgain_LeavesTheFile()
        {
            var file = Path.Combine(_root, "download_platform_map.json");
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.True);
            File.WriteAllText(file, "[]");

            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(File.ReadAllText(file), Is.EqualTo("[]"));
        }

        // The client's history outlives the failure: the old job must not fail a later grab whose own job is live
        [Test]
        public void FailedDownload_StillListed_LeavesALaterGrabPending()
        {
            const string later = "http://prowlarr:9696/2/download?apikey=x&file=Game+A";
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            Assert.That(grabs.HasPendingGrab(7), Is.False);

            grabs.Track(later, "switch", 7);
            service.TrackDownload(Status("nzo_2", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.True);
            Assert.That(grabs.IsFailedGrab(later, null, 7), Is.False);
        }

        [Test]
        public void FailedDownload_RetriedInTheClientAndFailedAgain_FailsTheGrabAgain()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);
            service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            Assert.That(grabs.HasPendingGrab(7), Is.True);

            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.False);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.True);
        }

        // With a known game, a live job of the game holds the mark back whatever its name
        [Test]
        public void FailedDownload_SameGameStillDownloading_FailsTheGrabOnceItIsGone()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Sonic (USA)", "genesis", 7);
            grabs.Track("Sonic (USA) [!]", "genesis", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Status("nzo_1", "Sonic (USA)", DownloadState.Downloading), 1, "SABnzbd", grabs);
            service.TrackDownload(Status("hash_b", "Sonic (USA) [!]", DownloadState.Downloading), 2, "qBittorrent", grabs);

            service.TrackDownload(Failed("nzo_1", "Sonic (USA)"), 1, "SABnzbd", grabs);

            Assert.That(grabs.IsFailedGrab("Sonic (USA)", null, 7), Is.False);

            service.ReconcileWithClientIds(new HashSet<(int, string)> { (1, "nzo_1") });
            service.TrackDownload(Failed("nzo_1", "Sonic (USA)"), 1, "SABnzbd", grabs);

            Assert.That(grabs.IsFailedGrab("Sonic (USA)", null, 7), Is.True);
        }

        // A torrent client keeps the failed torrent under its hash and answers the same release sent again after the
        // 7 days as a duplicate: the new grab is that failed job's
        [Test]
        public void FailedTorrent_SameReleaseSentAgain_FailsTheNewGrab()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed(Hash, "Game A"), 2, "qBittorrent", grabs);

            grabs.Track("Game A", "switch", 7);
            Assert.That(grabs.HasPendingGrab(7), Is.True);
            service.TrackDownload(Failed(Hash, "Game A"), 2, "qBittorrent", grabs);

            Assert.That(grabs.HasPendingGrab(7), Is.False);
            Assert.That(grabs.IsFailedGrab("Game A", null, 7), Is.True);
        }

        // The queue's map-platform stores a new mapping for the game the failed download is mapped to
        [Test]
        public void FailedDownload_MappedToAnotherGame_FailsTheNewMapping()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("http://prowlarr:9696/1/download?apikey=x&file=Game+A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            var tracked = service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            (tracked.GameId, tracked.GameSetByHand) = (8, true);
            grabs.SetPlatformForDownload("Game A", "switch", 8);
            Assert.That(grabs.HasPendingGrab(8), Is.True);
            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That(grabs.HasPendingGrab(8), Is.False);
        }

        // Mapped by hand to no game: the name decides, and the other job's grab is found by that name
        [Test]
        public void FailedDownloadWithoutGame_SameNameStillDownloading_KeepsTheGrabPending()
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Game A", "switch", 7);
            var service = new TrackedDownloadService(_root);
            var failed = service.TrackDownload(Status("nzo_1", "Game A", DownloadState.Downloading), 1, "SABnzbd", grabs);
            (failed.GameId, failed.GameSetByHand) = (null, true);
            var other = service.TrackDownload(Status("hash_b", "Game A", DownloadState.Downloading), 2, "qBittorrent", grabs);

            service.TrackDownload(Failed("nzo_1", "Game A"), 1, "SABnzbd", grabs);

            Assert.That((failed.State, other.GameId), Is.EqualTo((TrackedDownloadState.DownloadFailed, (int?)7)));
            Assert.That(grabs.HasPendingGrab(7), Is.True);
        }

        // NZBGet lost its queue and lists a deleted job under the number of the failed one: no retry of it
        [TestCase(DownloadState.Deleted)]
        [TestCase(DownloadState.Unknown)]
        public void FailedDownload_NumberTakenByAnotherJob_KeepsTheGrabFailed(DownloadState clientState)
        {
            var grabs = new DownloadPlatformTracker(_root);
            grabs.Track("Sonic (USA)", "genesis", 7);
            var service = new TrackedDownloadService(_root);
            service.TrackDownload(Failed("1", "Sonic (USA)"), 1, "NZBGet", grabs);

            service.TrackDownload(Status("1", "Sonic (USA) [!]", clientState), 1, "NZBGet", grabs);

            Assert.That(grabs.IsFailedGrab("Sonic (USA)", null, 7), Is.True);
        }
    }
}
