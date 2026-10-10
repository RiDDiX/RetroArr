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
using RetroArr.Api.V3.Discovery;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.Games
{
    // "Scan now" beside the Wine prefix scans the prefix as an external library, not as a library folder
    [TestFixture]
    public class WinePrefixScanTest
    {
        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // IGDB that finds nothing
        private sealed class NoMatches : HttpMessageHandler, IGameMetadataServiceFactory
        {
            public GameMetadataService CreateService()
            {
                var igdb = new IgdbClient("id", "secret");
                typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(this, false));
                return new GameMetadataService(igdb, new SteamClient());
            }

            public void RefreshConfiguration() { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri!.Host == "api.igdb.com" ? "[]" : "{\"access_token\":\"t\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                });
        }

        private string _root = null!;
        private string _lib = null!;
        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_wineprefix_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(PlatformDefinitions.AllPlatforms.Select(p => p.Clone()));
            ctx.SaveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private (MediaController Controller, MediaScannerService Scanner) Controller(string winePrefix, IGameRepository? repo = null)
        {
            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib, WinePrefixPath = winePrefix });
            config.SaveIgdbSettings(new IgdbSettings { ClientId = "id", ClientSecret = "secret" });
            var scanner = new MediaScannerService(config, new NoMatches(), repo ?? new SqliteGameRepository(new DbFactory(_db)), new TitleCleanerService());
            return (new MediaController(config, scanner, null!, null!, null!), scanner);
        }

        // The scanner skips a second scan, so a scan or discovery request says so instead of "scan started"
        [Test]
        public async Task ScanRequest_WhileAScanRuns_IsRefused()
        {
            using var reading = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            // The running scan waits in its first read of the library
            hook.Before = (method, _) =>
            {
                if (method.Name != nameof(IGameRepository.GetAllLightAsync)) return;
                reading.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            };
            var (controller, scanner) = Controller(string.Empty, repo);
            var running = Task.Run(() => scanner.ScanAsync());
            Assert.That(reading.Wait(TimeSpan.FromSeconds(30)), Is.True);

            var scan = controller.TriggerScan(new MediaController.ScanRequest { FolderPath = _lib, Platform = "default" });
            var discovery = new DiscoveryController(null!, scanner, null!, null!).TriggerDiscoveryScan(null);

            release.Set();
            await running;
            foreach (var result in new[] { scan, discovery })
            {
                Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
                Assert.That(((ConflictObjectResult)result).Value?.ToString(), Does.Contain(MediaScannerService.ScanRunningMessage));
            }
        }

        [Test]
        public void ExternalScan_WithoutAnExistingPrefix_IsRefused()
        {
            var external = new MediaController.ScanRequest { External = true };

            Assert.That(Controller(string.Empty).Controller.TriggerScan(external), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(Controller(Path.Combine(_root, "bottle")).Controller.TriggerScan(external), Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task ExternalScan_AddsTheGamesInThePrefixAsExternal()
        {
            var prefix = Directory.CreateDirectory(Path.Combine(_root, "bottle")).FullName;
            var install = Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "Games", "Hollow Knight")).FullName;
            File.WriteAllText(Path.Combine(install, "hollow_knight.exe"), "exe");
            // The library is not part of it
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(_lib, "gba")).FullName, "Advance Wars.gba"), "rom");
            var (controller, scanner) = Controller(prefix);
            var finished = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);
            scanner.OnScanFinished += added => finished.TrySetResult(added);

            // A folder sent along (what the button sent before) does not turn it into a library scan
            var result = controller.TriggerScan(new MediaController.ScanRequest { FolderPath = prefix, Platform = "default", External = true });
            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            await finished.Task.WaitAsync(TimeSpan.FromSeconds(60));

            using var ctx = new RetroArrDbContext(_db);
            var games = ctx.Games.AsNoTracking().ToList();
            Assert.That(games.Select(g => (g.Path, g.IsExternal)), Is.EqualTo(new[] { (install, true) }));
            // No sweep follows a scan of the prefix alone
            Assert.That(games.Single().Status, Is.EqualTo(GameStatus.Downloaded));
        }
    }
}
