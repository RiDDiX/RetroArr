using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.Download;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class NzbgetClientTest
    {
        // Parses editqueue like NZBGet's XmlRpc.cpp: command, optional int offset, required string param, ids.
        // Also serves the NZB file itself, with a Content-Disposition header taken from the cd= query parameter.
        private sealed class FakeNzbget : IDisposable
        {
            private readonly HttpListener _listener = new();

            public int Port { get; }
            public Dictionary<int, string> Queue { get; } = new() { [1] = "QUEUED", [2] = "PAUSED" };
            public HashSet<int> History { get; } = new() { 3 };
            public string? ListGroupsJson { get; set; }
            public string? HistoryJson { get; set; }
            public List<string> Appended { get; } = new();
            public List<string> Calls { get; } = new();
            public List<string> RawBodies { get; } = new();

            public FakeNzbget()
            {
                var probe = new TcpListener(IPAddress.Loopback, 0);
                probe.Start();
                Port = ((IPEndPoint)probe.LocalEndpoint).Port;
                probe.Stop();

                _listener.Prefixes.Add($"http://127.0.0.1:{Port}/");
                _listener.Start();
                _ = Task.Run(ServeAsync);
            }

            public string Url(string pathAndQuery) => $"http://127.0.0.1:{Port}{pathAndQuery}";

            private async Task ServeAsync()
            {
                while (_listener.IsListening)
                {
                    HttpListenerContext ctx;
                    try { ctx = await _listener.GetContextAsync(); }
                    catch { return; }

                    string body;
                    if (ctx.Request.HttpMethod == "GET")
                    {
                        var cd = ctx.Request.QueryString["cd"];
                        if (cd != null) ctx.Response.AddHeader("Content-Disposition", cd);
                        body = "<?xml version=\"1.0\"?><nzb xmlns=\"http://www.newzbin.com/DTD/2003/nzb\"></nzb>";
                    }
                    else
                    {
                        using var reader = new System.IO.StreamReader(ctx.Request.InputStream);
                        var raw = await reader.ReadToEndAsync();
                        lock (RawBodies) RawBodies.Add(raw);
                        using var doc = JsonDocument.Parse(raw);
                        var method = doc.RootElement.GetProperty("method").GetString()!;
                        var args = doc.RootElement.GetProperty("params").EnumerateArray().ToList();
                        lock (Calls) Calls.Add($"{method} {doc.RootElement.GetProperty("params").GetRawText()}");
                        // NZBGet before 26.3 steps over the closing ']' after DupeMode and takes a following "id" for a
                        // Parameters name; an array as the last param stops it
                        var idAfterParams = doc.RootElement.EnumerateObject().SkipWhile(p => p.Name != "params").Any(p => p.Name == "id");
                        body = method == "append" && idAfterParams && args[^1].ValueKind != JsonValueKind.Array
                            ? "{\"version\":\"1.1\",\"error\":{\"name\":\"JSONRPCError\",\"code\":2,\"message\":\"Invalid parameter (Parameters)\"}}"
                            : Handle(method, args);
                    }

                    var bytes = Encoding.UTF8.GetBytes(body);
                    await ctx.Response.OutputStream.WriteAsync(bytes);
                    ctx.Response.Close();
                }
            }

            private string Handle(string method, List<JsonElement> args)
            {
                switch (method)
                {
                    case "append":
                        lock (Appended) Appended.Add(args[0].GetString()!);
                        return "{\"result\":" + (100 + Appended.Count) + "}";
                    case "listgroups":
                        return "{\"result\":" + (ListGroupsJson ?? "[" + string.Join(",", Queue.Select(q =>
                            $"{{\"NZBID\":{q.Key},\"NZBName\":\"job{q.Key}\",\"FileSizeLo\":100,\"FileSizeHi\":0,\"RemainingSizeLo\":100,\"RemainingSizeHi\":0,\"Status\":\"{q.Value}\",\"Category\":\"retroarr\",\"DestDir\":\"/d/{q.Key}\"}}")) + "]") + "}";
                    case "history":
                        return "{\"result\":" + (HistoryJson ?? "[" + string.Join(",", History.Select(h =>
                            $"{{\"NZBID\":{h},\"NZBName\":\"job{h}\",\"FileSizeLo\":100,\"FileSizeHi\":0,\"Status\":\"SUCCESS/ALL\",\"Category\":\"retroarr\",\"DestDir\":\"/d/{h}\"}}")) + "]") + "}";
                    case "editqueue":
                        var i = 1;
                        if (args.Count > i && args[i].ValueKind == JsonValueKind.Number) i++;
                        if (args.Count <= i || args[i].ValueKind != JsonValueKind.String)
                            return "{\"version\":\"1.1\",\"error\":{\"name\":\"JSONRPCError\",\"code\":2,\"message\":\"Invalid parameter\"}}";
                        var ids = args.Skip(i + 1).SelectMany(a => a.ValueKind == JsonValueKind.Array ? a.EnumerateArray().ToArray() : new[] { a }).Select(a => a.GetInt32()).ToList();
                        return "{\"result\":" + (Edit(args[0].GetString()!, ids) ? "true" : "false") + "}";
                    default:
                        return "{\"result\":null}";
                }
            }

            private bool Edit(string command, List<int> ids)
            {
                var ok = false;
                foreach (var id in ids)
                {
                    if (command == "HistoryDelete") { ok |= History.Remove(id); continue; }
                    if (!Queue.ContainsKey(id)) continue;
                    ok = true;
                    if (command == "GroupPause") Queue[id] = "PAUSED";
                    else if (command == "GroupResume") Queue[id] = "QUEUED";
                    else if (command == "GroupDelete") { Queue.Remove(id); History.Add(id); }
                }
                return ok;
            }

            public void Dispose() => _listener.Close();
        }

        private static NzbgetClient Client(FakeNzbget nzbget) => new("127.0.0.1", nzbget.Port, "nzbget", "tegbzn6789");

        [Test]
        public async Task PauseResume_ActOnTheJob_AndReportWhetherItApplied()
        {
            using var nzbget = new FakeNzbget();
            var client = Client(nzbget);

            Assert.That(await client.PauseDownloadAsync("1"), Is.True);
            Assert.That(nzbget.Queue[1], Is.EqualTo("PAUSED"));
            Assert.That(await client.ResumeDownloadAsync("2"), Is.True);
            Assert.That(nzbget.Queue[2], Is.EqualTo("QUEUED"));

            Assert.That(await client.PauseDownloadAsync("42"), Is.False);
            Assert.That(await client.ResumeDownloadAsync("42"), Is.False);
            Assert.That(await client.PauseDownloadAsync("abc"), Is.False);
            // the offset stays for NZBGet < 18, which requires it
            Assert.That(nzbget.Calls, Has.Some.EqualTo("editqueue [\"GroupPause\",0,\"\",[1]]"));
        }

        [Test]
        public async Task Remove_DeletesQueuedAndHistoryJobs_FalseWhenNothingMatched()
        {
            using var nzbget = new FakeNzbget();
            var client = Client(nzbget);

            Assert.That(await client.RemoveDownloadAsync("1", deleteFiles: true), Is.True);
            Assert.That(await client.RemoveDownloadAsync("3", deleteFiles: false), Is.True);
            Assert.That(nzbget.Queue.Keys, Is.EquivalentTo(new[] { 2 }));
            Assert.That(nzbget.History, Is.Empty);

            Assert.That(await client.RemoveDownloadAsync("42", deleteFiles: true), Is.False);
        }

        [Test]
        public async Task History_CompoundStatusesMap()
        {
            using var nzbget = new FakeNzbget();
            nzbget.Queue.Clear();
            var statuses = new[]
            {
                "SUCCESS/ALL", "SUCCESS/UNPACK", "SUCCESS/HEALTH", "SUCCESS/MARK", "WARNING/SCRIPT",
                "WARNING/DAMAGED", "WARNING/PASSWORD", "FAILURE/HEALTH", "FAILURE/UNPACK", "DELETED/MANUAL", "DELETED/DUPE", "WHATEVER",
            };
            nzbget.HistoryJson = "[" + string.Join(",", statuses.Select((s, i) =>
                $"{{\"NZBID\":{i},\"NZBName\":\"{s}\",\"FileSizeLo\":1,\"FileSizeHi\":0,\"Status\":\"{s}\",\"Category\":\"retroarr\",\"DestDir\":\"/d\"}}")) + "]";

            var states = (await Client(nzbget).GetDownloadsAsync()).ToDictionary(d => d.Name, d => d.State);

            Assert.That(states, Is.EquivalentTo(new Dictionary<string, DownloadState>
            {
                ["SUCCESS/ALL"] = DownloadState.Completed,
                ["SUCCESS/UNPACK"] = DownloadState.Completed,
                ["SUCCESS/HEALTH"] = DownloadState.Completed,
                ["SUCCESS/MARK"] = DownloadState.Completed,
                ["WARNING/SCRIPT"] = DownloadState.Completed,
                ["WARNING/DAMAGED"] = DownloadState.Error,
                ["WARNING/PASSWORD"] = DownloadState.Error,
                ["FAILURE/HEALTH"] = DownloadState.Error,
                ["FAILURE/UNPACK"] = DownloadState.Error,
                ["DELETED/MANUAL"] = DownloadState.Deleted,
                ["DELETED/DUPE"] = DownloadState.Deleted,
                ["WHATEVER"] = DownloadState.Unknown,
            }));
        }

        [Test]
        public async Task Sizes_CombineHiAndLo()
        {
            using var nzbget = new FakeNzbget();
            // 5,000,000,000 bytes = Hi 1, Lo 705032704; 1,000,000,000 remaining
            nzbget.ListGroupsJson = "[{\"NZBID\":1,\"NZBName\":\"big\",\"FileSizeLo\":705032704,\"FileSizeHi\":1,\"RemainingSizeLo\":1000000000,\"RemainingSizeHi\":0,\"Status\":\"DOWNLOADING\",\"Category\":\"retroarr\",\"DestDir\":\"/d\"}]";
            nzbget.HistoryJson = "[{\"NZBID\":2,\"NZBName\":\"bigdone\",\"FileSizeLo\":4294967295,\"FileSizeHi\":2,\"Status\":\"SUCCESS/ALL\",\"Category\":\"retroarr\",\"DestDir\":\"/d\"}]";

            var downloads = (await Client(nzbget).GetDownloadsAsync()).ToDictionary(d => d.Name);

            Assert.That(downloads["big"].Size, Is.EqualTo(5_000_000_000L));
            Assert.That(downloads["big"].Progress, Is.EqualTo(80f).Within(0.01f));
            Assert.That(downloads["bigdone"].Size, Is.EqualTo((3L << 32) - 1));
        }

        [Test]
        public async Task Sizes_OldSignedLoIsReadUnsigned()
        {
            using var nzbget = new FakeNzbget();
            nzbget.Queue.Clear();
            // 3,000,000,000 as a negative Lo, as NZBGet before 21.1 could send it
            nzbget.HistoryJson = "[{\"NZBID\":2,\"NZBName\":\"old\",\"FileSizeLo\":-1294967296,\"FileSizeHi\":0,\"Status\":\"SUCCESS/ALL\",\"Category\":\"retroarr\",\"DestDir\":\"/d\"}]";

            var downloads = await Client(nzbget).GetDownloadsAsync();

            Assert.That(downloads.Single().Size, Is.EqualTo(3_000_000_000L));
        }

        [Test]
        public async Task Add_NamesEachJobAfterItsRelease()
        {
            using var nzbget = new FakeNzbget();
            var client = Client(nzbget);
            var cd = Uri.EscapeDataString("attachment; filename=\"Super.Mario.World.SNES-GRP.nzb\"");

            Assert.That(await client.AddNzbAsync(nzbget.Url($"/1/download?cd={cd}&file=Other"), "retroarr"), Is.True);
            Assert.That(await client.AddNzbAsync(nzbget.Url("/2/download?apikey=x&link=abc&file=Zelda%20Link%27s%20Awakening%20GB"), "retroarr"), Is.True);
            Assert.That(await client.AddNzbAsync(nzbget.Url("/getnzb/Metroid.Fusion.GBA.nzb?i=1&r=2"), "retroarr"), Is.True);
            Assert.That(await client.AddNzbAsync(nzbget.Url("/api?t=get&id=1"), "retroarr"), Is.True);
            Assert.That(await client.AddNzbAsync(nzbget.Url("/api?t=get&id=2"), "retroarr"), Is.True);

            Assert.That(nzbget.Appended.Take(3), Is.EqualTo(new[]
            {
                "Super.Mario.World.SNES-GRP.nzb",
                "Zelda Link's Awakening GB.nzb",
                "Metroid.Fusion.GBA.nzb",
            }));
            // no usable name: still unique, so NZBGet's duplicate check can't drop the second one
            Assert.That(nzbget.Appended.Skip(3), Has.All.Match("^RetroArr_[0-9a-f]{32}\\.nzb$"));
            Assert.That(nzbget.Appended[3], Is.Not.EqualTo(nzbget.Appended[4]));
        }

        [Test]
        public async Task Requests_AreNotUnicodeEscaped()
        {
            using var nzbget = new FakeNzbget();
            var client = Client(nzbget);

            Assert.That(await client.AddNzbAsync(nzbget.Url("/1/download?cd=0&file=Zelda%20Link%27s%20Awakening"), "Games & Co+"), Is.True);
            Assert.That(await client.PauseDownloadAsync("1"), Is.True);

            // NZBGet before 25.0 mangles \uXXXX, e.g. every '+' of the base64 NZB
            Assert.That(nzbget.RawBodies, Has.None.Contains("\\u"));
            Assert.That(nzbget.RawBodies, Has.Some.Contains("\"Zelda Link's Awakening.nzb\"").And.Some.Contains("\"Games & Co+\""));
        }

        [TestCase("attachment; filename=\"../../etc/Evil.Game.nzb\"", "Evil.Game.nzb")]
        [TestCase("attachment; filename=\"C:\\\\x\\\\Win.Game\"", "Win.Game.nzb")]
        [TestCase("attachment; filename*=UTF-8''Pok%C3%A9mon%20Red.NZB", "Pokémon Red.nzb")]
        [TestCase("attachment; filename=\"   \"", "RetroArr_")]
        public void Filename_IsSanitised(string header, string expected)
        {
            var name = NzbgetClient.BuildNzbFilename("http://x/api?t=get", ContentDispositionHeaderValue.Parse(header));

            if (expected == "RetroArr_") Assert.That(name, Does.Match("^RetroArr_[0-9a-f]{32}\\.nzb$"));
            else Assert.That(name, Is.EqualTo(expected));
        }

        [TestCase('a', 300, 200)]
        [TestCase('ゲ', 100, 66)]
        public void Filename_LongNamesAreCutToBytes(char c, int length, int kept)
        {
            var name = NzbgetClient.BuildNzbFilename("http://x/1/download?file=" + Uri.EscapeDataString(new string(c, length)), null);

            Assert.That(name, Is.EqualTo(new string(c, kept) + ".nzb"));
        }
    }
}
