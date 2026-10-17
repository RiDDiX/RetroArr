using System;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using MySqlConnector;
using Npgsql;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;

namespace RetroArr.Core.Test.Data
{
    [TestFixture]
    public class DatabaseConnectionTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_conn_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            DatabaseServiceExtensions.StartupError = null;
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        [TestCase("{\"Type\":\"MariaDB\",\"Host\":\"db\",\"Database\":\"retroarr\",\"Username\":\"u\"}")]
        [TestCase("{\"type\":2,\"host\":\"db\",\"port\":0,\"database\":\"retroarr\",\"username\":\"u\"}")]
        public void HandWrittenMariaDbConfig_WithoutPort_Uses3306(string json)
        {
            File.WriteAllText(Path.Combine(_root, "config", "database.json"), json);
            var svc = new ConfigurationService(_root, new SecretProtector(Path.Combine(_root, "keys")));

            Assert.That(svc.LoadDatabaseSettings().Port, Is.EqualTo(3306));
        }

        // The SQLite file can't be opened until its folder exists, like a server that
        // comes up after RetroArr: the background retry has to heal it without a restart.
        [Test]
        public void InitializeDatabase_RetriesUntilTheDatabaseIsReachable()
        {
            var dir = Path.Combine(_root, "later");
            using var services = new ServiceCollection()
                .AddDbContextFactory<RetroArrDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dir, "retroarr.db")}"))
                .BuildServiceProvider();
            using var stop = new CancellationTokenSource();

            DatabaseServiceExtensions.InitializeDatabase(services, TimeSpan.FromMilliseconds(50), stop.Token);
            Assert.That(DatabaseServiceExtensions.StartupError, Is.Not.Null);

            Directory.CreateDirectory(dir);
            var deadline = DateTime.UtcNow.AddSeconds(30);
            while (DatabaseServiceExtensions.StartupError != null && DateTime.UtcNow < deadline)
                Thread.Sleep(50);
            stop.Cancel();

            Assert.That(DatabaseServiceExtensions.StartupError, Is.Null);
            using var context = services.GetRequiredService<IDbContextFactory<RetroArrDbContext>>().CreateDbContext();
            Assert.That(context.Platforms.Count(), Is.GreaterThan(0));
        }

        // Live check, runs only with RETROARR_TEST_PG set to a superuser connection string
        // (Host=..;Port=..;Username=..;Password=..). PostgreSQL 15+ gives a non-owner no
        // CREATE on public.
        [Test]
        public void TestConnection_Postgres_ReportsMissingCreateRight()
        {
            var admin = Environment.GetEnvironmentVariable("RETROARR_TEST_PG");
            if (string.IsNullOrEmpty(admin)) Assert.Ignore("RETROARR_TEST_PG not set");
            var b = new NpgsqlConnectionStringBuilder(admin);
            const string db = "connection_ro", user = "connection_ro";

            // one statement per call, CREATE/DROP DATABASE refuse a multi-statement batch
            void Run(string database, params string[] sql)
            {
                using var conn = new NpgsqlConnection(new NpgsqlConnectionStringBuilder(admin) { Database = database, Pooling = false }.ConnectionString);
                conn.Open();
                foreach (var statement in sql)
                {
                    using var cmd = new NpgsqlCommand(statement, conn);
                    cmd.ExecuteNonQuery();
                }
            }

            Run("postgres", $"DROP DATABASE IF EXISTS {db} WITH (FORCE)", $"DROP ROLE IF EXISTS {user}");
            Run("postgres", $"CREATE ROLE {user} LOGIN PASSWORD 'pw'", $"CREATE DATABASE {db}");
            try
            {
                var settings = new DatabaseSettings { Type = DatabaseType.PostgreSQL, Host = b.Host!, Port = b.Port, Database = db, Username = user, Password = "pw" };

                Assert.That(DatabaseServiceExtensions.TestConnection(settings, _root, out var message), Is.False);
                Assert.That(message, Does.Contain("GRANT CREATE ON SCHEMA public"));

                Run(db, $"GRANT CREATE ON SCHEMA public TO {user}");
                Assert.That(DatabaseServiceExtensions.TestConnection(settings, _root, out message), Is.True, message);
                Assert.That(message, Is.Empty);

                settings.Database = "connection_missing";
                Assert.That(DatabaseServiceExtensions.TestConnection(settings, _root, out message), Is.True, message);
                Assert.That(message, Does.Contain("does not exist yet"));
            }
            finally
            {
                NpgsqlConnection.ClearAllPools();
                Run("postgres", $"DROP DATABASE IF EXISTS {db} WITH (FORCE)", $"DROP ROLE IF EXISTS {user}");
            }
        }

        // Live check, runs only with RETROARR_TEST_MARIADB set to a root connection string.
        [Test]
        public void TestConnection_MariaDb_ReportsMissingCreatePrivilege()
        {
            var admin = Environment.GetEnvironmentVariable("RETROARR_TEST_MARIADB");
            if (string.IsNullOrEmpty(admin)) Assert.Ignore("RETROARR_TEST_MARIADB not set");
            var b = new MySqlConnectionStringBuilder(admin);
            const string db = "connection_ro", user = "connection_ro";

            object? Run(string sql)
            {
                using var conn = new MySqlConnection(new MySqlConnectionStringBuilder(admin) { Database = "" }.ConnectionString);
                conn.Open();
                using var cmd = new MySqlCommand(sql, conn);
                return cmd.ExecuteScalar();
            }

            Run($"DROP DATABASE IF EXISTS {db}; DROP USER IF EXISTS '{user}'@'%';");
            Run($"CREATE DATABASE {db}; CREATE USER '{user}'@'%' IDENTIFIED BY 'pw'; GRANT SELECT, INSERT, UPDATE, DELETE ON {db}.* TO '{user}'@'%';");
            try
            {
                var settings = new DatabaseSettings { Type = DatabaseType.MariaDB, Host = b.Server, Port = (int)b.Port, Database = db, Username = user, Password = "pw" };

                Assert.That(DatabaseServiceExtensions.TestConnection(settings, _root, out var message), Is.False);
                Assert.That(message, Does.Contain("GRANT ALL PRIVILEGES"));

                Run($"GRANT ALL PRIVILEGES ON {db}.* TO '{user}'@'%';");
                Assert.That(DatabaseServiceExtensions.TestConnection(settings, _root, out message), Is.True, message);
                Assert.That(Convert.ToInt32(Run($"SELECT COUNT(*) FROM information_schema.TABLES WHERE TABLE_SCHEMA = '{db}'")), Is.EqualTo(0));
            }
            finally
            {
                MySqlConnection.ClearAllPools();
                Run($"DROP DATABASE IF EXISTS {db}; DROP USER IF EXISTS '{user}'@'%';");
            }
        }
    }
}
