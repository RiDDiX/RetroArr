using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Games;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class GameControllerTest
    {
        private const int Pc = 1;
        private const int Snes = 41;
        private const int Switch = 46;

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
            // SQLite, so the unique indexes are what an install has
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(
                new Platform { Id = Pc, Name = "PC", Slug = "pc", FolderName = "windows" },
                new Platform { Id = Snes, Name = "Super Nintendo", Slug = "snes", FolderName = "snes" },
                new Platform { Id = Switch, Name = "Nintendo Switch", Slug = "switch", FolderName = "switch" });
            ctx.SaveChanges();

            _root = Path.Combine(Path.GetTempPath(), "retroarr_gamectl_" + Path.GetRandomFileName());
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

        private GameController Controller(IGameRepository? repo = null) =>
            new(repo ?? new SqliteGameRepository(new DbFactory(_db)), null!, null!, null!, _config, null!, null!, null!, null!, null!, null!, null!,
                mergeService: new DuplicateGameMergeService(new DbFactory(_db), _config));

        private void Add(params Game[] games)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.Games.AddRange(games);
            ctx.SaveChanges();
        }

        private Game Row(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.AsNoTracking().Single(g => g.Id == id);
        }

        private static string Put(string folder, string name)
        {
            Directory.CreateDirectory(folder);
            var path = Path.Combine(folder, name);
            File.WriteAllText(path, "data");
            return path;
        }

        private static object? Value(object body, string name) => body.GetType().GetProperty(name)!.GetValue(body);

        // An installer is not the game: GOG's setup_game_1.0_(123).exe beside its .bin parts, any setup or install
        // exe, and what was found as an installer
        [TestCase("setup_game_1.0_(123).exe", GameStatus.Downloaded, false)]
        [TestCase("Setup-Game.exe", GameStatus.Downloaded, false)]
        [TestCase("install_game.exe", GameStatus.Downloaded, false)]
        [TestCase("GameSetup.exe", GameStatus.Downloaded, false)]
        [TestCase("game.exe", GameStatus.InstallerDetected, false)]
        [TestCase("game.exe", GameStatus.Downloaded, true)]
        public async Task GetById_AnInstaller_CannotBePlayed(string exe, GameStatus status, bool canPlay)
        {
            var folder = Path.Combine(_lib, "windows", "Game");
            var file = Put(folder, exe);
            Put(folder, "setup_game_1.0_(123)-1.bin");
            Add(new Game { Id = 1, Title = "Game", PlatformId = Pc, Path = file, ExecutablePath = file, Status = status });

            var result = await Controller().GetById(1);

            Assert.That(Value(((OkObjectResult)result.Result!).Value!, "canPlay"), Is.EqualTo(canPlay));
        }

        // The listing of a folder game shows its updates and DLC already; only what is linked from outside is added
        [Test]
        public async Task GetGameFiles_UpdatesAndDlcInTheGamesFolder_AreListedOnce()
        {
            var switchFolder = Path.Combine(_lib, "switch");
            var folder = Path.Combine(switchFolder, "Zelda");
            Put(folder, "Zelda.nsp");
            Put(Path.Combine(folder, "Updates"), "Zelda [v65536].nsp");
            Put(Path.Combine(folder, "DLC"), "Zelda DLC.nsp");
            var outside = Put(switchFolder, "Zelda Expansion DLC.nsp");
            Add(new Game { Id = 1, Title = "Zelda", PlatformId = Switch, Path = folder, Status = GameStatus.Downloaded });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.GameFiles.AddRange(
                    new GameFile { GameId = 1, RelativePath = "Zelda.nsp", FileType = "Main" },
                    new GameFile { GameId = 1, RelativePath = "Updates/Zelda [v65536].nsp", FileType = "Patch" },
                    new GameFile { GameId = 1, RelativePath = "DLC/Zelda DLC.nsp", FileType = "DLC" },
                    new GameFile { GameId = 1, RelativePath = outside, FileType = "DLC" });
                ctx.SaveChanges();
            }

            var result = await Controller().GetGameFiles(1);

            var body = ((OkObjectResult)result).Value!;
            Assert.That(((IEnumerable)Value(body, "files")!).Cast<object>().Count(), Is.EqualTo(3));
            Assert.That(((IEnumerable)Value(body, "supplementaryFiles")!).Cast<object>().Select(f => Value(f, "relativePath")), Is.EqualTo(new[] { outside }));
        }

        // A wanted game mapped to its file runs from the file and is no longer searched. Its folder is its path,
        // unless the file lies in a folder of many games (a platform folder): then the file is.
        [TestCase(false)]
        [TestCase(true)]
        public async Task MapFile_WantedGame_TakesTheFileAndItsContentState(bool flat)
        {
            var snes = Path.Combine(_lib, "snes");
            var file = Put(flat ? snes : Path.Combine(snes, "Chrono Trigger"), "Chrono Trigger (USA).sfc");
            Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Status = GameStatus.Missing, Monitored = true });

            var result = await Controller().MapFileToGame(1, new MapFileRequest { FilePath = file });

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            var row = Row(1);
            Assert.That((row.Path, row.ExecutablePath, row.Status), Is.EqualTo((flat ? file : Path.GetDirectoryName(file), file, GameStatus.Downloaded)));
        }

        // A folder another entry's file or exe lies in holds more than this game: the game is its file there, and the
        // folder is no game's folder
        [TestCase("path")]
        [TestCase("exe")]
        [TestCase("folder")]
        public async Task MapFile_FolderWithAnotherEntrysFile_IsNotTheGamesPath(string kind)
        {
            var collection = Path.Combine(_lib, "snes", "Collection");
            var mine = Put(collection, "Chrono Trigger (USA).sfc");
            var other = Put(collection, "Secret of Mana (USA).sfc");
            Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Status = GameStatus.Missing, Monitored = true },
                new Game { Id = 2, Title = "Secret of Mana", PlatformId = Snes, Path = kind == "exe" ? null : other, ExecutablePath = kind == "exe" ? other : null });

            var result = await Controller().MapFileToGame(1, new MapFileRequest { FilePath = kind == "folder" ? collection : mine });

            if (kind == "folder")
            {
                Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
                Assert.That(Row(1).Path, Is.Null);
            }
            else
            {
                Assert.That(result, Is.InstanceOf<OkObjectResult>());
                Assert.That((Row(1).Path, Row(1).ExecutablePath), Is.EqualTo((mine, mine)));
            }
        }

        // Titles are compared whatever the case of letters beyond ASCII ("ŌKAMI" is "Ōkami"), which the database does not
        [Test]
        public async Task CreateFromFile_TitleWithCapitalsBeyondAscii_GoesToThatEntry()
        {
            var file = Put(Path.Combine(_lib, "snes"), "Okami.sfc");
            Add(new Game { Id = 1, Title = "ŌKAMI", PlatformId = Snes, Status = GameStatus.Missing, Monitored = true });

            var result = await Controller().CreateGameFromFile(new CreateGameFromFileRequest { FilePath = file, Title = "Ōkami", PlatformId = Snes });

            var body = ((OkObjectResult)result).Value!;
            Assert.That((Value(body, "created"), Value(body, "gameId")), Is.EqualTo(((object)false, (object)1)));
        }

        // A library root or a platform folder holds many games and is no game's folder
        [TestCase("")]
        [TestCase("snes")]
        public async Task MapFile_FolderOfManyGames_IsRefused(string folder)
        {
            var path = Directory.CreateDirectory(Path.Combine(_lib, folder)).FullName;
            Put(Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Status = GameStatus.Missing, Monitored = true });

            var result = await Controller().MapFileToGame(1, new MapFileRequest { FilePath = path });

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(Row(1).Path, Is.Null);
        }

        // The file's region is the one given, or else its name's ("(USA)"). The entry of the title (in any case) on the
        // platform in that region is the one, or else an untagged one (a wanted game, which takes the region); no entry
        // of another region. Found without loading every game with its files.
        [TestCase(null, "USA", true)]
        [TestCase(null, "", true)]
        [TestCase(null, "Europe", false)]
        [TestCase("Europe", "Europe", true)]
        public async Task CreateFromFile_EntryOfTheTitleInTheFilesRegion_IsTheOne(string? region, string entryRegion, bool linked)
        {
            var file = Put(Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Region = entryRegion, Status = GameStatus.Missing, Monitored = true });
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, _) =>
            {
                if (method.Name == nameof(IGameRepository.GetAllAsync)) throw new InvalidOperationException("every game with its files");
            };

            var result = await Controller(repo).CreateGameFromFile(new CreateGameFromFileRequest { FilePath = file, Title = "chrono trigger", PlatformId = Snes, Region = region });

            var body = ((OkObjectResult)result).Value!;
            Assert.That((Value(body, "created"), Value(body, "gameId")), Is.EqualTo(((object)!linked, (object)(linked ? 1 : 2))));
            Assert.That((Row(linked ? 1 : 2).Path, Row(linked ? 1 : 2).Region), Is.EqualTo((file, region ?? "USA")));
            Assert.That(Row(1).Path, Is.EqualTo(linked ? file : null));
        }

        // A file in the platform folder becomes a game of its own, or goes to the entry of its title (a wanted one)
        [TestCase(false)]
        [TestCase(true)]
        public async Task CreateFromFile_FileInAPlatformFolder_IsTheGamesPath(bool existing)
        {
            var file = Put(Path.Combine(_lib, "snes"), "Chrono Trigger (USA).sfc");
            if (existing) Add(new Game { Id = 1, Title = "Chrono Trigger", PlatformId = Snes, Status = GameStatus.Missing, Monitored = true });

            var result = await Controller().CreateGameFromFile(new CreateGameFromFileRequest { FilePath = file, Title = "Chrono Trigger", PlatformId = Snes });

            var body = ((OkObjectResult)result).Value!;
            var row = Row((int)Value(body, "gameId")!);
            Assert.That((Value(body, "created"), row.Path, row.ExecutablePath, row.Status), Is.EqualTo((!existing, file, file, GameStatus.Downloaded)));
        }

        private async Task<string[]> Problems(GameController controller)
        {
            var json = System.Text.Json.JsonSerializer.Serialize(((OkObjectResult)(await controller.GetProblems()).Result!).Value);
            using var doc = System.Text.Json.JsonDocument.Parse(json);
            return doc.RootElement.EnumerateArray().Select(p => $"{p.GetProperty("id").GetInt32()}:{p.GetProperty("problemType").GetString()}").ToArray();
        }

        // A problem marked as resolved stays gone after a reload, and files that go missing after that show again
        [Test]
        public async Task ResolveProblem_StaysResolvedUntilTheFilesGoMissingAgain()
        {
            var snes = Path.Combine(_lib, "snes");
            Add(new Game { Id = 1, Title = "Movie", PlatformId = Snes, Path = Put(snes, "Movie.mkv"), IgdbId = 1 },
                new Game { Id = 2, Title = "Bare", PlatformId = Snes, Path = Put(snes, "Bare.sfc") },
                new Game { Id = 3, Title = "Gone", PlatformId = Snes, Path = Path.Combine(snes, "Gone.sfc"), IgdbId = 3, Status = GameStatus.Missing, MissingSince = DateTime.UtcNow.AddDays(-1) },
                new Game { Id = 4, Title = "Other", PlatformId = Snes, Path = Put(snes, "Other.sfc") });
            var controller = Controller();
            Assert.That(await Problems(controller), Is.EquivalentTo(new[] { "1:invalid_format", "2:no_metadata", "3:missing_file", "4:no_metadata" }));

            var repo = new SqliteGameRepository(new DbFactory(_db));
            var loadedBefore = await repo.GetAllAsync();
            foreach (var id in new[] { 1, 2, 3 }) Assert.That(await controller.ResolveProblem(id), Is.InstanceOf<OkObjectResult>());

            Assert.That(await Problems(controller), Is.EqualTo(new[] { "4:no_metadata" }));
            Assert.That(await controller.ResolveProblem(99), Is.InstanceOf<NotFoundResult>());

            // A job saving a copy it loaded before the click keeps it resolved
            foreach (var game in loadedBefore) await repo.UpdateAsync(game.Id, game);
            Assert.That(await Problems(controller), Is.EqualTo(new[] { "4:no_metadata" }));

            // Found again, then gone again
            await repo.ApplyContentStateAsync(3, GameContent.Present, DateTime.UtcNow, null);
            await repo.ApplyContentStateAsync(3, GameContent.Gone, DateTime.UtcNow, Path.Combine(snes, "Gone.sfc"));
            // Lost now, by a scan that started before the click
            await repo.ApplyContentStateAsync(2, GameContent.Gone, DateTime.UtcNow.AddHours(-1), Path.Combine(snes, "Bare.sfc"));
            Assert.That(await Problems(controller), Is.EquivalentTo(new[] { "2:missing_file", "3:missing_file", "4:no_metadata" }));
        }

        // One request for the whole selection: the rows go with what hangs on them, the files stay on disk
        [Test]
        public async Task DeleteMany_RemovesTheGamesButNotTheirFiles()
        {
            var snes = Path.Combine(_lib, "snes");
            var kept = Put(snes, "Kept.sfc");
            Add(new Game { Id = 1, Title = "One", PlatformId = Snes, Path = Put(snes, "One.sfc") },
                new Game { Id = 2, Title = "Two", PlatformId = Snes, Path = Put(snes, "Two.sfc") },
                new Game { Id = 3, Title = "Kept", PlatformId = Snes, Path = kept });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.GameFiles.AddRange(new GameFile { GameId = 1, RelativePath = "One.sfc" }, new GameFile { GameId = 3, RelativePath = "Kept.sfc" });
                ctx.Tags.Add(new Tag { Name = "Favs", GameTags = { new GameTag { GameId = 1 }, new GameTag { GameId = 3 } } });
                ctx.SaveChanges();
            }

            var result = await Controller().DeleteMany(new List<int> { 1, 2, 2, 99 });

            Assert.That((IEnumerable<int>)Value(((OkObjectResult)result).Value!, "deleted")!, Is.EquivalentTo(new[] { 1, 2 }));
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.Games.Select(g => g.Id), Is.EqualTo(new[] { 3 }));
            Assert.That(check.GameFiles.Select(f => f.GameId), Is.EqualTo(new[] { 3 }));
            Assert.That(check.GameTags.Select(t => t.GameId), Is.EqualTo(new[] { 3 }));
            Assert.That(File.Exists(Path.Combine(snes, "One.sfc")) && File.Exists(Path.Combine(snes, "Two.sfc")), Is.True);
            Assert.That(await Controller().DeleteMany(new List<int>()), Is.InstanceOf<BadRequestObjectResult>());
        }

        // Untracked, still with every file linked to its game
        [Test]
        public async Task GetAll_ReturnsTheGamesWithTheirFiles()
        {
            Add(new Game { Id = 1, Title = "One", PlatformId = Snes, GameFiles = { new GameFile { RelativePath = "a.sfc" }, new GameFile { RelativePath = "b.sfc" } } },
                new Game { Id = 2, Title = "Two", PlatformId = Snes });
            var repo = new SqliteGameRepository(new DbFactory(_db));

            var all = await repo.GetAllAsync();

            var one = all.Single(g => g.Id == 1);
            Assert.That(one.GameFiles.Select(f => f.RelativePath), Is.EquivalentTo(new[] { "a.sfc", "b.sfc" }));
            Assert.That(one.GameFiles.All(f => f.GameId == 1 && ReferenceEquals(f.Game, one)), Is.True);
            Assert.That(all.Single(g => g.Id == 2).GameFiles, Is.Empty);
            Assert.That((await repo.GetAllLightAsync()).Select(g => g.Title), Is.EquivalentTo(new[] { "One", "Two" }));

            // What a caller changes on the list is saved through the repository, as before
            one.Title = "One Renamed";
            await repo.UpdateAsync(1, one);
            Assert.That(Row(1).Title, Is.EqualTo("One Renamed"));
        }
    }
}
