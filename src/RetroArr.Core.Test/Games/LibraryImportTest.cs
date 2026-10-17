using System;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
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
            }

            string json;
            using (var ctx = new RetroArrDbContext(options))
            {
                var file = (FileContentResult)await new DashboardController(ctx, null!).ExportLibrary();
                json = Encoding.UTF8.GetString(file.FileContents);
                ctx.Games.RemoveRange(ctx.Games);
                ctx.SaveChanges();
            }

            using (var ctx = new RetroArrDbContext(options))
            {
                Assert.That(await new DashboardController(ctx, null!).ImportLibrary(JObject.Parse(json)), Is.InstanceOf<OkObjectResult>());
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
                var again = (OkObjectResult)await new DashboardController(ctx, null!).ImportLibrary(JObject.Parse(json));
                Assert.That(JObject.FromObject(again.Value!)["message"]!.ToString(), Does.Contain("0 games imported, 3 skipped"));
                Assert.That(ctx.Games.Count(), Is.EqualTo(3));
            }
        }
    }
}
