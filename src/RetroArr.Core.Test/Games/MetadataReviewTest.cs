using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Metadata;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class MetadataReviewTest
    {
        private const int Gba = 52;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // IGDB that knows Advance Wars as id 7
        private sealed class FakeIgdb : HttpMessageHandler, IGameMetadataServiceFactory
        {
            public GameMetadataService CreateService()
            {
                var igdb = new IgdbClient("id", "secret");
                typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(this, false));
                return new GameMetadataService(igdb, new SteamClient());
            }

            public void RefreshConfiguration() { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = request.RequestUri!.Host == "api.igdb.com"
                    ? "[{\"id\":7,\"name\":\"Advance Wars\",\"cover\":{\"image_id\":\"cover7\"}}]"
                    : "{\"access_token\":\"t\",\"expires_in\":3600}";
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") });
            }
        }

        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            // SQLite, so the unique indexes are what an install has
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.Add(new Platform { Id = Gba, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
            ctx.SaveChanges();
        }

        [TearDown]
        public void TearDown() => _connection.Dispose();

        // The scanner made the Europe release an entry of its own; the USA entry holds the IGDB id on the platform
        [Test]
        public async Task Confirm_RegionalRelease_KeepsTheEntry_WithoutTheOtherRegionsId()
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Advance Wars", Region = "USA", PlatformId = Gba, IgdbId = 7 },
                    new Game { Id = 2, Title = "Advance Wars", Region = "Europe", PlatformId = Gba, NeedsMetadataReview = true,
                        MetadataReviewReason = "Regional release of 'Advance Wars', IGDB id 7 kept on entry 1 (USA)" });
                await ctx.SaveChangesAsync();
            }
            var controller = new MetadataReviewController(new SqliteGameRepository(new DbFactory(_db)), new FakeIgdb(), new LocalMediaExportService(new HttpClient()));

            await controller.ConfirmMatch(2, new ConfirmMatchRequest { IgdbId = 7 });

            using var check = new RetroArrDbContext(_db);
            var rows = check.Games.AsNoTracking().OrderBy(g => g.Id).ToList();
            Assert.That(rows.Select(g => (g.Id, g.Region, g.IgdbId)), Is.EqualTo(new[] { (1, "USA", (int?)7), (2, "Europe", (int?)null) }));
            Assert.That((rows[1].MetadataConfirmedByUser, rows[1].NeedsMetadataReview), Is.EqualTo((true, false)));
            Assert.That(rows[1].MetadataReviewReason, Does.StartWith("IGDB id 7 stays with entry 1"));
        }
    }
}
