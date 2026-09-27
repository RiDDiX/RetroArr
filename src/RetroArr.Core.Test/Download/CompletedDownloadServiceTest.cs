using System;
using System.IO;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
using RetroArr.Core.Download.TrackedDownloads;
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

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
