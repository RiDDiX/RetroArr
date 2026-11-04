using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download.History;
using RetroArr.Core.Games;
using RetroArr.Core.Wishlist;

namespace RetroArr.Core.Test.Data
{
    // Settings > Database and Logging: health, repair, reset and the log export of a library
    [TestFixture]
    public class DatabaseAdminTest
    {
        private const int Snes = 41;
        private const int Gba = 52;

        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private string _root = null!;
        private string _lib = null!;
        private ConfigurationService _config = null!;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        [SetUp]
        public void Setup()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(
                new Platform { Id = Snes, Name = "Super Nintendo", Slug = "snes", FolderName = "snes" },
                new Platform { Id = Gba, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
            ctx.SaveChanges();

            _root = Path.Combine(Path.GetTempPath(), "retroarr_dbadmin_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            // not on disk: nothing below may stat its way to an answer
            _lib = Path.Combine(_root, "library");
            _config = new ConfigurationService(_root);
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib });
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private DatabaseController Controller(ReviewItemService? reviews = null) =>
            new DatabaseController(_config, new DbFactory(_db), null!, new DuplicateGameMergeService(new DbFactory(_db), _config), reviews ?? new ReviewItemService(_config));

        // Missing is what the last scan found, as on the Problems page: a share that is not mounted is not a library
        // of missing games, and a stat of every path took minutes on spinning disks
        [Test]
        public async Task Health_MissingPath_IsWhatTheScanFound()
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Region = "", Path = Path.Combine(_lib, "snes", "Chrono Trigger.sfc") },
                    new Game { Id = 2, Title = "Earthbound", PlatformId = Snes, Region = "", Path = Path.Combine(_lib, "snes", "Earthbound.sfc"),
                        Status = GameStatus.Missing, MissingSince = DateTime.UtcNow },
                    new Game { Id = 3, Title = "Wanted", PlatformId = Snes, Region = "", Status = GameStatus.Missing });
                await ctx.SaveChangesAsync();
            }

            var report = (DatabaseHealthReport)((OkObjectResult)(await Controller().GetHealth()).Result!).Value!;

            Assert.That((report.TotalGames, report.GamesWithMissingPath), Is.EqualTo((3, 1)));
        }

        // Two regions of a title are not duplicates, for the health as for the merge of the repair
        [Test]
        public async Task Health_Duplicates_AreWhatTheMergeTakes()
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Zelda", PlatformId = Snes, Region = "USA" },
                    new Game { Id = 2, Title = "Zelda", PlatformId = Snes, Region = "Europe" },
                    new Game { Id = 3, Title = "Golden Sun", PlatformId = Gba, Region = "" },
                    new Game { Id = 4, Title = "GOLDEN SUN", PlatformId = Gba });
                await ctx.SaveChangesAsync();
            }

            var report = (DatabaseHealthReport)((OkObjectResult)(await Controller().GetHealth()).Result!).Value!;

            Assert.That((report.DuplicateClusterCount, report.DuplicateGames), Is.EqualTo((1, 2)));
        }

        // The repair reads the rows as columns and loads only what changes: the platforms and regions it writes and
        // the counts stay as they were
        [Test]
        public async Task Repair_WritesOnlyTheRowsItChanges()
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.AddRange(
                    // on GBA, in the SNES folder
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Gba, Region = "USA", Path = Path.Combine(_lib, "snes", "Chrono Trigger (USA).sfc") },
                    new Game { Id = 2, Title = "Earthbound", PlatformId = Snes, Path = Path.Combine(_lib, "snes", "Earthbound.sfc") },
                    // two untagged rows of a title stay untagged for the merge
                    new Game { Id = 3, Title = "Golden Sun", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Golden Sun.gba") },
                    new Game { Id = 4, Title = "GOLDEN SUN", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Golden Sun (Rev 1).gba") },
                    new Game { Id = 5, Title = "Icon", PlatformId = Snes, Region = "", Path = Path.Combine(_lib, "snes", "Game.ps4", "sce_sys", "icon0.png") });
                await ctx.SaveChangesAsync();
            }

            var result = (DatabaseRepairResult)((OkObjectResult)(await Controller().Repair(new DatabaseRepairRequest())).Result!).Value!;

            Assert.That((result.RegionsCanonicalised, result.PlatformsHealed, result.ContainerOrphansRemoved, result.OrphansFixed),
                Is.EqualTo((1, 1, 1, 0)));
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.Games.AsNoTracking().OrderBy(g => g.Id).AsEnumerable().Select(g => (g.Id, g.PlatformId, g.Region)), Is.EqualTo(new[]
            {
                (1, Snes, (string?)"USA"),
                (2, Snes, (string?)""),
                (3, Gba, (string?)null),
                (4, Gba, (string?)null)
            }));
        }

        [Test]
        public async Task ResetLibrary_EmptiesTheLibraryTables_AndKeepsTagsAndHistory()
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Region = "", Images = { CoverUrl = "cover" } },
                    new Game { Id = 2, Title = "Golden Sun", PlatformId = Gba, Region = "" });
                ctx.GameFiles.Add(new GameFile { GameId = 1, RelativePath = "Chrono Trigger.sfc", FileType = "Main" });
                ctx.Collections.Add(new Collection { Id = 1, Name = "Favourites" });
                ctx.CollectionGames.Add(new CollectionGame { CollectionId = 1, GameId = 1 });
                ctx.Tags.Add(new Tag { Id = 1, Name = "played" });
                ctx.GameTags.Add(new GameTag { TagId = 1, GameId = 2 });
                ctx.GameReviews.Add(new GameReview { GameId = 1, Notes = "classic" });
                ctx.WishlistPriceWatches.Add(new WishlistPriceWatch { GameId = 2, Provider = "steam", ExternalId = "1" });
                ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "abc", Title = "Chrono Trigger", GameId = 1 });
                await ctx.SaveChangesAsync();
            }
            var controller = Controller();
            var challenge = (DatabaseResetChallenge)((OkObjectResult)(await controller.ResetChallenge("library")).Result!).Value!;

            var result = await controller.ResetLibrary(new DatabaseResetRequest { Token = challenge.Token, Confirmation = challenge.Confirmation });

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_db);
            Assert.That(new[] { check.Games.Count(), check.GameFiles.Count(), check.Collections.Count(), check.CollectionGames.Count(),
                check.GameTags.Count(), check.GameReviews.Count(), check.WishlistPriceWatches.Count() }, Is.All.Zero);
            Assert.That((check.Tags.Count(), check.Platforms.Count(), check.DownloadHistory.Count()), Is.EqualTo((1, 2, 1)));
        }

        // The review list's service holds the items in memory too, so its next change must not write the wiped ones back
        [Test]
        public async Task ResetLibrary_EmptiesTheReviewList()
        {
            var reviews = new ReviewItemService(_config);
            reviews.Add(new ReviewItem { FilePaths = { Path.Combine(_lib, "snes", "Chrono Trigger.sfc") } });
            var controller = Controller(reviews);
            var challenge = (DatabaseResetChallenge)((OkObjectResult)(await controller.ResetChallenge("library")).Result!).Value!;

            await controller.ResetLibrary(new DatabaseResetRequest { Token = challenge.Token, Confirmation = challenge.Confirmation });
            var later = reviews.Add(new ReviewItem { FilePaths = { Path.Combine(_lib, "gba", "Golden Sun.gba") } });
            reviews.Dispose();

            Assert.That(reviews.GetAll().Select(i => i.Id), Is.EqualTo(new[] { later.Id }));
            Assert.That(new ReviewItemService(_config).GetAll().Select(i => i.Id), Is.EqualTo(new[] { later.Id }));
        }

        [Test]
        public async Task ResetDownloadHistory_EmptiesHistoryAndBlacklist()
        {
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Region = "" });
                ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "abc", Title = "Chrono Trigger", GameId = 1 });
                ctx.DownloadBlacklist.Add(new DownloadBlacklistEntry { DownloadId = "def", Title = "Chrono Trigger", Reason = "bad dump" });
                await ctx.SaveChangesAsync();
            }
            var controller = Controller();
            var challenge = (DatabaseResetChallenge)((OkObjectResult)(await controller.ResetChallenge("download-history")).Result!).Value!;

            var result = await controller.ResetDownloadHistory(new DatabaseResetRequest { Token = challenge.Token, Confirmation = challenge.Confirmation });

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            using var check = new RetroArrDbContext(_db);
            Assert.That((check.DownloadHistory.Count(), check.DownloadBlacklist.Count(), check.Games.Count()), Is.EqualTo((0, 0, 1)));
        }

        // The export reads the logs line by line: every line is in the zip, with its keys redacted
        [Test]
        public void ExportLogs_RedactsEveryLine()
        {
            var logs = Directory.CreateDirectory(Path.Combine(_root, "logs")).FullName;
            _config.SaveLoggingSettings(new LoggingSettings { LogDirectory = logs, RedactTokens = true });
            var lines = new[]
            {
                "2026-11-03 12:00:00.0000|INFO |-|RetroArr.General|Scan started",
                "2026-11-03 12:00:01.0000|DEBUG|-|RetroArr.Download|GET http://prowlarr:9696/api/v1/search?query=x&apikey=0123456789abcdef",
                "2026-11-03 12:00:02.0000|DEBUG|-|RetroArr.Download|GET http://nzb/api?t=get&r=fedcba9876543210&id=1"
            };
            File.WriteAllLines(Path.Combine(logs, "app.log"), lines);

            var result = (FileStreamResult)new LoggingController(_config, null!).ExportDiagnostics(new ExportRequest { TimeRange = "24h" });

            using var zip = new ZipArchive(result.FileStream);
            using var reader = new StreamReader(zip.GetEntry("app.log")!.Open());
            Assert.That(reader.ReadToEnd().Split('\n', StringSplitOptions.RemoveEmptyEntries), Is.EqualTo(new[]
            {
                lines[0],
                "2026-11-03 12:00:01.0000|DEBUG|-|RetroArr.Download|GET http://prowlarr:9696/api/v1/search?query=x&apikey=[REDACTED]",
                "2026-11-03 12:00:02.0000|DEBUG|-|RetroArr.Download|GET http://nzb/api?t=get&r=[REDACTED]&id=1"
            }));
        }
    }
}
