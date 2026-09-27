using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download.History;

namespace RetroArr.Core.Test.Download
{
    // SQLite, so the unique index on DownloadId and ClientId is enforced the way it is in an install
    [TestFixture]
    public class DownloadHistoryRepositoryTest
    {
        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private DownloadHistoryRepository _repo = null!;

        [SetUp]
        public void SetUp()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using (var ctx = new RetroArrDbContext(_db)) ctx.Database.EnsureCreated();
            _repo = new DownloadHistoryRepository(new DbFactory(_db));
        }

        [TearDown]
        public void TearDown() => _connection.Dispose();

        private static DownloadHistoryEntry Row(string id, int clientId, string title, DownloadHistoryState state, long size = 0) =>
            new() { DownloadId = id, ClientId = clientId, ClientName = "c" + clientId, Title = title, State = state, Size = size };

        [Test]
        public async Task Upsert_SameClient_UpdatesItsRow()
        {
            await _repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.ImportFailed));
            await _repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.Imported));

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.DownloadHistory.Select(h => h.State).ToList(), Is.EqualTo(new[] { DownloadHistoryState.Imported }));
        }

        // Another client's job with the same number gets its own row and leaves the first client's import alone
        [Test]
        public async Task Upsert_SameNumberFromAnotherClient_AddsItsOwnRow()
        {
            await _repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.Imported));
            await _repo.UpsertAsync(Row("5", 3, "Game B", DownloadHistoryState.ImportFailed));

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.DownloadHistory.OrderBy(h => h.ClientId).ToList().Select(h => (h.ClientId, h.Title, h.State)),
                Is.EqualTo(new[] { (1, "Game A", DownloadHistoryState.Imported), (3, "Game B", DownloadHistoryState.ImportFailed) }));
            Assert.That((await _repo.FindByDownloadIdAsync("5", 3, "Game B", 0))?.Title, Is.EqualTo("Game B"));
            Assert.That((await _repo.FindByDownloadIdAsync("5", 1, "Game A", 0))?.Title, Is.EqualTo("Game A"));
        }

        private const string Hash = "0123456789abcdef0123456789abcdef01234567";

        // Transmission imports from before its ids became the hash are recorded under its per-session number
        [TestCase(100, true)]
        [TestCase(999999, false)]
        public async Task Find_HashOfAnImportUnderTheOldNumber_TakesItOverOnlyWithTheSameSize(long size, bool takesOver)
        {
            await _repo.UpsertAsync(Row("5", 2, "Game X", DownloadHistoryState.Imported, 100));

            var found = await _repo.FindByDownloadIdAsync(Hash, 2, "Game X", size);

            Assert.That(found?.DownloadId, Is.EqualTo(takesOver ? Hash : null));
            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.DownloadHistory.Single().DownloadId, Is.EqualTo(takesOver ? Hash : "5"));
        }

        // A database from an earlier build: DownloadId unique by itself, or no history table yet
        [TestCase("DROP INDEX IX_DownloadHistory_DownloadId_ClientId; CREATE UNIQUE INDEX IX_DownloadHistory_DownloadId ON DownloadHistory(DownloadId);")]
        [TestCase("DROP TABLE DownloadHistory;")]
        public async Task Migrate_EarlierDatabase_KeepsDownloadIdsUniquePerClient(string earlierSchema)
        {
            var dir = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retroarr_history_" + Path.GetRandomFileName())).FullName;
            try
            {
                var db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite($"Data Source={Path.Combine(dir, "retroarr.db")};Pooling=False").Options;
                using (var ctx = new RetroArrDbContext(db))
                {
                    ctx.Database.EnsureCreated();
                    ctx.Database.ExecuteSqlRaw(earlierSchema);
                }
                for (var run = 0; run < 2; run++)
                {
                    using var ctx = new RetroArrDbContext(db);
                    DatabaseMigrator.ApplyMigrations(ctx, DatabaseType.SQLite);
                }

                var repo = new DownloadHistoryRepository(new DbFactory(db));
                await repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.Imported));
                await repo.UpsertAsync(Row("5", 3, "Game B", DownloadHistoryState.Imported));

                using var check = new RetroArrDbContext(db);
                Assert.That(check.DownloadHistory.Count(), Is.EqualTo(2));
                check.DownloadHistory.Add(Row("5", 1, "Game A", DownloadHistoryState.Imported));
                Assert.Throws<DbUpdateException>(() => check.SaveChanges());
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
