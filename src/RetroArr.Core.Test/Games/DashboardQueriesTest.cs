using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RetroArr.Api.V3.Collections;
using RetroArr.Api.V3.Dashboard;
using RetroArr.Api.V3.Linux;
using RetroArr.Api.V3.Monitor;
using RetroArr.Api.V3.Tags;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.Linux;

namespace RetroArr.Core.Test.Games
{
    // The dashboard, tag, collection and platform monitor endpoints count and update in SQL.
    // PostgreSQL and MariaDB run only with RETROARR_TEST_PG / RETROARR_TEST_MARIADB set (see ProviderIntegrationTest).
    [TestFixture]
    public class DashboardQueriesTest
    {
        private const int Nes = 40;
        private const int Snes = 41;
        private SqliteConnection? _sqlite;

        [TearDown]
        public void TearDown() => _sqlite?.Dispose();

        private DbContextOptions<RetroArrDbContext> Database(DatabaseType type, string? variable)
        {
            var builder = new DbContextOptionsBuilder<RetroArrDbContext>();
            if (type == DatabaseType.SQLite)
            {
                // Shared in-memory database: the migrator closes its connection, the open one keeps the data
                var connectionString = $"DataSource=file:{Guid.NewGuid():N}?mode=memory&cache=shared";
                _sqlite = new SqliteConnection(connectionString);
                _sqlite.Open();
                builder.UseSqlite(connectionString);
            }
            else
            {
                var connectionString = Environment.GetEnvironmentVariable(variable!);
                if (string.IsNullOrEmpty(connectionString)) Assert.Ignore($"{variable} is not set");
                // Retrying as in production: that strategy buffers every query's whole result
                if (type == DatabaseType.PostgreSQL) builder.UseNpgsql(connectionString, o => o.EnableRetryOnFailure(3));
                else builder.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString), o => o.EnableRetryOnFailure(3));
            }

            using var ctx = new RetroArrDbContext(builder.Options);
            if (type != DatabaseType.SQLite) ctx.Database.EnsureDeleted();
            DatabaseMigrator.EnsureSchema(ctx);
            DatabaseMigrator.ApplyMigrations(ctx, type);
            return builder.Options;
        }

        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task Stats_CountTheLibrary(DatabaseType type, string? variable)
        {
            var options = Database(type, variable);
            var at = new DateTime(2025, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            var i = 0;
            Game G(string title, int platform, double? rating, int year, GameStatus status, string? install = null, bool external = false, params string[] genres) =>
                new Game { Title = title, PlatformId = platform, Rating = rating, Year = year, Status = status, InstallPath = install, IsExternal = external, Genres = genres.ToList(), Added = at.AddDays(++i) };
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Games.AddRange(
                    G("A", Nes, 85, 1988, GameStatus.Downloaded, "/a", true, "Shooter", "Action"),
                    G("B", Nes, 80, 1988, GameStatus.Missing, "", false, "Action"),
                    G("C", Nes, 79.9, 1970, GameStatus.Downloaded),
                    // A blank path counts as installed, as string.IsNullOrEmpty did
                    G("D", Snes, 60, 1991, GameStatus.Downloaded, " ", false, "Action", "RPG"),
                    G("E", Snes, 59, 1991, GameStatus.Released, null, false, "RPG"),
                    G("F", Nes, 40, 1995, GameStatus.Downloaded, null, true, "Action"),
                    G("G", Nes, 10, 0, GameStatus.Missing),
                    G("H", Nes, 0, 2001, GameStatus.Downloaded),
                    G("I", Snes, null, 2001, GameStatus.Downloaded),
                    G("J", Nes, -5, 2001, GameStatus.Downloaded),
                    G("K", Nes, 50, 1980, GameStatus.Downloaded));
                ctx.Games.Local.Single(g => g.Title == "K").Images.CoverUrl = "k.jpg";
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var stats = Json(await new DashboardController(ctx, null!).GetStats());
                Assert.Multiple(() =>
                {
                    Assert.That((int)stats["TotalGames"]!, Is.EqualTo(11));
                    Assert.That((int)stats["InstalledGames"]!, Is.EqualTo(2));
                    Assert.That((int)stats["ExternalGames"]!, Is.EqualTo(2));
                    Assert.That(Pairs(stats["GenreStats"]!, "Genre"), Is.EqualTo(new[] { "Action=4", "RPG=2", "Shooter=1" }));
                    Assert.That(Pairs(stats["PlatformStats"]!, "PlatformId"), Is.EqualTo(new[] { $"{Nes}=8", $"{Snes}=3" }));
                    Assert.That((string)stats["PlatformStats"]![0]!["Platform"]!, Is.EqualTo(PlatformDefinitions.AllPlatforms.Single(p => p.Id == Nes).Name));
                    Assert.That(Pairs(stats["YearStats"]!, "Year"), Is.EqualTo(new[] { "2001=3", "1995=1", "1991=2", "1988=2", "1980=1" }));
                    Assert.That(stats["RecentlyAdded"]!.Select(r => (string)r["Title"]!), Is.EqualTo(new[] { "K", "J", "I", "H", "G", "F", "E", "D", "C", "B" }));
                    Assert.That((string?)stats["RecentlyAdded"]![0]!["CoverUrl"], Is.EqualTo("k.jpg"));
                    var rating = stats["RatingStats"]!;
                    Assert.That(new[] { "Excellent", "Good", "Average", "Poor", "Unrated" }.Select(k => (int)rating[k]!), Is.EqualTo(new[] { 2, 2, 3, 1, 2 }));
                    Assert.That(stats["StatusStats"]!.Select(s => $"{s["Status"]}={s["Count"]}").OrderBy(s => s), Is.EqualTo(new[] { "Downloaded=8", "Missing=2", "Released=1" }));
                });
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Games.RemoveRange(ctx.Games);
                await ctx.SaveChangesAsync();
                var empty = Json(await new DashboardController(ctx, null!).GetStats());
                Assert.That((int)empty["TotalGames"]!, Is.EqualTo(0));
                Assert.That((int)empty["RatingStats"]!["Unrated"]!, Is.EqualTo(0));
            }
        }

        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task RandomGame_PicksOnlyAMatchingGame(DatabaseType type, string? variable)
        {
            var options = Database(type, variable);
            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(await new DashboardController(ctx, null!).GetRandomGame(), Is.InstanceOf<NotFoundObjectResult>());
                for (var n = 0; n < 6; n++)
                    ctx.Games.Add(new Game { Title = $"Nes {n}", PlatformId = Nes, Genres = { n == 4 ? "Puzzle" : "Action" } });
                ctx.Games.Add(new Game { Title = "Snes", PlatformId = Snes, Genres = { "Action" } });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var controller = new DashboardController(ctx, null!);
                var seen = new HashSet<string>();
                // 200 draws out of 7 games: every game turns up unless the pick is stuck
                for (var n = 0; n < 200; n++)
                    seen.Add(Game(await controller.GetRandomGame()).Title);
                Assert.That(seen, Has.Count.EqualTo(7));
                for (var n = 0; n < 5; n++)
                {
                    Assert.That(Game(await controller.GetRandomGame(platformId: Snes)).Title, Is.EqualTo("Snes"));
                    var puzzle = Game(await controller.GetRandomGame(platformId: Nes, genre: "Puzzle"));
                    Assert.That((puzzle.Title, puzzle.Platform?.Id), Is.EqualTo(("Nes 4", (int?)Nes)));
                }
                Assert.That(await controller.GetRandomGame(platformId: Snes, genre: "Puzzle"), Is.InstanceOf<NotFoundObjectResult>());

                var shuffled = (List<Game>)((OkObjectResult)await controller.GetShuffledGames(3)).Value!;
                Assert.That(shuffled.Select(g => g.Id).Distinct().Count(), Is.EqualTo(3));
                Assert.That(((List<Game>)((OkObjectResult)await controller.GetShuffledGames(50)).Value!).Count, Is.EqualTo(7));
            }
        }

        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task PlatformMonitorToggle_UpdatesEveryGameOfThatPlatformOnly(DatabaseType type, string? variable)
        {
            // Own platform ids, so the stored "monitor new items" default touches no other test
            const int platform = 999093, other = 999094;
            var options = Database(type, variable);
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Platforms.AddRange(
                    new Platform { Id = platform, Name = "Monitor test", Slug = "monitor-test", FolderName = "monitor-test" },
                    new Platform { Id = other, Name = "Monitor other", Slug = "monitor-other", FolderName = "monitor-other" });
                ctx.Games.AddRange(
                    new Game { Title = "A", PlatformId = platform, Monitored = false },
                    new Game { Title = "B", PlatformId = platform, Monitored = true },
                    new Game { Title = "C", PlatformId = platform, Monitored = false },
                    new Game { Title = "D", PlatformId = other, Monitored = false });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var controller = new MonitorController(ctx, null!, null!);
                var on = Json(await controller.SetPlatformMonitored(platform, new MonitorController.PlatformMonitoredRequest { Monitored = true }, CancellationToken.None));
                Assert.That(((int)on["updated"]!, (int)on["total"]!), Is.EqualTo((2, 3)));
                var keep = Json(await controller.SetPlatformMonitored(platform, new MonitorController.PlatformMonitoredRequest { Monitored = false, ApplyToExisting = false }, CancellationToken.None));
                Assert.That(((int)keep["updated"]!, (int)keep["total"]!), Is.EqualTo((0, 3)));
                Assert.That(PlatformService.GetMonitorNewItemsDefault(platform, true), Is.False);
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(ctx.Games.OrderBy(g => g.Title).Select(g => g.Monitored).ToList(), Is.EqualTo(new[] { true, true, true, false }));
            }
        }

        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task TagsAndCollections_AreCountedAndListed(DatabaseType type, string? variable)
        {
            var options = Database(type, variable);
            int collectionId, tagId;
            using (var ctx = new RetroArrDbContext(options))
            {
                var a = new Game { Title = "A", PlatformId = Nes, Year = 1990, Genres = { "Action" }, Images = { CoverUrl = "a.jpg" } };
                var b = new Game { Title = "B", PlatformId = Snes, Year = 1991 };
                var c = new Game { Title = "C", PlatformId = Nes, Year = 1992 };
                var collection = new Collection
                {
                    Name = "Picks",
                    CollectionGames = { new CollectionGame { Game = a, SortOrder = 2 }, new CollectionGame { Game = b, SortOrder = 0 }, new CollectionGame { Game = c, SortOrder = 1 } }
                };
                var tag = new Tag { Name = "Favs", GameTags = { new GameTag { Game = a }, new GameTag { Game = b } } };
                ctx.AddRange(collection, tag, new Tag { Name = "Empty" }, new Collection { Name = "Nothing yet", SortOrder = 1 });
                await ctx.SaveChangesAsync();
                (collectionId, tagId) = (collection.Id, tag.Id);
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var tags = JArray.FromObject(((OkObjectResult)(await new TagController(ctx).GetAll()).Result!).Value!);
                Assert.That(tags.Select(t => $"{t["Name"]}={t["GameCount"]}"), Is.EqualTo(new[] { "Empty=0", "Favs=2" }));
                var tag = JObject.FromObject(((OkObjectResult)(await new TagController(ctx).GetById(tagId)).Result!).Value!);
                Assert.That(tag["Games"]!.Select(g => (string)g["Title"]!).OrderBy(t => t), Is.EqualTo(new[] { "A", "B" }));
                Assert.That((await new TagController(ctx).GetById(-1)).Result, Is.InstanceOf<NotFoundResult>());

                var collections = JArray.FromObject(((OkObjectResult)(await new CollectionController(ctx).GetAll()).Result!).Value!);
                Assert.That(collections.Select(c => $"{c["Name"]}={c["GameCount"]}"), Is.EqualTo(new[] { "Picks=3", "Nothing yet=0" }));
                Assert.That(collections[0]!["Games"]!.Select(g => (string)g["Title"]!), Is.EqualTo(new[] { "B", "C", "A" }));
                Assert.That((string?)collections[0]!["Games"]![2]!["CoverUrl"], Is.EqualTo("a.jpg"));
                var one = JObject.FromObject(((OkObjectResult)(await new CollectionController(ctx).GetById(collectionId)).Result!).Value!);
                var first = one["Games"]![2]!;
                Assert.That(((string)first["Title"]!, (string)first["Platform"]!, first["Genres"]!.Values<string>().Single()),
                    Is.EqualTo(("A", PlatformDefinitions.AllPlatforms.Single(p => p.Id == Nes).Name, "Action")));
                Assert.That((await new CollectionController(ctx).GetById(-1)).Result, Is.InstanceOf<NotFoundResult>());

                // The export streams the same links
                var dashboard = new DashboardController(ctx, null!) { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };
                var body = new MemoryStream();
                dashboard.HttpContext.Response.Body = body;
                await dashboard.ExportLibrary();
                var export = JObject.Parse(Encoding.UTF8.GetString(body.ToArray()));
                Assert.That(export["games"]!.Select(g => (string)g["title"]!).OrderBy(t => t), Is.EqualTo(new[] { "A", "B", "C" }));
                Assert.That(export["collections"]!.Select(c => c["gameIds"]!.Count()).OrderBy(n => n), Is.EqualTo(new[] { 0, 3 }));
                Assert.That(export["tags"]!.Select(t => t["gameIds"]!.Count()).OrderBy(n => n), Is.EqualTo(new[] { 0, 2 }));
                Assert.That((string?)export["games"]!.Single(g => (string)g["title"]! == "A")["images"]!["coverUrl"], Is.EqualTo("a.jpg"));
            }
        }

        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task ReviewStats_CountTheReviews(DatabaseType type, string? variable)
        {
            var options = Database(type, variable);
            using (var ctx = new RetroArrDbContext(options))
            {
                var empty = Json(await new RetroArr.Api.V3.Reviews.ReviewController(ctx).GetStats());
                Assert.That(((int)empty["Favorites"]!, (double)empty["TotalPlaytimeHours"]!, (double?)empty["AverageUserRating"]), Is.EqualTo((0, 0.0, (double?)null)));

                var games = Enumerable.Range(1, 6).Select(n => new Game { Title = $"G{n}", PlatformId = Nes }).ToList();
                ctx.Games.AddRange(games);
                await ctx.SaveChangesAsync();
                ctx.GameReviews.AddRange(
                    new GameReview { GameId = games[0].Id, IsFavorite = true, CompletionStatus = CompletionStatus.Playing, PlaytimeMinutes = 90, UserRating = 80 },
                    new GameReview { GameId = games[1].Id, IsWishlisted = true, CompletionStatus = CompletionStatus.Completed },
                    new GameReview { GameId = games[2].Id, IsFavorite = true, IsWishlisted = true, CompletionStatus = CompletionStatus.Completed, PlaytimeMinutes = 30, UserRating = 70 },
                    // A rating of 0 is a rating, as Average over the set values counted it
                    new GameReview { GameId = games[3].Id, CompletionStatus = CompletionStatus.OnHold, PlaytimeMinutes = 0, UserRating = 0 },
                    new GameReview { GameId = games[4].Id });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var stats = Json(await new RetroArr.Api.V3.Reviews.ReviewController(ctx).GetStats());
                Assert.That(new[] { "TotalGames", "Favorites", "Wishlisted", "NotPlayed", "Playing", "Completed", "Mastered", "Dropped" }.Select(k => (int)stats[k]!),
                    Is.EqualTo(new[] { 6, 2, 2, 1, 1, 2, 0, 0 }));
                Assert.That((double)stats["TotalPlaytimeHours"]!, Is.EqualTo(2.0));
                Assert.That((double?)stats["AverageUserRating"], Is.EqualTo(50.0));
            }
        }

        [TestCase(DatabaseType.SQLite, null)]
        [TestCase(DatabaseType.PostgreSQL, "RETROARR_TEST_PG")]
        [TestCase(DatabaseType.MariaDB, "RETROARR_TEST_MARIADB")]
        public async Task SteamShortcuts_ListEveryGameWithAnExecutable(DatabaseType type, string? variable)
        {
            var options = Database(type, variable);
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Games.AddRange(
                    new Game { Title = "A", PlatformId = Nes, ExecutablePath = "/g/a/a.exe", GameFiles = { new GameFile { RelativePath = "a.exe" } } },
                    new Game { Title = "B", PlatformId = Nes, ExecutablePath = "" },
                    new Game { Title = "C", PlatformId = Nes },
                    // MariaDB pads on compare (" " = ''), string.IsNullOrEmpty kept it
                    new Game { Title = "D", PlatformId = Snes, ExecutablePath = " " });
                await ctx.SaveChangesAsync();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var result = (OkObjectResult)await new LinuxExportController(null!, ctx, null!, new SteamShortcutExportService(), null!).ExportAllSteamShortcuts();
                var export = JObject.FromObject(result.Value!);
                Assert.That((int)export["count"]!, Is.EqualTo(2));
                Assert.That(export["shortcuts"]!.Select(s => ((string)s["AppName"]!, (string)s["Exe"]!)).OrderBy(s => s.Item1),
                    Is.EqualTo(new[] { ("A", "\"/g/a/a.exe\""), ("D", "\" \"") }));
            }
        }

        private static JObject Json(IActionResult result) => JObject.FromObject(((OkObjectResult)result).Value!);
        private static Game Game(ActionResult result) => (Game)((OkObjectResult)result).Value!;
        private static IEnumerable<string> Pairs(JToken rows, string key) => rows.Select(r => $"{r[key]}={r["Count"]}");
    }
}
