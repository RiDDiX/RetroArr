using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using RetroArr.Api.V3.DownloadClients;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
using RetroArr.Core.Download.TrackedDownloads;

namespace RetroArr.Core.Test.Security
{
    [TestFixture]
    public class DownloadClientSecretsTest
    {
        private const string Placeholder = DownloadClientController.SecretPlaceholder;
        private string _root = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_dlsecrets_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _config = new ConfigurationService(_root);
            _config.SaveDownloadClients(new List<DownloadClient>
            {
                new() { Id = 1, Name = "qbit", Implementation = "qBittorrent", Host = "qbit", Port = 8080, Username = "admin", Password = "s3cret-pass", Category = "RetroArr" },
                new() { Id = 2, Name = "sab", Implementation = "SABnzbd", Host = "sab", Port = 8085, ApiKey = "sab-api-key-123" },
                new() { Id = 3, Name = "open", Implementation = "Transmission", Host = "tr", Port = 9091 },
            });
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true);
        }

        // Only the configuration service is needed for the client list endpoints
        private DownloadClientController Controller() => new(_config, null!, null!, null!, null!, null!, null!, null!);

        private static T Value<T>(ActionResult<T> result) => (T)((ObjectResult)result.Result!).Value!;

        [Test]
        public void List_AndSingleClient_NeverContainTheSecrets()
        {
            var clients = Value(Controller().GetAll());

            Assert.That(clients.Single(c => c.Id == 1).Password, Is.EqualTo(Placeholder));
            Assert.That(clients.Single(c => c.Id == 2).ApiKey, Is.EqualTo(Placeholder));
            Assert.That(clients.Single(c => c.Id == 3).Password, Is.Null.Or.Empty, "no placeholder when nothing is stored");
            Assert.That(Value(Controller().GetById(1)).Password, Is.EqualTo(Placeholder));

            // the stored values are untouched
            var stored = _config.LoadDownloadClients();
            Assert.That(stored.Single(c => c.Id == 1).Password, Is.EqualTo("s3cret-pass"));
            Assert.That(stored.Single(c => c.Id == 2).ApiKey, Is.EqualTo("sab-api-key-123"));
        }

        [Test]
        public void SavingTheFormUnchanged_KeepsTheStoredSecret()
        {
            var form = Value(Controller().GetById(1));
            form.Enable = false;

            var response = Value(Controller().Update(1, form));

            Assert.That(response.Password, Is.EqualTo(Placeholder));
            var stored = _config.LoadDownloadClients().Single(c => c.Id == 1);
            Assert.That(stored.Password, Is.EqualTo("s3cret-pass"));
            Assert.That(stored.Enable, Is.False);
        }

        [Test]
        public void NewOrClearedSecret_IsSaved()
        {
            var form = Value(Controller().GetById(1));
            form.Password = "new-pass";
            Controller().Update(1, form);
            Assert.That(_config.LoadDownloadClients().Single(c => c.Id == 1).Password, Is.EqualTo("new-pass"));

            form.Password = "";
            Controller().Update(1, form);
            Assert.That(_config.LoadDownloadClients().Single(c => c.Id == 1).Password, Is.Empty);
        }

        [Test]
        public async Task PlaceholderWithChangedHost_IsRefused_OnUpdateAndTest()
        {
            var form = Value(Controller().GetById(1));
            form.Host = "evil.example";
            Assert.That(Controller().Update(1, form).Result, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(_config.LoadDownloadClients().Single(c => c.Id == 1).Host, Is.EqualTo("qbit"));

            var request = new TestDownloadClientRequest { Id = 1, Implementation = "qBittorrent", Host = "evil.example", Port = 8080, Password = Placeholder };
            Assert.That(await Controller().TestConnection(request), Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task PlaceholderWithSameHost_PassesAndKeepsTheSecret()
        {
            var form = Value(Controller().GetById(1));
            form.Host = "QBIT";
            Assert.That(Controller().Update(1, form).Result, Is.InstanceOf<ObjectResult>().And.Not.InstanceOf<BadRequestObjectResult>());
            Assert.That(_config.LoadDownloadClients().Single(c => c.Id == 1).Password, Is.EqualTo("s3cret-pass"));

            var request = new TestDownloadClientRequest { Id = 1, Implementation = "qBittorrent", Host = "qbit", Port = 8080, Password = Placeholder };
            var result = await Controller().TestConnection(request);
            // the connection itself may fail here; what matters is that the address guard didn't refuse
            Assert.That(result is BadRequestObjectResult bad && bad.Value!.ToString()!.Contains("address changed"), Is.False);
        }

        [Test]
        public void Update_SavesUseSsl()
        {
            var form = Value(Controller().GetById(3));
            form.UseSsl = true;
            Controller().Update(3, form);
            Assert.That(_config.LoadDownloadClients().Single(c => c.Id == 3).UseSsl, Is.True);
        }

        [Test]
        public async Task Create_ReturnsTheClientWithoutItsSecret()
        {
            // Creating looks at tracked downloads and history for the ids in use
            var history = new DownloadHistoryRepository(new PooledDbContextFactory<RetroArrDbContext>(
                new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Path.GetRandomFileName()).Options));
            var controller = new DownloadClientController(_config, null!, null!, new TrackedDownloadService(Path.Combine(_root, "config")), null!, history, null!, null!);

            var created = (CreatedAtActionResult)(await controller.Create(new DownloadClient { Name = "deluge", Implementation = "Deluge", Host = "d", Port = 8112, Password = "deluge-pass" })).Result!;
            Assert.That(((DownloadClient)created.Value!).Password, Is.EqualTo(Placeholder));
            Assert.That(_config.LoadDownloadClients().Single(c => c.Name == "deluge").Password, Is.EqualTo("deluge-pass"));
        }

        [Test]
        public void QueueDelete_DoesNotDeleteFilesUnlessAsked()
        {
            var parameter = typeof(DownloadClientController).GetMethod(nameof(DownloadClientController.DeleteDownload))!
                .GetParameters().Single(p => p.Name == "deleteFiles");
            Assert.That(parameter.DefaultValue, Is.EqualTo(false));
        }

        private sealed class FakeClient : IDownloadClient
        {
            public List<DownloadStatus> Downloads { get; } = new();
            public Task<bool> TestConnectionAsync() => Task.FromResult(true);
            public Task<string> GetVersionAsync() => Task.FromResult("1");
            public Task<bool> AddTorrentAsync(string url, string? category = null) => Task.FromResult(true);
            public Task<bool> AddNzbAsync(string url, string? category = null) => Task.FromResult(true);
            public Task<bool> RemoveDownloadAsync(string id, bool deleteFiles) => Task.FromResult(true);
            public Task<bool> PauseDownloadAsync(string id) => Task.FromResult(true);
            public Task<bool> ResumeDownloadAsync(string id) => Task.FromResult(true);
            public Task<List<DownloadStatus>> GetDownloadsAsync() => Task.FromResult(Downloads);
        }

        [Test]
        public async Task QueueActions_OnlyReachTheConfiguredCategory()
        {
            var client = new FakeClient();
            client.Downloads.Add(new DownloadStatus { Id = "ours", Category = "retroarr" });
            client.Downloads.Add(new DownloadStatus { Id = "sonarr", Category = "tv-sonarr" });
            client.Downloads.Add(new DownloadStatus { Id = "nocat" });
            var config = new DownloadClient { Category = "RetroArr" };

            Assert.That(await DownloadClientController.FindManagedIdAsync(config, client, "ours"), Is.EqualTo("ours"));
            Assert.That(await DownloadClientController.FindManagedIdAsync(config, client, "OURS"), Is.EqualTo("ours"), "the client gets its own spelling");
            Assert.That(await DownloadClientController.FindManagedIdAsync(config, client, "sonarr"), Is.Null);
            Assert.That(await DownloadClientController.FindManagedIdAsync(config, client, "nocat"), Is.Null);
            Assert.That(await DownloadClientController.FindManagedIdAsync(config, client, "missing"), Is.Null);

            // without a category the queue shows the whole client, so everything listed in it counts...
            var open = new DownloadClient();
            Assert.That(await DownloadClientController.FindManagedIdAsync(open, client, "sonarr"), Is.EqualTo("sonarr"));
            // ...but only single, listed ids: qBittorrent reads "all" and "a|b", SABnzbd "all" and lists
            Assert.That(await DownloadClientController.FindManagedIdAsync(open, client, "all"), Is.Null);
            Assert.That(await DownloadClientController.FindManagedIdAsync(open, client, "ours|sonarr"), Is.Null);
            Assert.That(await DownloadClientController.FindManagedIdAsync(open, client, "ours,sonarr"), Is.Null);
        }
    }
}
