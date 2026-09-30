using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.DownloadClients;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
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

        private async Task<(DownloadClientController Controller, DownloadPlatformTracker Tracker)> SetUpAsync(Game game)
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
            var controller = new DownloadClientController(new ConfigurationService(_root), null!, tracker, null!, null!, null!, null!,
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
