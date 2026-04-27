using System.IO;
using NUnit.Framework;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Test.Configuration
{
    [TestFixture]
    public class DatabaseSettingsConnectionStringTest
    {
        [Test]
        public void Sqlite_BuildsRelativeDataSourceWithForeignKeys()
        {
            var s = new DatabaseSettings { Type = DatabaseType.SQLite, SqlitePath = "retroarr.db" };
            var cs = s.GetConnectionString("/cfg");

            // Foreign keys must be on — otherwise Game.PlatformId FKs aren't enforced.
            Assert.That(cs, Does.Contain("Foreign Keys=True"));
            Assert.That(cs, Does.Contain(Path.Combine("/cfg", "retroarr.db")));
        }

        [Test]
        public void Postgres_IncludesApplicationNameForDbaDiagnostics()
        {
            var s = new DatabaseSettings
            {
                Type = DatabaseType.PostgreSQL,
                Host = "pg.example.org",
                Port = 5432,
                Database = "retroarr",
                Username = "ra_user",
                Password = "secret"
            };

            var cs = s.GetConnectionString(string.Empty);

            // ApplicationName surfaces in pg_stat_activity / pgAdmin so DBAs
            // can identify which process holds a connection.
            Assert.That(cs, Does.Contain("Application Name=RetroArr").Or.Contain("ApplicationName=RetroArr"));
            Assert.That(cs, Does.Contain("Host=pg.example.org"));
            Assert.That(cs, Does.Contain("Database=retroarr"));
            Assert.That(cs, Does.Contain("Username=ra_user"));
        }

        [Test]
        public void Postgres_SslModeRequiredWhenUseSslIsTrue()
        {
            var s = new DatabaseSettings
            {
                Type = DatabaseType.PostgreSQL,
                Host = "h", Database = "d", Username = "u", Password = "p",
                UseSsl = true
            };
            var cs = s.GetConnectionString(string.Empty);

            Assert.That(cs, Does.Contain("SSL Mode=Require").Or.Contain("SslMode=Require"));
        }

        [Test]
        public void MariaDb_BuildsServerStringWithSslDisabledByDefault()
        {
            var s = new DatabaseSettings
            {
                Type = DatabaseType.MariaDB,
                Host = "maria.example.org",
                Port = 3306,
                Database = "retroarr",
                Username = "ra",
                Password = "p",
                UseSsl = false
            };

            var cs = s.GetConnectionString(string.Empty);

            Assert.That(cs, Does.Contain("Server=maria.example.org"));
            Assert.That(cs, Does.Contain("Port=3306"));
            Assert.That(cs, Does.Contain("Database=retroarr"));
            Assert.That(cs, Does.Contain("SslMode=None"));
        }

        [Test]
        public void IsConfigured_TrueForSqliteByDefault()
        {
            // SQLite has no auth, so a default-constructed settings object should
            // be valid out of the box for fresh installs.
            Assert.That(new DatabaseSettings { Type = DatabaseType.SQLite }.IsConfigured, Is.True);
        }

        [Test]
        public void IsConfigured_FalseForPostgresWithoutCredentials()
        {
            var s = new DatabaseSettings { Type = DatabaseType.PostgreSQL };
            Assert.That(s.IsConfigured, Is.False);
        }

        [Test]
        public void IsConfigured_TrueForPostgresWithCredentials()
        {
            var s = new DatabaseSettings
            {
                Type = DatabaseType.PostgreSQL,
                Host = "h", Database = "d", Username = "u"
            };
            Assert.That(s.IsConfigured, Is.True);
        }
    }
}
