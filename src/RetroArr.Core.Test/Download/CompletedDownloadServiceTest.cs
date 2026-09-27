using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
using RetroArr.Core.Download.TrackedDownloads;
using RetroArr.Core.Games;
using RetroArr.Core.IO;
using TrackedDownload = RetroArr.Core.Download.TrackedDownloads.TrackedDownload;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class CompletedDownloadServiceTest
    {
        private string _root = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private CompletedDownloadService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_completed_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var factory = new DbFactory(_db);
            _service = new CompletedDownloadService(null!, new DownloadPlatformTracker(_root), new TrackedDownloadService(_root),
                new DownloadHistoryRepository(factory), new DownloadBlacklistRepository(factory), null!,
                NullLogger<CompletedDownloadService>.Instance);
        }

        [TearDown]
        public void TearDown() => Directory.Delete(_root, true);

        private async Task AddHistory(DownloadHistoryState state)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "SABnzbd_nzo_old", Title = "Old Game", State = state });
            await ctx.SaveChangesAsync();
        }

        private static TrackedDownload Tracked(TrackedDownloadState state, string path) => new()
        {
            DownloadId = "SABnzbd_nzo_old",
            Title = "Old Game",
            OutputPath = path,
            PlatformFolder = "snes",
            State = state,
        };

        // An old SABnzbd history row whose files were cleaned up by its import
        [TestCase(TrackedDownloadState.ImportPending)]
        [TestCase(TrackedDownloadState.ImportBlocked)]
        public async Task Check_AlreadyImportedDownload_IsMarkedImported_NotBlockedOnItsGonePath(TrackedDownloadState state)
        {
            await AddHistory(DownloadHistoryState.Imported);
            var tracked = Tracked(state, Path.Combine(_root, "gone"));
            tracked.Warn("Path not accessible: 'gone'.");

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.Imported));
            Assert.That(tracked.StatusMessages, Is.Empty);
        }

        [Test]
        public async Task Check_EmptyPath_ShowsTheClientsReason()
        {
            var trackedService = new TrackedDownloadService(_root);
            var status = new DownloadStatus { Id = "qb_hash", Name = "Some Game", State = DownloadState.Completed, DownloadPath = null };
            status.StatusMessages.Add("qBittorrent saved this torrent straight into '/downloads' without a folder of its own.");
            var tracked = trackedService.TrackDownload(status, 1, "qBittorrent");
            tracked.PlatformFolder = "snes";

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
            Assert.That(tracked.StatusMessages, Does.Contain(status.StatusMessages[0]));
        }

        // Only a finished import skips: failed, dismissed or unknown ids still go ahead
        [TestCase(null)]
        [TestCase(DownloadHistoryState.ImportFailed)]
        [TestCase(DownloadHistoryState.Ignored)]
        [TestCase(DownloadHistoryState.Deleted)]
        public async Task Check_WithoutAnImportedRow_StillValidatesAndQueuesTheImport(DownloadHistoryState? history)
        {
            if (history.HasValue) await AddHistory(history.Value);

            var ready = Tracked(TrackedDownloadState.ImportPending, Directory.CreateDirectory(Path.Combine(_root, "dl")).FullName);
            await _service.CheckAsync(ready, new DownloadClient());
            Assert.That(ready.State, Is.EqualTo(TrackedDownloadState.ImportPending));

            var missing = Tracked(TrackedDownloadState.ImportPending, Path.Combine(_root, "gone"));
            await _service.CheckAsync(missing, new DownloadClient());
            Assert.That(missing.State, Is.EqualTo(TrackedDownloadState.ImportBlocked));
        }

        [Test]
        public async Task Check_EmptyPath_ShowsAReasonTheClientReportsOnALaterPoll()
        {
            var trackedService = new TrackedDownloadService(_root);
            var status = new DownloadStatus { Id = "qb_hash", Name = "Some Game", State = DownloadState.Completed, DownloadPath = null };
            var tracked = trackedService.TrackDownload(status, 1, "qBittorrent");
            tracked.PlatformFolder = "snes";
            status.StatusMessages.Add("qBittorrent saved this torrent straight into '/downloads' without a folder of its own.");
            trackedService.TrackDownload(status, 1, "qBittorrent");

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.StatusMessages, Does.Contain(status.StatusMessages[0]));
        }

        private const string Hash = "0123456789abcdef0123456789abcdef01234567";

        private async Task AddRow(string downloadId, int clientId, string title, DownloadHistoryState state, long size = 0)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = downloadId, ClientId = clientId, Title = title, State = state, Size = size });
            await ctx.SaveChangesAsync();
        }

        private TrackedDownload Ready(string downloadId, int clientId, string title) => new()
        {
            DownloadId = downloadId,
            DownloadClientId = clientId,
            Title = title,
            OutputPath = Directory.CreateDirectory(Path.Combine(_root, "dl", title)).FullName,
            PlatformFolder = "snes",
            State = TrackedDownloadState.ImportPending,
        };

        // NZBGet counts its ids from 1 per client, so another client's import with the same number is a different download
        [TestCase(1, TrackedDownloadState.Imported)]
        [TestCase(2, TrackedDownloadState.ImportPending)]
        public async Task Check_NumericIdImportedBefore_OnlySkipsForTheSameClient(int clientId, TrackedDownloadState expected)
        {
            await AddRow("15", 1, "Game A", DownloadHistoryState.Imported);
            var tracked = Ready("15", clientId, "Game B");

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(expected));
        }

        // A hash names the torrent itself, so it still counts after the client was removed and added again
        [Test]
        public async Task Check_HashImportedUnderAnotherClientId_IsSkipped()
        {
            await AddRow(Hash, 1, "Game A", DownloadHistoryState.Imported);
            var tracked = Ready(Hash, 4, "Game A");

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.Imported));
        }

        // Transmission imports from before its ids became the hash are recorded under the torrent's session number
        [Test]
        public async Task Check_TransmissionImportRecordedUnderItsOldNumber_IsNotImportedAgain()
        {
            await AddRow("15", 2, "Game A", DownloadHistoryState.Imported, 100);
            var tracked = Ready(Hash, 2, "Game A");
            tracked.Size = 100;

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.Imported));
            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.DownloadHistory.Select(h => h.DownloadId), Is.EqualTo(new[] { Hash }));
        }

        [TestCase("15", 2, "Other Game", DownloadHistoryState.Imported, Hash)]
        [TestCase("15", 3, "Game A", DownloadHistoryState.Imported, Hash)]
        [TestCase("15", 2, "Game A", DownloadHistoryState.ImportFailed, Hash)]
        [TestCase("SABnzbd_nzo_x", 2, "Game A", DownloadHistoryState.Imported, Hash)]
        [TestCase("15", 2, "Game A", DownloadHistoryState.Imported, "16")]
        public async Task Check_OtherOldRows_DontCountForATorrent(string rowId, int rowClient, string rowTitle, DownloadHistoryState rowState, string trackedId)
        {
            await AddRow(rowId, rowClient, rowTitle, rowState);
            var tracked = Ready(trackedId, 2, "Game A");

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.ImportPending));
            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.DownloadHistory.Select(h => h.DownloadId), Is.EqualTo(new[] { rowId }));
        }

        // NZBGet job 5 and Transmission torrent 5 are two downloads: the torrent keeps its client, so it is
        // processed as a torrent and its seeding folder stays as it was
        [Test]
        public async Task Import_TorrentWithAnNzbgetJobsNumber_IsProcessedAsATorrent()
        {
            var appRoot = Path.Combine(_root, "app");
            Directory.CreateDirectory(Path.Combine(appRoot, "config"));
            var config = new ConfigurationService(appRoot);
            var library = Directory.CreateDirectory(Path.Combine(_root, "library", "Game B")).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library") });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true, EnableDeepClean = true, UnwantedExtensions = new List<string> { ".nfo" } });
            config.SaveDownloadClients(new List<DownloadClient>
            {
                new() { Id = 1, Name = "nzbget", Implementation = "NZBGet" },
                new() { Id = 2, Name = "transmission", Implementation = "Transmission" },
            });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Game B", PlatformId = 40, Path = library });
                await ctx.SaveChangesAsync();
            }
            var torrentDir = Directory.CreateDirectory(Path.Combine(_root, "torrents", "Game B")).FullName;
            File.WriteAllText(Path.Combine(torrentDir, "Game B.sfc"), "rom");
            File.WriteAllText(Path.Combine(torrentDir, "release.nfo"), "nfo");

            var factory = new DbFactory(_db);
            var trackedService = new TrackedDownloadService(_root);
            var service = new CompletedDownloadService(
                new PostDownloadProcessor(config, new FileMoverService(), new SqliteGameRepository(factory), null!, new ArchiveService(), new TitleCleanerService()),
                new DownloadPlatformTracker(_root), trackedService, new DownloadHistoryRepository(factory), new DownloadBlacklistRepository(factory),
                null!, NullLogger<CompletedDownloadService>.Instance);

            trackedService.TrackDownload(new DownloadStatus { Id = "5", Name = "Game A", DownloadPath = Path.Combine(_root, "usenet", "Game A"), State = DownloadState.Completed }, 1, "nzbget");
            var torrent = trackedService.TrackDownload(new DownloadStatus { Id = "5", Name = "Game B", DownloadPath = torrentDir, State = DownloadState.Completed, GameId = 1 }, 2, "transmission");
            torrent.PlatformFolder = "snes";

            await service.ImportAsync(torrent);

            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported), string.Join("; ", torrent.StatusMessages));
            Assert.That(Directory.GetFiles(Path.Combine(_root, "library"), "*.sfc", SearchOption.AllDirectories), Has.Length.EqualTo(1));
            Assert.That(Directory.GetFiles(torrentDir).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Game B.sfc", "release.nfo" }));
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
