using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.Download;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class SabnzbdClientTest
    {
        // Answers like SABnzbd does: the queue category filter is an exact, case-sensitive match
        // against a comma separated list. Stored jobs carry the lowercased category, a URL grab
        // still carries the name exactly as it was sent.
        private sealed class FakeSabnzbd : IDisposable
        {
            private readonly HttpListener _listener = new();

            public int Port { get; }
            public (string Id, string Category)[] Queue { get; set; } =
            {
                ("q1", "retroarr"),
                ("q2", "RetroArr"),
                ("q3", "tv-sonarr"),
            };
            public (string Id, string Category)[] History { get; set; } =
            {
                ("h1", "retroarr"),
                ("h2", "tv-sonarr"),
            };
            public List<string> Requests { get; } = new();

            public FakeSabnzbd()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = Task.Run(ServeAsync);
            }

            private async Task ServeAsync()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }

                    lock (Requests) Requests.Add(ctx.Request.RawUrl!);
                    var query = ctx.Request.QueryString;
                    var wanted = query["category"]?.Split(',').Select(c => c.Trim()).ToArray();

                    string body;
                    if (query["mode"] == "queue")
                    {
                        var slots = Queue.Where(j => wanted == null || wanted.Contains(j.Category)).Select(j =>
                            $"{{\"nzo_id\":\"{j.Id}\",\"filename\":\"{j.Id}\",\"size\":\"1 MB\",\"percentage\":\"0\",\"status\":\"Queued\",\"cat\":\"{j.Category}\"}}");
                        body = $"{{\"queue\":{{\"slots\":[{string.Join(",", slots)}]}}}}";
                    }
                    else
                    {
                        var slots = History.Where(j => wanted == null || wanted.Contains(j.Category)).Select(j =>
                            $"{{\"nzo_id\":\"{j.Id}\",\"name\":\"{j.Id}\",\"size\":\"1 MB\",\"status\":\"Completed\",\"category\":\"{j.Category}\",\"storage\":\"/d/{j.Id}\"}}");
                        body = $"{{\"history\":{{\"slots\":[{string.Join(",", slots)}]}}}}";
                    }

                    var bytes = Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            public void Dispose() => _listener.Close();
        }

        private static async Task<string[]> Ids(SabnzbdClient client) =>
            (await client.GetDownloadsAsync()).Select(d => d.Id).OrderBy(i => i).ToArray();

        [Test]
        public async Task NoCategory_FetchesWholeQueueAndHistory()
        {
            using var sab = new FakeSabnzbd();
            var client = new SabnzbdClient("127.0.0.1", sab.Port, "key");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "h1", "h2", "q1", "q2", "q3" }));
            Assert.That(sab.Requests, Has.None.Contains("category="));
        }

        [Test]
        public async Task Category_FiltersQueueBySentAndStoredSpelling_HistoryUnchanged()
        {
            using var sab = new FakeSabnzbd();
            var client = new SabnzbdClient("127.0.0.1", sab.Port, "key", category: "RetroArr");

            // h2 still comes back: history stays unfiltered, the caller's category filter drops it
            Assert.That(await Ids(client), Is.EqualTo(new[] { "h1", "h2", "q1", "q2" }));
            Assert.That(sab.Requests, Has.Some.StartsWith("/api?mode=queue&category=RetroArr%2Cretroarr&"));
            Assert.That(sab.Requests, Has.Some.StartsWith("/api?mode=history&apikey="));
        }

        [TestCase(false, "del_files=0")]
        [TestCase(true, "del_files=1")]
        public async Task Remove_PassesDeleteFiles(bool deleteFiles, string expected)
        {
            using var sab = new FakeSabnzbd();
            await new SabnzbdClient("127.0.0.1", sab.Port, "key").RemoveDownloadAsync("q1", deleteFiles);

            Assert.That(sab.Requests.Where(r => r.Contains("name=delete")), Has.All.Contains(expected).And.Not.Empty);
        }

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("retroarr", "retroarr")]
        [TestCase("RetroArr", "RetroArr,retroarr")]
        [TestCase("Retro Games & Co+", "Retro Games & Co+,retro games & co+")]
        [TestCase(" retroarr", null)]
        [TestCase("retro,arr", null)]
        [TestCase("Rétro", null)]
        [TestCase("*", null)]
        [TestCase("Default", null)]
        [TestCase("none", null)]
        public void QueueCategoryFilter(string? configured, string? expected)
        {
            Assert.That(SabnzbdClient.QueueCategoryFilter(configured), Is.EqualTo(expected));
        }
    }
}
