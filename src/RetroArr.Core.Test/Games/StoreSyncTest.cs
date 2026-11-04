using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Settings;
using RetroArr.Api.V3.Steam;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // Steam/GOG account syncs and the disconnect that removes what they added
    [TestFixture]
    public class StoreSyncTest
    {
        private const int Pc = PlatformDefinitions.PcPlatformId;
        private const int Ps2 = 21;
        private const int Steam = PlatformDefinitions.SteamPlatformId;
        private const int Gog = PlatformDefinitions.GogPlatformId;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            private readonly Task _gate;
            public DbFactory(DbContextOptions<RetroArrDbContext> options, Task? gate = null)
            {
                _options = options;
                _gate = gate ?? Task.CompletedTask;
            }
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
            public async Task<RetroArrDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default)
            {
                await _gate;
                return CreateDbContext();
            }
        }

        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(
                new Platform { Id = Pc, Name = "PC", Slug = "pc", FolderName = "windows" },
                new Platform { Id = Ps2, Name = "PlayStation 2", Slug = "ps2", FolderName = "ps2" },
                new Platform { Id = Steam, Name = "Steam", Slug = "steam", FolderName = "steam" },
                new Platform { Id = Gog, Name = "GOG Galaxy", Slug = "gog", FolderName = "gog" });
            ctx.SaveChanges();
        }

        [TearDown]
        public void TearDown() => _connection.Dispose();

        private void Add(params Game[] games)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.Games.AddRange(games);
            ctx.SaveChanges();
        }

        [Test]
        public void PlatformIds_AreTheStorePlatforms()
        {
            Assert.That(PlatformDefinitions.PlatformDictionary[Steam].Slug, Is.EqualTo("steam"));
            Assert.That(PlatformDefinitions.PlatformDictionary[Gog].Slug, Is.EqualTo("gog"));
            Assert.That(PlatformDefinitions.PlatformDictionary[Pc].Slug, Is.EqualTo("pc"));
        }

        [Test]
        public void SteamMatcher_LinksAnAppIdAnywhere_AndATitleOnlyOnTheSteamOrPcPlatform()
        {
            var ps2Doom = new GameListDto { Id = 1, Title = "Doom", PlatformId = Ps2 };
            var portal = new GameListDto { Id = 2, Title = "Portal", PlatformId = Steam };
            var halfLife = new GameListDto { Id = 3, Title = "Half-Life", PlatformId = Pc, SteamId = 70 };
            var pcPortal2 = new GameListDto { Id = 4, Title = "Portal 2", PlatformId = Pc };
            var find = SettingsController.SteamMatcher(new[] { ps2Doom, portal, halfLife, pcPortal2 });

            Assert.That(find(2280, "DOOM"), Is.Null, "a same-named game on another platform is another game");
            Assert.That(find(400, "portal"), Is.SameAs(portal));
            Assert.That(find(620, "Portal 2"), Is.SameAs(pcPortal2), "a local PC game is the owned Steam app");
            Assert.That(find(70, "Half-Life"), Is.SameAs(halfLife));
            Assert.That(find(70, null), Is.SameAs(halfLife));
            Assert.That(find(1, null), Is.Null);
        }

        [Test]
        public void GogMatcher_LinksAGogIdAnywhere_AndATitleOnlyOnTheGogPlatform()
        {
            const int Snes = 41;
            var snesDoom = new Game { Id = 1, Title = "Doom", PlatformId = Snes };
            var gogWitcher = new Game { Id = 2, Title = "The Witcher", PlatformId = Gog };
            var gogQuake = new Game { Id = 3, Title = "Quake", PlatformId = Gog };
            var gogQuakeAgain = new Game { Id = 4, Title = "QUAKE", PlatformId = Gog, Region = "EU" };
            var ps2Witcher = new Game { Id = 5, Title = "The Witcher", PlatformId = Ps2, GogId = "1207658924" };
            var find = SettingsController.GogMatcher(new[] { snesDoom, gogWitcher, gogQuake, gogQuakeAgain, ps2Witcher });

            Assert.That(find("1207658753", "DOOM"), Is.Null, "a same-named game on another platform is another game");
            Assert.That(find("1207659189", "quake"), Is.SameAs(gogQuake), "the first row by id");
            Assert.That(find("1207658924", "The Witcher"), Is.SameAs(ps2Witcher), "the GOG id wins over a title, as GogController skips a known id");
            Assert.That(find("1", null), Is.Null);
        }

        [Test]
        public async Task GetSteamRowsAsync_ReadsTheLinkedAndTheSteamAndPcPlatformRows()
        {
            Add(new Game { Id = 1, Title = "Doom", PlatformId = Ps2, SteamId = 2280 },
                new Game { Id = 2, Title = "Portal", PlatformId = Steam, ProtonDbTier = "gold" },
                new Game { Id = 3, Title = "Chrono Trigger", PlatformId = Ps2 },
                new Game { Id = 4, Title = "Portal 2", PlatformId = Pc });

            var rows = await new SqliteGameRepository(new DbFactory(_db)).GetSteamRowsAsync();

            Assert.That(rows.Select(r => (r.Id, r.Title, r.PlatformId, r.SteamId, r.ProtonDbTier)), Is.EqualTo(new[]
            {
                (1, "Doom", Ps2, (int?)2280, (string?)null),
                (2, "Portal", Steam, (int?)null, (string?)"gold"),
                (4, "Portal 2", Pc, (int?)null, (string?)null)
            }));
        }

        [Test]
        public async Task Disconnect_RemovesOnlyTheStorePlatformsGames()
        {
            // The old Steam sync linked a same-named game on any platform by title, so a PS2 "Doom" carried an AppID
            Add(new Game { Id = 1, Title = "Doom", PlatformId = Ps2, SteamId = 2280, GogId = "1207658753" },
                new Game { Id = 2, Title = "Doom", PlatformId = Steam, SteamId = 2280 },
                new Game { Id = 3, Title = "Portal", PlatformId = Steam },
                new Game { Id = 4, Title = "Doom", PlatformId = Gog, GogId = "1207658753" },
                new Game { Id = 5, Title = "Portal 2", PlatformId = Pc, SteamId = 620 });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.GameFiles.Add(new GameFile { GameId = 2, RelativePath = "doom.exe" });
                ctx.SaveChanges();
            }
            var repo = new SqliteGameRepository(new DbFactory(_db));

            Assert.That(await repo.DeleteSteamGamesAsync(), Is.EqualTo(1));
            Assert.That(await repo.DeleteGogGamesAsync(), Is.EqualTo(1));

            using var check = new RetroArrDbContext(_db);
            Assert.That(check.Games.Select(g => g.Id).OrderBy(id => id), Is.EqualTo(new[] { 1, 3, 5 }));
            Assert.That(check.GameFiles.Count(), Is.EqualTo(0));
        }

        [Test]
        public async Task ProtonDbRefresh_ASecondClickWhileTheFirstLoads_IsRefused()
        {
            var gate = new TaskCompletionSource();
            var repo = new SqliteGameRepository(new DbFactory(_db, gate.Task));

            var first = new ProtonDbController(repo).RefreshAll();
            var second = new ProtonDbController(repo).RefreshAll();
            try
            {
                Assert.That(second.IsCompleted, Is.True, "the second run must not wait for the first one's load");
                Assert.That(await second, Is.InstanceOf<ConflictObjectResult>());
            }
            finally
            {
                gate.SetResult();
                await Task.WhenAll(first, second);
            }

            // No game has an AppID, so the first run ends at once and frees the flag
            Assert.That(await first, Is.InstanceOf<OkObjectResult>());
            var again = await new ProtonDbController(new SqliteGameRepository(new DbFactory(_db))).RefreshAll();
            Assert.That(again, Is.InstanceOf<OkObjectResult>());
        }
    }
}
