using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.MetadataSource.Gog;

namespace RetroArr.Core.Test.Gog
{
    [TestFixture]
    public class GogTokenTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_gog_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly string _body;
            public FakeHandler(string body) => _body = body;
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(_body) });
        }

        private static GogClient Client(string body)
        {
            var client = new GogClient("old-refresh");
            typeof(GogClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(client, new HttpClient(new FakeHandler(body)));
            return client;
        }

        [Test]
        public async Task Refresh_KeepsTheNewRefreshToken()
        {
            var client = Client("{\"access_token\":\"a\",\"refresh_token\":\"new-refresh\",\"expires_in\":3600}");
            Assert.That(await client.RefreshTokenAsync("id", "secret"), Is.True);
            Assert.That(client.RefreshToken, Is.EqualTo("new-refresh"));
        }

        [Test]
        public async Task Refresh_WithoutANewToken_KeepsTheOldOne()
        {
            var client = Client("{\"access_token\":\"a\",\"expires_in\":3600}");
            Assert.That(await client.RefreshTokenAsync("id", "secret"), Is.True);
            Assert.That(client.RefreshToken, Is.EqualTo("old-refresh"));
        }

        [Test]
        public void SaveGogRefreshToken_Persists_AndIgnoresUnchangedOrEmpty()
        {
            var config = new ConfigurationService(_root);
            config.SaveGogRefreshToken("t1");
            Assert.That(config.LoadGogSettings().RefreshToken, Is.EqualTo("t1"));

            config.SaveGogRefreshToken("t1");
            config.SaveGogRefreshToken(null);
            config.SaveGogRefreshToken("");
            Assert.That(config.LoadGogSettings().RefreshToken, Is.EqualTo("t1"));

            config.SaveGogRefreshToken("t2");
            Assert.That(config.LoadGogSettings().RefreshToken, Is.EqualTo("t2"));
        }
    }
}
