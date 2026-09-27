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
            // Newest first, cut to history_limit after the category filter like SAB does
            public int HistoryLimit { get; set; } = int.MaxValue;
            public List<string> Categories { get; } = new() { "*", "tv-sonarr" };
            public string? CreateCategoryError { get; set; }
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
                    if (query["mode"] == "get_cats")
                    {
                        body = $"{{\"categories\":[{string.Join(",", Categories.Select(c => $"\"{c}\""))}]}}";
                    }
                    else if (query["mode"] == "set_config")
                    {
                        var created = query["name"]!.ToLowerInvariant();
                        if (CreateCategoryError == null) Categories.Add(created);
                        body = CreateCategoryError == null
                            ? $"{{\"config\":{{\"categories\":[{{\"name\":\"{created}\",\"pp\":\"\",\"dir\":\"\"}}]}}}}"
                            : $"{{\"status\":false,\"error\":\"{CreateCategoryError}\"}}";
                    }
                    else if (query["mode"] == "addurl")
                    {
                        // An unknown category silently becomes Default
                        var cat = (query["cat"] ?? "").ToLowerInvariant();
                        Queue = Queue.Append(("added", Categories.Contains(cat) ? cat : "*")).ToArray();
                        body = "{\"status\":true,\"nzo_ids\":[\"added\"]}";
                    }
                    else if (query["mode"] == "queue")
                    {
                        var slots = Queue.Where(j => wanted == null || wanted.Contains(j.Category)).Select(j =>
                            $"{{\"nzo_id\":\"{j.Id}\",\"filename\":\"{j.Id}\",\"size\":\"1 MB\",\"percentage\":\"0\",\"status\":\"Queued\",\"cat\":\"{j.Category}\"}}");
                        body = $"{{\"queue\":{{\"slots\":[{string.Join(",", slots)}]}}}}";
                    }
                    else
                    {
                        var slots = History.Where(j => wanted == null || wanted.Contains(j.Category)).Take(HistoryLimit).Select(j =>
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
        public async Task NoCategory_FetchesWholeQueueAndHistory()
        {
            using var sab = new FakeSabnzbd();
            var client = new SabnzbdClient("127.0.0.1", sab.Port, "key");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "h1", "h2", "q1", "q2", "q3" }));
            Assert.That(sab.Requests, Has.None.Contains("category="));
        }

        [Test]
        public async Task Category_FiltersQueueBySentAndStoredSpelling_HistoryByStoredSpelling()
        {
            using var sab = new FakeSabnzbd();
            var client = new SabnzbdClient("127.0.0.1", sab.Port, "key", category: "RetroArr");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "h1", "q1", "q2" }));
            Assert.That(sab.Requests, Has.Some.StartsWith("/api?mode=queue&category=RetroArr%2Cretroarr&"));
            Assert.That(sab.Requests, Has.Some.StartsWith("/api?mode=history&category=retroarr&"));
        }

        [Test]
        public async Task History_OurJobStaysVisible_WhenNewerJobsOfOtherCategoriesFillHistoryLimit()
        {
            using var sab = new FakeSabnzbd
            {
                HistoryLimit = 10,
                History = Enumerable.Range(0, 12).Select(i => ($"tv{i}", "tv-sonarr")).Append(("ours", "retroarr")).ToArray(),
            };
            var client = new SabnzbdClient("127.0.0.1", sab.Port, "key", category: "RetroArr");

            // What DownloadMonitorService keeps after its own category filter
            var seen = (await client.GetDownloadsAsync())
                .Where(d => string.Equals(d.Category, "RetroArr", StringComparison.OrdinalIgnoreCase))
                .Select(d => d.Id);
            Assert.That(seen, Does.Contain("ours"));
        }

        [Test]
        public async Task AddNzb_CreatesMissingCategory_SoTheJobIsNotFiledUnderDefault()
        {
            using var sab = new FakeSabnzbd { Queue = Array.Empty<(string, string)>() };
            var client = new SabnzbdClient("127.0.0.1", sab.Port, "key", category: "RetroArr");

            var logs = await Logs(async () => Assert.That(await client.AddNzbAsync("http://indexer/get/1", "RetroArr"), Is.True));

            Assert.That(logs, Has.Some.EqualTo("Info|[Sabnzbd] Created missing category 'retroarr'"));
            Assert.That(sab.Categories, Does.Contain("retroarr"));
            Assert.That(sab.Requests.Select(r => r.Split('&')[0]).ToArray(),
                Is.EqualTo(new[] { "/api?mode=get_cats", "/api?mode=set_config", "/api?mode=addurl" }));
            Assert.That(sab.Requests[1], Does.Contain("section=categories&name=retroarr&"));
            Assert.That(await Ids(client), Is.EqualTo(new[] { "added", "h1" }));
        }

        [Test]
        public async Task AddNzb_ExistingCategory_IsLeftAlone()
        {
            using var sab = new FakeSabnzbd();
            sab.Categories.Add("retroarr");

            Assert.That(await new SabnzbdClient("127.0.0.1", sab.Port, "key").AddNzbAsync("http://indexer/get/1", "RetroArr"), Is.True);

            Assert.That(sab.Requests, Has.None.Contains("mode=set_config"));
            Assert.That(sab.Queue.Last(), Is.EqualTo(("added", "retroarr")));
        }

        [Test]
        public async Task AddNzb_MixedCaseCategoryFromOldSab_StillGetsTheLowercaseOne()
        {
            using var sab = new FakeSabnzbd();
            sab.Categories.Add("RetroArr");

            Assert.That(await new SabnzbdClient("127.0.0.1", sab.Port, "key").AddNzbAsync("http://indexer/get/1", "RetroArr"), Is.True);

            Assert.That(sab.Queue.Last(), Is.EqualTo(("added", "retroarr")));
        }

        [Test]
        public async Task AddNzb_CategoryCannotBeCreated_StillAdds()
        {
            // Older SABnzbd refuses with HTTP 200 and an error body
            using var sab = new FakeSabnzbd { CreateCategoryError = "API Key Incorrect" };

            var logs = await Logs(async () =>
                Assert.That(await new SabnzbdClient("127.0.0.1", sab.Port, "key").AddNzbAsync("http://indexer/get/1", "RetroArr"), Is.True));

            Assert.That(logs, Has.One.StartsWith("Warn|[Sabnzbd] Category 'retroarr' does not exist and could not be created (API Key Incorrect)"));
            Assert.That(logs, Has.None.Contains("Created missing category"));

            Assert.That(sab.Requests, Has.Some.Contains("mode=set_config"));
            Assert.That(sab.Queue.Last(), Is.EqualTo(("added", "*")));
        }

        [TestCase("")]
        [TestCase("Default")]
        [TestCase("*")]
        public async Task AddNzb_DefaultCategory_DoesNotTouchCategories(string category)
        {
            using var sab = new FakeSabnzbd();

            Assert.That(await new SabnzbdClient("127.0.0.1", sab.Port, "key").AddNzbAsync("http://indexer/get/1", category), Is.True);

            Assert.That(sab.Requests.Select(r => r.Split('&')[0]), Is.EqualTo(new[] { "/api?mode=addurl" }));
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

        [TestCase(null, null)]
        [TestCase("", null)]
        [TestCase("retroarr", "retroarr")]
        [TestCase("RetroArr", "retroarr")]
        [TestCase("Retro Games & Co+", "retro games & co+")]
        [TestCase(" retroarr", null)]
        [TestCase("retro,arr", null)]
        [TestCase("Rétro", null)]
        [TestCase("*", null)]
        [TestCase("Default", null)]
        [TestCase("none", null)]
        public void HistoryCategoryFilter(string? configured, string? expected)
        {
            Assert.That(SabnzbdClient.HistoryCategoryFilter(configured), Is.EqualTo(expected));
        }
    }
}
