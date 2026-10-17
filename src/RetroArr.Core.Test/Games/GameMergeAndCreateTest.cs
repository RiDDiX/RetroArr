using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Games;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // A merge that can't save its target changes nothing, and a new entry the database refuses is the caller's error
    [TestFixture]
    public class GameMergeAndCreateTest
    {
        private const int Gba = 52;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private string _root = null!;
        private string _lib = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.Add(new Platform { Id = Gba, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba" });
            ctx.SaveChanges();

            _root = Path.Combine(Path.GetTempPath(), "retroarr_gamesave_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
            _config = new ConfigurationService(_root);
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib });
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string Rom(string name)
        {
            var path = Path.Combine(Directory.CreateDirectory(Path.Combine(_lib, "gba")).FullName, name);
            File.WriteAllText(path, "rom");
            return path;
        }

        private void Seed(string sourcePath)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.Games.AddRange(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Region = "USA" },
                new Game { Id = 2, Title = "Advance Wars (Rev 1)", PlatformId = Gba, Path = sourcePath, ExecutablePath = sourcePath },
                new Game { Id = 3, Title = "Golden Sun", PlatformId = Gba, IgdbId = 7 });
            ctx.Tags.Add(new Tag { Id = 1, Name = "played" });
            ctx.GameTags.Add(new GameTag { TagId = 1, GameId = 2 });
            ctx.GameFiles.Add(new GameFile { GameId = 2, RelativePath = Path.GetFileName(sourcePath), Size = 3, FileType = "Main" });
            ctx.SaveChanges();
        }

        private GameController Controller() =>
            new(new SqliteGameRepository(new DbFactory(_db)), null!, null!, null!, _config, null!, null!, null!, null!, null!, null!, null!,
                mergeService: new DuplicateGameMergeService(new DbFactory(_db), _config));

        [Test]
        public async Task MergeInto_TargetSaveFails_SourceAndWhatHangsOffItStay()
        {
            var rom = Rom("Advance Wars (USA) (Rev 1).gba");
            Seed(rom);
            var repo = new SqliteGameRepository(new DbFactory(_db));
            var service = new DuplicateGameMergeService(new DbFactory(_db), _config);

            // The IGDB id is another entry's on the platform
            Assert.ThrowsAsync<DuplicateGameException>(async () =>
                await service.MergeIntoAsync(repo, (await repo.GetByIdAsync(2))!, 1, await repo.GetAllLightAsync(), t => t.IgdbId = 7));

            using var check = new RetroArrDbContext(_db);
            var source = check.Games.AsNoTracking().Single(g => g.Id == 2);
            Assert.That(source.Path, Is.EqualTo(rom));
            Assert.That(check.Games.AsNoTracking().Single(g => g.Id == 1).Path, Is.Null);
            Assert.That(check.GameTags.Select(t => t.GameId), Is.EqualTo(new[] { 2 }));
            Assert.That(check.GameFiles.AsEnumerable().Select(f => (f.GameId, f.RelativePath)),
                Is.EqualTo(new[] { (2, Path.GetFileName(rom)) }));
        }

        [Test]
        public async Task MergeInto_TargetSaveFails_Is409()
        {
            Seed(Rom("Advance Wars (USA) (Rev 1).gba"));
            // A unique index refusing the target, from a trigger here
            using (var ctx = new RetroArrDbContext(_db))
                ctx.Database.ExecuteSqlRaw("CREATE TRIGGER dup BEFORE UPDATE ON Games WHEN NEW.Id = 1 BEGIN SELECT RAISE(ABORT, 'UNIQUE constraint failed: Games.IgdbId, Games.PlatformId'); END;");

            var result = await Controller().MergeInto(2, 1);

            Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.Games.Select(g => g.Id).OrderBy(id => id), Is.EqualTo(new[] { 1, 2, 3 }));
        }

        [Test]
        public async Task MergeInto_ThroughTheCache_ForgetsBothEntries()
        {
            var rom = Rom("Advance Wars (USA) (Rev 1).gba");
            Seed(rom);
            var cache = new MissingContentTest.FakeCache();
            var repo = new CachedGameRepository(new SqliteGameRepository(new DbFactory(_db)), cache, new CacheSettings());

            var saved = await new DuplicateGameMergeService(new DbFactory(_db), _config)
                .MergeIntoAsync(repo, (await repo.GetByIdAsync(2))!, 1, await repo.GetAllLightAsync());

            Assert.That(saved!.Path, Is.EqualTo(rom));
            Assert.That(cache.Removed, Is.SupersetOf(new[] { CacheKeys.GameDetail(1), CacheKeys.GameDetail(2), CacheKeys.GamesAll }));
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.Games.Select(g => g.Id).OrderBy(id => id), Is.EqualTo(new[] { 1, 3 }));
            Assert.That(check.GameTags.Select(t => t.GameId), Is.EqualTo(new[] { 1 }));
        }

        [TestCase("path")]
        [TestCase("title")]
        [TestCase("igdb")]
        public async Task Create_Duplicate_Is409(string field)
        {
            var rom = Rom("Advance Wars (USA) (Rev 1).gba");
            Seed(rom);
            var game = new Game { Title = "New", PlatformId = Gba, Path = Rom("New.gba") };
            if (field == "path") game.Path = rom;
            if (field == "title") { game.Title = "Advance Wars"; game.Region = "USA"; }
            if (field == "igdb") game.IgdbId = 7;

            var result = await Controller().Create(game);

            Assert.That(result.Result, Is.InstanceOf<ConflictObjectResult>());
            var body = ((ConflictObjectResult)result.Result!).Value!;
            Assert.That(body.GetType().GetProperty("conflictField")!.GetValue(body),
                Is.EqualTo(field switch { "path" => "Path", "title" => "Title+PlatformId", _ => "IgdbId+PlatformId" }));
        }

        // MariaDB's and PostgreSQL's errors for a path longer than its column or index, from a trigger here
        [TestCase("Data too long for column 'Path' at row 1")]
        [TestCase("index row requires 9112 bytes, maximum size is 8191")]
        public async Task Create_ValueTooLongForTheColumn_Is400(string error)
        {
            var trigger = "CREATE TRIGGER too_long BEFORE INSERT ON Games WHEN length(NEW.Path) > 20 BEGIN SELECT RAISE(ABORT, '" + error.Replace("'", "''") + "'); END;";
            using (var ctx = new RetroArrDbContext(_db))
                ctx.Database.ExecuteSqlRaw(trigger);

            var result = await Controller().Create(new Game { Title = "New", PlatformId = Gba, Path = Rom("A rather long file name.gba") });

            Assert.That(result.Result, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(((BadRequestObjectResult)result.Result!).Value!.ToString(), Does.Contain(error));
        }

        // The stored row, with its dates in UTC, not the request's
        [Test]
        public async Task Create_ReturnsTheStoredRow()
        {
            var added = new DateTime(2026, 1, 2, 12, 0, 0, DateTimeKind.Local);

            var result = await Controller().Create(new Game { Title = "New", PlatformId = Gba, Path = Rom("New.gba"), Added = added });

            var game = (Game)((CreatedAtActionResult)result.Result!).Value!;
            Assert.That((game.Added.Kind, game.Added), Is.EqualTo((DateTimeKind.Utc, added.ToUniversalTime())));
        }
    }
}
