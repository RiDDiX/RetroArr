using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Games;
using RetroArr.Core.Cache;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class MissingContentTest
    {
        private const int Pc = 1;
        private const int MacOs = 2;
        private const int Ps1 = 20;
        private const int Ps3 = 22;
        private const int Snes = 41;
        private const int Switch = 46;
        private const int Gba = 52;
        private const int Dreamcast = 67;

        private static readonly DateTime Old = new(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        private static readonly DateTime At = new(2026, 9, 1, 0, 0, 0, DateTimeKind.Utc);

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // Every seeded game already exists, so no candidate ever needs metadata.
        private sealed class NoMetadata : IGameMetadataServiceFactory
        {
            public GameMetadataService CreateService() => null!;
            public void RefreshConfiguration() { }
        }

        private string _root = null!;
        private string _lib = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_missing_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // Runs an action before each repository call
        public class HookedRepository : DispatchProxy
        {
            public IGameRepository Inner { get; set; } = null!;
            public Action<MethodInfo, object?[]?> Before { get; set; } = (_, _) => { };

            protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
            {
                Before(targetMethod!, args);
                return targetMethod!.Invoke(Inner, args);
            }
        }

        private IGameRepository Hooked(Action<MethodInfo, object?[]?> before)
        {
            var repo = DispatchProxy.Create<IGameRepository, HookedRepository>();
            var hook = (HookedRepository)(object)repo;
            hook.Inner = new SqliteGameRepository(new DbFactory(_db));
            hook.Before = before;
            return repo;
        }

        // Records what is invalidated, and serves Stale as the cached copy of every game
        public sealed class FakeCache : ICacheService
        {
            public List<string> Removed { get; } = new();
            public Game? Stale { get; set; }
            public bool IsEnabled => true;
            public Task<T?> GetAsync<T>(string key) where T : class => Task.FromResult(Stale as T);
            public Task SetAsync<T>(string key, T value, TimeSpan? ttl = null) where T : class => Task.CompletedTask;
            public Task<T> GetOrSetAsync<T>(string key, Func<Task<T>> factory, TimeSpan? ttl = null) where T : class => factory();
            public Task RemoveAsync(string key) { Removed.Add(key); return Task.CompletedTask; }
            public Task RemoveByPrefixAsync(string prefix) { Removed.Add(prefix); return Task.CompletedTask; }
            public Task FlushAsync() => Task.CompletedTask;
        }

        private MediaScannerService Scanner(int retention = 14, IGameRepository? repo = null)
        {
            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib, MissingRetentionDays = retention });
            return new MediaScannerService(config, new NoMetadata(), repo ?? new SqliteGameRepository(new DbFactory(_db)), new TitleCleanerService());
        }

        private string Folder(params string[] parts) => Directory.CreateDirectory(Path.Combine(new[] { _lib }.Concat(parts).ToArray())).FullName;

        private static string Put(string folder, string name, string text = "data")
        {
            var path = Path.Combine(folder, name);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, text);
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

        private GameContent Check(Game game) => MediaScannerService.CheckContent(game, new[] { _lib });

        private static Game G(int platformId, string path, string? exe = null) =>
            new() { PlatformId = platformId, Path = path, ExecutablePath = exe };

        // Downloaded games in folder: the first ones hold their ROM, the rest have no folder any more
        private static List<Game> Games(string folder, int present, int gone, int firstId = 1) =>
            Enumerable.Range(firstId, present + gone).Select(id =>
            {
                var path = Path.Combine(folder, $"Game {id}");
                if (id < firstId + present) Put(Directory.CreateDirectory(path).FullName, $"Game {id}.gba");
                return new Game { Id = id, Title = $"Game {id}", PlatformId = Gba, Path = path, Status = GameStatus.Downloaded, Monitored = true };
            }).ToList();

        // ---- Game.ApplyContent ----

        [TestCase(GameStatus.Missing, false, GameContent.Present, GameStatus.Downloaded, "null", true, TestName = "Wanted_Present_Downloaded")]
        [TestCase(GameStatus.Missing, false, GameContent.Empty, GameStatus.Missing, "null", false, TestName = "Wanted_Empty_Unchanged")]
        [TestCase(GameStatus.Missing, false, GameContent.Gone, GameStatus.Missing, "null", false, TestName = "Wanted_Gone_Unchanged")]
        [TestCase(GameStatus.Released, false, GameContent.Present, GameStatus.Downloaded, "null", true, TestName = "Released_Present_Downloaded")]
        [TestCase(GameStatus.TBA, false, GameContent.Present, GameStatus.Downloaded, "null", true, TestName = "Tba_Present_Downloaded")]
        [TestCase(GameStatus.Announced, false, GameContent.Present, GameStatus.Downloaded, "null", true, TestName = "Announced_Present_Downloaded")]
        [TestCase(GameStatus.Released, false, GameContent.Gone, GameStatus.Missing, "at", true, TestName = "Released_Gone_Flagged")]
        [TestCase(GameStatus.Announced, false, GameContent.Gone, GameStatus.Missing, "at", true, TestName = "Announced_Gone_Flagged")]
        [TestCase(GameStatus.Released, false, GameContent.Empty, GameStatus.Released, "null", false, TestName = "Released_Empty_Unchanged")]
        [TestCase(GameStatus.Downloaded, false, GameContent.Empty, GameStatus.Missing, "at", true, TestName = "Downloaded_Empty_Flagged")]
        [TestCase(GameStatus.Downloaded, false, GameContent.Gone, GameStatus.Missing, "at", true, TestName = "Downloaded_Gone_Flagged")]
        [TestCase(GameStatus.InstallerDetected, false, GameContent.Empty, GameStatus.Missing, "at", true, TestName = "Installer_Empty_Flagged")]
        [TestCase(GameStatus.InstallerDetected, false, GameContent.Gone, GameStatus.Missing, "at", true, TestName = "Installer_Gone_Flagged")]
        [TestCase(GameStatus.InstallerDetected, false, GameContent.Present, GameStatus.InstallerDetected, "null", false, TestName = "Installer_Present_Kept")]
        [TestCase(GameStatus.Downloaded, false, GameContent.Present, GameStatus.Downloaded, "null", false, TestName = "Downloaded_Present_Unchanged")]
        [TestCase(GameStatus.Missing, true, GameContent.Present, GameStatus.Downloaded, "null", true, TestName = "Flagged_Present_Cleared")]
        [TestCase(GameStatus.Missing, true, GameContent.Empty, GameStatus.Missing, "old", false, TestName = "Flagged_Empty_KeepsItsDate")]
        [TestCase(GameStatus.Missing, true, GameContent.Gone, GameStatus.Missing, "old", false, TestName = "Flagged_Gone_KeepsItsDate")]
        [TestCase(GameStatus.Downloaded, false, GameContent.Unknown, GameStatus.Downloaded, "null", false, TestName = "Downloaded_Unknown_Unchanged")]
        [TestCase(GameStatus.Missing, true, GameContent.Unknown, GameStatus.Missing, "old", false, TestName = "Flagged_Unknown_Unchanged")]
        [TestCase(GameStatus.Downloaded, true, GameContent.Empty, GameStatus.Missing, "at", true, TestName = "DownloadedWithAnOldFlag_Empty_FlaggedAgain")]
        [TestCase(GameStatus.Downloaded, true, GameContent.Gone, GameStatus.Missing, "at", true, TestName = "DownloadedWithAnOldFlag_Gone_FlaggedAgain")]
        public void ApplyContent_Transitions(GameStatus status, bool flagged, GameContent content, GameStatus expected, string since, bool changed)
        {
            var game = new Game { Status = status, MissingSince = flagged ? Old : null };

            Assert.That(game.ApplyContent(content, At), Is.EqualTo(changed));
            Assert.That(game.Status, Is.EqualTo(expected));
            Assert.That(game.MissingSince, Is.EqualTo(since switch { "old" => Old, "at" => At, _ => (DateTime?)null }));
        }

        // ---- CheckContent ----

        [Test]
        public void CheckContent_RomFolder()
        {
            var game = Folder("gba", "Advance Wars");
            Assert.That(Check(G(Gba, game)), Is.EqualTo(GameContent.Empty), "empty folder");
            Put(game, "Advance Wars.nfo");
            Assert.That(Check(G(Gba, game)), Is.EqualTo(GameContent.Empty), ".nfo only");
            Put(game, "Advance Wars.srm");
            Put(game, "Advance Wars.sav");
            Assert.That(Check(G(Gba, game)), Is.EqualTo(GameContent.Empty), "saves only");
            Put(game, "Advance Wars.gba");
            Assert.That(Check(G(Gba, game)), Is.EqualTo(GameContent.Present), "ROM");
        }

        [Test]
        public void CheckContent_ExecutablePath()
        {
            var game = Folder("gba", "Golden Sun");
            var rar = Put(game, "Golden Sun.rar");
            Assert.That(Check(G(Gba, game, rar)), Is.EqualTo(GameContent.Present));
            Assert.That(Check(G(Gba, game)), Is.EqualTo(GameContent.Present), "an archive counts on a file-mode platform");

            File.Delete(rar);
            var nfo = Put(game, "Golden Sun.nfo");
            Assert.That(Check(G(Gba, game, nfo)), Is.EqualTo(GameContent.Empty), "a document as ExecutablePath proves nothing");
            Put(game, "sub/Golden Sun.gba");
            Assert.That(Check(G(Gba, game, nfo)), Is.EqualTo(GameContent.Present), "the walk still finds the ROM");
        }

        [Test]
        public void CheckContent_DescriptorsNeedTheirData()
        {
            var psx = Folder("psx");
            var cue = Put(psx, "Loose.cue", "FILE \"Loose (Track 1).bin\" BINARY\n  TRACK 01 MODE2/2352\n");
            Assert.That(Check(G(Ps1, cue, cue)), Is.EqualTo(GameContent.Empty), "loose cue, bins deleted");
            Put(psx, "Loose (Track 1).bin");
            Assert.That(Check(G(Ps1, cue, cue)), Is.EqualTo(GameContent.Present), "loose cue with its bin");

            var imported = Folder("psx", "Imported");
            var m3u = Put(imported, "Imported.m3u", "Imported (Disc 1).chd\nImported (Disc 2).chd\n");
            Put(imported, "Imported.nfo");
            Assert.That(Check(G(Ps1, imported, m3u)), Is.EqualTo(GameContent.Empty), "m3u left, every disc deleted");
            Put(imported, "Imported (Disc 2).chd");
            Assert.That(Check(G(Ps1, imported, m3u)), Is.EqualTo(GameContent.Present), "disc 1 deleted, disc 2 left");

            var chain = Folder("psx", "Chain");
            var list = Put(chain, "Chain.m3u", "Chain (Disc 1).cue\n");
            Put(chain, "Chain (Disc 1).cue", "FILE \"Chain (Disc 1).bin\" BINARY\n");
            Assert.That(Check(G(Ps1, list, list)), Is.EqualTo(GameContent.Empty), "m3u -> cue, bins deleted");

            var saved = Put(psx, "Saved.cue", "FILE \"Saved (Track 1).bin\" BINARY\n");
            Put(psx, "Saved.srm");
            Put(psx, "Saved Again.bin");
            Assert.That(Check(G(Ps1, saved, saved)), Is.EqualTo(GameContent.Empty), "a save, and another game's ROM, are not its tracks");

            var cloneCd = Folder("psx", "CloneCD");
            Put(cloneCd, "CloneCD.ccd");
            Put(cloneCd, "CloneCD.sub");
            Assert.That(Check(G(Ps1, cloneCd)), Is.EqualTo(GameContent.Empty), "ccd and subchannel data, image deleted");
            Put(cloneCd, "CloneCD.img");
            Assert.That(Check(G(Ps1, cloneCd)), Is.EqualTo(GameContent.Present));
        }

        [Test]
        public void CheckContent_TracksTheResolverMisses_StillCount()
        {
            var dc = Folder("dreamcast");
            Put(dc, "Crazy Taxi (USA) (Track 1).bin");
            Put(dc, "Crazy Taxi (USA) (Track 2).raw");
            var gdi = Put(dc, "Crazy Taxi (USA).gdi",
                "2\n1 0 4 2352 \"Crazy Taxi (USA) (Track 1).bin\" 0\n2 756 0 2352 \"Crazy Taxi (USA) (Track 2).raw\" 0\n");
            Assert.That(Check(G(Dreamcast, gdi, gdi)), Is.EqualTo(GameContent.Present), "quoted gdi track names");

            var psx = Folder("psx");
            Put(psx, "Upper (USA) (Track 1).bin");
            var cue = Put(psx, "Upper (USA).cue", "FILE \"UPPER (USA) (TRACK 1).BIN\" BINARY\n");
            Assert.That(Check(G(Ps1, cue, cue)), Is.EqualTo(GameContent.Present), "cue names in another case");
        }

        [Test]
        public void CheckContent_SoftwareFolders()
        {
            var exe = Folder("windows", "Unreal Game");
            Put(exe, "Binaries/Win64/Game.exe");
            Assert.That(Check(G(Pc, exe)), Is.EqualTo(GameContent.Present), "exe below Binaries");

            // Platform is null on every game here, so this also shows the rule comes from PlatformId
            var dlls = Folder("windows", "Dlls Only");
            Put(dlls, "steam_api64.dll");
            Put(dlls, "bin/engine.dll");
            Assert.That(Check(G(Pc, dlls)), Is.EqualTo(GameContent.Unknown), "DLLs only");
            Assert.That(Check(G(Pc, Folder("windows", "Emptied"))), Is.EqualTo(GameContent.Unknown), "empty software folder");

            var pak = Folder("windows", "Pak Game");
            Put(pak, "data/game.pak");
            Assert.That(Check(G(Pc, pak)), Is.EqualTo(GameContent.Present), ".pak");

            var elf = Folder("windows", "Elf Game");
            Put(elf, "game");
            Assert.That(Check(G(Pc, elf)), Is.EqualTo(GameContent.Present), "extensionless");

            var bundle = Folder("macintosh", "Bundle Game");
            Put(bundle, "Bundle Game.app/Contents/Resources/game.dat");
            Assert.That(Check(G(MacOs, bundle)), Is.EqualTo(GameContent.Present), ".app bundle");
        }

        [Test]
        public void CheckContent_FolderModeConsoles()
        {
            var ps3 = Folder("ps3", "Demon's Souls");
            Put(ps3, "PS3_GAME/PARAM.SFO");
            Assert.That(Check(G(Ps3, ps3)), Is.EqualTo(GameContent.Present), "folder-mode consoles take any non-blacklisted file");

            var eboot = Folder("ps3", "Eboot Only");
            Put(eboot, "PS3_GAME/USRDIR/EBOOT.BIN");
            Assert.That(Check(G(Ps3, eboot)), Is.EqualTo(GameContent.Present));
            Assert.That(Check(G(Ps3, Folder("ps3", "Emptied"))), Is.EqualTo(GameContent.Empty));
        }

        [Test]
        public void CheckContent_SwitchUpdatesAndDlcAlone_AreEmpty()
        {
            var game = Folder("switch", "Zelda");
            var update = Put(game, "Patches/Zelda v1.2.nsp");
            Put(game, "DLC/Zelda Expansion.nsp");
            Assert.That(Check(G(Switch, game)), Is.EqualTo(GameContent.Empty));
            Assert.That(Check(G(Switch, game, update)), Is.EqualTo(GameContent.Empty), "an update as ExecutablePath is not the base game");

            Put(game, "Zelda.nsp");
            Assert.That(Check(G(Switch, game)), Is.EqualTo(GameContent.Present));
        }

        [Test]
        public void CheckContent_NasAndDotEntries_AreIgnored()
        {
            var game = Folder("gba", "Metroid");
            Put(game, "._Metroid.gba");
            Put(game, ".hidden/Metroid.gba");
            Put(game, "@eaDir/Metroid.gba@SynoEAStream");
            Put(game, "@eaDir/Metroid.gba/SYNOINDEX_MEDIA_INFO");
            Put(game, "#recycle/Metroid.gba");

            Assert.That(Check(G(Gba, game)), Is.EqualTo(GameContent.Empty));
        }

        [Test]
        public void CheckContent_SharedFolders_OnlyJudgedByExecutablePath()
        {
            var gba = Folder("gba");
            var rom = Put(gba, "Mario Kart.gba");
            Put(gba, "Other.gba");

            Assert.That(Check(G(Gba, _lib)), Is.EqualTo(GameContent.Unknown), "the library root");
            Assert.That(Check(G(Gba, gba)), Is.EqualTo(GameContent.Unknown), "a platform folder without ExecutablePath");
            Assert.That(Check(G(Gba, gba, rom)), Is.EqualTo(GameContent.Present));
            Assert.That(Check(G(Gba, gba, Put(Folder("snes"), "Mario.sfc"))), Is.EqualTo(GameContent.Unknown), "ExecutablePath in another folder");
            File.Delete(rom);
            Assert.That(Check(G(Gba, gba, rom)), Is.EqualTo(GameContent.Empty), "its ROM deleted, others left");

            var emptied = Folder("gbc");
            Assert.That(Check(G(Gba, emptied, Path.Combine(emptied, "Pokemon.gbc"))), Is.EqualTo(GameContent.Unknown), "an empty platform folder, as after an outage");
        }

        [Test, Platform(Exclude = "Win")]
        public void CheckContent_DanglingSymlink_IsUnknown()
        {
            var gba = Folder("gba");
            Put(gba, "Other.gba");
            var link = Path.Combine(gba, "On The Nas");
            Directory.CreateSymbolicLink(link, Path.Combine(_root, "nas", "On The Nas"));

            Assert.That(Check(G(Gba, link)), Is.EqualTo(GameContent.Unknown));
        }

        [Test]
        public void CheckContent_GoneOrUnknown()
        {
            var nas = Path.Combine(_root, "nas");
            Assert.That(MediaScannerService.CheckContent(G(Gba, Path.Combine(nas, "gba", "Game")), new[] { nas }),
                Is.EqualTo(GameContent.Unknown), "root missing");

            var emptyRoot = Directory.CreateDirectory(Path.Combine(_root, "empty")).FullName;
            Assert.That(MediaScannerService.CheckContent(G(Gba, Path.Combine(emptyRoot, "gba", "Game")), new[] { emptyRoot }),
                Is.EqualTo(GameContent.Unknown), "root empty");
            Put(emptyRoot, ".DS_Store");
            Put(emptyRoot, "@eaDir/SYNOINDEX_MEDIA_INFO");
            Assert.That(MediaScannerService.CheckContent(G(Gba, Path.Combine(emptyRoot, "gba", "Game")), new[] { emptyRoot }),
                Is.EqualTo(GameContent.Unknown), "root with only NAS bookkeeping");

            Folder("gba");
            Assert.That(Check(G(Gba, Path.Combine(_lib, "gba", "Only Game"))), Is.EqualTo(GameContent.Unknown), "platform folder left empty");

            Put(Folder("snes"), "Mario.sfc");
            Assert.That(Check(G(Gba, Path.Combine(_lib, "gbc", "Game"))), Is.EqualTo(GameContent.Gone), "platform folder removed");

            Put(Folder("gba", "Other"), "Other.gba");
            Assert.That(Check(G(Gba, Path.Combine(_lib, "gba", "Removed"))), Is.EqualTo(GameContent.Gone), "game folder removed");

            var outside = Directory.CreateDirectory(Path.Combine(_root, "outside")).FullName;
            Put(outside, "keep.txt");
            Assert.That(Check(G(Gba, Path.Combine(outside, "gone", "Game"))), Is.EqualTo(GameContent.Gone), "outside every root");
        }

        [Test]
        public void LibraryRoots_TrimsAndDropsUnusablePaths()
        {
            Assert.That(MediaScannerService.LibraryRoots(new MediaSettings { FolderPath = _lib + Path.DirectorySeparatorChar, DestinationPath = _lib }),
                Is.EqualTo(new[] { _lib }));
            Assert.That(MediaScannerService.LibraryRoots(new MediaSettings { FolderPath = Path.Combine("relative", "games"), DestinationPath = " " }),
                Is.Empty);
        }

        // ---- Mass-loss breaker ----

        [TestCase(0, 4, 4, TestName = "CheckAll_FourGone_Flagged")]
        [TestCase(0, 5, 0, TestName = "CheckAll_FiveGone_Held")]
        [TestCase(5, 5, 5, TestName = "CheckAll_HalfGone_Flagged")]
        [TestCase(4, 6, 0, TestName = "CheckAll_MostGone_Held")]
        public void CheckAll_Breaker(int present, int gone, int flagged)
        {
            var gba = Folder("gba");
            Put(gba, "Kept/Kept.gba");
            var held = new List<string>();

            var results = MediaScannerService.CheckAll(Games(gba, present, gone), new[] { _lib }, held);

            Assert.That(results.Count(r => r.Content == GameContent.Gone), Is.EqualTo(flagged));
            Assert.That(held, flagged == gone ? Is.Empty : Is.EqualTo(new[] { $"{_lib}: {gone} of {present + gone}" }));
        }

        [Test]
        public void CheckAll_OldFlags_DoNotTripTheBreaker()
        {
            var gba = Folder("gba");
            Put(gba, "Kept/Kept.gba");
            var games = Enumerable.Range(1, 10)
                .Select(i => new Game { Id = i, PlatformId = Gba, Path = Path.Combine(gba, $"Old {i}"), Status = GameStatus.Missing, MissingSince = Old })
                .Append(new Game { Id = 11, PlatformId = Gba, Path = Path.Combine(gba, "Deleted"), Status = GameStatus.Downloaded })
                .ToList();

            var results = MediaScannerService.CheckAll(games, new[] { _lib }, new List<string>());

            Assert.That(results.Single(r => r.Game.Id == 11).Content, Is.EqualTo(GameContent.Gone));
        }

        [Test]
        public void CheckAll_EmptiedFolders_DoNotTripTheBreaker()
        {
            // Most ROMs of a platform deleted by hand, their folders kept
            var games = Games(Folder("gba"), 2, 0);
            games.AddRange(Enumerable.Range(3, 6).Select(i => new Game { Id = i, PlatformId = Gba, Path = Folder("gba", $"Emptied {i}"), Status = GameStatus.Downloaded }));

            var results = MediaScannerService.CheckAll(games, new[] { _lib }, new List<string>());

            Assert.That(results.Count(r => r.Content == GameContent.Empty), Is.EqualTo(6));
        }

        [Test]
        public void CheckAll_OneRootDown_HeldAcrossItsPlatforms()
        {
            // The NAS root is down, and an import recreated one game folder on its bare mount point
            var nas = Directory.CreateDirectory(Path.Combine(_root, "nas")).FullName;
            Directory.CreateDirectory(Path.Combine(nas, "gba", "Freshly Added"));
            var games = Games(Folder("snes"), 14, 0, 100);
            games.AddRange(Enumerable.Range(1, 6).Select(i =>
                new Game { Id = i, PlatformId = Gba, Path = Path.Combine(nas, i <= 3 ? "gba" : "snes", $"Game {i}"), Status = GameStatus.Downloaded }));
            var held = new List<string>();

            var results = MediaScannerService.CheckAll(games, new[] { _lib, nas }, held);

            Assert.That(results.Where(r => r.Game.Id <= 6).Select(r => r.Content), Is.All.EqualTo(GameContent.Unknown));
            Assert.That(held, Is.EqualTo(new[] { $"{nas}: 6 of 6" }));
        }

        [Test]
        public void CheckAll_PlatformFolderMountedOnItsOwn_IsHeld()
        {
            // ps3 is a share of its own and down; an import recreated one game folder on its mount point
            var games = Games(Folder("snes"), 30, 0, 100);
            var fresh = Folder("ps3", "Fresh");
            Put(fresh, "PS3_GAME/USRDIR/EBOOT.BIN");
            games.Add(new Game { Id = 1, PlatformId = Ps3, Path = fresh, Status = GameStatus.Downloaded });
            games.AddRange(Enumerable.Range(2, 12).Select(i =>
                new Game { Id = i, PlatformId = Ps3, Path = Path.Combine(_lib, "ps3", $"Game {i}"), Status = GameStatus.Downloaded }));
            var held = new List<string>();

            var results = MediaScannerService.CheckAll(games, new[] { _lib }, held);

            Assert.That(results.Where(r => r.Game.PlatformId == Ps3 && r.Game.Id > 1).Select(r => r.Content), Is.All.EqualTo(GameContent.Unknown));
            Assert.That(held, Is.EqualTo(new[] { $"{Path.Combine(_lib, "ps3")}: 12 of 13" }));
        }

        // ---- Scanner ----

        [Test]
        public async Task ImportedFolder_RomDeleted_BecomesMissing()
        {
            var game = Folder("gba", "Advance Wars");
            Put(game, "Advance Wars.nfo");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game, ExecutablePath = Path.Combine(game, "Advance Wars.gba"), Status = GameStatus.Downloaded, Monitored = true });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.GameFiles.Add(new GameFile { GameId = 1, RelativePath = "Advance Wars.gba", Size = 4, FileType = "Main" });
                await ctx.SaveChangesAsync();
            }

            await Scanner().ScanAsync();

            var row = Row(1);
            Assert.That(row.Status, Is.EqualTo(GameStatus.Missing));
            Assert.That(row.MissingSince, Is.Not.Null);
            using var check = new RetroArrDbContext(_db);
            Assert.That(check.GameFiles.Where(f => f.GameId == 1).Select(f => f.RelativePath), Has.None.EqualTo("Advance Wars.gba"));
        }

        [Test]
        public async Task OnlyGameOfPlatform_RomDeletedFolderKept_IsFlagged()
        {
            var game = Folder("gba", "Golden Sun");
            await Seed(new Game { Id = 1, Title = "Golden Sun", PlatformId = Gba, Path = game, Status = GameStatus.Downloaded });

            await Scanner().ScanAsync();

            Assert.That(Row(1).MissingSince, Is.Not.Null);
        }

        [Test]
        public async Task EmptiedFolder_Downloaded_BecomesMissing()
        {
            var game = Folder("gba", "Advance Wars");
            Put(Folder("gba", "Other"), "Other.gba");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game, Status = GameStatus.Downloaded, Monitored = true },
                new Game { Id = 2, Title = "Other", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Other"), Status = GameStatus.Downloaded });

            await Scanner().ScanAsync();

            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Missing));
            Assert.That(Row(1).MissingSince, Is.Not.Null);
            Assert.That(Row(2).MissingSince, Is.Null);
        }

        [Test]
        public async Task SettingsStyleScan_AlsoFlags()
        {
            var game = Folder("gba", "Advance Wars");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner().ScanAsync(_lib, "default");

            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Missing));
        }

        [Test]
        public async Task Restored_BecomesDownloaded()
        {
            var gba = Folder("gba");
            Put(gba, "gamelist.xml");
            var rom = Path.Combine(gba, "Mario Kart.gba");
            await Seed(new Game { Id = 1, Title = "Mario Kart", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner().ScanAsync();
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Missing));

            File.WriteAllText(rom, "rom");
            await Scanner().ScanAsync();

            var row = Row(1);
            Assert.That(row.Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(row.MissingSince, Is.Null);
        }

        [Test]
        public async Task ScannedReleasedWithContent_IsPromoted()
        {
            var game = Folder("gba", "Advance Wars");
            Put(game, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game, Status = GameStatus.Released, Monitored = true });

            await Scanner().ScanAsync();

            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
        }

        [Test]
        public async Task InstallerDetectedWithContent_Stays()
        {
            var game = Folder("windows", "Some Game");
            var setup = Put(game, "setup_some_game.exe");
            await Seed(new Game { Id = 1, Title = "Some Game", PlatformId = Pc, Path = game, ExecutablePath = setup, Status = GameStatus.InstallerDetected });

            await Scanner().ScanAsync();

            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.InstallerDetected));
        }

        [Test]
        public async Task WantedGame_EmptyOrRemovedFolder_NeverFlaggedNorPurged()
        {
            var gba = Folder("gba");
            Put(gba, "gamelist.xml");
            var wanted = Folder("gba", "Wanted Game");
            var ps3 = Folder("ps3", "Some Folder");
            await Seed(
                new Game { Id = 1, Title = "Wanted Game", PlatformId = Gba, Path = wanted, Status = GameStatus.Missing, Monitored = true },
                new Game { Id = 2, Title = "Wanted PS3 Game", PlatformId = Ps3, Path = ps3, Status = GameStatus.Missing, Monitored = true });

            await Scanner().ScanAsync();
            Directory.Delete(wanted);
            await Scanner().ScanAsync();
            using (var ctx = new RetroArrDbContext(_db))
            {
                // A flag set by either scan is pushed past the retention
                foreach (var g in ctx.Games.Where(g => g.MissingSince != null)) g.MissingSince = DateTime.UtcNow.AddDays(-30);
                await ctx.SaveChangesAsync();
            }
            await Scanner().ScanAsync();

            foreach (var id in new[] { 1, 2 })
            {
                var row = Row(id);
                Assert.That(row.Status, Is.EqualTo(GameStatus.Missing), $"game {id}");
                Assert.That(row.MissingSince, Is.Null, $"game {id}");
            }
        }

        [TestCase(false, GameStatus.Missing)]
        [TestCase(true, GameStatus.Downloaded)]
        public async Task WantedGame_FoundInAnotherFolder_OnlyContentMakesItDownloaded(bool content, GameStatus expected)
        {
            var found = Folder("ps3", "Demons Souls");
            if (content) Put(found, "PS3_GAME/USRDIR/EBOOT.BIN");
            await Seed(new Game { Id = 1, Title = "Demons Souls", PlatformId = Ps3, IgdbId = 7, Path = Path.Combine(_lib, "ps3", "Old Folder"), Status = GameStatus.Missing, Monitored = true });

            await Scanner().ImportDiscoveredAsync(new DiscoveredGame { Title = "Demons Souls", Path = found, PlatformKey = "ps3" });

            var row = Row(1);
            Assert.That(row.Path, Is.EqualTo(found));
            Assert.That(row.Status, Is.EqualTo(expected));
            Assert.That(row.MissingSince, Is.Null);
        }

        [Test]
        public async Task Cleanup_ImportLandingDuringThePass_IsNotFlagged()
        {
            var wanted = Folder("gba", "Advance Wars");
            var importing = Folder("gba", "Golden Sun");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = wanted, Status = GameStatus.Missing, Monitored = true },
                new Game { Id = 2, Title = "Golden Sun", PlatformId = Gba, Path = importing, Status = GameStatus.Downloaded, Monitored = true });
            // Golden Sun's import lands after the pass has checked every game, before it gets to Golden Sun
            var repo = Hooked((method, args) =>
            {
                if (method.Name == nameof(IGameRepository.ApplyContentStateAsync) && (int)args![0]! == 1)
                    File.WriteAllText(Path.Combine(importing, "Golden Sun.gba"), "rom");
            });

            await Scanner(repo: repo).ScanAsync(_lib, "default");

            Assert.That(Row(2).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Row(2).MissingSince, Is.Null);
        }

        [Test]
        public async Task Cleanup_GameMovedDuringTheScan_IsNotFlagged()
        {
            var gba = Folder("gba");
            var other = Put(gba, "Other.gba");
            var loose = Path.Combine(gba, "Advance Wars (USA).gba");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = loose, Status = GameStatus.Missing, MissingSince = Old, Monitored = true },
                new Game { Id = 2, Title = "Other", PlatformId = Gba, Path = other, ExecutablePath = other, Status = GameStatus.Downloaded });
            var imported = Path.Combine(gba, "Advance Wars");
            // The import of its new download lands in a folder of its own while the scan runs
            var repo = Hooked((method, args) =>
            {
                if (method.Name != nameof(IGameRepository.ApplyContentStateAsync) || (int)args![0]! != 1 || Directory.Exists(imported)) return;
                var rom = Put(Directory.CreateDirectory(imported).FullName, "Advance Wars.gba");
                using var ctx = new RetroArrDbContext(_db);
                var row = ctx.Games.Single(g => g.Id == 1);
                row.Path = imported;
                row.ExecutablePath = rom;
                row.Status = GameStatus.Downloaded;
                row.MissingSince = null;
                ctx.SaveChanges();
            });

            await Scanner(repo: repo).ScanAsync(_lib, "default");

            Assert.That(Directory.Exists(imported), Is.True, "the cleanup never got to the game");
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Row(1).MissingSince, Is.Null);
        }

        [Test]
        public async Task MountOutage_SmallPlatformRefilled_NothingFlagged()
        {
            // The share is down, and a game landed in gba on its bare mount point since
            var fresh = Put(Folder("gba"), "Fresh.gba");
            var games = new List<Game> { new() { Id = 1, Title = "Fresh", PlatformId = Gba, Path = fresh, ExecutablePath = fresh, Status = GameStatus.Downloaded, Monitored = true } };
            games.AddRange(Enumerable.Range(2, 3).Select(i =>
                new Game { Id = i, Title = $"Gba {i}", PlatformId = Gba, Path = Path.Combine(_lib, "gba", $"Gba {i}"), Status = GameStatus.Downloaded, Monitored = true }));
            games.AddRange(Enumerable.Range(10, 20).Select(i =>
                new Game { Id = i, Title = $"Snes {i}", PlatformId = Snes, Path = Path.Combine(_lib, "snes", $"Snes {i}"), Status = GameStatus.Downloaded, Monitored = true }));
            await Seed(games.ToArray());

            await Scanner().ScanAsync(_lib, "default");

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.Games.Count(g => g.MissingSince != null), Is.EqualTo(0));
        }

        [Test]
        public async Task Discovered_FlaggedGameAtItsOwnPath_IsCleared()
        {
            var rom = Put(Folder("gba"), "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = rom, ExecutablePath = rom, Status = GameStatus.Missing, MissingSince = Old, Monitored = true });

            // The region backfill writes the scan's copy of the game back
            await Scanner().ImportDiscoveredAsync(new DiscoveredGame { Title = "Advance Wars", Path = rom, PlatformKey = "gba", Region = "Europe" });

            Assert.That(Row(1).Region, Is.EqualTo("Europe"));
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Row(1).MissingSince, Is.Null);
        }

        [Test]
        public async Task MonitoredMissing_NotPurged()
        {
            Put(Folder("gba"), "gamelist.xml");
            await Seed(new Game { Id = 1, Title = "Metroid Fusion", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Metroid Fusion"), Status = GameStatus.Missing, Monitored = true, MissingSince = DateTime.UtcNow.AddDays(-20) });

            await Scanner().ScanAsync();

            Assert.That(Row(1).MissingSince, Is.Not.Null);
        }

        [Test]
        public async Task UnmonitoredGone_PurgedAfterRetention()
        {
            Put(Folder("gba"), "gamelist.xml");
            await Seed(new Game { Id = 1, Title = "Metroid Fusion", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Metroid Fusion"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow.AddDays(-20) });

            await Scanner().ScanAsync();

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.Games.Count(), Is.EqualTo(0));
        }

        [Test]
        public async Task UnmonitoredGone_PathFixedDuringTheScan_NotPurged()
        {
            Put(Folder("gba"), "gamelist.xml");
            var found = Folder("snes", "Metroid Fusion");
            await Seed(new Game { Id = 1, Title = "Metroid Fusion", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Metroid Fusion"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow.AddDays(-20) });
            // The user points the game at its real folder while the sweep runs
            var repo = Hooked((method, _) =>
            {
                if (method.Name != nameof(IGameRepository.DeleteMissingOlderThanAsync)) return;
                using var ctx = new RetroArrDbContext(_db);
                ctx.Games.Single().Path = found;
                ctx.SaveChanges();
            });

            await Scanner(repo: repo).ScanAsync();

            Assert.That(Row(1).Path, Is.EqualTo(found));
        }

        [Test]
        public async Task UnmonitoredEmpty_NotPurged()
        {
            var game = Folder("gba", "Metroid Fusion");
            await Seed(
                new Game { Id = 1, Title = "Metroid Fusion", PlatformId = Gba, Path = game, Status = GameStatus.Missing, MissingSince = DateTime.UtcNow.AddDays(-20) },
                new Game { Id = 2, Title = "Metroid Zero Mission", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Metroid Zero Mission"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow.AddDays(-20) });

            await Scanner().ScanAsync();

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.Games.Select(g => g.Id), Is.EqualTo(new[] { 1 }), "only the game whose folder is gone is purged");
        }

        [TestCase("platform folder empty")]
        [TestCase("root empty")]
        [TestCase("root refilled")]
        public async Task MountOutage_NothingFlagged(string outage)
        {
            if (outage == "platform folder empty")
            {
                Folder("gba");
                Put(Folder("snes"), "gamelist.xml");
            }
            else if (outage == "root refilled")
            {
                Folder("gba", "Freshly Added");
            }
            var games = Enumerable.Range(1, 10)
                .Select(i => new Game { Id = i, Title = $"Game {i}", PlatformId = Gba, Path = Path.Combine(_lib, "gba", $"Game {i}"), Status = GameStatus.Downloaded, Monitored = true })
                .Append(new Game { Id = 11, Title = "Flagged Long Ago", PlatformId = Gba, Path = Path.Combine(_lib, "gba", "Flagged Long Ago"), Status = GameStatus.Missing, MissingSince = DateTime.UtcNow.AddDays(-20) })
                .ToArray();
            await Seed(games);

            var logs = await Logs(() => Scanner().ScanAsync());

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.Games.Count(), Is.EqualTo(11), "a row was purged");
            Assert.That(ctx.Games.Count(g => g.MissingSince != null), Is.EqualTo(1), "a game was flagged");
            if (outage == "root refilled")
                Assert.That(logs, Has.Some.Contains("games look missing at once; nothing flagged"));
        }

        [Test]
        public async Task SoftwareGame_InstallerDeleted_NotFlagged()
        {
            var windows = Folder("windows");
            Put(windows, "gamelist.xml");
            var game = Folder("windows", "Some Game");
            Put(game, "steam_api64.dll");
            await Seed(new Game { Id = 1, Title = "Some Game", PlatformId = Pc, Path = game, Status = GameStatus.Downloaded, Monitored = true });

            await Scanner().ScanAsync();
            Assert.That(Row(1).MissingSince, Is.Null, "installed elsewhere, installer deleted");

            Directory.Delete(game, true);
            await Scanner().ScanAsync();
            Assert.That(Row(1).MissingSince, Is.Not.Null, "folder removed");
        }

        [Test]
        public async Task ApplyContentStateAsync_UsesTheFreshRow()
        {
            var repo = new SqliteGameRepository(new DbFactory(_db));
            var path = Path.Combine(_lib, "gba", "Advance Wars");
            await Seed(
                new Game { Id = 1, Title = "Downloaded", PlatformId = Gba, Path = path, Status = GameStatus.Downloaded },
                new Game { Id = 2, Title = "Wanted", PlatformId = Gba, Path = path, Status = GameStatus.Missing });

            // The stored row decides, whatever the caller's copy says
            var present = await repo.ApplyContentStateAsync(1, GameContent.Present, At, path);
            var empty = await repo.ApplyContentStateAsync(2, GameContent.Empty, At, path);

            Assert.That(present, Is.EqualTo((false, GameStatus.Downloaded, (DateTime?)null)));
            Assert.That(empty, Is.EqualTo((false, GameStatus.Missing, (DateTime?)null)));
            Assert.That(Row(2).MissingSince, Is.Null);
            Assert.That(await repo.ApplyContentStateAsync(3, GameContent.Present, At, path), Is.Null);

            // A loss seen at a path the row no longer points to is dropped
            Assert.That(await repo.ApplyContentStateAsync(1, GameContent.Gone, At, path + ".gba"), Is.EqualTo((false, GameStatus.Downloaded, (DateTime?)null)));
            Assert.That(await repo.ApplyContentStateAsync(1, GameContent.Gone, At, path + Path.DirectorySeparatorChar), Is.EqualTo((true, GameStatus.Missing, (DateTime?)At)));
        }

        [Test]
        public async Task CachedRepository_InvalidatesOnlyWhenTheRowChanged()
        {
            var path = Folder("gba", "Advance Wars");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = path, Status = GameStatus.Downloaded });
            var cache = new FakeCache();
            var repo = new CachedGameRepository(new SqliteGameRepository(new DbFactory(_db)), cache, new CacheSettings());

            await repo.ApplyContentStateAsync(1, GameContent.Present, At, path);
            Assert.That(cache.Removed, Is.Empty);

            await repo.ApplyContentStateAsync(1, GameContent.Empty, At, path);
            Assert.That(cache.Removed, Does.Contain(CacheKeys.GameDetail(1)));
            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Missing));
        }

        [Test]
        public async Task Rescan_StaleSnapshot_ClearsAFlagSetDuringTheScan()
        {
            var game = Folder("gba", "Advance Wars");
            Put(game, "Advance Wars.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game, Status = GameStatus.Downloaded });
            var scanner = Scanner();
            // Runs after the scan loaded its copy of every game
            scanner.OnScanStarted += () =>
            {
                using var ctx = new RetroArrDbContext(_db);
                var row = ctx.Games.Single();
                row.Status = GameStatus.Missing;
                row.MissingSince = DateTime.UtcNow;
                ctx.SaveChanges();
            };

            await scanner.ScanAsync(_lib, "default");

            Assert.That(Row(1).Status, Is.EqualTo(GameStatus.Downloaded));
            Assert.That(Row(1).MissingSince, Is.Null);
        }

        [Test]
        public async Task SyncGameFiles_EmptiedFolder_ClearsRows()
        {
            var game = Folder("gba", "Advance Wars");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game });
            using (var ctx = new RetroArrDbContext(_db))
            {
                ctx.GameFiles.Add(new GameFile { GameId = 1, RelativePath = "Advance Wars.gba", Size = 4, FileType = "Main" });
                await ctx.SaveChangesAsync();
            }

            await Scanner().SyncGameFilesFromDisk(1, game);

            using var check = new RetroArrDbContext(_db);
            Assert.That(check.GameFiles.Count(), Is.EqualTo(0));
        }

        [Test]
        public async Task Cleanup_UnknownContent_IsNotResynced()
        {
            // A map-file game: its Path is the platform folder, which it shares with other ROMs
            var gba = Folder("gba");
            Put(gba, "Mario Kart.gba");
            Put(gba, "notes.txt");
            await Seed(new Game { Id = 1, Title = "Mario Kart", PlatformId = Gba, Path = gba, Status = GameStatus.Downloaded });

            await Scanner().ScanAsync(_lib, "default");

            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.GameFiles.Count(), Is.EqualTo(0));
        }

        [Test]
        public async Task GetProblems_ListsFlaggedGameWithExistingFolder()
        {
            var game = Folder("gba", "Advance Wars");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Gba, Path = game, IgdbId = 7, Status = GameStatus.Missing, MissingSince = DateTime.UtcNow });
            var controller = new GameController(new SqliteGameRepository(new DbFactory(_db)), null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!);

            var result = await controller.GetProblems();

            var json = JsonSerializer.Serialize(((OkObjectResult)result.Result!).Value);
            using var doc = JsonDocument.Parse(json);
            var problem = doc.RootElement.EnumerateArray().Single();
            Assert.That(problem.GetProperty("problemType").GetString(), Is.EqualTo("missing_file"));
            Assert.That(problem.GetProperty("problemDescription").GetString(), Is.EqualTo("The game's files are missing from its folder."));
        }

        private static async Task<IList<string>> Logs(Func<Task> action)
        {
            var target = new NLog.Targets.MemoryTarget { Layout = "${level}|${message}" };
            var previous = NLog.LogManager.Configuration;
            var config = new NLog.Config.LoggingConfiguration();
            config.AddRuleForAllLevels(target);
            NLog.LogManager.Configuration = config;
            try
            {
                await action();
                return target.Logs.ToList();
            }
            finally
            {
                NLog.LogManager.Configuration = previous;
            }
        }
    }
}
