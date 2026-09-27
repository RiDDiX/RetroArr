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
                    else if (method == "core.get_torrents_status")
                    {
                        string[]? wanted = null;
                        if (args[0].TryGetProperty("label", out var f))
                            wanted = f.ValueKind == JsonValueKind.String ? new[] { f.GetString()! } : f.EnumerateArray().Select(v => v.GetString()!).ToArray();
                        result = Torrents
                            .Where(t => wanted == null || (LabelPlugin && wanted.Contains(t.Label)))
                            .ToDictionary(t => t.Hash, t => LabelPlugin
                                ? (object)new { name = t.Hash, state = "Paused", label = t.Label }
                                : new { name = t.Hash, state = "Paused" });
                    }

                    var bytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(new { result, error, id = 1 }));
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

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
    }
}
