using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.Download;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class DelugeClientTest
    {
        // Answers like deluge-web does: labels only exist with the Label plugin enabled (label.get_labels is
        // an unknown method otherwise) and the label filter is an exact, case-sensitive match.
        private sealed class FakeDeluge : IDisposable
        {
            private readonly HttpListener _listener = new();

            public int Port { get; }
            public bool LabelPlugin { get; set; } = true;
            public bool PluginListFails { get; set; }
            public string[] Labels { get; set; } = { "retroarr", "tv-sonarr" };
            public (string Hash, string Label)[] Torrents { get; set; } =
            {
                ("aaa", "retroarr"),
                ("bbb", "tv-sonarr"),
                ("ccc", ""),
            };
            public Dictionary<string, (string State, float Progress)> States { get; set; } = new();
            public List<string> Requests { get; } = new();

            public FakeDeluge()
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

                    // The indexer side of a grab: a download link that redirects to a magnet or serves a .torrent
                    if (ctx.Request.HttpMethod == "GET")
                    {
                        if (ctx.Request.Url!.AbsolutePath == "/magnet") { ctx.Response.StatusCode = 302; ctx.Response.RedirectLocation = Magnet; }
                        else { ctx.Response.ContentType = "application/x-bittorrent"; await ctx.Response.OutputStream.WriteAsync(Encoding.ASCII.GetBytes("d4:infod4:name1:xee")); }
                        ctx.Response.Close();
                        continue;
                    }

                    using var doc = JsonDocument.Parse(await new StreamReader(ctx.Request.InputStream).ReadToEndAsync());
                    var method = doc.RootElement.GetProperty("method").GetString();
                    var args = doc.RootElement.GetProperty("params");
                    lock (Requests) Requests.Add($"{method} {args.GetRawText()}");

                    object? result = true;
                    object? error = null;
                    if (method == "core.get_enabled_plugins" && PluginListFails)
                    {
                        result = null;
                        error = new { message = "boom", code = 3 };
                    }
                    else if (method == "core.get_enabled_plugins")
                    {
                        result = LabelPlugin ? new[] { "Label" } : Array.Empty<string>();
                    }
                    else if (method == "label.get_labels")
                    {
                        if (LabelPlugin) result = Labels;
                        else { result = null; error = new { message = "Unknown method", code = 2 }; }
                    }
                    else if (method!.StartsWith("core.add_torrent_"))
                    {
                        result = "newhash";
                    }
                    else if (method == "label.add" || method == "label.set_torrent")
                    {
                        // label_core.py: add() lowercases and rejects duplicates, set_torrent() wants an existing label
                        var label = args[method == "label.add" ? 0 : 1].GetString()!;
                        string? fault = !LabelPlugin ? "Unknown method"
                            : method == "label.add" ? (Labels.Contains(label.ToLowerInvariant()) ? "Label already exists" : null)
                            : (Labels.Contains(label) ? null : "Unknown Label");
                        if (fault != null) { result = null; error = new { message = fault, code = 4 }; }
                        else if (method == "label.add") Labels = Labels.Append(label.ToLowerInvariant()).ToArray();
                        else Torrents = Torrents.Where(t => t.Hash != args[0].GetString()).Append((args[0].GetString()!, label)).ToArray();
                    }
                    else if (method == "core.get_torrents_status")
                    {
                        string[]? wanted = null;
                        if (args[0].TryGetProperty("label", out var f))
                            wanted = f.ValueKind == JsonValueKind.String ? new[] { f.GetString()! } : f.EnumerateArray().Select(v => v.GetString()!).ToArray();
                        result = Torrents
                            .Where(t => wanted == null || (LabelPlugin && wanted.Contains(t.Label)))
                            .ToDictionary(t => t.Hash, t => LabelPlugin
                                ? (object)new { name = t.Hash, state = State(t.Hash), progress = Progress(t.Hash), save_path = "/downloads", label = t.Label }
                                : new { name = t.Hash, state = State(t.Hash), progress = Progress(t.Hash), save_path = "/downloads" });
                    }

                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { result, error, id = 1 }));
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            private string State(string hash) => States.TryGetValue(hash, out var s) ? s.State : "Paused";
            private float Progress(string hash) => States.TryGetValue(hash, out var s) ? s.Progress : 0;

            public void Dispose() => _listener.Close();
        }

        private static async Task<string[]> Ids(DelugeClient client) =>
            (await client.GetDownloadsAsync()).Select(d => d.Id).OrderBy(i => i).ToArray();

        [Test]
        public async Task NoCategory_FetchesWholeList()
        {
            using var deluge = new FakeDeluge();

            Assert.That(await Ids(new DelugeClient("127.0.0.1", deluge.Port, "pw")), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(deluge.Requests, Has.Some.StartsWith("core.get_torrents_status [{},"));
            Assert.That(deluge.Requests, Has.None.StartsWith("core.get_enabled_plugins"));
        }

        [Test]
        public async Task DownloadPath_IsTheTorrentsOwnFolder()
        {
            using var deluge = new FakeDeluge();
            var downloads = await new DelugeClient("127.0.0.1", deluge.Port, "pw").GetDownloadsAsync();

            Assert.That(downloads.Single(d => d.Id == "aaa").DownloadPath, Is.EqualTo(Path.Combine("/downloads", "aaa")));
        }

        [Test]
        public async Task EmptyCategory_FetchesWholeList()
        {
            using var deluge = new FakeDeluge();

            Assert.That(await Ids(new DelugeClient("127.0.0.1", deluge.Port, "pw", category: "")), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(deluge.Requests, Has.None.StartsWith("core.get_enabled_plugins"));
        }

        [Test]
        public async Task Category_AsksOnlyForTheMatchingRealLabel()
        {
            using var deluge = new FakeDeluge();
            var client = new DelugeClient("127.0.0.1", deluge.Port, "pw", category: "RetroArr");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa" }));
            Assert.That(deluge.Requests, Does.Contain("core.get_torrents_status [{\"label\":[\"retroarr\"]},[\"name\",\"total_size\",\"state\",\"progress\",\"save_path\",\"hash\",\"label\"]]"));
        }

        [Test]
        public async Task LabelLookupFailing_FallsBackToWholeList()
        {
            using var deluge = new FakeDeluge { PluginListFails = true };
            var client = new DelugeClient("127.0.0.1", deluge.Port, "pw", category: "RetroArr");

            Assert.That(await Ids(client), Is.EqualTo(new[] { "aaa", "bbb", "ccc" }));
            Assert.That(deluge.Requests, Has.Some.StartsWith("core.get_torrents_status [{},"));
        }

        [Test]
        public async Task Category_WithoutLabelPlugin_ReturnsNothingWithoutErrors()
        {
            using var deluge = new FakeDeluge { LabelPlugin = false };
            var client = new DelugeClient("127.0.0.1", deluge.Port, "pw", category: "RetroArr");

            Assert.That(await Ids(client), Is.Empty);
            Assert.That(deluge.Requests, Has.None.StartsWith("label."));
        }

        private const string Magnet = "magnet:?xt=urn:btih:0123456789abcdef0123456789abcdef01234567";

        [Test]
        public async Task Add_WithCategory_CreatesAndSetsTheLowercaseLabel()
        {
            using var deluge = new FakeDeluge { Labels = new[] { "tv-sonarr" }, Torrents = new[] { ("bbb", "tv-sonarr"), ("ccc", "") } };
            var client = new DelugeClient("127.0.0.1", deluge.Port, "pw", category: "RetroArr");

            Assert.That(await client.AddTorrentAsync(Magnet, "RetroArr"), Is.True);

            Assert.That(deluge.Requests, Does.Contain("label.add [\"retroarr\"]"));
            Assert.That(deluge.Requests, Does.Contain("label.set_torrent [\"newhash\",\"retroarr\"]"));
            Assert.That(deluge.Requests.Single(r => r.StartsWith("core.add_torrent_magnet")), Does.Not.Contain("label"));
            // and the caller's category filter now keeps it
            var mine = (await client.GetDownloadsAsync()).Where(d => "RetroArr".Equals(d.Category, StringComparison.OrdinalIgnoreCase));
            Assert.That(mine.Select(d => d.Id), Is.EqualTo(new[] { "newhash" }));
        }

        [Test]
        public async Task Add_ExistingLabel_IsReused()
        {
            using var deluge = new FakeDeluge();

            Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync(Magnet, "RETROARR"), Is.True);

            Assert.That(deluge.Requests, Has.None.StartsWith("label.add"));
            Assert.That(deluge.Requests, Does.Contain("label.set_torrent [\"newhash\",\"retroarr\"]"));
        }

        [Test]
        public async Task Add_LocalTorrentFile_IsLabelled()
        {
            using var deluge = new FakeDeluge();
            var file = Path.GetTempFileName();
            try
            {
                await File.WriteAllBytesAsync(file, Encoding.ASCII.GetBytes("d4:infod4:name1:xee"));
                Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync(file, "retroarr"), Is.True);
            }
            finally { File.Delete(file); }

            Assert.That(deluge.Requests, Has.Some.StartsWith("core.add_torrent_file"));
            Assert.That(deluge.Requests, Does.Contain("label.set_torrent [\"newhash\",\"retroarr\"]"));
        }

        [TestCase("/magnet", "core.add_torrent_magnet")]
        [TestCase("/game.torrent", "core.add_torrent_file")]
        public async Task Add_ResolvedDownloadLink_IsLabelled(string path, string addMethod)
        {
            using var deluge = new FakeDeluge();

            Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync($"http://127.0.0.1:{deluge.Port}{path}", "RetroArr"), Is.True);

            Assert.That(deluge.Requests, Has.Some.StartsWith(addMethod));
            Assert.That(deluge.Requests, Does.Contain("label.set_torrent [\"newhash\",\"retroarr\"]"));
        }

        [Test]
        public async Task Add_WithoutLabelPlugin_StillSucceeds()
        {
            using var deluge = new FakeDeluge { LabelPlugin = false };

            Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync(Magnet, "RetroArr"), Is.True);

            Assert.That(deluge.Requests, Has.Some.StartsWith("core.add_torrent_magnet"));
            Assert.That(deluge.Requests, Has.None.StartsWith("label."));
        }

        [Test]
        public async Task Add_CategoryThatCantBeALabel_StillSucceedsUnlabelled()
        {
            using var deluge = new FakeDeluge();

            Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync(Magnet, "Retro Games"), Is.True);

            Assert.That(deluge.Requests, Has.Some.StartsWith("core.add_torrent_magnet"));
            Assert.That(deluge.Requests, Has.None.StartsWith("label."));
        }

        [Test]
        public async Task Add_LabelFailing_StillSucceeds()
        {
            using var deluge = new FakeDeluge { PluginListFails = true };

            Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync(Magnet, "RetroArr"), Is.True);
        }

        [Test]
        public async Task Add_WithoutCategory_SetsNoLabel()
        {
            using var deluge = new FakeDeluge();

            Assert.That(await new DelugeClient("127.0.0.1", deluge.Port, "pw").AddTorrentAsync(Magnet, ""), Is.True);

            Assert.That(deluge.Requests, Has.None.StartsWith("core.get_enabled_plugins"));
            Assert.That(deluge.Requests, Has.None.StartsWith("label."));
        }

        [Test]
        public async Task QueuedState_IsQueuedUntilFinished()
        {
            using var deluge = new FakeDeluge
            {
                States = new()
                {
                    ["aaa"] = ("Queued", 0),
                    ["bbb"] = ("Queued", 100),
                    ["ccc"] = ("Paused", 100),
                }
            };
            var states = (await new DelugeClient("127.0.0.1", deluge.Port, "pw").GetDownloadsAsync()).ToDictionary(d => d.Id, d => d.State);

            Assert.That(states["aaa"], Is.EqualTo(DownloadState.Queued));
            Assert.That(states["bbb"], Is.EqualTo(DownloadState.Completed));
            Assert.That(states["ccc"], Is.EqualTo(DownloadState.Completed));
        }
    }
}
