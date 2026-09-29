using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Games;
using RetroArr.Core.Indexers;
using RetroArr.Core.Jackett;
using RetroArr.Core.Search;

namespace RetroArr.Core.Test.Search
{
    [TestFixture]
    public class MonitoredGameSearchServiceTest
    {
        // Answers every request with the same JSON body.
        private sealed class FakeServer : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly string _body;

            public int Port { get; }

            public FakeServer(string body)
            {
                _body = body;
                Port = FreePort();
                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = Task.Run(Serve);
            }

            private async Task Serve()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }
                    var bytes = Encoding.UTF8.GetBytes(_body);
                    ctx.Response.ContentType = "application/json";
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            public void Dispose() => _listener.Close();
        }

        // One Jackett result per (title, category).
        private static FakeServer FakeJackett(params (string Title, int Category)[] releases) =>
            new(JsonSerializer.Serialize(new
            {
                Results = releases.Select(r => new
                {
                    Title = r.Title,
                    Guid = "https://tracker.invalid/t/1",
                    Link = "",
                    MagnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
                    Tracker = "tracker",
                    Size = 0,
                    PublishDate = DateTime.UtcNow,
                    Category = new[] { r.Category },
                    Seeders = 50,
                    Peers = 60
                })
            }));

        private static FakeServer FakeHydra(string title) =>
            new(JsonSerializer.Serialize(new
            {
                downloads = new[] { new { title, uris = new[] { "magnet:?xt=urn:btih:fedcba9876543210fedcba9876543210fedcba98" }, fileSize = "7 GB" } }
            }));

        private static int FreePort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        private string _root = null!;
        private DbContextOptions<RetroArrDbContext> _dbOptions = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_monsearch_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using var ctx = new RetroArrDbContext(_dbOptions);
            ctx.Platforms.Add(new Platform { Id = 31, Name = "Xbox 360", Slug = "xbox360", FolderName = "xbox360" });
            ctx.Games.Add(new Game { Id = 1, Title = "Halo 3", PlatformId = 31, Monitored = true });
            ctx.SaveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        private async Task<MonitorSearchResult> Search(ConfigurationService config, int jackettPort, bool autoDispatch)
        {
            config.SaveJackettSettings(new JackettSettings { Url = $"http://127.0.0.1:{jackettPort}", ApiKey = "key", Enabled = true });
            using var db = new RetroArrDbContext(_dbOptions);
            var service = new MonitoredGameSearchService(db, config, new ReleaseScorer(),
                new DownloadPlatformTracker(Path.Combine(_root, "config")));
            return await service.SearchAndMaybeDispatchAsync(1, autoDispatch);
        }

        [Test]
        public async Task AutomaticSearch_KeepsJackettCategoriesForPlatformDetection()
        {
            // The title alone names no platform; only the Xbox 360 category does.
            using var jackett = FakeJackett(("Halo 3 (USA) P2P", 1050));

            var result = await Search(new ConfigurationService(_root), jackett.Port, autoDispatch: false);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.Scored, Has.Count.EqualTo(1));
            var release = result.Scored[0].Release;
            Assert.That(release.Categories.Select(c => c.Id), Is.EqualTo(new[] { 1050 }));
            Assert.That(release.PlatformFolder, Is.EqualTo("xbox360"));
        }

        [Test]
        public async Task AutoGrab_ToDelugeWithSsl_TalksTls()
        {
            using var jackett = FakeJackett(("Halo 3 X360 P2P", 1050));

            // Stands in for deluge-web behind TLS: records the first byte the client sends.
            // A TLS handshake starts with 0x16, a plain HTTP request with 'P' of POST.
            var deluge = new TcpListener(IPAddress.Loopback, 0);
            deluge.Start();
            var delugePort = ((IPEndPoint)deluge.LocalEndpoint).Port;
            var firstByte = Task.Run(async () =>
            {
                using var conn = await deluge.AcceptTcpClientAsync();
                deluge.Stop(); // refuse the client's follow-up calls instead of letting them time out
                var buf = new byte[1];
                var n = await conn.GetStream().ReadAsync(buf);
                return n == 1 ? buf[0] : -1;
            });

            try
            {
                var config = new ConfigurationService(_root);
                var monitor = MonitorSettings.CreateDefault();
                monitor.AutoDownloadThreshold = 0;
                monitor.RequireTrustedSourceForAuto = false;
                config.SaveMonitorSettings(monitor);
                config.SaveDownloadClients(new List<DownloadClient>
                {
                    new DownloadClient
                    {
                        Id = 1, Name = "deluge", Implementation = "Deluge", Host = "127.0.0.1", Port = delugePort,
                        Password = "pw", UseSsl = true, Enable = true
                    }
                });

                var result = await Search(config, jackett.Port, autoDispatch: true);

                Assert.That(result.Scored.Select(s => s.Decision), Has.Member(ReleaseDecision.AutoDownload));
                Assert.That(await Task.WhenAny(firstByte, Task.Delay(TimeSpan.FromSeconds(10))), Is.SameAs(firstByte),
                    "auto-grab never connected to the Deluge port");
                Assert.That(await firstByte, Is.EqualTo(0x16), "Deluge was called without TLS");
            }
            finally
            {
                deluge.Stop();
            }
        }

        [Test]
        public async Task AutoGrab_NeverDispatchesAReject()
        {
            using var jackett = FakeJackett(("Halo 3 (Europe) P2P", 1030));
            var deluge = new TcpListener(IPAddress.Loopback, 0);
            deluge.Start();
            try
            {
                var config = new ConfigurationService(_root);
                var monitor = MonitorSettings.CreateDefault();
                monitor.AutoDownloadThreshold = 0;
                monitor.RequireTrustedSourceForAuto = false;
                config.SaveMonitorSettings(monitor);
                config.SaveDownloadClients(new List<DownloadClient>
                {
                    new DownloadClient
                    {
                        Id = 1, Name = "deluge", Implementation = "Deluge", Host = "127.0.0.1",
                        Port = ((IPEndPoint)deluge.LocalEndpoint).Port, Password = "pw", Enable = true
                    }
                });

                var result = await Search(config, jackett.Port, autoDispatch: true);

                Assert.That(result.Scored.Single().Decision, Is.EqualTo(ReleaseDecision.Reject));
                Assert.That(result.AutoQueued, Is.False);
                Assert.That(deluge.Pending(), Is.False, "a rejected release reached the download client");
            }
            finally
            {
                deluge.Stop();
            }
        }

        [Test]
        public async Task Search_KeepsRejectsLastWithTheirReason()
        {
            // The Wii category makes the first release a platform mismatch for the Xbox 360 game.
            // The second has no trusted group, and the penalty clamps it to a Hide with score 0, the score a Reject gets.
            using var jackett = FakeJackett(("Halo 3 (Europe) P2P", 1030), ("Halo 3 (USA)", 1050));
            var config = new ConfigurationService(_root);
            var monitor = MonitorSettings.CreateDefault();
            monitor.UnknownUploaderPenalty = 200;
            config.SaveMonitorSettings(monitor);

            var result = await Search(config, jackett.Port, autoDispatch: false);

            Assert.That(result.Scored, Has.Count.EqualTo(2));
            Assert.That(result.RejectedCount, Is.EqualTo(1));
            Assert.That(result.Scored[0].Release.Title, Is.EqualTo("Halo 3 (USA)"));
            Assert.That(result.Scored[0].Decision, Is.EqualTo(ReleaseDecision.Hide));
            Assert.That(result.Scored[0].Score, Is.EqualTo(0));
            Assert.That(result.Scored[1].Decision, Is.EqualTo(ReleaseDecision.Reject));
            Assert.That(result.Scored[1].Reason, Does.StartWith("platform mismatch"));
            Assert.That(result.Queries, Is.EqualTo(new[] { "Halo 3" }));
            Assert.That(result.ProviderErrors, Is.Empty);
        }

        [Test]
        public async Task JackettFailure_IsReportedAndHydraResultsAreKept()
        {
            using var hydra = FakeHydra("Halo 3 (USA)");
            var config = new ConfigurationService(_root);
            config.SaveHydraIndexers(new List<HydraConfiguration>
            {
                new HydraConfiguration { Name = "hydra", Url = $"http://127.0.0.1:{hydra.Port}/", Enabled = true }
            });

            // Nothing listens on this port, so Jackett fails.
            var result = await Search(config, FreePort(), autoDispatch: false);

            Assert.That(result.Error, Is.Null);
            Assert.That(result.ProviderErrors, Has.Count.EqualTo(1));
            Assert.That(result.ProviderErrors[0], Does.StartWith("Jackett search failed: "));
            Assert.That(result.ProviderErrors[0], Does.Contain("refused").IgnoreCase);
            // Hydra sources carry no seeder count, so the kept row is a Reject.
            var hydraRow = result.Scored.Single();
            Assert.That(hydraRow.Release.Title, Is.EqualTo("Halo 3 (USA)"));
            Assert.That(hydraRow.Decision, Is.EqualTo(ReleaseDecision.Reject));
            Assert.That(hydraRow.Reason, Does.StartWith("insufficient seeders"));
        }

        [Test]
        public async Task HydraFailure_IsReportedAndJackettResultsAreKept()
        {
            using var jackett = FakeJackett(("Halo 3 (USA) P2P", 1050));
            var config = new ConfigurationService(_root);
            config.SaveHydraIndexers(new List<HydraConfiguration>
            {
                new HydraConfiguration { Name = "hydra", Url = $"http://127.0.0.1:{FreePort()}/", Enabled = true }
            });

            var result = await Search(config, jackett.Port, autoDispatch: false);

            Assert.That(result.ProviderErrors, Has.Count.EqualTo(1));
            Assert.That(result.ProviderErrors[0], Does.StartWith("hydra: "));
            Assert.That(result.ProviderErrors[0], Does.Contain("refused").IgnoreCase);
            Assert.That(result.Scored.Select(s => s.Release.Title), Is.EqualTo(new[] { "Halo 3 (USA) P2P" }));
        }
    }
}
