using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.Games
{
    // The scanner writes only what it changes, on the row as it is stored, and a second release
    // beside a game in the library never takes that game's place.
    [TestFixture]
    public class ScannerUpdateTest
    {
        private const int Gba = 52;
        private const int Ps3 = 22;
        private const int Ps1 = 20;
        private const int Pc = 1;
        private const int Switch = 46;
        private const int Ps4 = 23;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // IGDB that knows one game, found by its title or by search; every other search finds nothing
        private sealed class FakeIgdb : HttpMessageHandler, IGameMetadataServiceFactory
        {
            private readonly string _title;
            private readonly int _id;
            private readonly string _search;
            public FakeIgdb(string title = "", int id = 0, string? search = null) { _title = title; _id = id; _search = search ?? title; }
            public int Searches { get; private set; }

            public GameMetadataService CreateService()
            {
                var igdb = new IgdbClient("id", "secret");
                typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(this, false));
                return new GameMetadataService(igdb, new SteamClient());
            }

            public void RefreshConfiguration() { }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
            {
                var body = "{\"access_token\":\"t\",\"expires_in\":3600}";
                if (request.RequestUri!.Host == "api.igdb.com")
                {
                    var query = await request.Content!.ReadAsStringAsync(ct);
                    if (query.Contains("search ")) Searches++;
                    body = _id > 0 && (query.Contains($"search \"{_search}\"") || query.Contains($"where id = ({_id})"))
                        ? $"[{{\"id\":{_id},\"name\":\"{_title}\",\"cover\":{{\"image_id\":\"cover{_id}\"}}}}]"
                        : "[]";
                }
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
            }
        }

        private string _root = null!;
        private string _lib = null!;
        private SqliteConnection _connection = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_scanupdate_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            // Not "library": that name counts as a container
            _lib = Directory.CreateDirectory(Path.Combine(_root, "media")).FullName;
            // SQLite, so the statements and the unique indexes are what an install has
            _connection = new SqliteConnection("DataSource=:memory:");
            _connection.Open();
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(_connection).Options;
            using var ctx = new RetroArrDbContext(_db);
            ctx.Database.EnsureCreated();
            ctx.Platforms.AddRange(PlatformDefinitions.AllPlatforms.Select(p => p.Clone()));
            ctx.SaveChanges();
        }

        [TearDown]
        public void TearDown()
        {
            _connection.Dispose();
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private MediaScannerService Scanner(IGameMetadataServiceFactory metadata, IGameRepository? repo = null, string? winePrefix = null, DiscoveredGameRepository? discoveries = null)
        {
            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib, WinePrefixPath = winePrefix ?? string.Empty });
            return new MediaScannerService(config, metadata, repo ?? new SqliteGameRepository(new DbFactory(_db)), new TitleCleanerService(), discoveryRepo: discoveries);
        }

        private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine(new[] { _lib }.Concat(parts).ToArray())).FullName;

        private static string Put(string folder, string name)
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, "rom");
            return path;
        }

        private async Task Seed(params Game[] games)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.Games.AddRange(games);
            await ctx.SaveChangesAsync();
        }

        private Game Row(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.AsNoTracking().Single(g => g.Id == id);
        }

        private List<Game> Rows()
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.AsNoTracking().ToList();
        }

        // What the user does in the UI while a scan runs
        private void Unmonitor(int id) => Edit(id, g => g.Monitored = false);

        // What the user or an import does to the stored row
        private void Edit(int id, Action<Game> edit)
        {
            using var ctx = new RetroArrDbContext(_db);
            edit(ctx.Games.Single(g => g.Id == id));
            ctx.SaveChanges();
        }

        private void Delete(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.Games.Remove(ctx.Games.Single(g => g.Id == id));
            ctx.SaveChanges();
        }

        private List<string> GameFiles(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.GameFiles.Where(f => f.GameId == id).Select(f => f.RelativePath).ToList();
        }

        // ---- Repository ----

        [Test]
        public async Task UpdateFieldsAsync_LeavesOtherColumnsAsStored()
        {
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = "/lib/gba/Advance Wars", Monitored = true, Images = new GameImages { CoverUrl = "old" } });
            var repo = new SqliteGameRepository(new DbFactory(_db));

            var saved = await repo.UpdateFieldsAsync(1, g =>
            {
                // The user unmonitors the game after the row was read
                Unmonitor(1);
                g.Path = "/lib/gba/Advance Wars (Europe).gba";
                g.Images = new GameImages { CoverUrl = "new" };
            });

            var row = Row(1);
            Assert.That(row.Monitored, Is.False);
            Assert.That(row.Path, Is.EqualTo("/lib/gba/Advance Wars (Europe).gba"));
            Assert.That(row.Images.CoverUrl, Is.EqualTo("new"));
            Assert.That(saved!.Path, Is.EqualTo(row.Path));
            Assert.That(await repo.UpdateFieldsAsync(2, g => g.Path = "x"), Is.Null);
        }

        [Test]
        public async Task CachedRepository_UpdateFieldsAsync_Invalidates()
        {
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba });
            var cache = new MissingContentTest.FakeCache();
            var repo = new CachedGameRepository(new SqliteGameRepository(new DbFactory(_db)), cache, new CacheSettings());

            await repo.UpdateFieldsAsync(1, g => g.Path = "/lib/gba/Advance Wars");

            Assert.That(cache.Removed, Is.SupersetOf(new[] { CacheKeys.GameDetail(1), CacheKeys.GamesAll, CacheKeys.GamesAll + ":light" }));
            Assert.That(Row(1).Path, Is.EqualTo("/lib/gba/Advance Wars"));
        }

        [Test]
        public async Task UpsertGameFilesAsync_AddsAndUpdates_NeverRemoves()
        {
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch });
            var repo = new SqliteGameRepository(new DbFactory(_db));
            await repo.SyncGameFilesAsync(1, new List<GameFile>
            {
                new() { RelativePath = "Zelda.nsp", Size = 1, FileType = "Main" },
                new() { RelativePath = "Updates/Zelda v1.nsp", Size = 1, FileType = "Patch" }
            });

            await repo.UpsertGameFilesAsync(1, new List<GameFile>
            {
                new() { RelativePath = "Zelda.nsp", Size = 2, FileType = "Main" },
                new() { RelativePath = "/lib/switch/Updates/Zelda v2.nsp", Size = 3, FileType = "Patch" }
            });

            var files = (await repo.GetGameFilesAsync(1)).ToDictionary(f => f.RelativePath, f => f.Size);
            Assert.That(files, Is.EquivalentTo(new Dictionary<string, long>
            {
                ["Zelda.nsp"] = 2,
                ["Updates/Zelda v1.nsp"] = 1,
                ["/lib/switch/Updates/Zelda v2.nsp"] = 3
            }));
        }

        [Test]
        public async Task CachedRepository_UpsertGameFilesAsync_Invalidates()
        {
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch });
            var cache = new MissingContentTest.FakeCache();
            var repo = new CachedGameRepository(new SqliteGameRepository(new DbFactory(_db)), cache, new CacheSettings());

            await repo.UpsertGameFilesAsync(1, new List<GameFile> { new() { RelativePath = "Updates/Zelda v1.nsp", Size = 1, FileType = "Patch" } });

            Assert.That(cache.Removed, Is.SupersetOf(new[] { CacheKeys.GameFiles(1), CacheKeys.GameDetail(1) }));
            Assert.That(GameFiles(1), Is.EqualTo(new[] { "Updates/Zelda v1.nsp" }));
        }

        [Test]
        public async Task UpsertGameFilesAsync_RefreshesWhatTheNewClassificationFound()
        {
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch });
            var repo = new SqliteGameRepository(new DbFactory(_db));
            await repo.UpsertGameFilesAsync(1, new List<GameFile> { new() { RelativePath = "Updates/Zelda.nsp", Size = 1, FileType = "Patch", Version = "v1", ContentName = "Zelda" } });

            await repo.UpsertGameFilesAsync(1, new List<GameFile> { new() { RelativePath = "Updates/Zelda.nsp", Size = 1, FileType = "Patch", Version = "v2", TitleId = "0100000000010800" } });

            var row = (await repo.GetGameFilesAsync(1)).Single();
            Assert.That((row.Version, row.ContentName, row.TitleId, row.Serial), Is.EqualTo(("v2", "Zelda", "0100000000010800", (string?)null)));
        }

        [Test]
        public async Task CachedRepository_RemoveGameFilesAsync_InvalidatesTheGamesThatLostARow()
        {
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch }, new Game { Id = 2, Title = "Mario", PlatformId = Switch });
            var cache = new MissingContentTest.FakeCache();
            var repo = new CachedGameRepository(new SqliteGameRepository(new DbFactory(_db)), cache, new CacheSettings());
            await repo.UpsertGameFilesAsync(1, new List<GameFile>
            {
                new() { RelativePath = "Zelda.nsp", Size = 1, FileType = "Main" },
                new() { RelativePath = "Updates/Zelda v1.nsp", Size = 1, FileType = "Patch" },
                new() { RelativePath = "Updates/Zelda v2.nsp", Size = 1, FileType = "Patch" }
            });
            cache.Removed.Clear();
            var linked = await repo.GetSupplementaryFilesAsync(new[] { 1, 2 });
            Assert.That(linked.Select(f => f.RelativePath), Is.EquivalentTo(new[] { "Updates/Zelda v1.nsp", "Updates/Zelda v2.nsp" }));

            // Exactly these rows: game 1's v2 is not one of them, and no row has the second id
            var games = await repo.RemoveGameFilesAsync(new[] { linked.Single(f => f.RelativePath.EndsWith("v1.nsp")).Id, 999 });

            Assert.That(games, Is.EqualTo(new[] { 1 }));
            Assert.That(cache.Removed, Is.EquivalentTo(new[] { CacheKeys.GameFiles(1), CacheKeys.GameDetail(1) }));
            Assert.That(GameFiles(1), Is.EquivalentTo(new[] { "Zelda.nsp", "Updates/Zelda v2.nsp" }));
        }

        // ---- A second release beside a game ----

        // A "+" in the title makes no bundle of the game's update: it stays an update, no second game
        [Test]
        public async Task UpdateOfAGameWithAPlusInItsTitle_IsNoGameOfItsOwn()
        {
            var switchFolder = Folder("switch");
            var game = Put(switchFolder, "Super Mario 3D World + Bowser's Fury.nsp");
            Put(switchFolder, "Super Mario 3D World + Bowser's Fury Update v1.1.0.nsp");
            await Seed(new Game { Id = 1, Title = "Super Mario 3D World + Bowser's Fury", PlatformId = Switch, IgdbId = 9, Path = game, ExecutablePath = game, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner(new FakeIgdb("Super Mario 3D World + Bowser's Fury", 9)).ScanAsync();

            Assert.That(Rows().Select(g => g.Title), Is.EqualTo(new[] { "Super Mario 3D World + Bowser's Fury" }));
        }

        [Test]
        public async Task RegionFileInTheGamesFolder_IsPartOfTheGame()
        {
            var folder = Folder("gba", "Advance Wars");
            var rom = Put(folder, "Advance Wars.gba");
            Put(folder, "Advance Wars (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = folder, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });
            var igdb = new FakeIgdb("Advance Wars", 7);

            await Scanner(igdb).ScanAsync();

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(folder));
            Assert.That(row.ExecutablePath, Is.EqualTo(rom));
            Assert.That(igdb.Searches, Is.EqualTo(0), "the file was looked up as a game of its own");
            Assert.That(Rows(), Has.Count.EqualTo(1));
            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.GameFiles.Where(f => f.GameId == 1).Select(f => f.RelativePath), Does.Contain("Advance Wars (Europe).gba"));
        }

        [Test]
        public async Task RegionFileBesideTheGame_DoesNotMoveIt()
        {
            var folder = Folder("gba", "Advance Wars");
            var rom = Put(folder, "Advance Wars.gba");
            Put(Folder("gba"), "Advance Wars (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = folder, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });
            var igdb = new FakeIgdb("Advance Wars", 7);

            await Scanner(igdb).ScanAsync();

            Assert.That(igdb.Searches, Is.EqualTo(1));
            Assert.That(Row(1).Path, Is.EqualTo(folder));
            Assert.That(Row(1).ExecutablePath, Is.EqualTo(rom));
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task GameMappedToThePlatformFolder_ItsOwnFileNarrowsThePath()
        {
            var gba = Folder("gba");
            var rom = Put(gba, "Advance Wars (USA).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = gba, ExecutablePath = rom, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb("Advance Wars", 7)).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(rom));
            Assert.That(Row(1).ExecutablePath, Is.EqualTo(rom));
        }

        // An import put another release into the game's folder, and the game's main file still names the loose file
        // it had: that file lies outside the folder, so it is no narrowing and the game keeps its folder
        [Test]
        public async Task GameFolderWithAnImport_OldMainFileBesideIt_DoesNotMoveTheGame()
        {
            var old = Put(Folder("gba"), "Advance Wars (USA).gba");
            var folder = Folder("gba", "Advance Wars");
            Put(folder, "Advance Wars (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = folder, ExecutablePath = old, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb("Advance Wars", 7)).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(folder));
        }

        [Test]
        public async Task GameFoundAtANewPath_MovesThere_AndKeepsAnEditMadeDuringTheScan()
        {
            var loose = Put(Folder("gba"), "Advance Wars (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = Path.Combine(_lib, "gba", "Advance Wars"), Status = GameStatus.Downloaded, Monitored = true });
            var scanner = Scanner(new FakeIgdb("Advance Wars", 7));
            // Runs after the scan loaded its copy of every game
            scanner.OnScanStarted += () => Unmonitor(1);

            await scanner.ScanAsync();

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(loose));
            Assert.That(row.ExecutablePath, Is.EqualTo(loose));
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(row.Monitored, Is.False);
        }

        // ---- Other scanner writes keep a change made after the scan loaded the game ----

        [Test]
        public async Task DedupGuard_KeepsAnEditMadeDuringTheScan()
        {
            var folder = Folder("gba", "Pokemon Emerald Version");
            var rom = Put(folder, "Pokemon Emerald Version.gba");
            // Another dump of the game: the scan doesn't match its name, the lookup names it after the game
            Put(Folder("gba"), "Pokemon Emerald.gba");
            await Seed(new Game { Id = 1, Title = "Pokemon Emerald Version", PlatformId = Gba, Path = folder, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });
            var scanner = Scanner(new FakeIgdb("Pokemon Emerald Version", 9, search: "Pokemon Emerald"));
            scanner.OnScanStarted += () => Unmonitor(1);

            await scanner.ScanAsync();

            Assert.That(Row(1).Monitored, Is.False);
            Assert.That(Row(1).Path, Is.EqualTo(folder));
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RegionBackfill_KeepsAnEditMadeDuringTheScan()
        {
            // Metadata renamed the game, so its file is a candidate again
            var rom = Put(Folder("gba"), "Advance Wars 2 (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Black Hole Rising", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });
            var scanner = Scanner(new FakeIgdb());
            scanner.OnScanStarted += () => Unmonitor(1);

            await scanner.ScanAsync();

            Assert.That(Row(1).Region, Is.EqualTo("Europe"));
            Assert.That(Row(1).Monitored, Is.False);
        }

        // The user unmonitors the game after the scanner read it, right before the scanner writes it
        private IGameRepository UnmonitoredBeforeEachWrite(int id)
        {
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, _) =>
            {
                if (method.Name is nameof(IGameRepository.UpdateAsync) or nameof(IGameRepository.UpdateFieldsAsync)) Unmonitor(id);
            };
            return repo;
        }

        [TestCase(false, 9, "cover9")]
        [TestCase(true, null, null)]
        public async Task MetadataMerge_KeepsAnEditMadeDuringTheImport(bool confirmedByUser, int? igdbId, string? cover)
        {
            var folder = Folder("gba", "Advance Wars");
            var rom = Put(folder, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Status = GameStatus.Missing, Monitored = true, MetadataConfirmedByUser = confirmedByUser });

            await Scanner(new FakeIgdb("Advance Wars", 9), UnmonitoredBeforeEachWrite(1))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = folder, ExecutablePath = rom, PlatformKey = "gba" });

            var row = Row(1);
            Assert.That(row.IgdbId, Is.EqualTo(igdbId));
            Assert.That(row.Images.CoverUrl, cover == null ? Is.Null : Does.Contain(cover));
            Assert.That(row.Monitored, Is.False);
        }

        [Test]
        public async Task DiscoveredAtANewPath_KeepsAnEditMadeDuringTheImport()
        {
            var found = Folder("ps3", "Demons Souls");
            Put(found, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = 7, Path = Path.Combine(_lib, "ps3", "Old Folder"), Status = GameStatus.Missing, Monitored = true });

            await Scanner(new FakeIgdb(), UnmonitoredBeforeEachWrite(1))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Demons Souls", Path = found, PlatformKey = "ps3" });

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(found));
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(row.Monitored, Is.False);
        }

        // After a write the scan's copy is the stored row: an import moved the game while the scan ran, so the copy
        // found in the library stays unlinked and the game's files are read where the row points
        [Test]
        public async Task GameMovedDuringTheScan_ItsFilesAreReadWhereTheRowPoints()
        {
            var found = Folder("ps3", "Demons Souls");
            Put(found, "PS3_GAME/USRDIR/EBOOT.BIN");
            // Another library root, say
            var imported = Directory.CreateDirectory(Path.Combine(_root, "imported", "Demons Souls")).FullName;
            Put(imported, "PS3_GAME/USRDIR/EBOOT.BIN");
            Put(imported, "PS3_GAME/PARAM.SFO");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = 7, Path = Gone("ps3", "Old Folder"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });
            var scanner = Scanner(new FakeIgdb());
            scanner.OnScanStarted += () => Edit(1, g => g.Path = imported);

            await scanner.ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(imported));
            Assert.That(GameFiles(1), Does.Contain("PS3_GAME/PARAM.SFO"));
        }

        [Test]
        public async Task GameDeletedDuringTheScan_ItsCopyIsNotCountedAsFound()
        {
            var found = Folder("ps3", "Demons Souls");
            Put(found, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = 7, Path = Gone("ps3", "Old Folder"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });
            var scanner = Scanner(new FakeIgdb());
            scanner.OnScanStarted += () => Delete(1);

            var added = await scanner.ScanAsync();

            Assert.That(added, Is.Zero);
            Assert.That(GameFiles(1), Is.Empty);
        }

        // Each import judges the library as it is then: the games of a share that went down since the last one are held
        [Test]
        public async Task Import_ShareWentDownSinceTheLastImport_CopyDoesNotMoveTheGame()
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            var copy = Put(Folder("gba"), "Pokemon Emerald.gba");
            var stored = Gone("gba", "Pokemon Emerald Version");
            var others = Enumerable.Range(2, 5).Select(i => Folder("gba", $"Game {i}")).ToList();
            others.ForEach(f => Put(f, Path.GetFileName(f) + ".gba"));
            await Seed(others.Select((f, i) => new Game { Id = i + 2, Title = Path.GetFileName(f), PlatformId = Gba, Path = f, Status = GameStatus.Downloaded, Monitored = true })
                .Append(new Game { Id = 1, Title = "Pokemon Emerald Version", PlatformId = Gba, IgdbId = 9, Path = stored, Status = GameStatus.Downloaded, Monitored = true })
                .Append(new Game { Id = 7, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = Gone("gba", "Advance Wars"), Status = GameStatus.Downloaded, Monitored = true })
                .ToArray());
            var scanner = Scanner(new FakeIgdb("Pokemon Emerald Version", 9, search: "Pokemon Emerald"));

            Assert.That(await scanner.ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = rom, PlatformKey = "gba" }), Is.True);
            others.ForEach(f => Directory.Delete(f, true));
            await scanner.ImportDiscoveredAsync(new DiscoveredGame { Title = "Pokemon Emerald", Path = copy, PlatformKey = "gba" });

            Assert.That(Row(7).Path, Is.EqualTo(rom));
            Assert.That(Row(1).Path, Is.EqualTo(stored));
        }

        [Test]
        public async Task PlatformHeal_KeepsAnEditMadeDuringTheHeal()
        {
            var folder = Folder("gba", "Advance Wars");
            Put(folder, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Ps3, Path = folder, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner(new FakeIgdb(), UnmonitoredBeforeEachWrite(1)).HealWrongPlatformsAsync();

            Assert.That(Row(1).PlatformId, Is.EqualTo(Gba));
            Assert.That(Row(1).Monitored, Is.False);
        }

        // A row inside a PS4 container folder is junk and goes. A library below a folder that is only named like one
        // keeps its games.
        [TestCase(true)]
        [TestCase(false)]
        public async Task ContainerOrphans_ArePrunedOnlyBelowTheLibraryRoot(bool orphan)
        {
            _lib = Directory.CreateDirectory(Path.Combine(_root, "backup.ps4", "media")).FullName;
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            var junk = Put(Folder("ps4", "Bloodborne.ps4"), "CUSA03173/eboot.bin");
            await Seed(orphan
                ? new Game { Id = 1, Title = "Bloodborne", PlatformId = Ps4, Path = junk, Status = GameStatus.Downloaded }
                : new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Rows().Any(g => g.Id == 1), Is.EqualTo(!orphan));
        }

        // ---- Separate games are still found ----

        [Test]
        public async Task NewGameInItsOwnFolder_IsFound_BesideSharedFolders()
        {
            var gba = Folder("gba");
            var folder = Folder("gba", "Advance Wars");
            Put(folder, "Advance Wars.gba");
            var golden = Put(Folder("gba", "Golden Sun"), "Golden Sun.gba");
            var collection = Folder("gba", "Collection");
            var zelda = Put(collection, "Zelda.gba");
            // Games mapped to the library root, the platform folder or a container own none of them
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = folder, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "Mario Kart", PlatformId = Gba, Path = gba, Status = GameStatus.Downloaded },
                new Game { Id = 3, Title = "Metroid Fusion", PlatformId = Gba, Path = _lib, Status = GameStatus.Downloaded },
                new Game { Id = 4, Title = "Wario Land", PlatformId = Gba, Path = collection, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Rows().Select(g => g.Path), Does.Contain(golden).And.Contain(zelda));
        }

        [Test]
        public async Task GameMappedToAFolderOfOtherGames_TheyAreFound_AndItStays()
        {
            var nintendo = Folder("gba", "Nintendo");
            var golden = Put(nintendo, "Golden Sun.gba");
            var fusion = Put(Folder("gba", "Nintendo", "Metroid"), "Metroid Fusion.gba");
            await Seed(new Game { Id = 1, Title = "Mario Kart", PlatformId = Gba, Path = nintendo, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Rows().Select(g => g.Path), Does.Contain(golden).And.Contain(fusion));
            Assert.That(Row(1).Path, Is.EqualTo(nintendo));
        }

        // A Wine prefix inside the folder of another game: nothing above the prefix owns the games in it
        private async Task<(string Prefix, string Install)> WinePrefixInTheFolderOfDoom()
        {
            var doom = Directory.CreateDirectory(Path.Combine(_root, "apps")).FullName;
            Put(doom, "doom.exe");
            var prefix = Directory.CreateDirectory(Path.Combine(doom, "wine")).FullName;
            var install = Directory.CreateDirectory(Path.Combine(prefix, "drive_c", "Games", "Hollow Knight")).FullName;
            Put(install, "hollow_knight.exe");
            await Seed(new Game { Id = 1, Title = "Doom", PlatformId = Pc, Path = doom, Status = GameStatus.Downloaded });
            return (prefix, install);
        }

        [Test]
        public async Task WinePrefixInTheFolderOfAnotherGame_ItsGameIsFound()
        {
            var (prefix, install) = await WinePrefixInTheFolderOfDoom();

            await Scanner(new FakeIgdb(), winePrefix: prefix).ScanAsync();

            Assert.That(Rows().Where(g => g.IsExternal).Select(g => g.Path), Is.EqualTo(new[] { install }));
            Assert.That(Row(1).Path, Is.EqualTo(Path.Combine(_root, "apps")));
        }

        // The settings page sends the library root as the path to scan
        [TestCase(false)]
        [TestCase(true)]
        public async Task SettingsStyleScan_ScansTheWinePrefixToo(bool trailingSeparator)
        {
            var (prefix, install) = await WinePrefixInTheFolderOfDoom();

            await Scanner(new FakeIgdb(), winePrefix: prefix).ScanAsync(trailingSeparator ? _lib + Path.DirectorySeparatorChar : _lib, "default");

            Assert.That(Rows().Where(g => g.IsExternal).Select(g => g.Path), Is.EqualTo(new[] { install }));
        }

        // A discovery request while a discovery scan runs is skipped: the running one stays a discovery scan and keeps
        // its count, so the rest of what it finds doesn't land in the library
        [Test]
        public async Task DiscoveryRequestDuringADiscoveryScan_LeavesItsModeAndCount()
        {
            Put(Folder("gba"), "Advance Wars.gba");
            Put(Folder("snes"), "Super Metroid.sfc");
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var reads = 0;
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            // Held after the first platform's files, before the second's
            hook.Before = (method, _) =>
            {
                if (method.Name != nameof(IGameRepository.GetAllLightAsync) || Interlocked.Increment(ref reads) != 2) return;
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            };
            var discoveries = new DiscoveredGameRepository(new DbFactory(_db));
            var scanner = Scanner(new FakeIgdb(), repo, discoveries: discoveries);
            var running = Task.Run(() => scanner.DiscoverOnlyAsync());
            Assert.That(held.Wait(TimeSpan.FromSeconds(30)), Is.True);

            var second = await scanner.DiscoverOnlyAsync();

            release.Set();
            Assert.That(second, Is.Zero);
            Assert.That(await running, Is.EqualTo(2));
            Assert.That(Rows(), Is.Empty, "the rest of the discovery scan added games to the library");
            Assert.That(await discoveries.CountAsync(), Is.EqualTo(2));
            Assert.That(scanner.IsDiscoveryMode, Is.False, "the mode outlived its scan");
        }

        // A failing handler of the finished scan neither fails the scan nor frees the slot a second time: the scan that took
        // it meanwhile keeps it
        [Test]
        public async Task ScanFinishedHandlerFails_NextScanKeepsTheSlot()
        {
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var holdNext = false;
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, _) =>
            {
                if (!holdNext || method.Name != nameof(IGameRepository.GetAllLightAsync)) return;
                holdNext = false;
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            };
            var scanner = Scanner(new FakeIgdb(), repo);
            Task<int>? next = null;
            scanner.OnScanFinished += _ =>
            {
                if (next != null) return;
                holdNext = true;
                next = Task.Run(() => scanner.ScanAsync());
                held.Wait(TimeSpan.FromSeconds(30));
                throw new InvalidOperationException("handler failed");
            };

            await scanner.ScanAsync();
            var running = scanner.IsScanning;

            release.Set();
            await next!;
            Assert.That(running, Is.True, "the next scan's slot was freed");
            Assert.That(scanner.IsScanning, Is.False);
        }

        // A scan that ends before it got going leaves none running, so the next one is not skipped
        [TestCase(false)]
        [TestCase(true)]
        public async Task ScanEndingEarly_LeavesNoScanRunning(bool readFails)
        {
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, _) =>
            {
                if (readFails && method.Name == nameof(IGameRepository.GetAllLightAsync)) throw new IOException("database is locked");
            };
            var scanner = Scanner(new FakeIgdb(), repo);

            if (readFails)
                Assert.ThrowsAsync<IOException>(() => scanner.ScanAsync());
            else
                Assert.That(await scanner.ScanAsync(Path.Combine(_root, "gone")), Is.Zero);

            Assert.That(scanner.IsScanning, Is.False);
        }

        [Test]
        public async Task WinePrefixInTheFolderOfAnotherGame_GameTakesItsInstall()
        {
            var (prefix, install) = await WinePrefixInTheFolderOfDoom();
            // A store sync added the game after the discovery scan listed its install
            await Seed(new Game { Id = 2, Title = "Hollow Knight", PlatformId = Pc, IsExternal = true, Status = GameStatus.Released, Monitored = true });

            await Scanner(new FakeIgdb(), winePrefix: prefix)
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Hollow Knight", Path = install, ExecutablePath = Path.Combine(install, "hollow_knight.exe"), PlatformKey = "pc_windows", IsExternal = true });

            Assert.That(Row(2).Path, Is.EqualTo(install));
        }

        [Test]
        public async Task FolderModeConsole_NewFolderFound_AndAGameAtItsOwnFolderIsNotSkipped()
        {
            var souls = Folder("ps3", "Demons Souls (Europe)");
            Put(souls, "PS3_GAME/USRDIR/EBOOT.BIN");
            var rain = Folder("ps3", "Heavy Rain");
            Put(rain, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = "Demon's Souls", PlatformId = Ps3, Path = souls, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Rows().Select(g => g.Path), Does.Contain(rain));
            Assert.That(Row(1).Region, Is.EqualTo("Europe"));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task GameAtItsOwnFolder_StoredPathWrittenDifferently_IsNotAddedAgain(bool trailingSeparator)
        {
            var souls = Folder("ps3", "Demons Souls (Europe)");
            Put(souls, "PS3_GAME/USRDIR/EBOOT.BIN");
            var stored = trailingSeparator ? souls + Path.DirectorySeparatorChar : Path.Combine(_lib, "ps3", ".", "Demons Souls (Europe)");
            await Seed(new Game { Id = 1, Title = "Demon's Souls", PlatformId = Ps3, Path = stored, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Rows(), Has.Count.EqualTo(1));
            Assert.That(Row(1).Region, Is.EqualTo("Europe"));
        }

        // The file of another region is another game, whether or not the game in the library still has its own,
        // and when the lookup names it after that game too. The metadata id stays the other region's: one per platform.
        [TestCase(true, false)]
        [TestCase(false, false)]
        [TestCase(true, true)]
        [TestCase(false, true)]
        public async Task OtherRegion_GetsItsOwnRow(bool usaGone, bool lookupMatches)
        {
            var europe = Put(Folder("gba"), "Advance Wars (Europe).gba");
            var usa = usaGone ? Gone("gba", "Advance Wars (USA).gba") : Put(Folder("gba", "Advance Wars"), "Advance Wars (USA).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", Region = "USA", PlatformId = Gba, IgdbId = 7, Path = usa, ExecutablePath = usa, Status = usaGone ? GameStatus.Missing : GameStatus.Downloaded, MissingSince = usaGone ? DateTime.UtcNow : null, Monitored = true });

            await Scanner(lookupMatches ? new FakeIgdb("Advance Wars", 7) : new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(usa));
            Assert.That(Rows().Where(g => g.Id != 1).Select(g => (g.Path, g.Region, g.IgdbId)), Is.EqualTo(new[] { (europe, "Europe", (int?)null) }));
        }

        // The lookup names the file after the other region's game: the release takes its metadata, not its id
        [Test]
        public async Task OtherRegion_LookupMatch_TakesTheMetadataWithoutTheId()
        {
            Put(Folder("gba"), "Advance Wars (Europe).gba");
            var usa = Put(Folder("gba", "Advance Wars"), "Advance Wars (USA).gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", Region = "USA", PlatformId = Gba, IgdbId = 7, Path = usa, ExecutablePath = usa, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb("Advance Wars", 7)).ScanAsync();

            var europe = Rows().Single(g => g.Id != 1);
            Assert.That((europe.Title, europe.Region, europe.IgdbId), Is.EqualTo(("Advance Wars", "Europe", (int?)null)));
            Assert.That(europe.Images.CoverUrl, Does.Contain("cover7"));
            Assert.That(europe.Overview, Is.Not.EqualTo("Metadata not found. Added via offline fallback."));
            Assert.That(europe.MetadataReviewReason, Is.EqualTo("Regional release of 'Advance Wars', IGDB id 7 kept on entry 1 (USA)"));
        }

        // The library has the game on two platforms: the scanned platform's entry takes the file, and no second
        // entry with the id is made there
        [Test]
        public async Task LookupMatch_GameOnTwoPlatforms_TheScannedPlatformsEntryTakesTheFile()
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Ps1, IgdbId = 7, Status = GameStatus.Released },
                new Game { Id = 2, Title = "Advance Wars (GBA)", PlatformId = Gba, IgdbId = 7, Path = Gone("gba", "Advance Wars (GBA).gba"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });

            await Scanner(new FakeIgdb("Advance Wars", 7)).ScanAsync();

            Assert.That(Row(2).Path, Is.EqualTo(rom));
            Assert.That(Rows(), Has.Count.EqualTo(2));
        }

        // ---- A game moved on disk is found at its new place ----

        private string Gone(params string[] parts) => Path.Combine(new[] { _lib }.Concat(parts).ToArray());

        [TestCase(7)]
        [TestCase(null)]
        public async Task MovedRom_IsFoundAtItsNewPath(int? igdbId)
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            // Without an id the game is looked up again, and the lookup finds nothing (or can't reach the source)
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = igdbId, Year = 2001, Overview = "Turn-based tactics", Images = new GameImages { CoverUrl = "cover" }, Path = Gone("gba", "Advance Wars"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(rom));
            Assert.That(row.ExecutablePath, Is.EqualTo(rom));
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded), "a monitored game that isn't there is downloaded again");
            Assert.That(row.MissingSince, Is.Null);
            Assert.That((row.Overview, row.Year, row.Images.CoverUrl), Is.EqualTo(("Turn-based tactics", 2001, "cover")), "the offline fallback replaced the metadata");
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        // Where the file system tells case apart, a folder renamed in case only is another folder
        [Test, Platform(Exclude = "Win,MacOsX")]
        public async Task GameFolderRenamedInCase_IsFoundThere()
        {
            var rom = Put(Folder("gba", "Advance Wars"), "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = Gone("gba", "advance wars"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(rom));
        }

        [Test]
        public async Task MovedFolderGame_IsFoundAtItsNewFolder()
        {
            var found = Folder("ps3", "Demons Souls");
            Put(found, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = 7, Path = Gone("ps3", "Old Folder"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(found));
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task MovedRom_NamedAfterTheGameOnlyByTheLookup_IsFound()
        {
            // The scan doesn't match the file's name to the game; the lookup names it after the game
            var rom = Put(Folder("gba"), "Pokemon Emerald (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Pokemon Emerald Version", Region = "Europe", PlatformId = Gba, Path = Gone("gba", "Pokemon Emerald Version (Europe).gba"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });

            await Scanner(new FakeIgdb("Pokemon Emerald Version", 9, search: "Pokemon Emerald")).ScanAsync();

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(rom));
            Assert.That(row.ExecutablePath, Is.EqualTo(rom));
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded), "a monitored game that isn't there is downloaded again");
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task MovedRegionRelease_IsFound_TheOtherRegionKeepsItsFile()
        {
            var europe = Put(Folder("gba"), "Advance Wars (Europe).gba");
            var usa = Put(Folder("gba", "Moved"), "Advance Wars (USA).gba");
            // The metadata id is one region's alone on a platform
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", Region = "USA", PlatformId = Gba, IgdbId = 7, Path = Gone("gba", "Advance Wars (USA)"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true },
                new Game { Id = 2, Title = "Advance Wars", Region = "Europe", PlatformId = Gba, Path = europe, ExecutablePath = europe, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(usa));
            Assert.That(Row(2).Path, Is.EqualTo(europe));
            Assert.That(Rows(), Has.Count.EqualTo(2));
        }

        [Test]
        public async Task MovedM3uGame_KeepsItsPlaylist_NotALaterDisc()
        {
            var folder = Folder("psx", "FF7");
            Put(folder, "CD1/Final Fantasy VII.chd");
            Put(folder, "CD2/Final Fantasy VII.chd");
            var m3u = Path.Combine(folder, "Final Fantasy VII.m3u");
            File.WriteAllText(m3u, "CD1/Final Fantasy VII.chd\nCD2/Final Fantasy VII.chd\n");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, IgdbId = 7, Path = Gone("psx", "Final Fantasy VII", "Final Fantasy VII.m3u"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(m3u));
            Assert.That(Row(1).ExecutablePath, Is.EqualTo(m3u));
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        [Test]
        public async Task RomInAnotherGamesFolder_DoesNotMoveALostGame()
        {
            var golden = Folder("gba", "Golden Sun");
            var goldenRom = Put(golden, "Golden Sun.gba");
            Put(golden, "Advance Wars.gba");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = Gone("gba", "Advance Wars"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true },
                new Game { Id = 2, Title = "Golden Sun", PlatformId = Gba, IgdbId = 8, Path = golden, ExecutablePath = goldenRom, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(Gone("gba", "Advance Wars")));
            Assert.That(Row(2).Path, Is.EqualTo(golden));
            Assert.That(Rows(), Has.Count.EqualTo(2));
        }

        // Where the folder of a GBA game lies while its library is offline
        private async Task<string> OfflinePath(string outage, string folder)
        {
            if (outage == "share unmounted")
            {
                // The game lies on a share outside the library, mounted on an empty folder
                Directory.CreateDirectory(Path.Combine(_root, "share"));
                return Path.Combine(_root, "share", "gba", folder);
            }
            // Too many games gone at once: the breaker holds them all
            await Seed(Enumerable.Range(2, 5).Select(i => new Game { Id = i, Title = $"Game {i}", PlatformId = Gba, Path = Gone("gba", $"Game {i}"), Status = GameStatus.Downloaded, Monitored = true }).ToArray());
            return Gone("gba", folder);
        }

        // A game whose library is offline comes back with it, so a copy found elsewhere doesn't take its place
        [TestCase("share unmounted")]
        [TestCase("platform share down, its mount point refilled")]
        public async Task LibraryOffline_FoundCopyDoesNotMoveTheGame(string outage)
        {
            Put(Folder("gba"), "Advance Wars.gba");
            var stored = await OfflinePath(outage, "Advance Wars");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, IgdbId = 7, Path = stored, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(stored));
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Rows().Select(g => g.Path), Has.None.EndsWith("Advance Wars.gba"));
        }

        // Only the lookup names the copy after the game: by the game's id, or by its title (the dedup guard)
        [TestCase("share unmounted", 9)]
        [TestCase("share unmounted", null)]
        [TestCase("platform share down, its mount point refilled", 9)]
        [TestCase("platform share down, its mount point refilled", null)]
        public async Task LibraryOffline_CopyTheLookupNamesAfterTheGame_DoesNotMoveIt(string outage, int? igdbId)
        {
            var copy = Put(Folder("gba"), "Pokemon Emerald.gba");
            var stored = await OfflinePath(outage, "Pokemon Emerald Version");
            await Seed(new Game { Id = 1, Title = "Pokemon Emerald Version", PlatformId = Gba, IgdbId = igdbId, Path = stored, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner(new FakeIgdb("Pokemon Emerald Version", 9, search: "Pokemon Emerald")).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(stored));
            Assert.That(Rows().Select(g => g.Path), Has.None.EqualTo(copy));
        }

        [Test]
        public async Task LibraryOffline_FolderCopyFoundElsewhere_KeepsTheGamesFiles()
        {
            Put(Folder("ps3", "Demons Souls"), "PS3_GAME/USRDIR/EBOOT.BIN");
            // The game lies on a share outside the library, mounted on an empty folder
            Directory.CreateDirectory(Path.Combine(_root, "share"));
            var stored = Path.Combine(_root, "share", "ps3", "Demons Souls");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = 7, Path = stored, Status = GameStatus.Downloaded, Monitored = true });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.GameFiles.AddRange(
                    new GameFile { GameId = 1, RelativePath = "PS3_GAME/USRDIR/EBOOT.BIN", Size = 3, FileType = "Main" },
                    new GameFile { GameId = 1, RelativePath = "DLC/Demons Souls DLC.pkg", Size = 3, FileType = "DLC" });
                await ctx.SaveChangesAsync();
            }

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(stored));
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.GameFiles.Where(f => f.GameId == 1).Select(f => f.RelativePath),
                Is.EquivalentTo(new[] { "PS3_GAME/USRDIR/EBOOT.BIN", "DLC/Demons Souls DLC.pkg" }));
        }

        // ---- Disc sets ----

        [Test]
        public async Task LooseDiscs_AreOneGame_AndTheDiscIsNoRevision()
        {
            var psx = Folder("psx");
            Put(psx, "Final Fantasy VII (USA) (Disc 1).chd");
            Put(psx, "Final Fantasy VII (USA) (Disc 2).chd");

            await Scanner(new FakeIgdb()).ScanAsync();

            var rows = Rows();
            Assert.That(rows, Has.Count.EqualTo(1));
            Assert.That(rows[0].Revision, Is.Null, "a disc revision gave single-disc releases the revision bonus");
        }

        [Test]
        public async Task M3uGame_DiscsInSubfolders_KeepTheirFolder()
        {
            var psx = Folder("psx");
            Put(psx, "CD1/Final Fantasy VII.chd");
            Put(psx, "CD2/Final Fantasy VII.chd");
            var m3u = Path.Combine(psx, "Final Fantasy VII.m3u");
            File.WriteAllText(m3u, "CD1/Final Fantasy VII.chd\nCD2/Final Fantasy VII.chd\n");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = m3u, ExecutablePath = m3u, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).SyncGameFilesFromDisk(1, m3u);

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.GameFiles.Where(f => f.GameId == 1).Select(f => f.RelativePath),
                Is.EquivalentTo(new[] { "Final Fantasy VII.m3u", "CD1/Final Fantasy VII.chd", "CD2/Final Fantasy VII.chd" }));
        }

        // ---- Updates and DLC ----

        // Linked from the game's folder and from the platform's Updates folder, they outlast the resync of the
        // game's own file and every further scan, until their file is gone
        [Test]
        public async Task SingleFileGame_UpdatesInItsFolderAndThePlatformsUpdatesFolder_StayLinkedUntilGone()
        {
            var folder = Folder("switch", "Zelda");
            var nsp = Put(folder, "Zelda [0100000000010000].nsp");
            Put(folder, "Updates/Zelda [0100000000010800][v65536].nsp");
            var updates = Folder("switch", "Updates");
            var v2 = Put(updates, "Zelda [0100000000010800][v131072].nsp");
            var v3 = Put(updates, "Zelda [0100000000010800][v196608].nsp");
            var dlc = Put(updates, "Zelda DLC/Zelda [0100000000011000][v0].nsp");
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch, Path = nsp, ExecutablePath = nsp, Status = GameStatus.Downloaded });
            string Full(string path) => path.Replace(Path.DirectorySeparatorChar, '/');

            await Scanner(new FakeIgdb()).ScanAsync();
            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(GameFiles(1), Is.EquivalentTo(new[]
            {
                "Zelda [0100000000010000].nsp", "Updates/Zelda [0100000000010800][v65536].nsp", Full(v2), Full(v3), Full(dlc)
            }));

            File.Delete(v2);
            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(GameFiles(1), Is.EquivalentTo(new[]
            {
                "Zelda [0100000000010000].nsp", "Updates/Zelda [0100000000010800][v65536].nsp", Full(v3), Full(dlc)
            }));
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }

        // Stored relative to the game's folder, or to the folder a single-file game lies in; a file outside it by its full path
        [TestCase(true)]
        [TestCase(false)]
        public async Task UpdateInThePlatformsUpdatesFolder_IsStoredAgainstTheGamesFolder(bool folderGame)
        {
            var sw = Folder("switch");
            var nsp = Put(folderGame ? Folder("switch", "Zelda") : sw, "Zelda [0100000000010000].nsp");
            var update = Put(sw, "Updates/Zelda [0100000000010800][v65536].nsp");
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch, Path = folderGame ? Path.GetDirectoryName(nsp) : nsp, ExecutablePath = nsp, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(GameFiles(1), Is.EquivalentTo(new[]
            {
                "Zelda [0100000000010000].nsp",
                folderGame ? update.Replace(Path.DirectorySeparatorChar, '/') : "Updates/Zelda [0100000000010800][v65536].nsp"
            }));
        }

        // Linked to the game it matched best, an update goes to a better match added since, and leaves the first
        [Test]
        public async Task LinkedUpdate_MovesToABetterMatchAddedLater()
        {
            var sw = Folder("switch");
            var zelda = Put(sw, "Zelda.nsp");
            var update = Put(sw, "Updates/Zelda II [0100000000020800][v65536].nsp");
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Switch, Path = zelda, ExecutablePath = zelda, Status = GameStatus.Downloaded });
            await Scanner(new FakeIgdb()).ScanAsync();
            Assert.That(GameFiles(1), Does.Contain("Updates/Zelda II [0100000000020800][v65536].nsp"));

            var zelda2 = Put(Folder("switch", "Zelda II"), "Zelda II [0100000000020000].nsp");
            await Seed(new Game { Id = 2, Title = "Zelda II", PlatformId = Switch, Path = Path.GetDirectoryName(zelda2), ExecutablePath = zelda2, Status = GameStatus.Downloaded });
            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(GameFiles(1), Is.EqualTo(new[] { "Zelda.nsp" }));
            Assert.That(GameFiles(2), Is.EquivalentTo(new[] { "Zelda II [0100000000020000].nsp", update.Replace(Path.DirectorySeparatorChar, '/') }));
        }

        // A game folder named without the TitleID: its game file carries it
        [Test]
        public async Task Update_MatchesAFolderGameByTheTitleIdOfItsGameFile()
        {
            var nsp = Put(Folder("switch", "The Legend of Zelda"), "The Legend of Zelda [0100000000010000].nsp");
            var update = Put(Folder("switch"), "Updates/BotW [0100000000010800][v65536].nsp");
            await Seed(new Game { Id = 1, Title = "The Legend of Zelda", PlatformId = Switch, Path = Path.GetDirectoryName(nsp), ExecutablePath = nsp, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(GameFiles(1), Does.Contain(update.Replace(Path.DirectorySeparatorChar, '/')));
        }

        // The platform's linked rows are read once, so memory and time grow with them, not with games times linked files
        [Test]
        public async Task Unlink_ManyGamesAndFiles_StaysLinear()
        {
            var sw = Folder("switch");
            var games = Enumerable.Range(1, 2000)
                .Select(i => new Game { Id = i, Title = $"Game {i}", PlatformId = Switch, Path = Path.Combine(sw, $"Game {i}.nsp") })
                .ToList();
            Put(sw, "Game 1.nsp");
            Put(sw, "Game 2.nsp");
            await Seed(games.ToArray());
            var linkedTo = Enumerable.Range(0, 4000).ToDictionary(i => Path.Combine(sw, "Updates", $"Update {i}.nsp"), i => i % 2000 + 1);
            using (var ctx = new RetroArrDbContext(_db))
            {
                // Update 1 is game 2's now
                ctx.GameFiles.AddRange(
                    new GameFile { GameId = 1, RelativePath = "Updates/Update 1.nsp", FileType = "Patch" },
                    new GameFile { GameId = 2, RelativePath = "Updates/Update 1.nsp", FileType = "Patch" });
                await ctx.SaveChangesAsync();
            }
            var scanner = Scanner(new FakeIgdb());

            GC.Collect();
            var before = GC.GetTotalAllocatedBytes(true);
            var watch = Stopwatch.StartNew();
            await scanner.UnlinkFromOtherGamesAsync(linkedTo, games, "switch");
            watch.Stop();
            var allocated = GC.GetTotalAllocatedBytes(true) - before;

            Assert.That(GameFiles(1), Is.Empty);
            Assert.That(GameFiles(2), Is.EqualTo(new[] { "Updates/Update 1.nsp" }));
            Assert.That(allocated, Is.LessThan(64L << 20), $"{allocated >> 20} MB allocated");
            Assert.That(watch.Elapsed, Is.LessThan(TimeSpan.FromSeconds(20)));
        }

        // A file of an update or DLC is no game: a wanted game doesn't take it as its own and count as downloaded, and no
        // entry is made for it. The game with its DLC is a game.
        [TestCase("Mario Kart 8 Deluxe (DLCs).nsp", false)]
        [TestCase("Mario Kart 8 Deluxe - All DLCs.nsp", false)]
        [TestCase("Mario Kart 8 Deluxe Updates.nsp", false)]
        [TestCase("Mario Kart 8 Deluxe + All DLCs.nsp", true)]
        public async Task WantedGame_FileOfItsUpdateOrDlc_IsNoGame(string name, bool game)
        {
            var file = Put(Folder("switch"), name);
            await Seed(new Game { Id = 1, Title = "Mario Kart 8 Deluxe", PlatformId = Switch, IgdbId = 7, Status = GameStatus.Released, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            var holders = Rows().Where(g => g.Path == file).ToList();
            Assert.That(holders.Select(g => g.Status), Is.EqualTo(game ? new[] { GameStatus.Downloaded } : Array.Empty<GameStatus>()));
            if (!game) Assert.That((Rows().Count, Row(1).Status), Is.EqualTo((1, GameStatus.Released)));
        }

        [Test]
        public async Task UpdateAndDlcFiles_AddNoGames()
        {
            var sw = Folder("switch");
            Put(sw, "Mario Kart 8 Deluxe - All DLCs.nsp");
            Put(sw, "Mario Kart 8 Deluxe Updates.nsp");
            Put(sw, "Mario Kart 8 Deluxe [0100152000023800][v1376256].nsp");

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Rows(), Is.Empty);
        }

        // A folder of several games: the update beside them is not linked to every one of them
        [Test]
        public async Task SingleFileGamesSharingAFolder_UpdateThereIsNotLinkedToTheOtherGame()
        {
            var folder = Folder("switch", "Nintendo");
            var zelda = Put(folder, "Zelda.nsp");
            var mario = Put(folder, "Mario.nsp");
            Put(folder, "Updates/Zelda Update v2.nsp");
            await Seed(
                new Game { Id = 1, Title = "Zelda", PlatformId = Switch, Path = zelda, ExecutablePath = zelda, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "Mario", PlatformId = Switch, Path = mario, ExecutablePath = mario, Status = GameStatus.Downloaded });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(GameFiles(2), Is.EqualTo(new[] { "Mario.nsp" }));
        }

        // ---- An import stores the game as what it finds ----

        // A game added from a file has it, and doesn't wait for the next scan to count as downloaded
        [TestCase(false)]
        [TestCase(true)]
        public async Task ImportedNewGame_IsDownloadedAtOnce(bool lookupMatches)
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");

            var imported = await Scanner(lookupMatches ? new FakeIgdb("Advance Wars", 7) : new FakeIgdb())
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = rom, ExecutablePath = rom, PlatformKey = "gba" });

            Assert.That(imported, Is.True);
            Assert.That(Rows().Single().Status, Is.EqualTo(GameStatus.Downloaded));
        }

        [Test]
        public async Task ImportedFileOfAWantedGame_FoundByTheLookup_IsDownloadedAtOnce()
        {
            var rom = Put(Folder("gba"), "Pokemon Emerald (Europe).gba");
            await Seed(new Game { Id = 1, Title = "Pokemon Emerald Version", Region = "Europe", PlatformId = Gba, IgdbId = 9, Status = GameStatus.Released, Monitored = true });

            await Scanner(new FakeIgdb("Pokemon Emerald Version", 9, search: "Pokemon Emerald"))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Pokemon Emerald", Path = rom, ExecutablePath = rom, PlatformKey = "gba", Region = "Europe" });

            Assert.That(Row(1).Path, Is.EqualTo(rom));
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(GameFiles(1), Is.EqualTo(new[] { "Pokemon Emerald (Europe).gba" }));
        }

        // ---- After the upgrade: games older versions stored as Released ----

        [Test]
        public async Task ContentPass_PromotesGamesWithTheirFiles_AndFlagsNothing()
        {
            var gba = Folder("gba");
            var rom = Put(gba, "Advance Wars.gba");
            var setupFolder = Folder("windows", "Some Game");
            var setup = Put(setupFolder, "setup_some_game.exe");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Released },
                new Game { Id = 2, Title = "Some Game", PlatformId = Pc, Path = setupFolder, ExecutablePath = setup, Status = GameStatus.InstallerDetected },
                new Game { Id = 3, Title = "Golden Sun", PlatformId = Gba, Path = Gone("gba", "Golden Sun.gba"), Status = GameStatus.Downloaded },
                new Game { Id = 4, Title = "Metroid Fusion", PlatformId = Gba, Path = Gone("gba", "Metroid Fusion.gba"), Status = GameStatus.Released });

            var promoted = await Scanner(new FakeIgdb()).PromotePresentGamesAsync();

            Assert.That(promoted, Is.EqualTo(1));
            Assert.That(Rows().OrderBy(g => g.Id).Select(g => (g.Status, g.MissingSince)), Is.EqualTo(new[]
            {
                (GameStatus.Downloaded, (DateTime?)null), (GameStatus.InstallerDetected, null), (GameStatus.Downloaded, null), (GameStatus.Released, null)
            }));
        }

        [Test]
        public async Task ContentPass_WaitsForARunningScan()
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Released });
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, _) =>
            {
                if (method.Name != nameof(IGameRepository.GetAllLightAsync) || held.IsSet) return;
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            };
            var scanner = Scanner(new FakeIgdb(), repo);
            var running = Task.Run(() => scanner.ScanAsync());
            Assert.That(held.Wait(TimeSpan.FromSeconds(30)), Is.True);

            var promoted = await scanner.PromotePresentGamesAsync();
            var status = Row(1).Status;

            release.Set();
            await running;
            Assert.That(promoted, Is.Null);
            Assert.That(status, Is.EqualTo(GameStatus.Released));
        }

        [Test]
        public async Task ContentPass_CanBeStopped()
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Released });
            using var held = new ManualResetEventSlim();
            using var release = new ManualResetEventSlim();
            var repo = DispatchProxy.Create<IGameRepository, MissingContentTest.HookedRepository>();
            var hook = (MissingContentTest.HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = (method, _) =>
            {
                if (method.Name != nameof(IGameRepository.GetAllLightAsync)) return;
                held.Set();
                release.Wait(TimeSpan.FromSeconds(30));
            };
            var scanner = Scanner(new FakeIgdb(), repo);
            var pass = Task.Run(() => scanner.PromotePresentGamesAsync());
            Assert.That(held.Wait(TimeSpan.FromSeconds(30)), Is.True);

            scanner.StopScan();
            release.Set();

            Assert.That(async () => await pass, Throws.InstanceOf<OperationCanceledException>());
            Assert.That(scanner.IsScanning, Is.False);
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Released));
        }

        // An import while the pass ran kept its check of the library; the pass changed what it found
        [Test]
        public async Task ContentPass_LeavesNoCheckOfTheLibraryBehind()
        {
            var scanner = Scanner(new FakeIgdb());
            var check = typeof(MediaScannerService).GetField("_libraryContent", BindingFlags.NonPublic | BindingFlags.Instance)!;
            check.SetValue(scanner, new Dictionary<int, GameContent> { [1] = GameContent.Gone });

            await scanner.PromotePresentGamesAsync();

            Assert.That(check.GetValue(scanner), Is.Null);
        }

        [Test]
        public async Task ContentPassAfterTheUpgrade_RunsOnce()
        {
            var gba = Folder("gba");
            var rom = Put(gba, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Released });
            var config = new ConfigurationService(_root);

            var first = new ContentStateUpgradeService(Scanner(new FakeIgdb()), config);
            await first.StartAsync(CancellationToken.None);
            await first.ExecuteTask!;
            var later = Put(gba, "Golden Sun.gba");
            await Seed(new Game { Id = 2, Title = "Golden Sun", PlatformId = Gba, Path = later, ExecutablePath = later, Status = GameStatus.Released });
            var second = new ContentStateUpgradeService(Scanner(new FakeIgdb()), config);
            await second.StartAsync(CancellationToken.None);
            await second.ExecuteTask!;

            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Row(2).Status, Is.EqualTo(GameStatus.Released), "the pass ran again");
            Assert.That(File.Exists(Path.Combine(config.GetConfigDirectory(), ContentStateUpgradeService.MarkerName)), Is.True);
        }

        // ---- A metadata match brings the file to a game without one ----

        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataMerge_WantedGameTakesThePathOfItsFile(bool confirmedByUser)
        {
            var folder = Folder("gba", "Advance Wars");
            var rom = Put(folder, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Status = GameStatus.Missing, Monitored = true, MetadataConfirmedByUser = confirmedByUser });

            var imported = await Scanner(new FakeIgdb("Advance Wars", 9))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = folder, ExecutablePath = rom, PlatformKey = "gba" });

            var row = Row(1);
            Assert.That(imported, Is.True);
            Assert.That(row.Path, Is.EqualTo(folder));
            Assert.That(row.ExecutablePath, Is.EqualTo(rom));
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded), "without a path the game is searched for again");
        }

        [Test]
        public async Task MetadataMerge_ExternalGameStaysExternal()
        {
            // A store sync added the game, then its install turned up in an external library
            var install = Directory.CreateDirectory(Path.Combine(_root, "wine", "Hollow Knight")).FullName;
            var exe = Put(install, "hollow_knight.exe");
            await Seed(new Game { Id = 1, Title = "Hollow Knight", PlatformId = Pc, IsExternal = true, Status = GameStatus.Released, Monitored = true });

            await Scanner(new FakeIgdb("Hollow Knight", 9))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Hollow Knight", Path = install, ExecutablePath = exe, PlatformKey = "pc_windows", IsExternal = true });

            var row = Row(1);
            Assert.That(row.IsExternal, Is.True, "monitored games that aren't external get downloaded");
            Assert.That(row.Path, Is.EqualTo(install));
            Assert.That(row.ExecutablePath, Is.EqualTo(exe));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataMerge_GameWhosePathIsGone_TakesThePathOfItsFile(bool confirmedByUser)
        {
            var folder = Folder("gba", "Advance Wars");
            var rom = Put(folder, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = Gone("gba", "Old Folder"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow, Monitored = true, MetadataConfirmedByUser = confirmedByUser });

            await Scanner(new FakeIgdb("Advance Wars", 9))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = folder, ExecutablePath = rom, PlatformKey = "gba" });

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(folder));
            Assert.That(row.ExecutablePath, Is.EqualTo(rom));
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded));
        }

        // The copy stays in the discovery list: nothing took it
        [TestCase(null)]
        [TestCase(9)]
        public async Task MetadataMerge_FolderGameWithItsContent_KeepsItsFolder(int? igdbId)
        {
            // A copy of a console folder game; neither folder has an executable
            var souls = Folder("ps3", "Demons Souls");
            Put(souls, "PS3_GAME/USRDIR/EBOOT.BIN");
            var copy = Folder("ps3", "Demons Souls (Copy)");
            Put(copy, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = igdbId, Path = souls, Status = GameStatus.Downloaded, Monitored = true });

            var imported = await Scanner(new FakeIgdb("Demons Souls", 9))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Demons Souls", Path = copy, PlatformKey = "ps3" });

            Assert.That(Row(1).Path, Is.EqualTo(souls));
            Assert.That(imported, Is.False);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task MetadataMerge_ExternalGameWithItsInstall_KeepsItAndStaysExternal(bool confirmedByUser)
        {
            // A copy of an installed store game turns up in the library
            var install = Directory.CreateDirectory(Path.Combine(_root, "wine", "Hollow Knight")).FullName;
            var exe = Put(install, "hollow_knight.exe");
            var copy = Folder("windows", "Hollow Knight");
            var copyExe = Put(copy, "hollow_knight.exe");
            await Seed(new Game { Id = 1, Title = "Hollow Knight", PlatformId = Pc, IsExternal = true, Path = install, ExecutablePath = exe, Status = GameStatus.Downloaded, Monitored = true, MetadataConfirmedByUser = confirmedByUser });

            await Scanner(new FakeIgdb("Hollow Knight", 9))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Hollow Knight", Path = copy, ExecutablePath = copyExe, PlatformKey = "pc_windows" });

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(install));
            Assert.That(row.ExecutablePath, Is.EqualTo(exe));
            Assert.That(row.IsExternal, Is.True, "monitored games that aren't external get downloaded");
        }

        [TestCase("default")]
        [TestCase(null)]
        public async Task MetadataMerge_FolderNamingNoPlatform_KeepsTheGamesPlatform(string? platformKey)
        {
            var folder = Directory.CreateDirectory(Path.Combine(_root, "incoming", "Advance Wars")).FullName;
            var rom = Put(folder, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Status = GameStatus.Missing, Monitored = true });

            await Scanner(new FakeIgdb("Advance Wars", 9))
                .ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = folder, ExecutablePath = rom, PlatformKey = platformKey });

            var row = Row(1);
            Assert.That(row.IgdbId, Is.EqualTo(9));
            Assert.That(row.PlatformId, Is.EqualTo(Gba));
            Assert.That(row.NeedsMetadataReview, Is.False);
        }

        // Added from a search, then its files were copied into the library by hand
        [TestCase(false)]
        [TestCase(true)]
        public async Task GameWithoutAPath_TakesItsFilesFoundInTheLibrary(bool folderGame)
        {
            var found = folderGame ? Folder("ps3", "Demons Souls") : Put(Folder("gba"), "Advance Wars.gba");
            if (folderGame) Put(found, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = folderGame ? "Demons Souls" : "Advance Wars", PlatformId = folderGame ? Ps3 : Gba, IgdbId = 7, Status = GameStatus.Released, Monitored = true });

            await Scanner(new FakeIgdb()).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(found));
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded), "a monitored game without its files is downloaded again");
            Assert.That(Rows(), Has.Count.EqualTo(1));
        }
    }
}
