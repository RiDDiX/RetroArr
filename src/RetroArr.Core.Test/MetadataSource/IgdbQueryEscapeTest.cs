using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.Igdb;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class IgdbQueryEscapeTest
    {
        private sealed class Recorder : HttpMessageHandler
        {
            public string Query = "";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = "{\"access_token\":\"t\",\"expires_in\":3600}";
                if (request.RequestUri!.Host == "api.igdb.com")
                {
                    Query = await request.Content!.ReadAsStringAsync(ct);
                    body = "[]";
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        }

        // A quote in a title would end the search string early and IGDB answers 400
        [Test]
        public async Task Search_QuoteAndBackslashInTheTitle_AreEscaped()
        {
            var recorder = new Recorder();
            var igdb = new IgdbClient("id", "secret");
            typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(recorder, false));

            await igdb.SearchGamesAsync("Odin \"Sphere\" a\\b");

            Assert.That(recorder.Query, Does.Contain("search \"Odin \\\"Sphere\\\" a\\\\b\";"));
        }
    }
}
