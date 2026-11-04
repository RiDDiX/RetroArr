using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.Games
{
    // Settings > Media "Rescan metadata": a background run over the library that takes hours to days on a big one
    [TestFixture]
    public class MetadataRescanTest
    {
        private const int Gba = 52;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // IGDB that knows Advance Wars as id 7, and runs During on its first search
        private sealed class FakeIgdb : HttpMessageHandler, IGameMetadataServiceFactory
        {
            public Action? During;

            public GameMetadataService CreateService()
            {
                var igdb = new IgdbClient("id", "secret");
                typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(this, false));
                return new GameMetadataService(igdb, new SteamClient());
            }

            public void RefreshConfiguration() { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                string body;
                if (request.RequestUri!.Host == "api.igdb.com")
                {
                    Interlocked.Exchange(ref During, null)?.Invoke();
                    body = "[{\"id\":7,\"name\":\"Advance Wars\",\"summary\":\"Turn-based tactics\",\"cover\":{\"image_id\":\"cover7\"}}]";
                }
                else body = "{\"access_token\":\"t\",\"expires_in\":3600}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }

        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private string _root = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.Add(new Platform { Id = Gba, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
            ctx.Games.Add(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Region = "", Path = "/library/gba/Advance Wars.gba",
                Status = GameStatus.Downloaded, Monitored = true });
            ctx.SaveChanges();

            _root = Path.Combine(Path.GetTempPath(), "retroarr_rescan_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _config = new ConfigurationService(_root);
            _config.SaveIgdbSettings(new IgdbSettings { ClientId = "id", ClientSecret = "secret" });
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private MediaController Controller(FakeIgdb igdb) =>
            new(_config, null!, new SqliteGameRepository(new DbFactory(_db)), igdb, new LocalMediaExportService(new HttpClient(igdb, false)));

        private static async Task Finished()
        {
            for (var waited = 0; MediaController.IsRescanningMetadata && waited < 10000; waited += 50) await Task.Delay(50);
            Assert.That(MediaController.IsRescanningMetadata, Is.False, "the rescan did not finish");
        }

        // The game was moved, flagged missing and unmonitored after the rescan read the library; its write sets only the metadata
        [Test]
        public async Task Rescan_KeepsWhatChangedOnTheGameDuringTheRun()
        {
            var movedTo = "/library/gba/Advance Wars (USA).gba";
            var since = new DateTime(2026, 11, 3, 12, 0, 0, DateTimeKind.Utc);
            var igdb = new FakeIgdb
            {
                During = () =>
                {
                    using var ctx = new RetroArrDbContext(_db);
                    var row = ctx.Games.Single(g => g.Id == 1);
                    (row.Path, row.Status, row.MissingSince, row.Monitored) = (movedTo, GameStatus.Missing, since, false);
                    ctx.SaveChanges();
                }
            };

            var result = Controller(igdb).TriggerMetadataRescan(new MediaController.MetadataRescanRequest());
            await Finished();

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_db);
            var game = check.Games.AsNoTracking().Single(g => g.Id == 1);
            Assert.That(igdb.During, Is.Null, "IGDB was not asked");
            Assert.That((game.IgdbId, game.Overview, game.MetadataSource), Is.EqualTo(((int?)7, "Turn-based tactics", "IGDB")));
            Assert.That((game.Path, game.Status, game.MissingSince, game.Monitored), Is.EqualTo((movedTo, GameStatus.Missing, (DateTime?)since, false)));
        }

        // The user matched or renamed the game in Metadata Review after the rescan read it: the match the rescan found
        // for the old title would stay for good, since later scans leave a confirmed game alone
        [TestCase("Advance Wars 2: Black Hole Rising", true, TestName = "Rescan_GameMatchedByTheUserDuringTheRun_KeepsTheirMatch")]
        [TestCase("Advance Wars", true, TestName = "Rescan_GameConfirmedByTheUserDuringTheRun_KeepsTheirMatch")]
        [TestCase("Advance Wars (USA)", false, TestName = "Rescan_GameRenamedDuringTheRun_KeepsItsNewTitle")]
        public async Task Rescan_KeepsWhatTheUserSetDuringTheRun(string title, bool confirmed)
        {
            var igdb = new FakeIgdb
            {
                During = () =>
                {
                    using var ctx = new RetroArrDbContext(_db);
                    var row = ctx.Games.Single(g => g.Id == 1);
                    (row.Title, row.IgdbId, row.Overview, row.MetadataConfirmedByUser) = (title, 99, "user pick", confirmed);
                    ctx.SaveChanges();
                }
            };

            Controller(igdb).TriggerMetadataRescan(new MediaController.MetadataRescanRequest());
            await Finished();

            using var check = new RetroArrDbContext(_db);
            var game = check.Games.AsNoTracking().Single(g => g.Id == 1);
            Assert.That(igdb.During, Is.Null, "IGDB was not asked");
            Assert.That((game.Title, game.IgdbId, game.Overview, game.MetadataConfirmedByUser), Is.EqualTo((title, (int?)99, "user pick", confirmed)));
        }

        // A second click while the first run starts gets the conflict instead of a second run over the library
        [Test]
        public async Task Rescan_SecondStart_IsRefusedWhileOneRuns()
        {
            var controller = Controller(new FakeIgdb());

            var first = controller.TriggerMetadataRescan(new MediaController.MetadataRescanRequest());
            var second = controller.TriggerMetadataRescan(new MediaController.MetadataRescanRequest());
            await Finished();

            Assert.That(first, Is.InstanceOf<OkObjectResult>());
            Assert.That(second, Is.InstanceOf<ConflictObjectResult>());
        }
    }
}
