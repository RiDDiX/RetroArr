using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NUnit.Framework;
using RetroArr.Api.V3.Auth;
using RetroArr.Api.V3.SystemInfo;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Security
{
    [TestFixture]
    public class ApiKeyAuthTest
    {
        private string _root = null!;
        private ApiKeyService _keys = null!;
        private string Key => _keys.GetApiKey();

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_auth_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _keys = new ApiKeyService(new ConfigurationService(_root));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private static DefaultHttpContext Request(string path, string peer, string query = "", string method = "GET", params (string Name, string Value)[] headers)
        {
            var context = new DefaultHttpContext();
            context.Request.Method = method;
            context.Request.Path = path;
            context.Request.QueryString = new QueryString(query);
            context.Request.Host = new HostString("localhost", 5002);
            context.Connection.RemoteIpAddress = IPAddress.Parse(peer);
            foreach (var (name, value) in headers) context.Request.Headers[name] = value;
            return context;
        }

        // capture -> UseForwardedHeaders (default trusted ranges) -> auth -> endpoint, like Program.cs
        private async Task<(int Status, bool Reached)> Send(DefaultHttpContext context, string? trustedProxies = null)
        {
            var reached = false;
            RequestDelegate endpoint = ctx => { reached = true; ctx.Response.StatusCode = 200; return Task.CompletedTask; };
            var auth = new ApiKeyAuthMiddleware(endpoint, _keys);

            var options = new ForwardedHeadersOptions
            {
                ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedHost,
                ForwardLimit = null
            };
            TrustedProxies.Apply(options, trustedProxies, _ => { });
            var forwarded = new ForwardedHeadersMiddleware(auth.Invoke, NullLoggerFactory.Instance, Options.Create(options));

            LocalRequest.CaptureSocketPeer(context);
            await forwarded.Invoke(context);
            return (context.Response.StatusCode, reached);
        }

        [Test]
        public async Task ForgedForwardedFor_FromTrustedRange_DoesNotSkipTheKey()
        {
            // The report: a container or LAN host sends X-Forwarded-For: 127.0.0.1
            var context = Request("/api/v3/settings/monitor", "172.17.0.1", headers: ("X-Forwarded-For", "127.0.0.1"));

            var (status, reached) = await Send(context);

            // UseForwardedHeaders did rewrite the address and consume the header...
            Assert.That(IPAddress.IsLoopback(context.Connection.RemoteIpAddress!), Is.True);
            Assert.That(context.Request.Headers.ContainsKey("X-Forwarded-For"), Is.False);
            // ...but the request is still treated as remote
            Assert.That(status, Is.EqualTo(401));
            Assert.That(reached, Is.False);
        }

        [Test]
        public async Task ForgedForwardedFor_CannotReadTheBootstrapKey()
        {
            var context = Request("/api/v3/system/apikey/bootstrap", "192.168.1.50", headers: ("X-Forwarded-For", "127.0.0.1"));
            var (status, reached) = await Send(context);
            Assert.That(status, Is.EqualTo(401));
            Assert.That(reached, Is.False);
        }

        [Test]
        public async Task ForgedForwardedFor_WithEveryProxyTrusted_StillNeedsTheKey()
        {
            var context = Request("/api/v3/settings/monitor", "10.0.0.7", headers: ("X-Forwarded-For", "::1"));
            var (status, _) = await Send(context, "0.0.0.0/0,::/0");
            Assert.That(status, Is.EqualTo(401));
        }

        [Test]
        public async Task ProxyOnLoopback_ForwardingARemoteClient_NeedsTheKey()
        {
            var context = Request("/api/v3/settings/monitor", "127.0.0.1", headers: ("X-Forwarded-For", "203.0.113.9"));
            var (status, _) = await Send(context);
            Assert.That(status, Is.EqualTo(401));
        }

        [TestCase("127.0.0.1")]
        [TestCase("::1")]
        [TestCase("::ffff:127.0.0.1")]
        public async Task DirectLoopbackRequest_NeedsNoKey(string peer)
        {
            var context = Request("/api/v3/settings/monitor", peer);
            var (status, reached) = await Send(context);
            Assert.That(status, Is.EqualTo(200));
            Assert.That(reached, Is.True);
            Assert.That(context.Items.ContainsKey(ApiKeyAuthMiddleware.AuthenticatedItem), Is.True);
        }

        [Test]
        public async Task LanRequest_WithoutKey_IsRefused()
        {
            var (status, reached) = await Send(Request("/api/v3/settings/monitor", "192.168.1.50"));
            Assert.That(status, Is.EqualTo(401));
            Assert.That(reached, Is.False);
        }

        [Test]
        public async Task LanRequest_WithKey_Passes()
        {
            Assert.That((await Send(Request("/api/v3/settings/monitor", "192.168.1.50", headers: ("X-Api-Key", Key)))).Status, Is.EqualTo(200));
            Assert.That((await Send(Request("/api/v3/settings/monitor", "192.168.1.50", "?apiKey=" + Key))).Status, Is.EqualTo(200));
            Assert.That((await Send(Request("/hubs/progress", "192.168.1.50", "?access_token=" + Key))).Status, Is.EqualTo(200));
            // the query name SignalR needs is not a second way in for the rest of the API
            Assert.That((await Send(Request("/api/v3/settings/monitor", "192.168.1.50", "?access_token=" + Key))).Status, Is.EqualTo(401));
            Assert.That((await Send(Request("/api/v3/settings/monitor", "192.168.1.50", headers: ("Authorization", "Bearer " + Key)))).Status, Is.EqualTo(200));
            Assert.That((await Send(Request("/api/v3/settings/monitor", "192.168.1.50", headers: ("X-Api-Key", "wrong")))).Status, Is.EqualTo(401));
        }

        [Test]
        public async Task Rom_IsNoLongerAnonymous()
        {
            var (status, reached) = await Send(Request("/api/v3/emulator/5/rom", "192.168.1.50"));
            Assert.That(status, Is.EqualTo(401));
            Assert.That(reached, Is.False);
        }

        [Test]
        public async Task Rom_WithSignedLink_Passes()
        {
            var signed = SignedUrl.Sign("/api/v3/emulator/5/rom", Key, DateTimeOffset.UtcNow);
            var query = signed[signed.IndexOf('?')..];
            var (status, reached) = await Send(Request("/api/v3/emulator/5/rom", "192.168.1.50", query));
            Assert.That(status, Is.EqualTo(200));
            Assert.That(reached, Is.True);
        }

        [Test]
        public async Task SignedLink_ForAnotherGame_OrPost_IsRefused()
        {
            var signed = SignedUrl.Sign("/api/v3/emulator/5/rom", Key, DateTimeOffset.UtcNow);
            var query = signed[signed.IndexOf('?')..];
            Assert.That((await Send(Request("/api/v3/emulator/6/rom", "192.168.1.50", query))).Status, Is.EqualTo(401));
            Assert.That((await Send(Request("/api/v3/emulator/5/rom", "192.168.1.50", query, "POST"))).Status, Is.EqualTo(401));
        }

        [TestCase("/api/v3/emulator/player")]
        [TestCase("/api/v3/emulator/assets/loader.js")]
        [TestCase("/api/v3/system/status")]
        public async Task AnonymousRoutes_PassWithoutKey_ButAreNotMarkedAuthenticated(string path)
        {
            var context = Request(path, "192.168.1.50");
            var (status, reached) = await Send(context);
            Assert.That(status, Is.EqualTo(200));
            Assert.That(reached, Is.True);
            Assert.That(context.Items.ContainsKey(ApiKeyAuthMiddleware.AuthenticatedItem), Is.False);
        }

        [Test]
        public async Task Status_WithWrongKey_IsRefused_SoTheKeyCheckInTheUiWorks()
        {
            var (status, _) = await Send(Request("/api/v3/system/status", "192.168.1.50", headers: ("X-Api-Key", "wrong")));
            Assert.That(status, Is.EqualTo(401));
        }

        [Test]
        public async Task NonApiPaths_AreNotGated()
        {
            var (status, reached) = await Send(Request("/library", "192.168.1.50"));
            Assert.That(status, Is.EqualTo(200));
            Assert.That(reached, Is.True);
        }

        // ---- LocalRequest ----

        [Test]
        public void LocalRequest_WithoutRecordedPeer_IsNotLocal()
        {
            var context = Request("/api/v3/x", "127.0.0.1");
            Assert.That(LocalRequest.IsLocal(context), Is.False);
            context.Items[LocalRequest.SocketPeerItem] = null;
            Assert.That(LocalRequest.IsLocal(context), Is.False);
        }

        [TestCase("X-Forwarded-For")]
        [TestCase("X-Forwarded-Host")]
        [TestCase("X-Forwarded-Proto")]
        [TestCase("Forwarded")]
        [TestCase("X-Real-IP")]
        [TestCase("X-Original-For")]
        public void LocalRequest_ThroughAProxy_IsNotLocal(string header)
        {
            var context = Request("/api/v3/x", "127.0.0.1", headers: (header, "x"));
            LocalRequest.CaptureSocketPeer(context);
            Assert.That(LocalRequest.IsLocal(context), Is.False);
        }

        [TestCase("rebind.evil.example")]
        [TestCase("192.168.1.10")]
        public void LocalRequest_WithForeignHost_IsNotLocal(string host)
        {
            // DNS rebinding: the browser reaches the loopback port under the attacker's name
            var context = Request("/api/v3/system/apikey/bootstrap", "127.0.0.1");
            context.Request.Host = new HostString(host, 5002);
            LocalRequest.CaptureSocketPeer(context);
            Assert.That(LocalRequest.IsLocal(context), Is.False);
        }

        [TestCase("localhost", true)]
        [TestCase("127.0.0.1", true)]
        [TestCase("[::1]", true)]
        [TestCase("", false)]
        public void LocalRequest_HostHeader(string host, bool local)
        {
            var context = Request("/api/v3/x", "127.0.0.1");
            context.Request.Host = host.Length == 0 ? new HostString() : new HostString(host + ":2727");
            LocalRequest.CaptureSocketPeer(context);
            Assert.That(LocalRequest.IsLocal(context), Is.EqualTo(local));
        }

        [TestCase("cross-site", false)]
        [TestCase("same-site", false)]
        [TestCase("same-origin", true)]
        [TestCase("none", true)]
        public void LocalRequest_BrowserFetchSite(string site, bool local)
        {
            // a page elsewhere firing requests at http://localhost:5002 (CSRF)
            var context = Request("/api/v3/system/apikey/rotate", "127.0.0.1", method: "POST", headers: ("Sec-Fetch-Site", site));
            LocalRequest.CaptureSocketPeer(context);
            Assert.That(LocalRequest.IsLocal(context), Is.EqualTo(local));
        }

        // ---- status / bootstrap ----

        private SystemController Controller(DefaultHttpContext context) =>
            new(_keys) { ControllerContext = new ControllerContext { HttpContext = context } };

        [Test]
        public void Status_Anonymous_OnlySaysOk()
        {
            var result = (OkObjectResult)Controller(Request("/api/v3/system/status", "192.168.1.50")).GetStatus();
            var json = System.Text.Json.JsonSerializer.Serialize(result.Value);
            Assert.That(json, Is.EqualTo("{\"status\":\"ok\"}"));
        }

        [Test]
        public void Status_Authenticated_HasVersionAndRuntime()
        {
            var context = Request("/api/v3/system/status", "192.168.1.50");
            context.Items[ApiKeyAuthMiddleware.AuthenticatedItem] = true;
            var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)Controller(context).GetStatus()).Value);
            Assert.That(json, Does.Contain("\"version\"").And.Contain("\"runtime\"").And.Contain("\"os\""));
        }

        [Test]
        public void Bootstrap_OnlyForTheSocketPeer_NotTheForwardedAddress()
        {
            var forged = Request("/api/v3/system/apikey/bootstrap", "172.17.0.1");
            LocalRequest.CaptureSocketPeer(forged);
            forged.Connection.RemoteIpAddress = IPAddress.Loopback; // what UseForwardedHeaders leaves behind
            Assert.That(Controller(forged).BootstrapApiKey(), Is.InstanceOf<NotFoundResult>());

            var local = Request("/api/v3/system/apikey/bootstrap", "127.0.0.1");
            LocalRequest.CaptureSocketPeer(local);
            var ok = (OkObjectResult)Controller(local).BootstrapApiKey();
            Assert.That(System.Text.Json.JsonSerializer.Serialize(ok.Value), Does.Contain(Key));
        }

        // ---- trusted proxies ----

        private static List<string> Networks(string? configured, List<string>? log = null)
        {
            var options = new ForwardedHeadersOptions();
            TrustedProxies.Apply(options, configured, m => log?.Add(m));
            Assert.That(options.KnownProxies, Is.Empty);
            return options.KnownNetworks.Select(n => $"{n.Prefix}/{n.PrefixLength}").ToList();
        }

        [Test]
        public void TrustedProxies_Default_IsPrivateRangesAndLoopback()
        {
            Assert.That(Networks(null), Is.EquivalentTo(new[] { "10.0.0.0/8", "172.16.0.0/12", "192.168.0.0/16", "127.0.0.0/8", "::1/128" }));
        }

        [Test]
        public void TrustedProxies_SingleAddresses_GetAFullPrefix()
        {
            Assert.That(Networks("172.20.0.5; fd00::1"), Is.EquivalentTo(new[] { "172.20.0.5/32", "fd00::1/128" }));
        }

        [Test]
        public void TrustedProxies_InvalidEntries_AreSkippedAndLogged()
        {
            var log = new List<string>();
            Assert.That(Networks("172.20.0.0/16,bogus,10.0.0.0/33,10.1.0.0/x", log), Is.EquivalentTo(new[] { "172.20.0.0/16" }));
            Assert.That(log, Has.Count.EqualTo(3));
        }

        [Test]
        public void TrustedProxies_NothingValid_FallsBackToLoopback_NotToEveryone()
        {
            var log = new List<string>();
            Assert.That(Networks("bogus, 300.1.1.1/8", log), Is.EquivalentTo(new[] { "127.0.0.0/8", "::1/128" }));
            Assert.That(log.Last(), Does.Contain("loopback only"));
        }
    }
}
