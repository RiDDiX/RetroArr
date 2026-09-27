using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Download.History;

namespace RetroArr.Core.Test.Download
{
    // SQLite, so the unique index on DownloadId is enforced the way it is in an install
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

        private static DownloadHistoryEntry Row(string id, int clientId, string title, DownloadHistoryState state) =>
            new() { DownloadId = id, ClientId = clientId, ClientName = "c" + clientId, Title = title, State = state };

        [Test]
        public async Task Upsert_SameClient_UpdatesItsRow()
        {
            await _repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.ImportFailed));
            await _repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.Imported));

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.DownloadHistory.Select(h => h.State).ToList(), Is.EqualTo(new[] { DownloadHistoryState.Imported }));
        }

        // Another client's job with the same number must not turn the first client's import into its own
        [Test]
        public async Task Upsert_SameNumberFromAnotherClient_LeavesTheFirstRowAlone()
        {
            await _repo.UpsertAsync(Row("5", 1, "Game A", DownloadHistoryState.Imported));

            Assert.ThrowsAsync<DbUpdateException>(() => _repo.UpsertAsync(Row("5", 3, "Game B", DownloadHistoryState.ImportFailed)));

            using var ctx = new RetroArrDbContext(_db);
            var row = ctx.DownloadHistory.Single();
            Assert.That((row.ClientId, row.Title, row.State), Is.EqualTo((1, "Game A", DownloadHistoryState.Imported)));
            Assert.That(await _repo.FindByDownloadIdAsync("5", 3, "Game B"), Is.Null);
            Assert.That((await _repo.FindByDownloadIdAsync("5", 1, "Game A"))?.Title, Is.EqualTo("Game A"));
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
