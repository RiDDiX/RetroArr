using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Games;
using RetroArr.Api.V3.Metadata;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download.History;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;
using RetroArr.Core.Wishlist;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class MetadataReviewTest
    {
        private const int Gba = 52;
        private const int Snes = 41;

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
        private string _root = null!;
        private string _lib = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            // SQLite, so the unique indexes are what an install has
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(
                new Platform { Id = Gba, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" },
                new Platform { Id = Snes, Name = "Super Nintendo", Slug = "snes", FolderName = "snes" });
            ctx.SaveChanges();

            _root = Path.Combine(Path.GetTempPath(), "retroarr_review_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
            _config = new ConfigurationService(_root);
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib });
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // The media export fetches covers for a game with files, so it goes to the fake as well
        private MetadataReviewController Controller(IGameRepository? repo = null) =>
            new(repo ?? new SqliteGameRepository(new DbFactory(_db)), new FakeIgdb(), new LocalMediaExportService(new HttpClient(new FakeIgdb())),
                new DuplicateGameMergeService(new DbFactory(_db), _config));

        private ReviewController Review(ReviewItemService reviews) =>
            new(reviews, new SqliteGameRepository(new DbFactory(_db)), new DuplicateGameMergeService(new DbFactory(_db), _config));

        private Game Row(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.AsNoTracking().Single(g => g.Id == id);
        }

        private static string Put(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, "rom");
            return path;
        }

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
            await Controller().ConfirmMatch(2, new ConfirmMatchRequest { IgdbId = 7 });

            using var check = new RetroArrDbContext(_db);
            var rows = check.Games.AsNoTracking().OrderBy(g => g.Id).ToList();
            Assert.That(rows.Select(g => (g.Id, g.Region, g.IgdbId)), Is.EqualTo(new[] { (1, "USA", (int?)7), (2, "Europe", (int?)null) }));
            Assert.That((rows[1].MetadataConfirmedByUser, rows[1].NeedsMetadataReview), Is.EqualTo((true, false)));
            Assert.That(rows[1].MetadataReviewReason, Does.StartWith("IGDB id 7 stays with entry 1"));
        }

        // The reviewed entry is a second copy of an entry of its region and goes. What hangs off it moves over, and
        // the entry that stays keeps a path whose content is there; with none or a lost one it takes the copy's.
        [TestCase("none")]
        [TestCase("gone")]
        [TestCase("present")]
        public async Task Confirm_SecondCopyOfAnEntry_MergesItsPathAndRowsIntoTheEntry(string survivorPath)
        {
            var gba = Path.Combine(_lib, "gba");
            var copy = Put(gba, "Advance Wars (USA) (Rev 1).gba");
            var own = survivorPath switch
            {
                "gone" => Path.Combine(gba, "Advance Wars (USA).gba"),
                "present" => Put(gba, "Advance Wars (USA).gba"),
                _ => null
            };
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = own, ExecutablePath = own, Monitored = true,
                        Status = own == null ? GameStatus.Missing : GameStatus.Released },
                    new Game { Id = 2, Title = "Advnce Wars", PlatformId = Gba, Path = copy, ExecutablePath = copy, Status = GameStatus.Released,
                        NeedsMetadataReview = true, MetadataReviewReason = "Low confidence" });
                ctx.Collections.Add(new Collection { Id = 1, Name = "Favourites" });
                ctx.CollectionGames.Add(new CollectionGame { CollectionId = 1, GameId = 2 });
                ctx.Tags.Add(new Tag { Id = 1, Name = "played" });
                ctx.GameTags.Add(new GameTag { TagId = 1, GameId = 2 });
                ctx.GameFiles.Add(new GameFile { GameId = 2, RelativePath = "Advance Wars (USA) (Rev 1).gba", Size = 3, FileType = "Main" });
                ctx.GameReviews.Add(new GameReview { GameId = 2, Notes = "copy" });
                ctx.WishlistPriceWatches.Add(new WishlistPriceWatch { GameId = 2, Provider = "steam", ExternalId = "1" });
                ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "abc", Title = "Advance Wars", GameId = 2 });
                await ctx.SaveChangesAsync();
            }

            await Controller().ConfirmMatch(2, new ConfirmMatchRequest { IgdbId = 7 });

            using var check = new RetroArrDbContext(_db);
            var survivor = check.Games.AsNoTracking().Single();
            Assert.That((survivor.Id, survivor.IgdbId, survivor.MetadataConfirmedByUser, survivor.NeedsMetadataReview), Is.EqualTo((1, (int?)7, true, false)));
            var expected = survivorPath == "present" ? own : copy;
            Assert.That((survivor.Path, survivor.ExecutablePath, survivor.Status, survivor.Monitored), Is.EqualTo((expected, expected, GameStatus.Downloaded, true)));
            Assert.That(check.CollectionGames.Select(r => r.GameId)
                .Concat(check.GameTags.Select(r => r.GameId))
                .Concat(check.GameFiles.Select(r => r.GameId))
                .Concat(check.GameReviews.Select(r => r.GameId))
                .Concat(check.WishlistPriceWatches.Select(r => r.GameId))
                .Concat(check.DownloadHistory.Select(r => r.GameId!.Value)), Is.EqualTo(new[] { 1, 1, 1, 1, 1, 1 }));
        }

        // The user monitors the entry and an import points it at its file after the review read it, right before the
        // review writes it: both stay
        [TestCase("confirm")]
        [TestCase("skip")]
        [TestCase("dismiss")]
        public async Task Review_EditMadeAfterTheRead_Stays(string action)
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Advnce Wars", PlatformId = Gba, NeedsMetadataReview = true, MetadataReviewReason = "Low confidence" });
                await ctx.SaveChangesAsync();
            }
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, args) =>
            {
                if (method.Name is not (nameof(IGameRepository.UpdateAsync) or nameof(IGameRepository.UpdateFieldsAsync))) return;
                using var ctx = new RetroArrDbContext(_db);
                var game = ctx.Games.Single(g => g.Id == (int)args![0]!);
                game.Monitored = true;
                game.Path = "/gba/Advance Wars.gba";
                ctx.SaveChanges();
            };
            var controller = Controller(repo);

            var result = action switch
            {
                "confirm" => await controller.ConfirmMatch(1, new ConfirmMatchRequest { IgdbId = 7 }),
                "skip" => await controller.SkipReview(1),
                _ => await controller.DismissReview(1)
            };

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var row = Row(1);
            Assert.That((row.Monitored, row.Path), Is.EqualTo((true, "/gba/Advance Wars.gba")));
            Assert.That((row.MetadataConfirmedByUser, row.NeedsMetadataReview), Is.EqualTo((true, false)));
        }

        // A wanted game got its folder when it was added to the library; the file the user finalizes into it is the
        // game (flat beside the folder, or inside it). The game points at the file and is no longer searched.
        [TestCase(false)]
        [TestCase(true)]
        public async Task Finalize_IntoAWantedGame_LinksTheFileAndEndsTheSearch(bool inFolder)
        {
            var folder = Directory.CreateDirectory(Path.Combine(_lib, "snes", "Chrono Trigger")).FullName;
            var file = Put(inFolder ? folder : Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Path = folder, Status = GameStatus.Missing, Monitored = true });
                await ctx.SaveChangesAsync();
            }
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(new ReviewItem { FilePaths = { file }, DetectedTitle = "Chrono Trigger", AssignedPlatformId = Snes, AssignedGameId = 1 });

            var result = await Review(reviews).FinalizeItem(item.Id);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var row = Row(1);
            Assert.That((row.Path, row.ExecutablePath), Is.EqualTo((inFolder ? folder : file, file)));
            Assert.That((row.Status, row.MissingSince), Is.EqualTo((GameStatus.Downloaded, (DateTime?)null)));
            Assert.That(reviews.GetById(item.Id)!.Status, Is.EqualTo(ReviewStatus.Finalized));
        }

        // Without an assigned game the item goes to the entry of its title on the platform in its region, or else to
        // an untagged one (here the wanted one, which takes the region), by the same rules, instead of a second entry
        // the unique index refuses
        [TestCase("USA")]
        [TestCase("")]
        public async Task Finalize_TitleOfAnEntry_GoesToThatEntry(string entryRegion)
        {
            var folder = Directory.CreateDirectory(Path.Combine(_lib, "snes", "Chrono Trigger")).FullName;
            var file = Put(Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Region = entryRegion, Path = folder, Status = GameStatus.Missing, Monitored = true });
                await ctx.SaveChangesAsync();
            }
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(new ReviewItem { FilePaths = { file }, DetectedTitle = "Chrono Trigger", Region = "USA", AssignedPlatformId = Snes });

            var result = await Review(reviews).FinalizeItem(item.Id);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_db);
            var row = check.Games.AsNoTracking().Single();
            Assert.That((row.Id, row.Path, row.ExecutablePath, row.Status, row.Region), Is.EqualTo((1, file, file, GameStatus.Downloaded, "USA")));
        }

        // Titles are compared whatever the case of letters beyond ASCII ("ÉLITE BEAT AGENTS" is "Élite Beat Agents")
        [Test]
        public async Task Finalize_TitleWithCapitalsBeyondAscii_GoesToThatEntry()
        {
            var file = Put(Path.Combine(_lib, "snes"), "Elite Beat Agents.sfc");
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "ÉLITE BEAT AGENTS", PlatformId = Snes, Status = GameStatus.Missing, Monitored = true });
                await ctx.SaveChangesAsync();
            }
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(new ReviewItem { FilePaths = { file }, DetectedTitle = "Élite Beat Agents", AssignedPlatformId = Snes });

            var result = await Review(reviews).FinalizeItem(item.Id);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.Games.AsNoTracking().AsEnumerable().Select(g => (g.Id, g.Path)), Is.EqualTo(new[] { (1, file) }));
        }

        // A game whose share is offline keeps its path: its content is not known to be lost
        [Test]
        public async Task Finalize_GameOnAnOfflineShare_KeepsItsPath()
        {
            var offline = Path.Combine(_root, "offline");
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = offline });
            var own = Path.Combine(offline, "snes", "Chrono Trigger");
            var file = Put(Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Path = own, Status = GameStatus.Downloaded });
                await ctx.SaveChangesAsync();
            }
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(new ReviewItem { FilePaths = { file }, DetectedTitle = "Chrono Trigger", AssignedPlatformId = Snes, AssignedGameId = 1 });

            await Review(reviews).FinalizeItem(item.Id);

            Assert.That((Row(1).Path, Row(1).ExecutablePath), Is.EqualTo((own, (string?)null)));
        }

        // A new entry runs from a file only, and is Downloaded only when its content is there
        [TestCase(true)]
        [TestCase(false)]
        public async Task Finalize_NewEntry_ItsContentDecidesItsStatus(bool folderWithContent)
        {
            var snes = Path.Combine(_lib, "snes");
            var path = folderWithContent ? Path.Combine(snes, "Chrono Trigger") : Path.Combine(snes, "Chrono Trigger (USA).sfc");
            if (folderWithContent) Put(path, "Chrono Trigger (USA).sfc");
            var reviews = new ReviewItemService(_config);
            var item = reviews.Add(new ReviewItem { FilePaths = { path }, DetectedTitle = "Chrono Trigger", AssignedPlatformId = Snes });

            var result = await Review(reviews).FinalizeItem(item.Id);

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_db);
            var row = check.Games.AsNoTracking().Single();
            Assert.That((row.Path, row.ExecutablePath, row.Status),
                Is.EqualTo((path, (string?)null, folderWithContent ? GameStatus.Downloaded : GameStatus.Released)));
        }
    }
}
