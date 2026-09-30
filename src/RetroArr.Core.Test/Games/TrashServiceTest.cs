using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RetroArr.Api.V3.Settings;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class TrashServiceTest
    {
        private string _root = null!;
        private string _trash = null!;
        private ConfigurationService _config = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_trash_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            _trash = Path.Combine(_root, "trash");
            _config = new ConfigurationService(_root);
            _config.SaveMediaSettings(new MediaSettings { TrashPath = _trash });
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        // A trash on another volume: the rename fails and the payload is copied
        private TrashService OtherVolume()
        {
            var trash = new TrashService(_config);
            trash.Rename = (_, _) => throw new IOException("Invalid cross-device link");
            return trash;
        }

        private string Game()
        {
            var game = Directory.CreateDirectory(Path.Combine(_root, "library", "Final Fantasy VII")).FullName;
            File.WriteAllText(Path.Combine(game, "Disc 1.bin"), "disc 1");
            File.WriteAllText(Path.Combine(game, "Disc 2.bin"), "disc 2");
            Directory.CreateDirectory(Path.Combine(game, "saves"));
            File.WriteAllText(Path.Combine(game, "saves", "ff7.srm"), "save");
            return game;
        }

        private static string[] Contents(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(folder, f) + "=" + File.ReadAllText(f))
                .OrderBy(s => s, StringComparer.Ordinal)
                .ToArray();

        // A delete on the source that stops after the given step
        private static void FailOn(TrashService trash, string source, Action partly)
        {
            var delete = trash.Delete;
            trash.Delete = path =>
            {
                if (path != source)
                {
                    delete(path);
                    return;
                }
                partly();
                throw new IOException("Device or resource busy");
            };
        }

        [Test]
        public async Task OtherVolume_FolderIsCopiedAndTheSourceRemoved()
        {
            var game = Game();
            var before = Contents(game);

            var entry = await OtherVolume().MoveAsync(game);

            Assert.That(Directory.Exists(game), Is.False);
            Assert.That(Contents(entry!.TrashPath), Is.EqualTo(before));
        }

        [Test]
        public void OtherVolume_DeleteStopsHalfway_TheSourceIsWholeAgain()
        {
            var game = Game();
            var before = Contents(game);
            var trash = OtherVolume();
            FailOn(trash, game, () => File.Delete(Path.Combine(game, "Disc 1.bin")));

            Assert.ThrowsAsync<IOException>(() => trash.MoveAsync(game));

            Assert.That(Contents(game), Is.EqualTo(before));
            Assert.That(Directory.GetFileSystemEntries(_trash), Is.Empty);
        }

        // The copy is all that is left of what the delete took, so it stays and is listed
        [Test]
        public void OtherVolume_SourceCannotBeMadeWhole_TheCopyStaysInTheTrash()
        {
            var game = Game();
            var before = Contents(game);
            var trash = OtherVolume();
            FailOn(trash, game, () =>
            {
                File.Delete(Path.Combine(game, "Disc 1.bin"));
                Directory.Delete(Path.Combine(game, "saves"), true);
                File.WriteAllText(Path.Combine(game, "saves"), "not a folder");
            });

            var ex = Assert.ThrowsAsync<TrashPartialMoveException>(() => trash.MoveAsync(game));

            var entry = trash.List().Single();
            Assert.That(entry.OriginalPath, Is.EqualTo(game));
            Assert.That(Contents(entry.TrashPath), Is.EqualTo(before));
            Assert.That(ex!.Entry.Id, Is.EqualTo(entry.Id));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void OtherVolume_NotEnoughSpace_NothingIsCopied(bool folder)
        {
            var game = Game();
            var source = folder ? game : Path.Combine(game, "Disc 1.bin");
            var before = Contents(game);
            var trash = OtherVolume();
            trash.FreeSpace = _ => 3;

            var ex = Assert.ThrowsAsync<IOException>(() => trash.MoveAsync(source));

            Assert.That(ex!.Message, Does.Contain("Not enough free space"));
            Assert.That(Contents(game), Is.EqualTo(before));
            Assert.That(Directory.GetFileSystemEntries(_trash), Is.Empty);
        }

        // A socket can't be copied
        [Test]
        [Platform(Exclude = "Win")]
        public void OtherVolume_CopyFails_NothingIsLeftInTheTrash()
        {
            var game = Game();
            using var socket = new System.Net.Sockets.Socket(System.Net.Sockets.AddressFamily.Unix, System.Net.Sockets.SocketType.Stream, System.Net.Sockets.ProtocolType.Unspecified);
            socket.Bind(new System.Net.Sockets.UnixDomainSocketEndPoint(Path.Combine(game, "saves", "emulator.sock")));

            Assert.CatchAsync<IOException>(() => OtherVolume().MoveAsync(game));

            Assert.That(File.ReadAllText(Path.Combine(game, "Disc 1.bin")), Is.EqualTo("disc 1"));
            Assert.That(Directory.GetFileSystemEntries(_trash), Is.Empty);
        }

        // On a volume that ignores case Data and data are one folder: a copy that meets a name already there stops
        // and the source stays, instead of merging the two and losing one when the source goes
        [Test]
        public void OtherVolume_NameThatIsTaken_FailsTheMoveAndKeepsTheSource()
        {
            var game = Game();
            var before = Contents(game);
            var trash = new TrashService(_config);
            trash.Rename = (_, to) =>
            {
                Directory.CreateDirectory(to);
                File.WriteAllText(Path.Combine(to, "Disc 1.bin"), "the other disc 1");
                throw new IOException("Invalid cross-device link");
            };

            Assert.ThrowsAsync<IOException>(() => trash.MoveAsync(game));

            Assert.That(Contents(game), Is.EqualTo(before));
            Assert.That(Directory.GetFileSystemEntries(_trash), Is.Empty);
        }

        // The sidecar is there before the payload moves, so a payload in the trash can always be listed and put back
        [Test]
        public async Task Sidecar_IsWrittenBeforeThePayloadMoves()
        {
            var game = Game();
            var trash = new TrashService(_config);
            var listed = false;
            trash.Rename = (from, to) =>
            {
                listed = File.Exists(Path.Combine(Path.GetDirectoryName(to)!, "meta.json"));
                Directory.Move(from, to);
            };

            var entry = await trash.MoveAsync(game);

            Assert.That(listed, Is.True);
            Assert.That(trash.List().Single().Id, Is.EqualTo(entry!.Id));
        }

        [Test]
        public void OtherVolume_FileThatCannotBeDeleted_LeavesNoCopyBehind()
        {
            var file = Path.Combine(Game(), "Disc 1.bin");
            var trash = OtherVolume();
            FailOn(trash, file, () => { });

            Assert.ThrowsAsync<IOException>(() => trash.MoveAsync(file));

            Assert.That(File.ReadAllText(file), Is.EqualTo("disc 1"));
            Assert.That(Directory.GetFileSystemEntries(_trash), Is.Empty);
        }

        // Links go as links: a restore brings them back and a link to a folder above can't loop
        [TestCase(false)]
        [TestCase(true)]
        [Platform(Exclude = "Win")]
        public async Task OtherVolume_LinksAreMovedAsLinks(bool loop)
        {
            var game = Game();
            var shared = Directory.CreateDirectory(Path.Combine(_root, "shared")).FullName;
            File.WriteAllText(Path.Combine(shared, "bios.bin"), new string('b', 1000));
            Directory.CreateSymbolicLink(Path.Combine(game, "bios"), shared);
            File.CreateSymbolicLink(Path.Combine(game, "disc.cue"), "Disc 1.bin");
            if (loop) Directory.CreateSymbolicLink(Path.Combine(game, "loop"), game);
            var trash = OtherVolume();
            // room for the game's own files only
            trash.FreeSpace = _ => 100;

            var entry = await trash.MoveAsync(game);

            Assert.That(new DirectoryInfo(Path.Combine(entry!.TrashPath, "bios")).LinkTarget, Is.EqualTo(shared));
            Assert.That(new FileInfo(Path.Combine(entry.TrashPath, "disc.cue")).LinkTarget, Is.EqualTo("Disc 1.bin"));
            Assert.That(trash.Restore(entry.Id), Is.True);
            Assert.That(new DirectoryInfo(Path.Combine(game, "bios")).LinkTarget, Is.EqualTo(shared));
            Assert.That(File.ReadAllText(Path.Combine(shared, "bios.bin")), Has.Length.EqualTo(1000));
        }

        // A game folder that is a link goes as the link, however big the folder it points at
        [Test]
        [Platform(Exclude = "Win")]
        public async Task OtherVolume_LinkedGameFolder_GoesAsTheLink()
        {
            var real = Game();
            var before = Contents(real);
            var link = Path.Combine(_root, "library", "FF7");
            Directory.CreateSymbolicLink(link, real);
            var trash = OtherVolume();
            trash.FreeSpace = _ => 3;

            var entry = await trash.MoveAsync(link);

            Assert.That(new DirectoryInfo(entry!.TrashPath).LinkTarget, Is.EqualTo(real));
            Assert.That(Directory.Exists(link), Is.False);
            Assert.That(Contents(real), Is.EqualTo(before));
        }

        // ---- Emptying the trash ----

        // A trash folder set to one that holds other things keeps them, even another program's meta.json
        [TestCase(true)]
        [TestCase(false)]
        public async Task EmptyingTheTrash_LeavesWhatIsNotAnEntry(bool all)
        {
            var entry = await new TrashService(_config).MoveAsync(Game());
            var other = Path.Combine(_trash, "Games", "Final Fantasy VII.bin");
            Directory.CreateDirectory(Path.GetDirectoryName(other)!);
            File.WriteAllText(other, "not in the trash");
            File.WriteAllText(Path.Combine(_trash, "Games", "meta.json"), "{\"TrashPath\": \"Final Fantasy VII.bin\"}");
            var trash = new TrashService(_config);

            var purged = all ? trash.PurgeAll() : trash.PurgeExpired();

            Assert.That(purged, Is.EqualTo(all ? 1 : 0));
            Assert.That(Directory.Exists(Path.GetDirectoryName(entry!.TrashPath)), Is.EqualTo(!all));
            Assert.That(File.Exists(other), Is.True);
        }

        // Emptied while the source of a move to another volume is being deleted: the copy is all there is of what the
        // delete took already, so its entry stays until the move is done
        [Test]
        public async Task EmptyingTheTrash_WhileAMoveIsUnderWay_KeepsItsEntry()
        {
            var game = Game();
            var before = Contents(game);
            var trash = OtherVolume();
            var purgedMeanwhile = -1;
            var delete = trash.Delete;
            trash.Delete = path =>
            {
                if (path == game) purgedMeanwhile = trash.PurgeAll();
                delete(path);
            };

            var entry = await trash.MoveAsync(game);

            Assert.That(purgedMeanwhile, Is.EqualTo(0));
            Assert.That(Directory.Exists(game), Is.False);
            Assert.That(Contents(entry!.TrashPath), Is.EqualTo(before));
            Assert.That(trash.PurgeAll(), Is.EqualTo(1));
        }

        // A move that stopped halfway leaves an entry that may hold the only copy of what the delete took: it is listed,
        // and stays whatever its age until the user puts it back or deletes it
        [Test]
        public void EntryOfAMoveThatStoppedHalfway_OnlyGoesByHand()
        {
            var game = Game();
            var trash = OtherVolume();
            FailOn(trash, game, () =>
            {
                File.Delete(Path.Combine(game, "Disc 1.bin"));
                Directory.Delete(Path.Combine(game, "saves"), true);
                File.WriteAllText(Path.Combine(game, "saves"), "not a folder");
            });
            var ex = Assert.ThrowsAsync<TrashPartialMoveException>(() => trash.MoveAsync(game));
            // long past the retention
            var sidecar = Path.Combine(Path.GetDirectoryName(trash.List().Single().TrashPath)!, "meta.json");
            var stored = System.Text.Json.JsonSerializer.Deserialize<TrashEntry>(File.ReadAllText(sidecar))!;
            stored.DeletedAt = new DateTime(2020, 1, 1);
            File.WriteAllText(sidecar, System.Text.Json.JsonSerializer.Serialize(stored));

            Assert.That(trash.PurgeExpired(), Is.EqualTo(0));
            Assert.That(trash.PurgeAll(), Is.EqualTo(0));
            Assert.That(trash.List().Single().Incomplete, Is.True);
            Assert.That(trash.PurgeOne(ex!.Entry.Id), Is.True);
            Assert.That(trash.List(), Is.Empty);
        }

        [Test]
        public async Task FileNamedLikeTheSidecar_IsKept()
        {
            var file = Path.Combine(Game(), "meta.json");
            File.WriteAllText(file, "the game's own");
            var trash = new TrashService(_config);

            var entry = await trash.MoveAsync(file);

            Assert.That(File.ReadAllText(entry!.TrashPath), Is.EqualTo("the game's own"));
            Assert.That(trash.Restore(entry.Id), Is.True);
            Assert.That(File.ReadAllText(file), Is.EqualTo("the game's own"));
        }

        // A sidecar that names no payload has nothing to restore, least of all its own folder
        [Test]
        public void EntryWithoutPayloadName_IsNotRestored()
        {
            var original = Path.Combine(_root, "library", "Spyro");
            var entryDir = Directory.CreateDirectory(Path.Combine(_trash, "20200101-000000_deadbeef")).FullName;
            File.WriteAllText(Path.Combine(entryDir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(new TrashEntry { Id = "20200101-000000_deadbeef", OriginalPath = original, TrashPath = "" }));

            Assert.That(new TrashService(_config).Restore("20200101-000000_deadbeef"), Is.False);
            Assert.That(Directory.Exists(original), Is.False);
            Assert.That(Directory.Exists(entryDir), Is.True);
        }

        // A sidecar only speaks for its own entry folder, whatever path it names
        [Test]
        public void ExpiredEntry_OnlyItsOwnFolderIsPurged()
        {
            var victim = Directory.CreateDirectory(Path.Combine(_root, "library", "Crash Bandicoot")).FullName;
            File.WriteAllText(Path.Combine(victim, "Crash Bandicoot.bin"), "crash");
            var entryDir = Directory.CreateDirectory(Path.Combine(_trash, "20200101-000000_deadbeef")).FullName;
            File.WriteAllText(Path.Combine(entryDir, "meta.json"), System.Text.Json.JsonSerializer.Serialize(new TrashEntry
            {
                Id = "20200101-000000_deadbeef",
                OriginalPath = Path.Combine(_root, "library", "Spyro"),
                TrashPath = Path.Combine(victim, "Spyro"),
                DeletedAt = new DateTime(2020, 1, 1),
            }));

            var purged = new TrashService(_config).PurgeExpired();

            Assert.That(purged, Is.EqualTo(1));
            Assert.That(Directory.Exists(entryDir), Is.False);
            Assert.That(File.Exists(Path.Combine(victim, "Crash Bandicoot.bin")), Is.True);
        }

        // ---- The trash folder setting ----

        private MediaController Settings()
        {
            _config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library"), DownloadPath = Path.Combine(_root, "downloads"), TrashPath = _trash });
            return new MediaController(_config, null!, null!, null!, null!);
        }

        [TestCase("{root}/library")]
        [TestCase("{root}/library/.trash")]
        [TestCase("{root}")]
        [TestCase("{root}/downloads/trash")]
        [TestCase("/etc", ExcludePlatform = "Win")]
        [TestCase("trash")]
        public void TrashFolder_InOrAroundTheLibraryOrDownloads_IsRefused(string folder)
        {
            var controller = Settings();

            var result = controller.SaveSettings(JObject.FromObject(new { TrashPath = folder.Replace("{root}", _root) }));

            Assert.That(result, Is.InstanceOf<BadRequestObjectResult>());
            Assert.That(_config.LoadMediaSettings().TrashPath, Is.EqualTo(_trash));
        }

        [Test]
        public void TrashFolder_BesideTheLibrary_IsSaved()
        {
            var controller = Settings();
            var beside = Path.Combine(_root, "media-trash");

            var result = controller.SaveSettings(JObject.FromObject(new { TrashPath = beside }));

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(_config.LoadMediaSettings().TrashPath, Is.EqualTo(beside));
        }

        // A trash folder saved before the check doesn't block saving other settings
        [Test]
        public void OlderTrashFolderInTheLibrary_OtherSettingsStillSave()
        {
            var inside = Path.Combine(_root, "library", ".trash");
            _config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library"), TrashPath = inside });

            var result = new MediaController(_config, null!, null!, null!, null!).SaveSettings(JObject.Parse("{\"renameOnImport\": true}"));

            Assert.That(result, Is.InstanceOf<OkObjectResult>());
            Assert.That(_config.LoadMediaSettings().RenameOnImport, Is.True);
        }
    }
}
