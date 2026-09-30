using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Api.V3.Games;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Download.History;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class GameDeleteFilesTest
    {
        private const int Pc = 1;
        private const int Ps1 = 20;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // IGDB that knows no game
        private sealed class NoMatches : HttpMessageHandler, IGameMetadataServiceFactory
        {
            public GameMetadataService CreateService()
            {
                var igdb = new IgdbClient("id", "secret");
                typeof(IgdbClient).GetField("_httpClient", BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(igdb, new HttpClient(this, false));
                return new GameMetadataService(igdb, new SteamClient());
            }

            public void RefreshConfiguration() { }

            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new StringContent(request.RequestUri!.Host == "api.igdb.com" ? "[]" : "{\"access_token\":\"t\",\"expires_in\":3600}", Encoding.UTF8, "application/json")
                });
        }

        private string _root = null!;
        private string _lib = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_delete_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _lib = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            _config = new ConfigurationService(_root);
            Settings(Path.Combine(_root, "trash"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private void Settings(string trash) =>
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib, DownloadPath = Path.Combine(_root, "downloads"), TrashPath = trash });

        private GameController Controller(TrashService? trash = null) =>
            new GameController(new SqliteGameRepository(new DbFactory(_db)), null!, null!, null!, _config, null!, null!, null!, trash ?? new TrashService(_config), null!, null!, null!, null, new DownloadHistoryRepository(new DbFactory(_db)));

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

        private bool Kept(int id)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.Any(g => g.Id == id);
        }

        private string[] Trashed() => new TrashService(_config).List().Select(e => e.OriginalPath).OrderBy(p => p, StringComparer.Ordinal).ToArray();

        private static string[] Sorted(params string[] paths) => paths.OrderBy(p => p, StringComparer.Ordinal).ToArray();

        private string[] Files() => Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Where(f => !f.StartsWith(Path.Combine(_root, "config"), StringComparison.Ordinal))
            .OrderBy(p => p, StringComparer.Ordinal).ToArray();

        private static async Task<JsonElement> Plan(GameController controller, int id) =>
            JsonSerializer.SerializeToElement(((OkObjectResult)await controller.DeletePlan(id)).Value);

        private static string[] Strings(JsonElement array) => array.EnumerateArray().Select(p => p.GetString()!).ToArray();

        // Where downloads were imported from, as the completed-download handler records it
        private async Task History(params (int? GameId, string SourcePath)[] grabs)
        {
            using var ctx = new RetroArrDbContext(_db);
            var n = 0;
            ctx.DownloadHistory.AddRange(grabs.Select(g => new DownloadHistoryEntry { DownloadId = "d" + ++n, Title = Path.GetFileName(g.SourcePath), GameId = g.GameId, SourcePath = g.SourcePath }));
            await ctx.SaveChangesAsync();
        }

        private async Task Imported(int gameId, string sourcePath, string destinationPath)
        {
            using var ctx = new RetroArrDbContext(_db);
            ctx.DownloadHistory.Add(new DownloadHistoryEntry { DownloadId = "i" + gameId, Title = Path.GetFileName(sourcePath), GameId = gameId, SourcePath = sourcePath, DestinationPath = destinationPath });
            await ctx.SaveChangesAsync();
        }

        private string Download(params string[] parts) => Directory.CreateDirectory(Path.Combine(new[] { _root, "downloads" }.Concat(parts).ToArray())).FullName;

        private static bool Served(ActionResult result)
        {
            (result as FileStreamResult)?.FileStream.Dispose();
            return result is FileStreamResult;
        }

        [Test]
        public async Task ImportedGame_FolderGoesToTheTrashWhole()
        {
            var psx = Folder("psx");
            var ff7 = Folder("psx", "Final Fantasy VII");
            var disc1 = Put(ff7, "Final Fantasy VII (Disc 1).cue", "FILE \"Final Fantasy VII (Disc 1).bin\" BINARY");
            Put(ff7, "Final Fantasy VII (Disc 1).bin");
            Put(ff7, "Final Fantasy VII (Disc 2).cue", "FILE \"Final Fantasy VII (Disc 2).bin\" BINARY");
            Put(ff7, "Final Fantasy VII (Disc 2).bin");
            Put(ff7, Path.Combine("Patches", "Final Fantasy VII - Update.ppf"));
            var crash = Put(Folder("psx", "Crash Bandicoot"), "Crash Bandicoot.cue");
            await Seed(
                new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7, ExecutablePath = disc1 },
                new Game { Id = 2, Title = "Crash Bandicoot", PlatformId = Ps1, Path = Path.GetDirectoryName(crash), ExecutablePath = crash });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Directory.Exists(ff7), Is.False);
            Assert.That(Trashed(), Is.EqualTo(new[] { ff7 }));
            Assert.That(File.Exists(crash), Is.True);
            Assert.That(Kept(1), Is.False);
        }

        // The other game holds the folder through its path or only through its main file
        [TestCase(false)]
        [TestCase(true)]
        public async Task SharedFolder_OnlyTheGamesOwnFilesGo(bool onlyMainFile)
        {
            var shared = Folder("misc");
            var cue = Put(shared, "Ape Escape.cue", "FILE \"Ape Escape.bin\" BINARY");
            var bin = Put(shared, "Ape Escape.bin");
            var nds = Put(shared, "Mario Kart DS.nds");
            await Seed(
                new Game { Id = 1, Title = "Ape Escape", PlatformId = Ps1, Path = shared, ExecutablePath = cue },
                new Game { Id = 2, Title = "Mario Kart DS", PlatformId = Ps1, Path = onlyMainFile ? null : nds, ExecutablePath = nds });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(cue, bin)));
            Assert.That(File.Exists(nds), Is.True);
            Assert.That(Kept(2), Is.True);
        }

        [Test]
        public async Task SingleFileGame_TakesItsTracksButNothingOutsideItsFolder()
        {
            var roms = Folder("roms");
            var outside = Put(_root, "outside.bin");
            var cue = Put(roms, "Spyro.cue", "FILE \"Spyro.bin\" BINARY\nFILE \"../../outside.bin\" BINARY");
            var bin = Put(roms, "Spyro.bin");
            var loose = Put(roms, "Tekken 3.bin");
            await Seed(new Game { Id = 1, Title = "Spyro", PlatformId = Ps1, Path = cue, ExecutablePath = cue });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(cue, bin)));
            Assert.That(File.Exists(outside), Is.True);
            Assert.That(File.Exists(loose), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task LibraryRootOrPlatformFolder_IsNeverTrashed(bool platformFolder)
        {
            var folder = platformFolder ? Folder("nds") : _lib;
            var rom = Put(folder, "Mario Kart DS.nds");
            await Seed(new Game { Id = 1, Title = "Mario Kart DS", PlatformId = Ps1, Path = folder });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
            Assert.That(File.Exists(rom), Is.True);
            Assert.That(Trashed(), Is.Empty);
            Assert.That(Kept(1), Is.True);
        }

        [Test]
        public async Task ContainerPickedInsideTheLibrary_GoesWhole()
        {
            var container = Folder("pc", "Half-Life");
            var bin = Folder("pc", "Half-Life", "bin");
            var exe = Put(bin, "hl.exe");
            Put(container, "readme.txt");
            await Seed(new Game { Id = 1, Title = "Half-Life", PlatformId = Pc, Path = bin, ExecutablePath = exe });

            var result = await Controller().Delete(1, deleteFiles: true, targetPath: container);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Directory.Exists(container), Is.False);
            Assert.That(Trashed(), Is.EqualTo(new[] { container }));
        }

        // A picked folder goes as it is or nothing happens: the platform folder the old dialog sent, a collection
        // not named after the game, the folder of a single-file game, a folder outside the library
        [TestCase(1, "psx")]
        [TestCase(2, "Games/Collections")]
        [TestCase(3, "roms")]
        [TestCase(4, "../ext")]
        public async Task PickedFolder_ThatWouldNotGoAsItIs_IsRefused(int id, string picked)
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            var demons = Folder("Games", "Collections", "Sony", "Demons Souls");
            Put(demons, "PS3_GAME/USRDIR/EBOOT.BIN");
            Put(Folder("Games", "Collections", "Nintendo"), "Zelda.nsp");
            var roms = Folder("roms");
            var spyro = Put(roms, "Spyro.cue", "FILE \"Spyro.bin\" BINARY");
            Put(roms, "Spyro.bin");
            Put(roms, "Tekken 3.bin");
            var smb = Directory.CreateDirectory(Path.Combine(_root, "ext", "Super Mario Bros. 3")).FullName;
            Put(smb, "Super Mario Bros. 3.nes");
            Put(Path.Combine(_root, "ext"), "notes.txt");
            await Seed(
                new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 },
                new Game { Id = 2, Title = "Demons Souls", PlatformId = Ps1, Path = demons },
                new Game { Id = 3, Title = "Spyro", PlatformId = Ps1, Path = spyro, ExecutablePath = spyro },
                new Game { Id = 4, Title = "Super Mario Bros. 3", PlatformId = Ps1, Path = smb });
            var before = Files();

            var result = await Controller().Delete(id, deleteFiles: true, targetPath: Path.GetFullPath(Path.Combine(_lib, picked)));

            Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
            Assert.That(Files(), Is.EqualTo(before));
            Assert.That(Kept(id), Is.True);
        }

        // On Linux a folder whose name differs only in case is another folder: a picked container,
        // a track the cue points at and a main file there are not the game's
        [TestCase(1, "pc/half-life")]
        [TestCase(2, null)]
        [TestCase(3, null)]
        [Platform(Exclude = "Win,MacOsX")]
        public async Task FolderDifferingOnlyInCase_IsNotTheGamesFolder(int id, string? picked)
        {
            var bin = Folder("pc", "Half-Life", "bin");
            Put(bin, "hl.exe");
            var cue = Put(Folder("roms"), "Spyro.cue", "FILE \"../ROMS/Spyro.bin\" BINARY");
            var exe = Put(Folder("MISC"), "Ape Escape.cue");
            var outside = new[] { Put(Folder("pc", "half-life"), "save.dat"), Put(Folder("ROMS"), "Spyro.bin"), exe };
            var nds = Put(Folder("misc"), "Mario Kart DS.nds");
            await Seed(
                new Game { Id = 1, Title = "Half-Life", PlatformId = Pc, Path = bin },
                new Game { Id = 2, Title = "Spyro", PlatformId = Ps1, Path = cue, ExecutablePath = cue },
                new Game { Id = 3, Title = "Ape Escape", PlatformId = Ps1, Path = Folder("misc"), ExecutablePath = exe },
                new Game { Id = 4, Title = "Mario Kart DS", PlatformId = Ps1, Path = nds, ExecutablePath = nds });

            await Controller().Delete(id, deleteFiles: true, targetPath: picked == null ? null : Path.Combine(_lib, picked));

            Assert.That(outside.Where(f => !File.Exists(f)), Is.Empty);
        }

        [Test]
        public async Task MainFileOutsideTheGamesFolder_IsNotTrashed()
        {
            Put(Folder("psx"), "Crash Bandicoot.cue");
            var elsewhere = Folder("elsewhere");
            var cue = Put(elsewhere, "Ape Escape.cue", "FILE \"Ape Escape.bin\" BINARY");
            Put(elsewhere, "Ape Escape.bin");
            await Seed(new Game { Id = 1, Title = "Ape Escape", PlatformId = Ps1, Path = Folder("psx"), ExecutablePath = cue });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
            Assert.That(Trashed(), Is.Empty);
            Assert.That(File.Exists(cue), Is.True);
        }

        // A disc a leftover entry of its own points at stays with that entry
        [Test]
        public async Task M3uGame_DiscAnotherEntryPointsAt_Stays()
        {
            var psx = Folder("psx");
            var disc1 = Put(psx, "Final Fantasy VII (Disc 1).chd");
            var disc2 = Put(psx, "Final Fantasy VII (Disc 2).chd");
            var m3u = Put(psx, "Final Fantasy VII.m3u", "Final Fantasy VII (Disc 1).chd\nFinal Fantasy VII (Disc 2).chd\n");
            await Seed(
                new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = m3u, ExecutablePath = m3u },
                new Game { Id = 2, Title = "Final Fantasy VII (Disc 2)", PlatformId = Ps1, Path = disc2, ExecutablePath = disc2 });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(m3u, disc1)));
            Assert.That(File.Exists(disc2), Is.True);
        }

        // The tracks of a disc another entry points at stay with it, not only the cue it names
        [Test]
        public async Task M3uGame_CueDiscOfAnotherEntry_KeepsItsTracks()
        {
            var psx = Folder("psx");
            var cue1 = Put(psx, "FF7 (Disc 1).cue", "FILE \"FF7 (Disc 1).bin\" BINARY");
            var bin1 = Put(psx, "FF7 (Disc 1).bin");
            var cue2 = Put(psx, "FF7 (Disc 2).cue", "FILE \"FF7 (Disc 2).bin\" BINARY");
            var bin2 = Put(psx, "FF7 (Disc 2).bin");
            var m3u = Put(psx, "FF7.m3u", "FF7 (Disc 1).cue\nFF7 (Disc 2).cue\n");
            await Seed(
                new Game { Id = 1, Title = "FF7", PlatformId = Ps1, Path = m3u, ExecutablePath = m3u },
                new Game { Id = 2, Title = "FF7 (Disc 2)", PlatformId = Ps1, Path = cue2, ExecutablePath = cue2 });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(m3u, cue1, bin1)));
            Assert.That(File.Exists(cue2) && File.Exists(bin2), Is.True);
        }

        // The playlist lies in the platform folder, its discs in a folder of their own: a leftover entry of disc 1
        // goes without the disc the playlist lists
        [Test]
        public async Task LeftoverDiscEntry_DiscOfAPlaylistInAFolderAbove_Stays()
        {
            var psx = Folder("psx");
            var discs = Folder("psx", "FF7");
            var cue1 = Put(discs, "FF7 (Disc 1).cue", "FILE \"FF7 (Disc 1).bin\" BINARY");
            var bin1 = Put(discs, "FF7 (Disc 1).bin");
            Put(discs, "FF7 (Disc 2).cue", "FILE \"FF7 (Disc 2).bin\" BINARY");
            Put(discs, "FF7 (Disc 2).bin");
            var m3u = Put(psx, "FF7.m3u", "FF7/FF7 (Disc 1).cue\nFF7/FF7 (Disc 2).cue\n");
            await Seed(
                new Game { Id = 1, Title = "FF7", PlatformId = Ps1, Path = m3u, ExecutablePath = m3u },
                new Game { Id = 2, Title = "FF7 (Disc 1)", PlatformId = Ps1, Path = cue1, ExecutablePath = cue1 });

            var result = await Controller().Delete(2, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.Empty);
            Assert.That(File.Exists(cue1) && File.Exists(bin1), Is.True);
            Assert.That(Kept(1), Is.True);
            Assert.That(Kept(2), Is.False);
        }

        [Test]
        public async Task DiscImage_SavesAndArtOfTheSameName_Stay()
        {
            var roms = Folder("roms");
            var bin = Put(roms, "Sonic CD.bin");
            var cue = Put(roms, "Sonic CD.cue", "FILE \"Sonic CD.bin\" BINARY");
            var saves = new[] { "Sonic CD.srm", "Sonic CD.sav", "Sonic CD.state", "Sonic CD.state1", "Sonic CD.png", "Sonic CD.txt" }.Select(n => Put(roms, n)).ToArray();
            await Seed(new Game { Id = 1, Title = "Sonic CD", PlatformId = Ps1, Path = bin, ExecutablePath = bin });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(bin, cue)));
            Assert.That(saves.Where(f => !File.Exists(f)), Is.Empty);
        }

        // "Move files to the trash" is checked by default, so a game whose files are gone still goes
        [Test]
        public async Task MissingGame_WithDeleteFiles_IsRemoved()
        {
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = Path.Combine(_lib, "psx", "Final Fantasy VII"), Status = GameStatus.Missing });
            var controller = Controller();

            var plan = await Plan(controller, 1);
            var result = await controller.Delete(1, deleteFiles: true);

            Assert.That(plan.GetProperty("paths").GetArrayLength(), Is.Zero);
            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Kept(1), Is.False);
        }

        [Test]
        [Platform(Exclude = "Win")]
        public async Task SystemFolder_IsNeverPlanned()
        {
            await Seed(new Game { Id = 1, Title = "Settings", PlatformId = Pc, Path = "/etc" });

            var plan = await Plan(Controller(), 1);

            Assert.That(plan.GetProperty("paths").ValueKind, Is.EqualTo(JsonValueKind.Null));
            Assert.That(plan.GetProperty("refused").GetString(), Does.Contain("/etc"));
        }

        // The plan the dialog shows is what the delete moves
        [Test]
        public async Task Plan_ListsWhatTheDeleteMoves()
        {
            var roms = Folder("roms");
            var cue = Put(roms, "Spyro.cue", "FILE \"Spyro.bin\" BINARY");
            var bin = Put(roms, "Spyro.bin");
            Put(roms, "Tekken 3.bin");
            await Seed(new Game { Id = 1, Title = "Spyro", PlatformId = Ps1, Path = cue, ExecutablePath = cue });
            var controller = Controller();

            var plan = await Plan(controller, 1);
            await controller.Delete(1, deleteFiles: true);

            Assert.That(Strings(plan.GetProperty("paths")).OrderBy(p => p, StringComparer.Ordinal), Is.EqualTo(Sorted(cue, bin)));
            Assert.That(Trashed(), Is.EqualTo(Sorted(cue, bin)));
        }

        // A later file that cannot go puts back the ones that went, so the game keeps all of its files
        [Test]
        public async Task FileSetThatCannotGoWhole_PutsBackWhatWent()
        {
            var roms = Folder("roms");
            var cue = Put(roms, "Spyro.cue", "FILE \"Spyro.bin\" BINARY");
            var bin = Put(roms, "Spyro.bin");
            await Seed(new Game { Id = 1, Title = "Spyro", PlatformId = Ps1, Path = cue, ExecutablePath = cue });
            var trash = new TrashService(_config);
            trash.Rename = (from, to) =>
            {
                if (from == bin) throw new IOException("Invalid cross-device link");
                Directory.Move(from, to);
            };
            trash.FreeSpace = _ => 0;

            var result = await Controller(trash).Delete(1, deleteFiles: true);

            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(500));
            Assert.That(JsonSerializer.SerializeToElement(((ObjectResult)result).Value).GetProperty("message").GetString(), Does.Contain("Not enough free space"));
            Assert.That(File.Exists(cue) && File.Exists(bin), Is.True);
            Assert.That(Trashed(), Is.Empty);
            Assert.That(Kept(1), Is.True);
        }

        // ---- The game's downloads ----

        [Test]
        public async Task GrabbedDownload_IsPlannedAndGoesToTheTrashWithTheGame()
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            var grab = Download("complete", "Final.Fantasy.VII.PSX-GROUP");
            Put(grab, "ff7.bin");
            var crash = Put(Download("complete", "Crash.Bandicoot.PSX-GROUP"), "crash.bin");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });
            await History((1, grab), (2, Path.GetDirectoryName(crash)!));
            var controller = Controller();

            var plan = await Plan(controller, 1);
            var result = await controller.Delete(1, deleteFiles: true, deleteDownloadFiles: true);

            Assert.That(Strings(plan.GetProperty("downloads")), Is.EqualTo(new[] { grab }));
            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(ff7, grab)));
            Assert.That(File.Exists(crash), Is.True);
        }

        // A recorded path that is not the game's download alone is neither offered nor taken when a caller names it.
        // Downloads that land in the library (a client mapped into it) still leave the library alone.
        [TestCase("download root")]
        [TestCase("category")]
        [TestCase("platform")]
        [TestCase("holds another download")]
        [TestCase("holds another download of the game")]
        [TestCase("library")]
        [TestCase("outside the download folder")]
        public async Task RecordedDownload_ThatIsNotTheGamesAlone_IsRefused(string recorded)
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            Put(Download("retroarr"), "Tekken 3.bin");
            Put(Download("psx"), "Spyro.bin");
            var crash = Download("complete", "Crash.Bandicoot.PSX-GROUP");
            Put(crash, "crash.bin");
            var elsewhere = Directory.CreateDirectory(Path.Combine(_root, "elsewhere", "Final.Fantasy.VII.PSX-GROUP")).FullName;
            Put(elsewhere, "ff7.bin");
            var untracked = Folder("psx", "Stray");
            Put(untracked, "Stray.bin");
            _config.SaveDownloadClients(new List<DownloadClient>
            {
                new DownloadClient { Id = 1, Name = "qBittorrent", Implementation = "qBittorrent", Category = "retroarr" },
                new DownloadClient { Id = 2, Name = "SABnzbd", Implementation = "SABnzbd", RemotePathMapping = "/data", LocalPathMapping = _lib },
            });
            var path = recorded switch
            {
                "download root" => Download(),
                "category" => Download("retroarr"),
                "platform" => Download("psx"),
                "holds another download" or "holds another download of the game" => Download("complete"),
                "library" => untracked,
                _ => elsewhere,
            };
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });
            if (recorded == "holds another download") await History((1, path), (2, crash));
            else if (recorded == "holds another download of the game") await History((1, path), (1, crash));
            else await History((1, path));
            var controller = Controller();
            var before = Files();

            var plan = await Plan(controller, 1);
            var result = await controller.Delete(1, deleteDownloadFiles: true, downloadPath: path);

            Assert.That(Strings(plan.GetProperty("downloads")), Does.Not.Contain(path));
            Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
            Assert.That(Files(), Is.EqualTo(before));
            Assert.That(Kept(1), Is.True);
        }

        // Without a recorded grab only a folder named exactly like the game counts, not one named like the folder
        // above the game or a longer name
        [TestCase("complete/Final Fantasy VII", true)]
        [TestCase("complete/Final Fantasy VII Remake", false)]
        [TestCase("Square", false)]
        public async Task DownloadFolder_WithoutAGrab_OnlyWhenNamedExactlyLikeTheGame(string folder, bool found)
        {
            var ff7 = Folder("Square", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            var download = Download(folder.Split('/'));
            Put(download, "ff7.bin");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });

            var plan = await Plan(Controller(), 1);

            Assert.That(Strings(plan.GetProperty("downloads")), Is.EqualTo(found ? new[] { download } : Array.Empty<string>()));
        }

        // A recorded download holding another entry's files, or the game's own library files, is not the game's to trash
        [TestCase(false)]
        [TestCase(true)]
        public async Task RecordedDownload_HoldingLibraryFiles_IsNotOffered(bool ownFiles)
        {
            var ff7 = Folder("psx", "FF7");
            Put(ff7, "FF7.bin");
            var pack = Download("complete", "PSX.Pack-GRP");
            Put(pack, "ff7.bin");
            var inPlace = Directory.CreateDirectory(Path.Combine(pack, "Crash")).FullName;
            var crash = Put(inPlace, "Crash.bin");
            // the game itself, or another one, linked in place inside the download it came with
            await Seed(
                new Game { Id = 1, Title = "FF7", PlatformId = Ps1, Path = ownFiles ? pack : ff7 },
                new Game { Id = 2, Title = "Crash", PlatformId = Ps1, Path = ownFiles ? ff7 : inPlace, ExecutablePath = ownFiles ? null : crash });
            await History((1, pack));
            var controller = Controller();

            var plan = await Plan(controller, 1);
            var result = await controller.Delete(1, deleteDownloadFiles: true);

            Assert.That(plan.GetProperty("downloads").GetArrayLength(), Is.Zero);
            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.Empty);
            Assert.That(File.Exists(crash), Is.True);
        }

        // The folder named like the game is another entry's recorded grab: the other region, or another platform
        [Test]
        public async Task NameMatch_ThatIsAnotherEntrysGrab_IsNotOffered()
        {
            var usa = Folder("gba", "Advance Wars");
            Put(usa, "Advance Wars (USA).gba");
            var eur = Folder("gba", "Advance Wars (Europe)");
            Put(eur, "Advance Wars (Europe).gba");
            var grab = Download("Advance Wars");
            Put(grab, "Advance Wars (USA).gba");
            await Seed(
                new Game { Id = 1, Title = "Advance Wars", Region = "USA", PlatformId = Ps1, Path = usa },
                new Game { Id = 2, Title = "Advance Wars", Region = "Europe", PlatformId = Ps1, Path = eur });
            await History((1, grab));
            var controller = Controller();

            Assert.That(Strings((await Plan(controller, 1)).GetProperty("downloads")), Is.EqualTo(new[] { grab }));
            Assert.That((await Plan(controller, 2)).GetProperty("downloads").GetArrayLength(), Is.Zero);
        }

        // Only the title finds a download by name: the game's folder may be a generic one like Setup
        [Test]
        public async Task NameMatch_UsesTheTitleOnly()
        {
            var setup = Folder("windows", "Half-Life", "Setup");
            var exe = Put(setup, "setup.exe");
            Put(Download("Portal.2-GRP", "Setup"), "setup.exe");
            await Seed(new Game { Id = 1, Title = "Half-Life", PlatformId = Pc, Path = setup, ExecutablePath = exe });

            Assert.That((await Plan(Controller(), 1)).GetProperty("downloads").GetArrayLength(), Is.Zero);
        }

        // A grab recorded for the game whose files went to another entry of the title is that entry's download
        [TestCase("windows", true)]
        [TestCase("switch", false)]
        public async Task Grab_CountsWhereItsFilesWent(string importedTo, bool offered)
        {
            var zelda = Folder("windows", "Zelda");
            Put(zelda, "zelda.exe");
            var grab = Download("Zelda.NSW-VENOM");
            Put(grab, "zelda.nsp");
            await Seed(new Game { Id = 1, Title = "Zelda", PlatformId = Pc, Path = zelda });
            await Imported(1, grab, Folder(importedTo, "Zelda"));

            var plan = await Plan(Controller(), 1);

            Assert.That(Strings(plan.GetProperty("downloads")), Is.EqualTo(offered ? new[] { grab } : Array.Empty<string>()));
        }

        // ---- Links and names ----

        // The library root is set through a link, an old entry has the real path of the root or of a folder in it
        [TestCase("root")]
        [TestCase("container")]
        [Platform(Exclude = "Win")]
        public async Task LinkedLibraryRoot_RealPathOfAFolderHoldingOthers_IsRefused(string which)
        {
            var real = Directory.CreateDirectory(Path.Combine(_root, "pool", "games")).FullName;
            var alias = Path.Combine(_root, "games");
            Directory.CreateSymbolicLink(alias, real);
            _config.SaveMediaSettings(new MediaSettings { FolderPath = alias, DestinationPath = alias, TrashPath = Path.Combine(_root, "trash") });
            var crash = Put(Path.Combine(real, "Collections", "Crash"), "Crash.bin");
            Put(Path.Combine(real, "Collections", "Spyro"), "Spyro.bin");
            await Seed(
                new Game { Id = 1, Title = "Spyro", PlatformId = Ps1, Path = which == "root" ? real : Path.Combine(real, "Collections") },
                new Game { Id = 2, Title = "Crash", PlatformId = Ps1, Path = Path.Combine(alias, "Collections", "Crash") });

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<ConflictObjectResult>());
            Assert.That(Trashed(), Is.Empty);
            Assert.That(File.Exists(crash), Is.True);
        }

        // A platform folder is one right below a library root; a game named like a platform is a game
        [Test]
        public async Task GameFolderNamedLikeAPlatform_IsTheGamesFolder()
        {
            var pegasus = Folder("windows", "Pegasus");
            var exe = Put(pegasus, "Pegasus.exe");
            Put(pegasus, "data/level1.pak", "level");
            await Seed(new Game { Id = 1, Title = "Pegasus", PlatformId = Pc, Path = pegasus, ExecutablePath = exe });
            var controller = Controller();

            var plan = await Plan(controller, 1);
            var served = Served(await controller.DownloadGameFile(1, "data/level1.pak"));

            Assert.That(Strings(plan.GetProperty("paths")), Is.EqualTo(new[] { pegasus }));
            Assert.That(served, Is.True);
        }

        // A folder named like a platform further down holds games of that platform, not one game: only the game's
        // own files go
        [Test]
        public async Task PlatformFolderBelowAnotherFolder_OnlyTheGamesOwnFilesGo()
        {
            var dos = Folder("Collections", "dos");
            var doom = Put(dos, "DOOM.EXE");
            var keen = Put(dos, "KEEN.EXE");
            await Seed(new Game { Id = 1, Title = "Doom", PlatformId = Pc, Path = dos, ExecutablePath = doom });

            var plan = await Plan(Controller(), 1);
            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(Strings(plan.GetProperty("paths")), Is.EqualTo(new[] { doom }));
            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(new[] { doom }));
            Assert.That(File.Exists(keen), Is.True);
        }

        // The scan of a library without platform folders right below it makes one entry of such a folder of programs
        [Test]
        public async Task PlatformFolderBelowAnotherFolder_EntryTheScanMade_OtherProgramsStay()
        {
            var dos = Folder("Collections", "dos");
            var doom = Put(dos, "DOOM.EXE");
            var keen = Put(dos, "KEEN.EXE");
            await new MediaScannerService(_config, new NoMatches(), new SqliteGameRepository(new DbFactory(_db)), new TitleCleanerService()).ScanAsync();
            Game entry;
            using (var ctx = new RetroArrDbContext(_db)) entry = ctx.Games.AsNoTracking().Single();
            Assert.That((entry.Path, entry.ExecutablePath), Is.EqualTo((dos, doom)));

            var result = await Controller().Delete(entry.Id, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(new[] { doom }));
            Assert.That(File.Exists(keen), Is.True);
        }

        // ---- When a move fails or the plan changed ----

        [Test]
        public async Task PutBackThatFails_IsReportedWithWhatIsStillInTheTrash()
        {
            var roms = Folder("roms");
            var cue = Put(roms, "Spyro.cue", "FILE \"Spyro.bin\" BINARY");
            var bin = Put(roms, "Spyro.bin");
            await Seed(new Game { Id = 1, Title = "Spyro", PlatformId = Ps1, Path = cue, ExecutablePath = cue });
            var trash = new TrashService(_config);
            // the bin can't go and the cue can't come back
            trash.Rename = (from, to) =>
            {
                if (from == bin || to == cue) throw new IOException("Invalid cross-device link");
                Directory.Move(from, to);
            };
            trash.FreeSpace = _ => 0;

            var result = (ObjectResult)await Controller(trash).Delete(1, deleteFiles: true);
            var body = JsonSerializer.SerializeToElement(result.Value);

            Assert.That(result.StatusCode, Is.EqualTo(500));
            Assert.That(body.GetProperty("message").GetString(), Does.Contain("still in the trash"));
            Assert.That(body.GetProperty("stuck").EnumerateArray().Select(e => e.GetProperty("OriginalPath").GetString()), Is.EqualTo(new[] { cue }));
            Assert.That(Trashed(), Is.EqualTo(new[] { cue }));
            Assert.That(Kept(1), Is.True);
        }

        // A folder only partly moved stays listed in the trash, and the answer says where its files are
        [Test]
        public async Task PartlyMovedFolder_IsReportedWithItsTrashEntry()
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            var saves = Folder("psx", "Final Fantasy VII", "saves");
            Put(saves, "ff7.srm");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });
            var trash = new TrashService(_config);
            trash.Rename = (_, _) => throw new IOException("Invalid cross-device link");
            var delete = trash.Delete;
            // the source delete stops halfway and what it took can't be put back
            trash.Delete = p =>
            {
                if (p != ff7) { delete(p); return; }
                if (Directory.Exists(saves)) Directory.Delete(saves, true);
                if (!File.Exists(saves)) File.WriteAllText(saves, "not a folder");
                throw new IOException("Device or resource busy");
            };

            var result = (ObjectResult)await Controller(trash).Delete(1, deleteFiles: true);
            var body = JsonSerializer.SerializeToElement(result.Value);

            Assert.That(result.StatusCode, Is.EqualTo(500));
            Assert.That(body.GetProperty("stuck").EnumerateArray().Select(e => e.GetProperty("OriginalPath").GetString()), Is.EqualTo(new[] { ff7 }));
            Assert.That(body.GetProperty("message").GetString(), Does.Contain("still in the trash"));
            Assert.That(Trashed(), Is.EqualTo(new[] { ff7 }));
            Assert.That(Kept(1), Is.True);
        }

        // The confirmed delete moves what the dialog showed, or nothing
        [Test]
        public async Task PlanThatChangedSinceTheDialog_MovesNothing()
        {
            var roms = Folder("roms");
            var bin = Put(roms, "Sonic CD.bin");
            var cue = Put(roms, "Sonic CD.cue", "FILE \"Sonic CD.bin\" BINARY");
            await Seed(new Game { Id = 1, Title = "Sonic CD", PlatformId = Ps1, Path = bin, ExecutablePath = bin });
            var controller = Controller();
            var shown = new GameController.DeleteExpectation { Paths = Strings((await Plan(controller, 1)).GetProperty("paths")).ToList(), Downloads = new List<string>() };
            var sub = Put(roms, "Sonic CD.sub");
            var before = Files();

            var changed = await controller.Delete(1, deleteFiles: true, expected: shown);
            var files = Files();
            shown.Paths = Strings((await Plan(controller, 1)).GetProperty("paths")).ToList();
            var confirmed = await controller.Delete(1, deleteFiles: true, expected: shown);

            Assert.That(changed, Is.InstanceOf<ConflictObjectResult>());
            Assert.That(files, Is.EqualTo(before));
            Assert.That(confirmed, Is.InstanceOf<NoContentResult>());
            Assert.That(Trashed(), Is.EqualTo(Sorted(bin, cue, sub)));
        }

        // ---- Listing and download ----

        // A game in a platform folder lists what the download serves: its main file's set, not the folder
        [TestCase(true)]
        [TestCase(false)]
        public async Task Listing_GameInAPlatformFolder_ListsItsMainFileSetOnly(bool hasMainFile)
        {
            var nds = Folder("nds");
            var rom = Put(nds, "Mario Kart DS.nds");
            Put(nds, "New Super Mario Bros.nds");
            await Seed(new Game { Id = 1, Title = "Mario Kart DS", PlatformId = Ps1, Path = nds, ExecutablePath = hasMainFile ? rom : null });

            var listing = JsonSerializer.SerializeToElement(((OkObjectResult)await Controller().GetGameFiles(1)).Value);

            Assert.That(listing.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()),
                Is.EqualTo(hasMainFile ? new[] { "Mario Kart DS.nds" } : Array.Empty<string>()));
        }

        // The listing offers what the download serves: a single file's members inside its folder
        [TestCase("../../outside.bin")]
        [TestCase("../ROMS/Spyro (Track 2).bin", ExcludePlatform = "Win,MacOsX")]
        public async Task Listing_SingleFileGame_OnlyListsFilesInItsFolder(string track)
        {
            var roms = Folder("roms");
            var cue = Put(roms, "Spyro.cue", $"FILE \"Spyro.bin\" BINARY\nFILE \"{track}\" BINARY");
            Put(roms, "Spyro.bin");
            Put(roms, track, "not the game's");
            await Seed(new Game { Id = 1, Title = "Spyro", PlatformId = Ps1, Path = cue, ExecutablePath = cue });

            var listing = JsonSerializer.SerializeToElement(((OkObjectResult)await Controller().GetGameFiles(1)).Value);

            Assert.That(listing.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()), Is.EquivalentTo(new[] { "Spyro.cue", "Spyro.bin" }));
        }

        [Test]
        public async Task M3uGame_DiscInASubfolder_IsListedAndDownloadedFromIt()
        {
            var psx = Folder("psx");
            Put(psx, "CD1/Final Fantasy VII.chd", "disc 1");
            Put(psx, "CD2/Final Fantasy VII.chd", "disc 2");
            // Another file of the same name beside the playlist
            Put(psx, "Final Fantasy VII.chd", "not the game");
            var m3u = Put(psx, "Final Fantasy VII.m3u", "CD1/Final Fantasy VII.chd\nCD2/Final Fantasy VII.chd\n");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = m3u, ExecutablePath = m3u });
            var controller = Controller();

            var listing = JsonSerializer.SerializeToElement(((OkObjectResult)await controller.GetGameFiles(1)).Value);
            var listed = listing.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("relativePath").GetString()).ToArray();
            var download = (FileStreamResult)await controller.DownloadGameFile(1, "CD2/Final Fantasy VII.chd");
            using var reader = new StreamReader(download.FileStream);

            Assert.That(listed, Is.EquivalentTo(new[] { "Final Fantasy VII.m3u", "CD1/Final Fantasy VII.chd", "CD2/Final Fantasy VII.chd" }));
            Assert.That(reader.ReadToEnd(), Is.EqualTo("disc 2"));
        }

        // The folder the settings name for the game is listed, but the game is not pointed at it
        [TestCase(true)]
        [TestCase(false)]
        public async Task Listing_GameAwayFromItsFolder_ListsTheSettingsFolder_AndLeavesThePath(bool moved)
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            var stored = moved ? Path.Combine(_lib, "psx", "Old Folder") : null;
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = stored });

            var listing = JsonSerializer.SerializeToElement(((OkObjectResult)await Controller().GetGameFiles(1)).Value);

            Assert.That(listing.GetProperty("resolvedPath").GetString(), Is.EqualTo(ff7));
            Assert.That(listing.GetProperty("files").EnumerateArray().Select(f => f.GetProperty("name").GetString()), Is.EqualTo(new[] { "Final Fantasy VII.bin" }));
            using var ctx = new RetroArrDbContext(_db);
            Assert.That(ctx.Games.Single().Path, Is.EqualTo(stored));
        }

        [TestCase("../Final Fantasy VII (Europe)/Final Fantasy VII (Europe).bin")]
        [TestCase("../../../outside.bin")]
        public async Task Download_OutsideTheGamesFolder_IsRefused(string path)
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            Put(Folder("psx", "Final Fantasy VII (Europe)"), "Final Fantasy VII (Europe).bin");
            Put(_root, "outside.bin");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });

            var result = await Controller().DownloadGameFile(1, path);

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        [Platform(Exclude = "Win,MacOsX")]
        public async Task Download_SiblingThatDiffersOnlyInCase_IsRefused()
        {
            var ff7 = Folder("psx", "FF7");
            Put(ff7, "FF7.bin");
            Put(Folder("psx", "ff7"), "ff7.bin");
            await Seed(new Game { Id = 1, Title = "FF7", PlatformId = Ps1, Path = ff7 });

            var result = await Controller().DownloadGameFile(1, "../ff7/ff7.bin");

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [TestCase("outside.bin", false)]
        [TestCase("ext/notes.txt", false)]
        [TestCase("disc.bin", true)]
        [Platform(Exclude = "Win")]
        public async Task Download_ThroughALink_OnlyWhenItStaysInTheFolder(string path, bool served)
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            var disc = Put(ff7, "Discs/Final Fantasy VII.bin");
            var ext = Directory.CreateDirectory(Path.Combine(_root, "ext")).FullName;
            Put(ext, "notes.txt");
            File.CreateSymbolicLink(Path.Combine(ff7, "outside.bin"), Put(_root, "outside.bin"));
            Directory.CreateSymbolicLink(Path.Combine(ff7, "ext"), ext);
            File.CreateSymbolicLink(Path.Combine(ff7, "disc.bin"), disc);
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });

            var result = await Controller().DownloadGameFile(1, path);

            Assert.That(Served(result), Is.EqualTo(served));
            if (!served) Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        // A single-file game serves its own file set, not the rest of its folder
        [Test]
        public async Task Download_SingleFileGame_ServesOnlyItsOwnFiles()
        {
            var roms = Folder("roms");
            var rom = Put(roms, "Advance Wars.gba");
            Put(roms, "Pokemon Emerald.gba");
            await Seed(new Game { Id = 1, Title = "Advance Wars", PlatformId = Ps1, Path = rom, ExecutablePath = rom });
            var controller = Controller();

            Assert.That(await controller.DownloadGameFile(1, "Pokemon Emerald.gba"), Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(Served(await controller.DownloadGameFile(1, "Advance Wars.gba")), Is.True);
        }

        // Without the destination pattern a game that has no path resolves to the library root
        [Test]
        public async Task Download_FromTheLibraryRoot_IsRefused()
        {
            Put(Folder("psx", "Crash Bandicoot"), "Crash Bandicoot.bin");
            _config.SaveMediaSettings(new MediaSettings { FolderPath = _lib, DestinationPath = _lib, UseDestinationPattern = false, TrashPath = Path.Combine(_root, "trash") });
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1 });

            var result = await Controller().DownloadGameFile(1, "psx/Crash Bandicoot/Crash Bandicoot.bin");

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
        }

        [Test]
        public async Task FilesThatCannotBeTrashed_KeepTheGame()
        {
            var ff7 = Folder("psx", "Final Fantasy VII");
            Put(ff7, "Final Fantasy VII.bin");
            await Seed(new Game { Id = 1, Title = "Final Fantasy VII", PlatformId = Ps1, Path = ff7 });
            Settings(Path.Combine(Put(_root, "blocker"), "trash"));

            var result = await Controller().Delete(1, deleteFiles: true);

            Assert.That(result, Is.InstanceOf<ObjectResult>());
            Assert.That(((ObjectResult)result).StatusCode, Is.EqualTo(500));
            Assert.That(Directory.Exists(ff7), Is.True);
            Assert.That(Kept(1), Is.True);
        }
    }
}
