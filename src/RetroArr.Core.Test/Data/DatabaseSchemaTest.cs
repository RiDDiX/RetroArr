using System;
using System.Linq;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Data
{
    [TestFixture]
    public class DatabaseSchemaTest
    {
        // Issue #10: HasFilter("[IgdbId] IS NOT NULL") is SQL Server quoting and
        // PostgreSQL stopped EnsureCreated with 42601 at the '['.
        [Test]
        public void PostgresCreateScript_HasNoBracketQuotedIdentifiers()
        {
            var options = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseNpgsql("Host=localhost;Database=retroarr")
                .Options;
            using var context = new RetroArrDbContext(options);

            var script = context.Database.GenerateCreateScript();

            // text[] is a PostgreSQL array type, an identifier in brackets is not
            Assert.That(Regex.IsMatch(script, @"\[\w"), Is.False, script);
            Assert.That(script, Does.Contain("CREATE UNIQUE INDEX \"IX_Games_IgdbId_PlatformId\""));
        }

        [Test]
        public void MariaDbCreateScript_KeepsLongPathsAndComparesByBytes()
        {
            var options = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseMySql("Server=localhost;Database=retroarr", new MariaDbServerVersion(new Version(11, 4)))
                .Options;
            using var context = new RetroArrDbContext(options);

            var script = context.Database.GenerateCreateScript();

            Assert.That(script, Does.Match(@"`Path` varchar\(768\) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL"));
            Assert.That(script, Does.Match(@"`Title` varchar\(255\) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL"));
            // the UI stores theme variables such as var(--ctp-sapphire)
            Assert.That(script, Does.Match(@"`Color` varchar\(32\)"));
        }

        // Npgsql refuses any DateTime that isn't Kind=Utc; SQLite hands back Unspecified.
        [Test]
        public void DateTimes_AreStoredAndReadAsUtc()
        {
            using var connection = new SqliteConnection("Data Source=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options;

            var local = new DateTime(2026, 10, 17, 12, 0, 0, DateTimeKind.Local);
            var unspecified = new DateTime(1996, 6, 23, 0, 0, 0, DateTimeKind.Unspecified);
            using (var context = new RetroArrDbContext(options))
            {
                context.Database.EnsureCreated();
                context.Tags.Add(new Tag { Name = "local", CreatedAt = local });
                context.Tags.Add(new Tag { Name = "unspecified", CreatedAt = unspecified });
                context.SaveChanges();
            }

            using (var context = new RetroArrDbContext(options))
            {
                var tags = context.Tags.ToDictionary(t => t.Name);
                Assert.That(tags.Values.Select(t => t.CreatedAt.Kind), Is.All.EqualTo(DateTimeKind.Utc));
                Assert.That(tags["local"].CreatedAt, Is.EqualTo(local.ToUniversalTime()));
                Assert.That(tags["unspecified"].CreatedAt, Is.EqualTo(DateTime.SpecifyKind(unspecified, DateTimeKind.Utc)));

                // query parameters go through the same conversion
                Assert.That(context.Tags.Count(t => t.CreatedAt >= local), Is.EqualTo(1));
            }
        }
    }
}
