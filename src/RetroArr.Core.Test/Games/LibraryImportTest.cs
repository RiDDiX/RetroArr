using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RetroArr.Api.V3.Dashboard;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class LibraryImportTest
    {
        private const int Nes = 40;
        private const int Snes = 41;

        // Import used to match on the title alone and kept only a handful of fields
        [Test]
        public async Task ExportWipeImport_RestoresEveryGameAndItsFields()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options;
            var added = new DateTime(2024, 3, 1, 8, 30, 0, DateTimeKind.Utc);
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Database.EnsureCreated();
                ctx.Platforms.AddRange(
                    new Platform { Id = Nes, Name = "NES", Slug = "nes", FolderName = "nes" },
                    new Platform { Id = Snes, Name = "Super Nintendo", Slug = "snes", FolderName = "snes" });
                ctx.Games.AddRange(
                    new Game
                    {
                        Title = "Contra", PlatformId = Nes, Region = "USA", Year = 1988, Overview = "Run and gun",
                        Storyline = "Red Falcon", ReleaseDate = new DateTime(1988, 2, 1, 0, 0, 0, DateTimeKind.Utc),
                        Developer = "Konami", Publisher = "Konami", Genres = { "Shooter" }, IgdbId = 1234, SteamId = 5,
                        GogId = "g1", EpicId = "e1", Rating = 81.5, Status = GameStatus.Downloaded,
                        InstallPath = "/games/contra", IsExternal = true, Added = added,
                        Images = { CoverUrl = "https://img/c.jpg", Screenshots = { "https://img/s1.jpg" } }
                    },
                    new Game { Title = "Contra", PlatformId = Nes, Region = "Japan", Added = added },
                    new Game { Title = "Contra", PlatformId = Snes, Region = "", Added = added });
                ctx.SaveChanges();
                var first = ctx.Games.OrderBy(g => g.Id).First();
                ctx.Tags.Add(new Tag { Name = "Favs", GameTags = { new GameTag { GameId = first.Id } } });
                ctx.Collections.Add(new Collection { Name = "Run and gun", CollectionGames = { new CollectionGame { GameId = first.Id } } });
                ctx.GameReviews.Add(new GameReview { GameId = first.Id, UserRating = 90 });
                ctx.SaveChanges();
            }

            string json;
            using (var ctx = new RetroArrDbContext(options))
            {
                var controller = Controller(ctx);
                var body = new MemoryStream();
                controller.HttpContext.Response.Body = body;
                await controller.ExportLibrary();
                json = Encoding.UTF8.GetString(body.ToArray());
                var export = JObject.Parse(json);
                var firstId = ctx.Games.Min(g => g.Id);
                Assert.That(export["games"]!.Count(), Is.EqualTo(3));
                Assert.That(export["tags"]![0]!["gameIds"]!.Values<int>(), Is.EqualTo(new[] { firstId }));
                Assert.That(export["collections"]![0]!["gameIds"]!.Values<int>(), Is.EqualTo(new[] { firstId }));
                Assert.That(export["reviews"]![0]!["userRating"]!.Value<int>(), Is.EqualTo(90));
                ctx.Games.RemoveRange(ctx.Games);
                ctx.SaveChanges();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(await Controller(ctx, json).ImportLibrary(), Is.InstanceOf<OkObjectResult>());
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var games = ctx.Games.OrderBy(g => g.PlatformId).ThenBy(g => g.Region).ToList();
                Assert.That(games.Select(g => (g.PlatformId, g.Region)), Is.EqualTo(new[] { (Nes, "Japan"), (Nes, "USA"), (Snes, "") }));
                var contra = games[1];
                Assert.That(contra.Year, Is.EqualTo(1988));
                Assert.That(contra.Overview, Is.EqualTo("Run and gun"));
                Assert.That(contra.Storyline, Is.EqualTo("Red Falcon"));
                Assert.That(contra.ReleaseDate, Is.EqualTo(new DateTime(1988, 2, 1, 0, 0, 0, DateTimeKind.Utc)));
                Assert.That((contra.Developer, contra.Publisher), Is.EqualTo(("Konami", "Konami")));
                Assert.That(contra.Genres, Is.EqualTo(new[] { "Shooter" }));
                Assert.That((contra.IgdbId, contra.SteamId, contra.GogId, contra.EpicId), Is.EqualTo(((int?)1234, (int?)5, "g1", "e1")));
                Assert.That(contra.Rating, Is.EqualTo(81.5));
                Assert.That(contra.Status, Is.EqualTo(GameStatus.Downloaded));
                Assert.That((contra.InstallPath, contra.IsExternal), Is.EqualTo(("/games/contra", true)));
                Assert.That(contra.Added, Is.EqualTo(added));
                Assert.That(contra.Images.CoverUrl, Is.EqualTo("https://img/c.jpg"));
                Assert.That(contra.Images.Screenshots, Is.EqualTo(new[] { "https://img/s1.jpg" }));
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                var again = (OkObjectResult)await Controller(ctx, json).ImportLibrary();
                Assert.That(JObject.FromObject(again.Value!)["message"]!.ToString(), Does.Contain("0 games imported, 3 skipped"));
                Assert.That(ctx.Games.Count(), Is.EqualTo(3));
            }
        }

        // Kestrel's 30 MB default answered a large library's export with 413
        [Test]
        public void Import_TakesExportsLargerThanTheDefaultBodyLimit()
        {
            var limit = typeof(DashboardController).GetMethod(nameof(DashboardController.ImportLibrary))!
                .GetCustomAttributes(true).OfType<IRequestSizeLimitMetadata>().SingleOrDefault();
            Assert.That(limit?.MaxRequestBodySize, Is.GreaterThanOrEqualTo(1L << 30));
        }

        [Test]
        public async Task Import_WithoutAGamesList_IsABadRequest()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            using var ctx = new RetroArrDbContext(new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options);
            ctx.Database.EnsureCreated();
            Assert.That(await Controller(ctx, "{\"version\":\"1.0\"}").ImportLibrary(), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(await Controller(ctx, "not json").ImportLibrary(), Is.InstanceOf<BadRequestObjectResult>());
        }

        // One query for every game: the retrying PostgreSQL/MySQL strategy buffers a whole result before the first row is written
        [Test]
        public async Task Export_ReadsTheGamesInBatches()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var gameQueries = 0;
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection)
                .LogTo(m => { if (m.Contains("FROM \"Games\" AS")) gameQueries++; }, new[] { RelationalEventId.CommandExecuted })
                .Options;
            using var ctx = new RetroArrDbContext(options);
            ctx.Database.EnsureCreated();
            ctx.Platforms.Add(new Platform { Id = Nes, Name = "NES", Slug = "nes", FolderName = "nes" });
            ctx.Games.AddRange(Enumerable.Range(0, 1200).Select(n => new Game { Title = $"Game {n}", PlatformId = Nes }));
            ctx.SaveChanges();

            var controller = Controller(ctx);
            var body = new MemoryStream();
            controller.HttpContext.Response.Body = body;
            gameQueries = 0;
            await controller.ExportLibrary();
            var games = JObject.Parse(Encoding.UTF8.GetString(body.ToArray()))["games"]!;
            Assert.That(games.Select(g => (string)g["title"]!), Is.EquivalentTo(Enumerable.Range(0, 1200).Select(n => $"Game {n}")));
            Assert.That(gameQueries, Is.EqualTo(3));
        }

        private static DashboardController Controller(RetroArrDbContext ctx, string? body = null)
        {
            var http = new DefaultHttpContext();
            if (body != null) http.Request.Body = new MemoryStream(Encoding.UTF8.GetBytes(body));
            return new DashboardController(ctx, null!) { ControllerContext = new ControllerContext { HttpContext = http } };
        }
    }
}
