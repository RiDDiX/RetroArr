using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Download;
using RetroArr.Core.Download.TrackedDownloads;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class TransmissionClientTest
    {
        // Records every RPC call; torrent-get answers with Torrents, torrent-add with AddedKey
        private sealed class FakeTransmission : IDisposable
        {
            private readonly HttpListener _listener = new();

            public int Port { get; }
            public string AddedKey { get; set; } = "torrent-added";
            public string Torrents { get; set; } = "[]";
            public List<string> Requests { get; } = new();

            public FakeTransmission()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();
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

                    using var doc = JsonDocument.Parse(await new StreamReader(ctx.Request.InputStream).ReadToEndAsync());
                    var method = doc.RootElement.GetProperty("method").GetString();
                    lock (Requests) Requests.Add($"{method} {doc.RootElement.GetProperty("arguments").GetRawText()}");

                    var arguments = method switch
                    {
                        "torrent-add" => $"{{\"{AddedKey}\":{{\"id\":7,\"name\":\"Some.Game-GRP\",\"hashString\":\"abc123\"}}}}",
                        "torrent-get" => $"{{\"torrents\":{Torrents}}}",
                        _ => "{}"
                    };
                    var bytes = Encoding.UTF8.GetBytes($"{{\"arguments\":{arguments},\"result\":\"success\"}}");
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            public void Dispose() => _listener.Close();
        }

        private const string Magnet = "magnet:?xt=urn:btih:abc123";

        [TestCase("[]", "[\"RetroArr\"]")]
        [TestCase("[{\"labels\":[\"tv\"]}]", "[\"tv\",\"RetroArr\"]")]
        [TestCase("[{\"labels\":0}]", "[\"RetroArr\"]")]
        public async Task Add_WithCategory_LabelsTheTorrentWhenTheAddDidNot(string torrents, string expectedLabels)
        {
            using var tr = new FakeTransmission { Torrents = torrents };

            Assert.That(await new TransmissionClient("127.0.0.1", tr.Port, "u", "p").AddTorrentAsync(Magnet, "RetroArr"), Is.True);
            Assert.That(tr.Requests[0], Is.EqualTo($"torrent-add {{\"filename\":\"{Magnet}\",\"labels\":[\"RetroArr\"]}}"));
            Assert.That(tr.Requests, Does.Contain($"torrent-set {{\"ids\":[\"abc123\"],\"labels\":{expectedLabels}}}"));
        }

        // The torrent was already there and may belong to another app (Deluge leaves it alone too). Without
        // the label it never shows up in RetroArr, so the log says so.
        [Test]
        public async Task Add_Duplicate_IsNotLabelled()
        {
            using var tr = new FakeTransmission { AddedKey = "torrent-duplicate", Torrents = "[{\"labels\":[]}]" };
            var added = false;

            var logs = await Logs(async () => added = await new TransmissionClient("127.0.0.1", tr.Port, "u", "p").AddTorrentAsync(Magnet, "RetroArr"));

            Assert.That(added, Is.True);
            Assert.That(tr.Requests, Is.EqualTo(new[] { $"torrent-add {{\"filename\":\"{Magnet}\",\"labels\":[\"RetroArr\"]}}" }));
            Assert.That(logs, Has.Some.StartsWith("Warn|[Transmission] 'Some.Game-GRP' is already in Transmission and was not labelled 'RetroArr'"));
        }

        private static async Task<IList<string>> Logs(Func<Task> action)
        {
            var target = new NLog.Targets.MemoryTarget { Layout = "${level}|${message}" };
            var previous = NLog.LogManager.Configuration;
            var config = new NLog.Config.LoggingConfiguration();
            config.AddRuleForAllLevels(target);
            NLog.LogManager.Configuration = config;
            try
            {
                await action();
                return target.Logs.ToList();
            }
            finally
            {
                NLog.LogManager.Configuration = previous;
            }
        }

        [Test]
        public async Task Add_LabelAlreadyApplied_SetsNothing()
        {
            using var tr = new FakeTransmission { Torrents = "[{\"labels\":[\"retroarr\"]}]" };

            Assert.That(await new TransmissionClient("127.0.0.1", tr.Port, "u", "p").AddTorrentAsync(Magnet, "RetroArr"), Is.True);
            Assert.That(tr.Requests, Has.None.StartsWith("torrent-set"));
        }

        [TestCase("")]
        [TestCase("Retro,Arr")]
        public async Task Add_CategoryThatCantBeALabel_AddsWithoutOne(string category)
        {
            using var tr = new FakeTransmission();

            Assert.That(await new TransmissionClient("127.0.0.1", tr.Port, "u", "p").AddTorrentAsync(Magnet, category), Is.True);
            Assert.That(tr.Requests, Is.EqualTo(new[] { $"torrent-add {{\"filename\":\"{Magnet}\"}}" }));
        }

        [TestCase("[\"tv\",\"retroarr\"]", "RetroArr", "retroarr")]
        [TestCase("[\"tv\",\"retroarr\"]", null, "tv")]
        [TestCase("[\"tv\"]", "RetroArr", "tv")]
        [TestCase("[]", "RetroArr", null)]
        [TestCase("0", "RetroArr", null)]
        public async Task Downloads_CategoryComesFromTheLabels(string labels, string? category, string? expected)
        {
            using var tr = new FakeTransmission
            {
                Torrents = "[{\"hashString\":\"0123456789abcdef0123456789abcdef01234567\",\"name\":\"Some.Game-GRP\",\"totalSize\":1,\"percentDone\":0.5,\"status\":4," +
                           $"\"downloadDir\":\"/downloads\",\"error\":0,\"errorString\":\"\",\"labels\":{labels}}}]"
            };

            var downloads = await new TransmissionClient("127.0.0.1", tr.Port, "u", "p", category).GetDownloadsAsync();

            Assert.That(downloads.Single().Category, Is.EqualTo(expected));
            Assert.That(tr.Requests.Single(), Does.Contain("\"labels\"]"));
        }

        // The numeric id is renumbered on every daemon restart, so a new torrent could take an imported one's id
        [Test]
        public async Task Downloads_IdIsTheHash()
        {
            using var tr = new FakeTransmission
            {
                Torrents = "[{\"id\":3,\"hashString\":\"0123456789abcdef0123456789abcdef01234567\",\"name\":\"Some.Game-GRP\"," +
                           "\"totalSize\":1,\"percentDone\":1,\"status\":6,\"downloadDir\":\"/downloads\"}]"
            };

            var downloads = await new TransmissionClient("127.0.0.1", tr.Port, "u", "p").GetDownloadsAsync();

            Assert.That(downloads.Single().Id, Is.EqualTo("0123456789abcdef0123456789abcdef01234567"));
            Assert.That(tr.Requests.Single(), Does.Contain("\"hashString\""));
        }

        // The monitor keeps only the configured category, so the client must report that label, not the first one
        [Test]
        public async Task Monitor_TracksATorrentWhoseCategoryIsNotItsFirstLabel()
        {
            const string hash = "0123456789abcdef0123456789abcdef01234567";
            using var tr = new FakeTransmission
            {
                Torrents = $"[{{\"hashString\":\"{hash}\",\"name\":\"Some.Game-GRP\",\"totalSize\":1,\"percentDone\":0.5," +
                           "\"status\":4,\"downloadDir\":\"/downloads\",\"labels\":[\"tv\",\"RetroArr\"]}]"
            };
            var root = Path.Combine(Path.GetTempPath(), "retroarr_trmonitor_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "config"));
            try
            {
                var config = new ConfigurationService(root);
                config.SaveDownloadClients(new List<DownloadClient>
                {
                    new() { Id = 2, Name = "tr", Implementation = "Transmission", Host = "127.0.0.1", Port = tr.Port, Category = "RetroArr" }
                });
                config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = false });
                var tracked = new TrackedDownloadService(root);
                using var monitor = new DownloadMonitorService(config, tracked, null!, new DownloadPlatformTracker(root),
                    new ImportStatusService(), NullLogger<DownloadMonitorService>.Instance);

                await monitor.StartAsync(CancellationToken.None);
                for (var i = 0; i < 100 && tracked.GetTrackedDownloads().Count == 0; i++) await Task.Delay(50);
                await monitor.StopAsync(CancellationToken.None);

                Assert.That(tracked.Find(2, hash)?.Category, Is.EqualTo("RetroArr"));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        [TestCase(0, 1.0, DownloadState.Completed)]
        [TestCase(5, 1.0, DownloadState.Completed)]
        [TestCase(6, 1.0, DownloadState.Completed)]
        [TestCase(0, 0.5, DownloadState.Paused)]
        [TestCase(4, 0.5, DownloadState.Downloading)]
        public async Task Downloads_FinishedTorrentIsCompletedEvenWhenStopped(int status, double percentDone, DownloadState expected)
        {
            using var tr = new FakeTransmission
            {
                Torrents = FormattableString.Invariant($"[{{\"hashString\":\"0123456789abcdef0123456789abcdef01234567\",\"name\":\"Some.Game-GRP\",\"totalSize\":1,\"percentDone\":{percentDone},\"status\":{status},\"downloadDir\":\"/downloads\"}}]")
            };

            var downloads = await new TransmissionClient("127.0.0.1", tr.Port, "u", "p").GetDownloadsAsync();

            Assert.That(downloads.Single().State, Is.EqualTo(expected));
        }

        [Test]
        public async Task Actions_SendNumericIdsAsNumbersAndHashesAsStrings()
        {
            using var tr = new FakeTransmission();
            var client = new TransmissionClient("127.0.0.1", tr.Port, "u", "p");
            const string hash = "0123456789abcdef0123456789abcdef01234567";

            Assert.That(await client.RemoveDownloadAsync("5", false), Is.True);
            Assert.That(await client.RemoveDownloadAsync("6", true), Is.True);
            Assert.That(await client.PauseDownloadAsync("7"), Is.True);
            Assert.That(await client.ResumeDownloadAsync("8"), Is.True);
            Assert.That(await client.RemoveDownloadAsync(hash, true), Is.True);

            Assert.That(tr.Requests, Is.EqualTo(new[]
            {
                "torrent-remove {\"ids\":[5],\"delete-local-data\":false}",
                "torrent-remove {\"ids\":[6],\"delete-local-data\":true}",
                "torrent-stop {\"ids\":[7]}",
                "torrent-start {\"ids\":[8]}",
                $"torrent-remove {{\"ids\":[\"{hash}\"],\"delete-local-data\":true}}",
            }));
        }

        [Test]
        public async Task DownloadPath_IsTheTorrentsOwnFolder()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            using var listener = new HttpListener();
            listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            listener.Start();
            _ = Task.Run(async () =>
            {
                while (listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await listener.GetContextAsync(); }
                    catch { return; }
                    var body = "{\"arguments\":{\"torrents\":[{\"hashString\":\"0123456789abcdef0123456789abcdef01234567\",\"name\":\"Some.Game-GRP\",\"totalSize\":1,\"percentDone\":1," +
                               "\"status\":6,\"downloadDir\":\"/downloads/complete\",\"error\":0,\"errorString\":\"\"}]},\"result\":\"success\"}";
                    var bytes = Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            });

            var downloads = await new TransmissionClient("127.0.0.1", port, "u", "p").GetDownloadsAsync();

            Assert.That(downloads.Single().DownloadPath, Is.EqualTo(Path.Combine("/downloads/complete", "Some.Game-GRP")));
        }
    }
}
