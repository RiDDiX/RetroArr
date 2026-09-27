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
using RetroArr.Core.Jackett;
using RetroArr.Core.Search;

namespace RetroArr.Core.Test.Search
{
    [TestFixture]
    public class MonitoredGameSearchServiceTest
    {
        // Answers every request with one Jackett result for the given title and categories.
        private sealed class FakeJackett : IDisposable
        {
            private readonly HttpListener _listener = new();
            private readonly string _body;

            public int Port { get; }

            public FakeJackett(string title, params int[] categories)
            {
                _body = JsonSerializer.Serialize(new
                {
                    Results = new[]
                    {
                        new
                        {
                            Title = title,
                            Guid = "https://tracker.invalid/t/1",
                            Link = "",
                            MagnetUri = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567",
                            Tracker = "tracker",
                            Size = 0,
                            PublishDate = DateTime.UtcNow,
                            Category = categories,
                            Seeders = 50,
                            Peers = 60
                        }
                    }
                });
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
            using var jackett = new FakeJackett("Halo 3 (USA) P2P", 1050);

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
            using var jackett = new FakeJackett("Halo 3 X360 P2P", 1050);

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
    }
}
