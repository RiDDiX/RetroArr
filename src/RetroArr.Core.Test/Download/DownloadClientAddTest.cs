using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroArr.Api.V3.DownloadClients;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
using RetroArr.Core.Download.TrackedDownloads;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class DownloadClientAddTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_dladd_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        [Test]
        public async Task AddWithGameOnly_TracksTheGamePlatformFolder()
        {
            var (controller, tracker) = await SetUpAsync(new Game { Id = 1, Title = "Halo 3", PlatformId = 31 });

            await controller.AddTorrent(new AddTorrentRequest { Url = "magnet:?xt=urn:btih:abc", Protocol = "torrent", GameId = 1 });

            var tracked = tracker.GetAll().Single();
            Assert.That(tracked.GameId, Is.EqualTo(1));
            Assert.That(tracked.PlatformFolder, Is.EqualTo("xbox360"));
        }

        // The release title picks the subfolder with the import's own rules: a bundle is the game,
        // a keyword in the game's title is no marker. A subfolder the caller sends stays.
        [TestCase("Zelda TotK [NSP] + Update 1.2.1", "The Legend of Zelda: Tears of the Kingdom", null, null)]
        [TestCase("Patch Quest v1.0.3", "Patch Quest", null, null)]
        [TestCase("Hades_Update_v1.2", "Hades", null, "Patches")]
        [TestCase("Hades.DLC.Soundtrack-RUNE", "Hades", null, "DLC")]
        [TestCase("Hades_Update_v1.2", "Hades", "DLC", "DLC")]
        // The game is on the Switch, so its title id counts
        [TestCase("Metroid Dread [010093801237C800][v131072]", "Metroid Dread", null, "Patches")]
        public async Task AddWithReleaseTitle_TracksTheSubfolderItsContentGoesTo(string releaseTitle, string gameTitle, string? sent, string? expected)
        {
            var (controller, tracker) = await SetUpAsync(new Game { Id = 1, Title = gameTitle, PlatformId = 46 });

            await controller.AddTorrent(new AddTorrentRequest
            {
                Url = "magnet:?xt=urn:btih:abc", Protocol = "torrent", GameId = 1, ReleaseTitle = releaseTitle, ImportSubfolder = sent
            });

            Assert.That(tracker.GetAll().Single().ImportSubfolder, Is.EqualTo(expected));
        }

        // The release title is sent with every grab, the platform picked for it still stays
        [Test]
        public async Task AddWithPlatformAndReleaseTitle_KeepsThePickedPlatform()
        {
            var (controller, tracker) = await SetUpAsync(new Game { Id = 1, Title = "Halo 3", PlatformId = 31 });

            await controller.AddTorrent(new AddTorrentRequest
            {
                Url = "magnet:?xt=urn:btih:abc", Protocol = "torrent", GameId = 1, PlatformFolder = "windows", ReleaseTitle = "Halo 3 Update v1.2"
            });

            var tracked = tracker.GetAll().Single();
            Assert.That(tracked.PlatformFolder, Is.EqualTo("windows"));
            Assert.That(tracked.ImportSubfolder, Is.EqualTo("Patches"));
        }

        // The map dialog starts from what the import would do with the game and platform picked there:
        // the game's title is no marker, a title id counts on the Switch
        [TestCase("Patch Quest v1.0.3", 1, null, "Main")]
        [TestCase("Patch Quest v1.0.3", null, null, "Patches")]
        [TestCase("Patch Quest DLC Soundtrack", 1, null, "DLC")]
        [TestCase("Metroid Dread [010093801237C800][v131072]", null, "switch", "Patches")]
        [TestCase("Metroid Dread [010093801237C800][v131072]", null, null, "Main")]
        public async Task SuggestImportSubfolder_UsesTheImportRules(string title, int? gameId, string? platform, string expected)
        {
            var (controller, _) = await SetUpAsync(new Game { Id = 1, Title = "Patch Quest", PlatformId = 1 });

            var result = await controller.SuggestImportSubfolder(title, gameId, platform);

            var body = JsonSerializer.SerializeToElement(((OkObjectResult)result).Value);
            Assert.That(body.GetProperty("importSubfolder").GetString(), Is.EqualTo(expected));
        }

        // Only Main, Patches and DLC name a subfolder: a path in it would put the import outside the game's folder
        [TestCase("../x")]
        [TestCase("/etc")]
        [TestCase("Patches/../..")]
        [TestCase("Extras")]
        public async Task UnknownSubfolder_IsRefused(string subfolder)
        {
            var (controller, tracker) = await SetUpAsync(new Game { Id = 1, Title = "Halo 3", PlatformId = 31 });

            var add = await controller.AddTorrent(new AddTorrentRequest { Url = "magnet:?xt=urn:btih:abc", Protocol = "torrent", GameId = 1, ImportSubfolder = subfolder });
            var map = controller.MapPlatform(new MapPlatformRequest { DownloadName = "Halo 3", PlatformFolder = "xbox360", ImportSubfolder = subfolder });

            Assert.That(add, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(map, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(tracker.GetAll(), Is.Empty, "the subfolder was stored");
        }

        // "New game from download title" in the map dialog sends game 0: the download is no grab of the game it was linked
        // to, and the grab's own entry doesn't link it again when the monitor or the import looks the game up by name
        [Test]
        public async Task MapToNoGame_TakesBackTheLinkedGame()
        {
            var trackedDownloads = new TrackedDownloadService(Path.Combine(_root, "config"));
            var (controller, tracker) = await SetUpAsync(new Game { Id = 5, Title = "Halo 3", PlatformId = 31 }, trackedDownloads);
            tracker.Track("magnet:?xt=urn:btih:abc&dn=Halo+3+(USA)", "xbox360", 5);
            var tracked = trackedDownloads.TrackDownload(new DownloadStatus { Id = "abc", Name = "Halo 3 (USA)", GameId = 5 }, 1, "client");

            var result = controller.MapPlatform(new MapPlatformRequest
            {
                DownloadName = "Halo 3 (USA)", ClientId = 1, DownloadId = "abc", PlatformFolder = "xbox360", GameId = 0, ImportSubfolder = "Main"
            });

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(tracked.GameId, Is.Null);
            Assert.That(tracker.LookupGameId("Halo 3 (USA)"), Is.Null);
        }

        // Mapped to another game or to none, the download is no grab of the game it was linked to: that game is searched
        // for again, and a lookup by the name doesn't link the download back to it
        [TestCase(7)]
        [TestCase(0)]
        public async Task Remap_EndsTheOldGamesGrab(int gameId)
        {
            var trackedDownloads = new TrackedDownloadService(Path.Combine(_root, "config"));
            var (controller, tracker) = await SetUpAsync(new Game { Id = 5, Title = "Halo 3", PlatformId = 31 }, trackedDownloads);
            tracker.Track("magnet:?xt=urn:btih:abc&dn=Halo+3+(USA)", "xbox360", 5);
            trackedDownloads.TrackDownload(new DownloadStatus { Id = "abc", Name = "Halo 3 (USA)" }, 1, "client", tracker);

            controller.MapPlatform(new MapPlatformRequest { DownloadName = "Halo 3 (USA)", ClientId = 1, DownloadId = "abc", PlatformFolder = "xbox360", GameId = gameId });

            Assert.That(tracker.HasPendingGrab(5), Is.False);
            Assert.That(tracker.LookupGameId("Halo 3 (USA)"), Is.EqualTo(gameId > 0 ? gameId : null));
        }

        // Only grabs under the download's own name end: another release of the game, still downloading, stays its pending grab
        [Test]
        public async Task Remap_KeepsTheGrabOfAnotherRelease()
        {
            var trackedDownloads = new TrackedDownloadService(Path.Combine(_root, "config"));
            var (controller, tracker) = await SetUpAsync(new Game { Id = 5, Title = "Halo 3", PlatformId = 31 }, trackedDownloads);
            tracker.Track("magnet:?xt=urn:btih:aaa&dn=Halo+3+(USA)", "xbox360", 5);
            tracker.Track("magnet:?xt=urn:btih:bbb&dn=Halo+3+(USA)+[TU5]", "xbox360", 5);
            trackedDownloads.TrackDownload(new DownloadStatus { Id = "bbb", Name = "Halo 3 (USA) [TU5]" }, 1, "client", tracker);

            controller.MapPlatform(new MapPlatformRequest { DownloadName = "Halo 3 (USA) [TU5]", ClientId = 1, DownloadId = "bbb", PlatformFolder = "xbox360", GameId = 7 });

            Assert.That(tracker.HasPendingGrab(5), Is.True, "the other release's grab ended, the game would be grabbed again");
            Assert.That(tracker.LookupGameId("Halo 3 (USA)"), Is.EqualTo(5));
            Assert.That(tracker.LookupGameId("Halo 3 (USA) [TU5]"), Is.EqualTo(7));
            Assert.That(tracker.GetAll().Count(e => e.GameId == 5), Is.EqualTo(1), "the remapped release's own grab stayed");
        }

        // "No game" sticks when another tracked download has the same title: no mapping by name is stored then and the grab
        // stays, the other download may be the game's. Neither the monitor's next poll nor the manual import links it again.
        [Test]
        public async Task MapToNoGame_Sticks_WhenAnotherDownloadHasTheTitle()
        {
            var trackedDownloads = new TrackedDownloadService(Path.Combine(_root, "config"));
            var (controller, tracker) = await SetUpAsync(new Game { Id = 5, Title = "Halo 3", PlatformId = 31 }, trackedDownloads);
            tracker.Track("magnet:?xt=urn:btih:abc&dn=Halo+3+(USA)", "xbox360", 5);
            var mine = trackedDownloads.TrackDownload(new DownloadStatus { Id = "abc", Name = "Halo 3 (USA)" }, 1, "client", tracker);
            var other = trackedDownloads.TrackDownload(new DownloadStatus { Id = "def", Name = "Halo 3 (USA)" }, 2, "other", tracker);

            controller.MapPlatform(new MapPlatformRequest { DownloadName = "Halo 3 (USA)", ClientId = 1, DownloadId = "abc", PlatformFolder = "xbox360", GameId = 0 });
            trackedDownloads.TrackDownload(new DownloadStatus { Id = "abc", Name = "Halo 3 (USA)" }, 1, "client", tracker);
            trackedDownloads.TrackDownload(new DownloadStatus { Id = "def", Name = "Halo 3 (USA)" }, 2, "other", tracker);

            Assert.That(mine.GameId, Is.Null);
            Assert.That(other.GameId, Is.EqualTo(5));
            Assert.That(tracker.HasPendingGrab(5), Is.True, "the grab may be the other download's");
            Assert.That(new TrackedDownloadService(Path.Combine(_root, "config")).Find(1, "abc")!.GameSetByHand, Is.True, "not saved");
        }

        // The monitor's poll doesn't link a download mapped to no game by hand again, the grab of its name notwithstanding
        [Test]
        public async Task Monitor_KeepsNoGameMappedByHand()
        {
            const string hash = "0123456789abcdef0123456789abcdef01234567";
            using var client = new OneTorrent(hash, "Halo 3 (USA)");
            var config = new ConfigurationService(_root);
            config.SaveDownloadClients(new List<DownloadClient> { new() { Id = 1, Name = "tr", Implementation = "Transmission", Host = "127.0.0.1", Port = client.Port } });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = false });
            var trackedDownloads = new TrackedDownloadService(Path.Combine(_root, "config"));
            var tracker = new DownloadPlatformTracker(Path.Combine(_root, "config"));
            tracker.Track("magnet:?xt=urn:btih:abc&dn=Halo+3+(USA)", "xbox360", 5);
            var mine = trackedDownloads.TrackDownload(new DownloadStatus { Id = hash, Name = "Halo 3 (USA)" }, 1, "tr", tracker);
            mine.GameId = null;
            mine.GameSetByHand = true;
            using var monitor = new DownloadMonitorService(config, trackedDownloads, null!, tracker, new ImportStatusService(), NullLogger<DownloadMonitorService>.Instance);

            await monitor.RefreshClientDownloadsAsync(config.LoadDownloadClients().Single(), new HashSet<(int, string)>());

            Assert.That(mine.Progress, Is.GreaterThan(0), "the monitor never polled");
            Assert.That(mine.GameId, Is.Null);
        }

        // Answers every Transmission call with the one torrent it holds
        private sealed class OneTorrent : IDisposable
        {
            private readonly HttpListener _listener;
            public int Port { get; }

            public OneTorrent(string hash, string name)
            {
                // A port probed free can be taken before the listener binds it: then another one
                for (var attempt = 1; ; attempt++)
                {
                    var probe = new TcpListener(IPAddress.Loopback, 0);
                    probe.Start();
                    Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                    probe.Stop();
                    var listener = new HttpListener();
                    listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                    try
                    {
                        listener.Start();
                        _listener = listener;
                        break;
                    }
                    catch (HttpListenerException) when (attempt < 5)
                    {
                        listener.Close();
                    }
                }
                var body = Encoding.UTF8.GetBytes("{\"arguments\":{\"torrents\":[{\"hashString\":\"" + hash + "\",\"name\":\"" + name
                    + "\",\"totalSize\":1,\"percentDone\":0.5,\"status\":4,\"downloadDir\":\"/downloads\"}]},\"result\":\"success\"}");
                _ = Task.Run(async () =>
                {
                    while (_listener.IsListening)
                    {
                        HttpListenerContext ctx;
                        try { ctx = await _listener.GetContextAsync(); }
                        catch { return; }
                        await ctx.Response.OutputStream.WriteAsync(body);
                        ctx.Response.Close();
                    }
                });
            }

            public void Dispose() => _listener.Close();
        }

        // The queue's platform editor sends the platform only: the game and subfolder mapped before stay, on the download
        // and for the next lookup by its name
        [Test]
        public async Task PlatformEdit_KeepsTheMappedGameAndSubfolder()
        {
            var trackedDownloads = new TrackedDownloadService(Path.Combine(_root, "config"));
            var (controller, tracker) = await SetUpAsync(new Game { Id = 7, Title = "Halo 3", PlatformId = 31 }, trackedDownloads);
            var tracked = trackedDownloads.TrackDownload(new DownloadStatus { Id = "abc", Name = "Halo 3 Update" }, 1, "client");
            controller.MapPlatform(new MapPlatformRequest
            {
                DownloadName = "Halo 3 Update", ClientId = 1, DownloadId = "abc", PlatformFolder = "xbox360", GameId = 7, ImportSubfolder = "Patches"
            });

            controller.MapPlatform(new MapPlatformRequest { DownloadName = "Halo 3 Update", ClientId = 1, DownloadId = "abc", PlatformFolder = "windows" });

            Assert.That((tracked.PlatformFolder, tracked.GameId, tracked.ImportSubfolder), Is.EqualTo(("windows", (int?)7, "Patches")));
            Assert.That(tracker.LookupByName("Halo 3 Update"), Is.EqualTo("windows"));
            Assert.That(tracker.LookupGameId("Halo 3 Update"), Is.EqualTo(7));
            Assert.That(tracker.LookupImportSubfolder("Halo 3 Update"), Is.EqualTo("Patches"));
        }

        private async Task<(DownloadClientController Controller, DownloadPlatformTracker Tracker)> SetUpAsync(Game game, TrackedDownloadService? trackedDownloads = null)
        {
            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(game);
                await ctx.SaveChangesAsync();
            }
            var tracker = new DownloadPlatformTracker(Path.Combine(_root, "config"));
            // No download client is configured: the request is tracked, then refused.
            var controller = new DownloadClientController(new ConfigurationService(_root), null!, tracker, trackedDownloads!, null!, null!, null!,
                new SqliteGameRepository(new DbFactory(dbOptions)));
            return (controller, tracker);
        }

        [Test]
        public async Task BlacklistFromHistory_EndsThePendingGrab()
        {
            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            var factory = new DbFactory(dbOptions);
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.DownloadHistory.Add(new DownloadHistoryEntry { Id = 3, DownloadId = "abc", Title = "Halo 3 (USA)", State = DownloadHistoryState.ImportFailed });
                await ctx.SaveChangesAsync();
            }
            var tracker = new DownloadPlatformTracker(Path.Combine(_root, "config"));
            tracker.Track("magnet:?xt=urn:btih:abc&dn=Halo+3+(USA)", "xbox360", 1);
            var controller = new DownloadHistoryController(new DownloadHistoryRepository(factory), new DownloadBlacklistRepository(factory), null!, tracker);

            await controller.BlacklistFromHistory(3);

            Assert.That(tracker.HasPendingGrab(1), Is.False);
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
