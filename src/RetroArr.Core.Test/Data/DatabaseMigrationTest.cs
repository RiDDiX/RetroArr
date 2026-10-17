using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Data
{
    [TestFixture]
    public class DatabaseMigrationTest
    {
        [Test]
        public void GamesOfAMissingPlatform_MoveToUnknownForReview()
        {
            var platforms = new List<Platform> { new() { Id = 1 }, new() { Id = PlatformDefinitions.UnknownPlatformId } };
            var games = new List<Game> { new() { Id = 1, PlatformId = 1 }, new() { Id = 2, PlatformId = 999 } };

            Assert.That(DatabaseMigrationService.MoveOrphansToUnknownPlatform(platforms, games), Is.EqualTo(1));
            Assert.That(games[0].PlatformId, Is.EqualTo(1));
            Assert.That(games[0].NeedsMetadataReview, Is.False);
            Assert.That(games[1].PlatformId, Is.EqualTo(PlatformDefinitions.UnknownPlatformId));
            Assert.That(games[1].NeedsMetadataReview, Is.True);
            Assert.That(games[1].MetadataReviewReason, Is.Not.Empty);
        }

        [Test]
        public void TooLongValues_AreListedWithTheirIds()
        {
            var pg = new DbContextOptionsBuilder<RetroArrDbContext>().UseNpgsql("Host=localhost;Database=retroarr").Options;
            using var pgContext = new RetroArrDbContext(pg);
            var tags = new List<Tag>
            {
                new() { Id = 3, Name = new string('x', 51) },
                // 50 characters, 100 UTF-16 units
                new() { Id = 4, Name = string.Concat(Enumerable.Repeat("\U0001F3AE", 50)) }
            };
            var collections = new List<Collection> { new() { Id = 7, Name = "c", Description = new string('d', 501) } };

            var problems = DatabaseMigrationService.FindTooLongValues(pgContext.Model, tags, collections, new List<Game>());

            Assert.That(problems, Is.EquivalentTo(new[]
            {
                "Tags.Name over 50 characters (Id 3)",
                "Collections.Description over 500 characters (Id 7)"
            }));

            // Pomelo gives indexed strings varchar(255) without a max length in the model
            var maria = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseMySql("Server=localhost;Database=retroarr", new MariaDbServerVersion(new Version(11, 4))).Options;
            using var mariaContext = new RetroArrDbContext(maria);
            var games = new List<Game> { new() { Id = 9, Title = new string('t', 256) } };
            Assert.That(DatabaseMigrationService.FindTooLongValues(mariaContext.Model, games),
                Is.EqualTo(new[] { "Games.Title over 255 characters (Id 9)" }));
        }

        // Earlier builds re-added Genres and the image lists as nullable columns, and old games
        // have no Added date.
        [Test]
        public void ApplyMigrations_HealsNullListsAndMissingAddedDates()
        {
            var path = Path.Combine(Path.GetTempPath(), $"retroarr-migration-{Guid.NewGuid():N}.db");
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite($"Data Source={path}").Options;
            var fileDate = new DateTime(2024, 5, 1, 10, 0, 0, DateTimeKind.Utc);
            try
            {
                using (var context = new RetroArrDbContext(options))
                {
                    context.Database.EnsureCreated();
                    context.Platforms.Add(new Platform { Id = 1, Name = "PC", Slug = "pc", FolderName = "pc" });
                    context.Games.Add(new Game { Id = 1, Title = "Old", PlatformId = 1, Added = DateTime.MinValue });
                    context.Games.Add(new Game { Id = 2, Title = "No files", PlatformId = 1, Added = DateTime.MinValue });
                    context.GameFiles.Add(new GameFile { GameId = 1, RelativePath = "a", DateAdded = fileDate.AddDays(3) });
                    context.GameFiles.Add(new GameFile { GameId = 1, RelativePath = "b", DateAdded = fileDate });
                    context.SaveChanges();

                    context.Database.ExecuteSqlRaw("ALTER TABLE Games DROP COLUMN Genres");
                    context.Database.ExecuteSqlRaw("ALTER TABLE Games DROP COLUMN Images_Artworks");
                    context.Database.ExecuteSqlRaw("ALTER TABLE Games ADD COLUMN Images_Artworks TEXT");

                    DatabaseMigrator.ApplyMigrations(context, DatabaseType.SQLite);
                }

                using (var context = new RetroArrDbContext(options))
                {
                    var games = context.Games.OrderBy(g => g.Id).ToList();
                    Assert.That(games[0].Genres, Is.Empty);
                    Assert.That(games[0].Images.Artworks, Is.Empty);
                    Assert.That(games[0].Added, Is.EqualTo(fileDate));
                    Assert.That(games[1].Added, Is.EqualTo(DateTime.MinValue));
                }
            }
            finally
            {
                SqliteConnection.ClearAllPools();
                File.Delete(path);
            }
        }
    }
}
