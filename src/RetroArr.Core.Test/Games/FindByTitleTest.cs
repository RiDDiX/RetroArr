using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class FindByTitleTest
    {
        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new(_options);
            public Task<RetroArrDbContext> CreateDbContextAsync(CancellationToken ct = default) => Task.FromResult(CreateDbContext());
        }

        private SqliteConnection _connection = null!;
        private SqliteGameRepository _repo = null!;

        [SetUp]
        public void SetUp()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(options);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(
                new Platform { Id = 1, Name = "A", Slug = "a", FolderName = "a" },
                new Platform { Id = 2, Name = "B", Slug = "b", FolderName = "b" },
                new Platform { Id = 41, Name = "C", Slug = "c", FolderName = "c" },
                new Platform { Id = 42, Name = "D", Slug = "d", FolderName = "d" });
            ctx.Games.AddRange(
                new Game { Id = 1, Title = "Super Mario World", PlatformId = 1 },
                new Game { Id = 2, Title = "Super Mario World", PlatformId = 2 },
                new Game { Id = 3, Title = "Super Mario Land", PlatformId = 1 },
                new Game { Id = 4, Title = "Ōkami", PlatformId = 1 },
                new Game { Id = 10, Title = "X", PlatformId = 41, IgdbId = 100 },
                new Game { Id = 11, Title = "Y", PlatformId = 42, IgdbId = 100 },
                new Game { Id = 12, Title = "Z", PlatformId = 41, IgdbId = 200 });
            ctx.SaveChanges();
            _repo = new SqliteGameRepository(new DbFactory(options));
        }

        [TearDown]
        public void TearDown() => _connection.Dispose();

        [Test]
        public async Task SameTitleInOtherCase_OnThePlatform_IsFound()
        {
            var rows = await _repo.FindByTitleAsync(1, "SUPER mario world");
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Id, Is.EqualTo(1));
        }

        [Test]
        public async Task OtherPlatformOrOtherTitle_IsNotFound()
        {
            Assert.That(await _repo.FindByTitleAsync(2, "Super Mario Land"), Is.Empty);
            Assert.That((await _repo.FindByTitleAsync(2, "super mario world"))[0].Id, Is.EqualTo(2));
            Assert.That(await _repo.FindByTitleAsync(1, "Super Mario Wor1d"), Is.Empty);
        }

        [Test]
        public async Task UnicodeCase_Matches()
        {
            var rows = await _repo.FindByTitleAsync(1, "ŌKAMI");
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Id, Is.EqualTo(4));
        }

        [Test]
        public async Task FindByIgdbId_ReturnsOnlyThatPlatformAndId()
        {
            var rows = await _repo.FindByIgdbIdAsync(41, 100);
            Assert.That(rows.Select(r => r.Id), Is.EqualTo(new[] { 10 }));
            Assert.That(rows.Select(r => r.IgdbId), Is.All.EqualTo(100));
            Assert.That(await _repo.FindByIgdbIdAsync(41, 999), Is.Empty);
        }
    }
}
