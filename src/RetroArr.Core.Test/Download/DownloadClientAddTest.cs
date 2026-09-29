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
            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Halo 3", PlatformId = 31 });
                await ctx.SaveChangesAsync();
            }
            var tracker = new DownloadPlatformTracker(Path.Combine(_root, "config"));
            // No download client is configured: the request is tracked, then refused.
            var controller = new DownloadClientController(new ConfigurationService(_root), null!, tracker, null!, null!, null!, null!,
                new SqliteGameRepository(new DbFactory(dbOptions)));

            await controller.AddTorrent(new AddTorrentRequest { Url = "magnet:?xt=urn:btih:abc", Protocol = "torrent", GameId = 1 });

            var tracked = tracker.GetAll().Single();
            Assert.That(tracked.GameId, Is.EqualTo(1));
            Assert.That(tracked.PlatformFolder, Is.EqualTo("xbox360"));
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
