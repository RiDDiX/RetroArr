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
    public class TransmissionClientTest
    {
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
                    var body = "{\"arguments\":{\"torrents\":[{\"id\":1,\"name\":\"Some.Game-GRP\",\"totalSize\":1,\"percentDone\":1," +
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
