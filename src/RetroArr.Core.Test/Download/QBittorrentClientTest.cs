using System;
using System.Collections.Generic;
using System.IO;
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
    public class QBittorrentClientTest
    {
        // Answers like qBittorrent does: the category filter is exact and case-sensitive, and a '+'
        // in the query string turns into a space even when sent as %2B (form bodies decode fine).
        private sealed class FakeQBittorrent : IDisposable
        {
            private readonly HttpListener _listener = new();

            public int Port { get; }
            public string Categories { get; set; } = "{}";
            public int CategoriesStatus { get; set; } = 200;
            public int InfoPostStatus { get; set; } = 200;
            public bool IgnoreCategory { get; set; }
            public List<string> AcceptEncodings { get; } = new();
            public (string Hash, string Category)[] Torrents { get; set; } =
            {
                ("aaa", "Retro Games"),
                ("bbb", "tv-sonarr"),
                ("ccc", "radarr"),
            };
            public List<string> Requests { get; } = new();

            public FakeQBittorrent()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = Task.Run(ServeAsync);
            }

            private static string? Param(string encoded, string key)
            {
                foreach (var pair in encoded.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = pair.Split('=', 2);
                    if (kv[0] == key) return kv.Length > 1 ? kv[1] : "";
                }
                return null;
            }

            private async Task ServeAsync()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }

                    var path = ctx.Request.Url!.AbsolutePath;
                    var form = await new StreamReader(ctx.Request.InputStream).ReadToEndAsync();
                    lock (Requests)
                    {
                        Requests.Add($"{ctx.Request.HttpMethod} {ctx.Request.RawUrl} {form}".Trim());
                        AcceptEncodings.Add(ctx.Request.Headers["Accept-Encoding"] ?? "");
                    }

                    var body = "Ok.";
                    if (path == "/api/v2/auth/login")
                    {
                        ctx.Response.AddHeader("Set-Cookie", "SID=test; path=/");
                    }
                    else if (path == "/api/v2/torrents/categories")
                    {
                        ctx.Response.StatusCode = CategoriesStatus;
                        body = Categories;
                    }
                    else if (path == "/api/v2/torrents/info" && ctx.Request.HttpMethod == "POST" && InfoPostStatus != 200)
                    {
                        ctx.Response.StatusCode = InfoPostStatus;
                    }
                    else if (path == "/api/v2/torrents/info")
                    {
                        var raw = Param(ctx.Request.Url.Query, "category");
                        var category = raw != null ? Uri.UnescapeDataString(raw).Replace('+', ' ') : null;
                        raw = Param(form, "category");
                        if (raw != null) category = Uri.UnescapeDataString(raw.Replace('+', ' '));

                        var hits = Torrents.Where(t => IgnoreCategory || category == null || t.Category == category);
                        body = "[" + string.Join(",", hits.Select(t =>
                            $"{{\"hash\":\"{t.Hash}\",\"name\":\"{t.Hash}\",\"state\":\"downloading\",\"category\":\"{t.Category}\"}}")) + "]";
                    }

                    var bytes = Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            public void Dispose() => _listener.Close();
        }

        private static string CategoryJson(params string[] names) =>
            "{" + string.Join(",", names.Select(n => $"\"{n}\":{{\"name\":\"{n}\",\"savePath\":\"\"}}")) + "}";

        private static async Task<string[]> Ids(QBittorrentClient client) =>
            (await client.GetDownloadsAsync()).Select(d => d.Id).OrderBy(i => i).ToArray();

        [Test]
        public async Task NoCategory_FetchesWholeList()
        {
            using var qb = new FakeQBittorrent();
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(qb.Requests, Does.Contain("GET /api/v2/torrents/info"));
            Assert.That(qb.Requests, Has.None.Contains("categories"));
        }

        [Test]
        public async Task Category_FetchesEverySpellingQBittorrentKnows()
        {
            using var qb = new FakeQBittorrent
            {
                Categories = CategoryJson("Retro Games", "retro games", "tv-sonarr"),
                Torrents = new[] { ("aaa", "Retro Games"), ("eee", "retro games"), ("bbb", "tv-sonarr") },
            };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "RETRO GAMES");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "eee" }));
            Assert.That(qb.Requests, Does.Not.Contain("GET /api/v2/torrents/info"));
        }

        [Test]
        public async Task Category_WithPlusAndAmpersand_IsSentIntact()
        {
            using var qb = new FakeQBittorrent
            {
                Categories = CategoryJson("C++ & Co", "C   & Co"),
                Torrents = new[] { ("ddd", "C++ & Co"), ("fff", "C   & Co"), ("bbb", "tv-sonarr") },
            };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "c++ & co");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "ddd" }));
        }

        [Test]
        public async Task CategoryMissingFromList_StillAsksForTheConfiguredSpelling()
        {
            // qBittorrent look-alikes don't always list every category their torrents use
            using var qb = new FakeQBittorrent
            {
                Categories = CategoryJson("retroarr"),
                Torrents = new[] { ("aaa", "RetroArr"), ("bbb", "tv-sonarr") },
            };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "RetroArr");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa" }));
            Assert.That(qb.Requests, Does.Not.Contain("GET /api/v2/torrents/info"));
        }

        [Test]
        public async Task CategoryUnknown_ReturnsNothingWithoutTheWholeList()
        {
            using var qb = new FakeQBittorrent { Categories = CategoryJson("tv-sonarr") };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "retroarr");

            Assert.That(await Ids(client), Is.Empty);
            Assert.That(qb.Requests, Does.Not.Contain("GET /api/v2/torrents/info"));
        }

        [Test]
        public async Task EmptyCategory_FetchesWholeList()
        {
            using var qb = new FakeQBittorrent();
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(qb.Requests, Has.None.Contains("categories"));
        }

        [Test]
        public async Task SameTorrentFromTwoSpellings_IsListedOnce()
        {
            using var qb = new FakeQBittorrent { Categories = CategoryJson("RetroArr", "retroarr"), IgnoreCategory = true };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "RetroArr");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
        }

        [Test]
        public async Task CategoriesNotAnObject_StillAsksForTheConfiguredSpelling()
        {
            using var qb = new FakeQBittorrent { Categories = "[]" };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "Retro Games");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa" }));
        }

        [TestCase("Ok.", 200)]
        [TestCase("{}", 405)]
        public async Task CategoriesNotJsonOrFilterRejected_FallsBackToWholeList(string categories, int infoPostStatus)
        {
            using var qb = new FakeQBittorrent { Categories = categories, InfoPostStatus = infoPostStatus };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "retro games");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
        }

        [Test]
        public async Task AsksForCompressedResponses()
        {
            using var qb = new FakeQBittorrent();
            await Ids(new QBittorrentClient("127.0.0.1", qb.Port, "u", "p"));

            Assert.That(qb.AcceptEncodings, Has.All.Contains("gzip"));
        }

        [Test]
        public async Task NoCategoriesEndpoint_FallsBackToWholeList()
        {
            using var qb = new FakeQBittorrent { CategoriesStatus = 404 };
            var client = new QBittorrentClient("127.0.0.1", qb.Port, "u", "p", category: "retro games");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(qb.Requests, Does.Contain("GET /api/v2/torrents/info"));
        }
    }
}
