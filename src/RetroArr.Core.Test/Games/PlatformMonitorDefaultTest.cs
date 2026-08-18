using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // Covers the monitoring feature backend: the GameListDto projection now carries
    // Monitored, and PlatformService stores a per-platform "monitor new items" default
    // with a caller-supplied fallback.
    [TestFixture]
    public class PlatformMonitorDefaultTest
    {
        private DbContextOptions<RetroArrDbContext> _dbOptions = null!;

        [SetUp]
        public void Setup()
        {
            _dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
                .Options;
        }

        [Test]
        public async Task GetAllPaged_ProjectsMonitoredFlagPerGame()
        {
            using (var ctx = new RetroArrDbContext(_dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Monitored A", PlatformId = 40, Monitored = true });
                ctx.Games.Add(new Game { Id = 2, Title = "Unmonitored B", PlatformId = 40, Monitored = false });
                ctx.Games.Add(new Game { Id = 3, Title = "Monitored C", PlatformId = 41, Monitored = true });
                await ctx.SaveChangesAsync();
            }

            var repo = new SqliteGameRepository(new TestDbContextFactory(_dbOptions));
            var page = await repo.GetAllPagedAsync(1, 50);

            var byId = page.Items.ToDictionary(i => i.Id, i => i.Monitored);
            Assert.Multiple(() =>
            {
                Assert.That(byId[1], Is.True, "game 1 should project Monitored=true");
                Assert.That(byId[2], Is.False, "game 2 should project Monitored=false");
                Assert.That(byId[3], Is.True, "game 3 should project Monitored=true");
            });
        }

        [Test]
        public void MonitorDefault_UnsetPlatform_ReturnsFallback()
        {
            // A platform id the feature never touches must fall back to the caller default.
            const int untouched = 999090;
            Assert.That(PlatformService.GetMonitorNewItemsDefault(untouched, false), Is.False);
            Assert.That(PlatformService.GetMonitorNewItemsDefault(untouched, true), Is.True);
        }

        [Test]
        public void MonitorDefault_SetThenGet_Roundtrips()
        {
            const int pid = 999091;
            PlatformService.SetMonitorNewItemsDefault(pid, true);
            Assert.That(PlatformService.GetMonitorNewItemsDefault(pid, false), Is.True,
                "stored default overrides the fallback");
            Assert.That(PlatformService.GetAllMonitorNewItemsDefaults().ContainsKey(pid), Is.True);

            PlatformService.SetMonitorNewItemsDefault(pid, false);
            Assert.That(PlatformService.GetMonitorNewItemsDefault(pid, true), Is.False,
                "stored false wins over a true fallback");
        }

        private sealed class TestDbContextFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public TestDbContextFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
