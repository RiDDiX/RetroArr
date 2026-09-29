using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
using RetroArr.Core.Download.TrackedDownloads;
using RetroArr.Core.Games;
using RetroArr.Core.IO;
using RetroArr.Core.Test.Games;
using TrackedDownload = RetroArr.Core.Download.TrackedDownloads.TrackedDownload;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class CompletedDownloadServiceTest
    {
        private string _root = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private DownloadPlatformTracker _tracker = null!;
        private CompletedDownloadService _service = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_completed_" + Path.GetRandomFileName());
            Directory.CreateDirectory(_root);
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var factory = new DbFactory(_db);
            _tracker = new DownloadPlatformTracker(_root);
            _service = new CompletedDownloadService(null!, _tracker, new TrackedDownloadService(_root),
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
        public async Task Check_BlacklistedDownload_EndsItsPendingGrab()
        {
            _tracker.Track("magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567&dn=Old+Game", "snes", 5);
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.DownloadBlacklist.Add(new DownloadBlacklistEntry { Title = "Old Game", Reason = "fake" });
                await ctx.SaveChangesAsync();
            }
            var tracked = Tracked(TrackedDownloadState.ImportPending, _root);

            await _service.CheckAsync(tracked, new DownloadClient());

            Assert.That(tracked.State, Is.EqualTo(TrackedDownloadState.Ignored));
            Assert.That(_tracker.HasPendingGrab(5), Is.False);
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

        private const int Gba = 52;
        private static readonly DownloadClient Transmission = new() { Id = 2, Name = "transmission", Implementation = "Transmission" };

        // A library whose game "Advance Wars" lost its ROM, and a seeding torrent that still has it.
        private sealed class Reimport
        {
            public CompletedDownloadService Service = null!;
            public TrackedDownloadService Tracked = null!;
            public ConfigurationService Config = null!;
            public string GameFolder = null!;
            public string TorrentDir = null!;
        }

        private Reimport SetUpReimport(GameStatus status, DateTime? missingSince, bool monitored = true, ICacheService? cache = null)
        {
            var appRoot = Path.Combine(_root, "app");
            Directory.CreateDirectory(Path.Combine(appRoot, "config"));
            var config = new ConfigurationService(appRoot);
            var library = Path.Combine(_root, "library");
            config.SaveMediaSettings(new MediaSettings { FolderPath = library, DestinationPath = library });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true });
            config.SaveDownloadClients(new List<DownloadClient> { Transmission });

            var gameFolder = Directory.CreateDirectory(Path.Combine(library, "gba", "Advance Wars")).FullName;
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game
                {
                    Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = gameFolder, ExecutablePath = Path.Combine(gameFolder, "Advance Wars.gba"),
                    Status = status, MissingSince = missingSince, Monitored = monitored
                });
                ctx.SaveChanges();
            }
            var torrentDir = Directory.CreateDirectory(Path.Combine(_root, "torrents", "Advance Wars")).FullName;
            File.WriteAllText(Path.Combine(torrentDir, "Advance Wars.gba"), "rom");
            File.WriteAllText(Path.Combine(torrentDir, "release.nfo"), "nfo");

            var factory = new DbFactory(_db);
            var tracked = new TrackedDownloadService(_root);
            IGameRepository games = new SqliteGameRepository(factory);
            if (cache != null) games = new CachedGameRepository(games, cache, new CacheSettings());
            var service = new CompletedDownloadService(
                new PostDownloadProcessor(config, new FileMoverService(), new SqliteGameRepository(factory), null!, new ArchiveService(), new TitleCleanerService()),
                _tracker, tracked, new DownloadHistoryRepository(factory), new DownloadBlacklistRepository(factory),
                games, NullLogger<CompletedDownloadService>.Instance);
            return new Reimport { Service = service, Tracked = tracked, Config = config, GameFolder = gameFolder, TorrentDir = torrentDir };
        }

        private static TrackedDownload Seeding(Reimport r, int? gameId, string? outputPath = null) =>
            r.Tracked.TrackDownload(new DownloadStatus
            {
                Id = Hash, Name = "Advance Wars", DownloadPath = outputPath ?? r.TorrentDir, State = DownloadState.Completed, GameId = gameId
            }, Transmission.Id, Transmission.Name);

        private Game Game1()
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.AsNoTracking().Single(g => g.Id == 1);
        }

        private async Task AssertImportedAgain(Reimport r, TrackedDownload torrent)
        {
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported), string.Join("; ", torrent.StatusMessages));
            Assert.That(Directory.GetFiles(r.GameFolder, "*.gba", SearchOption.AllDirectories), Has.Length.EqualTo(1), "the ROM is not back in the library");
            Assert.That(Game1().Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Directory.GetFiles(r.TorrentDir).Select(Path.GetFileName), Is.EquivalentTo(new[] { "Advance Wars.gba", "release.nfo" }),
                "the seeding folder was changed");

            // The next content check sees the ROM, so the flag is gone after it at the latest
            var game = Game1();
            await new SqliteGameRepository(new DbFactory(_db)).ApplyContentStateAsync(1,
                MediaScannerService.CheckContent(game, new[] { Path.Combine(_root, "library") }), DateTime.UtcNow, game.Path);
            Assert.That(Game1().MissingSince, Is.Null);
        }

        [Test]
        public async Task Reopen_TorrentStillInClient_WhenGameFlagged()
        {
            var since = DateTime.UtcNow.AddHours(-1);
            var r = SetUpReimport(GameStatus.Missing, since);
            await AddRow(Hash, Transmission.Id, "Advance Wars", DownloadHistoryState.Imported);
            var torrent = Seeding(r, 1);
            torrent.PlatformFolder = "gba";
            torrent.MarkImported();
            r.Tracked.Save();

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });

            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.ImportPending));
            Assert.That(torrent.ReimportedFor, Is.EqualTo(since));
            Assert.That(new TrackedDownloadService(_root).GetTrackedDownloads().Single().State, Is.EqualTo(TrackedDownloadState.ImportPending), "not saved");
            await r.Service.CheckAsync(torrent, Transmission);
            await r.Service.ImportAsync(torrent);
            await AssertImportedAgain(r, torrent);
        }

        [Test]
        public async Task DownloadMonitor_ReimportsForAFlaggedGame()
        {
            var r = SetUpReimport(GameStatus.Missing, DateTime.UtcNow.AddHours(-1));
            await AddRow(Hash, Transmission.Id, "Advance Wars", DownloadHistoryState.Imported);
            var torrent = Seeding(r, 1);
            torrent.PlatformFolder = "gba";
            torrent.MarkImported();
            using var monitor = new DownloadMonitorService(r.Config, r.Tracked, r.Service, _tracker, new ImportStatusService(), NullLogger<DownloadMonitorService>.Instance);

            await monitor.ProcessTrackedDownloadsAsync(new List<DownloadClient> { Transmission });

            await AssertImportedAgain(r, torrent);
        }

        [Test]
        public async Task Reopen_GameSkippedOnAStaleCachedCopy_IsReopenedAgain()
        {
            var since = DateTime.UtcNow.AddHours(-1);
            // The monitor flagged the game without touching the cached copy
            var cache = new MissingContentTest.FakeCache { Stale = new Game { Id = 1, Title = "Advance Wars", Status = GameStatus.Downloaded, Monitored = true } };
            var r = SetUpReimport(GameStatus.Missing, since, cache: cache);
            await AddRow(Hash, Transmission.Id, "Advance Wars", DownloadHistoryState.Imported);
            var torrent = Seeding(r, 1);
            torrent.PlatformFolder = "gba";
            torrent.MarkImported();

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });
            await r.Service.CheckAsync(torrent, Transmission);
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported), "not skipped on the cached copy");

            cache.Stale = null;
            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.ImportPending), "never tried again for this flag");
            await r.Service.CheckAsync(torrent, Transmission);
            await r.Service.ImportAsync(torrent);
            await AssertImportedAgain(r, torrent);
        }

        [Test]
        public async Task ReaddedTorrent_WithImportedHistory_WhenGameFlagged_IsImported()
        {
            var r = SetUpReimport(GameStatus.Missing, null);
            var first = Seeding(r, 1);
            first.PlatformFolder = "gba";
            await r.Service.CheckAsync(first, Transmission);
            await r.Service.ImportAsync(first);
            Assert.That(first.State, Is.EqualTo(TrackedDownloadState.Imported), string.Join("; ", first.StatusMessages));

            // The ROM is deleted from the library, the game is flagged, and the torrent is added to the client again
            foreach (var rom in Directory.GetFiles(r.GameFolder, "*.gba", SearchOption.AllDirectories)) File.Delete(rom);
            var since = DateTime.UtcNow;
            using (var ctx = new RetroArrDbContext(_db))
            {
                var game = ctx.Games.Single();
                game.Status = GameStatus.Missing;
                game.MissingSince = since;
                ctx.SaveChanges();
            }
            r.Tracked.StopTracking(Transmission.Id, Hash);
            var again = Seeding(r, null);

            await r.Service.CheckAsync(again, Transmission);
            Assert.That(again.ReimportedFor, Is.EqualTo(since), "a later reopen would import it again for this flag");
            await r.Service.ImportAsync(again);

            await AssertImportedAgain(r, again);
        }

        [Test]
        public async Task ReaddedTorrent_ThroughAPathMapping_IsImported()
        {
            var r = SetUpReimport(GameStatus.Missing, null);
            var mapped = new DownloadClient
            {
                Id = Transmission.Id, Implementation = "Transmission",
                RemotePathMapping = "/remote/downloads", LocalPathMapping = Path.GetDirectoryName(r.TorrentDir)!
            };
            var first = Seeding(r, 1, "/remote/downloads/Advance Wars");
            first.PlatformFolder = "gba";
            await r.Service.CheckAsync(first, mapped);
            await r.Service.ImportAsync(first);
            Assert.That(first.State, Is.EqualTo(TrackedDownloadState.Imported), string.Join("; ", first.StatusMessages));

            foreach (var rom in Directory.GetFiles(r.GameFolder, "*.gba", SearchOption.AllDirectories)) File.Delete(rom);
            using (var ctx = new RetroArrDbContext(_db))
            {
                var game = ctx.Games.Single();
                game.Status = GameStatus.Missing;
                game.MissingSince = DateTime.UtcNow;
                ctx.SaveChanges();
            }
            r.Tracked.StopTracking(Transmission.Id, Hash);
            var again = Seeding(r, null, "/remote/downloads/Advance Wars");

            await r.Service.CheckAsync(again, mapped);

            Assert.That(again.State, Is.EqualTo(TrackedDownloadState.ImportPending), string.Join("; ", again.StatusMessages));
        }

        [Test]
        public async Task ImportedHash_GameHasContent_StillSkipped_AndEndsThePendingGrab()
        {
            var r = SetUpReimport(GameStatus.Downloaded, null);
            File.WriteAllText(Path.Combine(r.GameFolder, "Advance Wars.gba"), "rom");
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = Hash, ClientId = Transmission.Id, Title = "Advance Wars", State = DownloadHistoryState.Imported, GameId = 1 });
                await ctx.SaveChangesAsync();
            }
            _tracker.Track($"magnet:?xt=urn:btih:{Hash}&dn=Advance+Wars", "gba", 1);
            var torrent = Seeding(r, null);

            await r.Service.CheckAsync(torrent, Transmission);

            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported));
            Assert.That(_tracker.HasPendingGrab(1), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(r.GameFolder, "Advance Wars.gba")), Is.EqualTo("rom"));
        }

        [Test]
        public async Task Reopen_OncePerFlag()
        {
            var since = DateTime.UtcNow.AddHours(-1);
            var r = SetUpReimport(GameStatus.Missing, since);
            var torrent = Seeding(r, 1);
            torrent.MarkImported();

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.ImportPending));
            // An import that doesn't clear the flag (it went to Patches, say)
            torrent.MarkImported();

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported), "reopened twice for one flag");

            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Single().MissingSince = DateTime.UtcNow;
                ctx.SaveChanges();
            }
            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.ImportPending), "a later flag reopens it again");
        }

        [Test]
        public async Task Reopen_DownloadedGameWithALeftoverFlag_DoesNothing()
        {
            // An import that set Downloaded and left MissingSince, until the next check clears it
            var r = SetUpReimport(GameStatus.Downloaded, DateTime.UtcNow);
            var torrent = Seeding(r, 1);
            torrent.MarkImported();

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });

            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported));
        }

        [Test]
        public async Task Reopen_UnmonitoredOrGoneOutput_DoesNothing()
        {
            var r = SetUpReimport(GameStatus.Missing, DateTime.UtcNow, monitored: false);
            var torrent = Seeding(r, 1);
            torrent.MarkImported();

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { Transmission });
            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.Imported), "unmonitored");

            // No game lookup at all while nothing is on disk: this service has no game repository
            var gone = Tracked(TrackedDownloadState.Imported, Path.Combine(_root, "gone"));
            gone.GameId = 1;
            await _service.ReopenForMissingGamesAsync(new[] { gone }, new[] { new DownloadClient() });
            Assert.That(gone.State, Is.EqualTo(TrackedDownloadState.Imported), "output gone");
        }

        [Test]
        public async Task Reopen_LooksAtTheMappedPath()
        {
            var r = SetUpReimport(GameStatus.Missing, DateTime.UtcNow);
            var torrent = Seeding(r, 1, "/remote/downloads/Advance Wars");
            torrent.MarkImported();
            var mapped = new DownloadClient
            {
                Id = Transmission.Id, Implementation = "Transmission",
                RemotePathMapping = "/remote/downloads", LocalPathMapping = Path.GetDirectoryName(r.TorrentDir)!
            };

            await r.Service.ReopenForMissingGamesAsync(r.Tracked.GetTrackedDownloads(), new[] { mapped });

            Assert.That(torrent.State, Is.EqualTo(TrackedDownloadState.ImportPending));
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
