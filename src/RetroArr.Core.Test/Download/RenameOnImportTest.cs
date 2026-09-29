using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Games;
using RetroArr.Core.IO;
using RetroArr.Core.Rename;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class RenameOnImportTest
    {
        private string _root = null!;
        private DbContextOptions<RetroArrDbContext> _db = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_rename_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        private string Library => Path.Combine(_root, "library");

        private static MediaSettings Rename(string scope, string template = "{Title}", string conflict = "Skip") =>
            new MediaSettings { RenameOnImport = true, ApplyRenameToPlatforms = scope, MainFileTemplate = template, FileConflictBehavior = conflict };

        // A game on the platform with its folder in the library; returns the processor and the game folder
        private async Task<(PostDownloadProcessor Processor, string Folder)> SetUpAsync(string title, int platformId, MediaSettings? settings = null,
            IFileMoverService? mover = null, Action<Game>? seed = null, bool torrent = false)
        {
            var config = new ConfigurationService(_root);
            var platform = PlatformDefinitions.AllPlatforms.Single(p => p.Id == platformId);
            var folder = Directory.CreateDirectory(Path.Combine(Library, platform.FolderName, title)).FullName;
            settings ??= new MediaSettings();
            settings.FolderPath = Library;
            settings.DestinationPath = Library;
            config.SaveMediaSettings(settings);
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = false, EnableDeepClean = false });
            if (torrent) config.SaveDownloadClients(new List<DownloadClient> { new DownloadClient { Id = 1, Name = "client", Implementation = "qBittorrent" } });

            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using (var ctx = new RetroArrDbContext(_db))
            {
                var game = new Game { Id = 1, Title = title, PlatformId = platformId, Path = folder };
                seed?.Invoke(game);
                ctx.Games.Add(game);
                await ctx.SaveChangesAsync();
            }

            var processor = new PostDownloadProcessor(config, mover ?? new FileMoverService(),
                new SqliteGameRepository(new DbFactory(_db)), null!, new ArchiveService(), new TitleCleanerService(),
                new FileRenamer(new TemplateRenderer()));
            return (processor, folder);
        }

        private Game GameRow(int id = 1)
        {
            using var ctx = new RetroArrDbContext(_db);
            return ctx.Games.Single(g => g.Id == id);
        }

        private string Downloads => Directory.CreateDirectory(Path.Combine(_root, "downloads")).FullName;

        private Task<PostDownloadResult> ImportFileAsync(PostDownloadProcessor processor, string fileName, string content,
            string? release = null, string? platformFolder = null)
        {
            var file = Path.Combine(Downloads, fileName);
            File.WriteAllText(file, content);
            return processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = fileName, Name = release ?? Path.GetFileNameWithoutExtension(fileName), DownloadPath = file, GameId = 1,
                PlatformFolder = platformFolder, State = DownloadState.Completed
            });
        }

        private string MakeRelease(string release, params (string Name, byte[] Content)[] files)
        {
            var folder = Directory.CreateDirectory(Path.Combine(Downloads, release)).FullName;
            foreach (var (name, content) in files)
            {
                var path = Path.Combine(folder, name);
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);
                File.WriteAllBytes(path, content);
            }
            return folder;
        }

        private Task<PostDownloadResult> ImportFolderAsync(PostDownloadProcessor processor, string release, params (string Name, string Content)[] files) =>
            ImportFolderAsync(processor, release, false, null, files.Select(f => (f.Name, Encoding.UTF8.GetBytes(f.Content))).ToArray());

        private Task<PostDownloadResult> ImportFolderAsync(PostDownloadProcessor processor, string release, bool torrent, string? subfolder, params (string Name, byte[] Content)[] files)
        {
            var folder = MakeRelease(release, files);
            return processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = release, ClientId = torrent ? 1 : 0, Name = release, DownloadPath = folder, GameId = 1,
                ImportSubfolder = subfolder, State = DownloadState.Completed
            });
        }

        private static string[] Names(string folder) =>
            Directory.GetFiles(folder, "*", SearchOption.AllDirectories).Select(f => Path.GetRelativePath(folder, f).Replace('\\', '/')).OrderBy(n => n, StringComparer.Ordinal).ToArray();

        // Track data is bigger than its descriptor, so the descriptor has to win ExecutablePath on rank
        private static string Big(string tag) => tag + new string('.', 4096);

        private static string Cue(params string[] bins) =>
            string.Concat(bins.Select((b, i) => $"FILE \"{b}\" BINARY\n  TRACK {i + 1:00} MODE2/2352\n    INDEX 01 00:00:00\n"));

        // ── Single files ────────────────────────────────────────────────

        [TestCase("Skip")]
        [TestCase("Suffix")]
        [TestCase("Overwrite")]
        public async Task Disc1ThenDisc2_KeepTheirDiscTokens(string conflict)
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1", conflict: conflict));

            var one = await ImportFileAsync(processor, "Final Fantasy VII (USA) (Disc 1).chd", "disc1");
            var two = await ImportFileAsync(processor, "Final Fantasy VII (USA) (Disc 2).chd", "disc2");

            Assert.That(one.Success && two.Success, Is.True, one.Reason + two.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Final Fantasy VII (Disc 1).chd", "Final Fantasy VII (Disc 2).chd" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII (Disc 1).chd")), Is.EqualTo("disc1"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII (Disc 2).chd")), Is.EqualTo("disc2"));
        }

        [Test]
        public async Task OtherRegion_LabelledFromTheFile_NotFromTheGame()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba", "{Title} ({Region})"), seed: g => g.Region = "USA");

            await ImportFileAsync(processor, "Advance Wars (USA).gba", "usa");
            await ImportFileAsync(processor, "Advance Wars (Europe).gba", "eur");

            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars (Europe).gba", "Advance Wars (USA).gba" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars (USA).gba")), Is.EqualTo("usa"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars (Europe).gba")), Is.EqualTo("eur"));
        }

        [Test]
        public async Task EmptyTokens_LeaveNoEmptyBrackets()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba", "{Title} ({Region}) [{Languages}]"));

            await ImportFileAsync(processor, "aw.gba", "rom");

            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars.gba" }));
        }

        [Test]
        public async Task IdenticalReimport_IsAlreadyThere()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba", "{Title} ({Region})"));
            await ImportFileAsync(processor, "Advance Wars (USA).gba", "usa");
            var file = Path.Combine(folder, "Advance Wars (USA).gba");
            var stamp = new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            File.SetLastWriteTimeUtc(file, stamp);

            var again = await ImportFileAsync(processor, "Advance Wars (USA).gba", "usa");

            Assert.That(again.Success, Is.True, again.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars (USA).gba" }));
            Assert.That(File.GetLastWriteTimeUtc(file), Is.EqualTo(stamp), "library file was replaced");
            Assert.That(File.Exists(Path.Combine(Downloads, "Advance Wars (USA).gba")), Is.False, "source was kept");
        }

        [TestCase("Skip")]
        [TestCase("Suffix")]
        [TestCase("Overwrite")]
        public async Task SameNameOtherContent(string conflict)
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba", conflict: conflict));
            await ImportFileAsync(processor, "Advance Wars.gba", "one");

            var second = await ImportFileAsync(processor, "Advance Wars.gba", "two");

            Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars.gba")), Is.EqualTo("one"));
            if (conflict != "Suffix")
            {
                Assert.That(second.Success, Is.False);
                Assert.That(second.Reason, Does.Contain("not overwriting"));
                Assert.That(File.Exists(Path.Combine(Downloads, "Advance Wars.gba")), Is.True, "source was removed");
                Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars.gba" }));
            }
            else
            {
                Assert.That(second.Success, Is.True, second.Reason);
                Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars (1).gba")), Is.EqualTo("two"));
            }
        }

        [Test]
        public async Task Suffix_TagAlreadyInTheName_IsNotRepeated()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba", "{Title} ({Region})", "Suffix"));

            await ImportFileAsync(processor, "Advance Wars (USA).gba", "one");
            var second = await ImportFileAsync(processor, "Advance Wars (USA).gba", "two");

            Assert.That(second.Success, Is.True, second.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars (USA) (1).gba", "Advance Wars (USA).gba" }));
        }

        [TestCase("Super Mario World", "Super Mario World.gba", "Super Mario World", "Super Mario World.gba")]
        [TestCase("Advance Wars", "aw.gba", "Advance Wars (Europe)", "Advance Wars (Europe).gba")]
        public async Task Region_FromBracketTags_OfTheFileElseTheRelease(string title, string file, string release, string expected)
        {
            var (processor, folder) = await SetUpAsync(title, 52, Rename("gba", "{Title} ({Region})"));

            await ImportFileAsync(processor, file, "rom", release);

            Assert.That(Names(folder), Is.EqualTo(new[] { expected }));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task TwoUnversionedPatches_BothKept(bool rename)
        {
            var (processor, folder) = await SetUpAsync("Hades", 1, rename ? Rename("pc") : null);

            var a = await ImportFileAsync(processor, "Hades Update.zip", "patchA");
            var b = await ImportFileAsync(processor, "Hades Hotfix.zip", "patchB");

            Assert.That(a.Success && b.Success, Is.True, a.Reason + b.Reason);
            var patches = Directory.GetFiles(Path.Combine(folder, "Patches")).Select(File.ReadAllText);
            Assert.That(patches, Is.EquivalentTo(new[] { "patchA", "patchB" }));
        }

        [TestCase("Hades.Update.v1.2-RUNE", "Patches/Hades - Update 1.2 [RUNE].zip")]
        [TestCase("Hades DLC Soundtrack", "DLC/Hades - DLC - Soundtrack.zip")]
        public async Task PatchAndDlc_NamedByTheirTemplates(string release, string expected)
        {
            var settings = Rename("pc");
            settings.IncludeReleaseGroupInFilename = true;
            var (processor, folder) = await SetUpAsync("Hades", 1, settings);

            var result = await ImportFolderAsync(processor, release, ("hades.zip", "content"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { expected }));
        }

        [TestCase(100, true)]
        [TestCase(100, false)]
        [TestCase(68, true)]
        [TestCase(68, false)]
        [TestCase(72, true)]
        [TestCase(72, false)]
        [TestCase(76, true)]
        [TestCase(76, false)]
        public async Task ArcadeRomset_KeepsItsName(int platformId, bool rename)
        {
            var (processor, folder) = await SetUpAsync("Metal Slug", platformId, rename ? Rename("arcade,naomi,chihiro,hikaru") : null);

            var result = await ImportFileAsync(processor, "mslug.zip", "set");

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "mslug.zip" }));
        }

        [TestCase(52, "Advance Wars", "Advance Wars (USA).gba", "Advance Wars.gba")]
        [TestCase(20, "Final Fantasy IX", "Final Fantasy IX (USA) (Disc 2).chd", "Final Fantasy IX (Disc 2).chd")]
        public async Task RenameOff_NamesAsBefore(int platformId, string title, string file, string expected)
        {
            var settings = new MediaSettings { RenameOnImport = false, ApplyRenameToPlatforms = "gba,ps1", MainFileTemplate = "{Title} ({Region})" };
            var (processor, folder) = await SetUpAsync(title, platformId, settings);

            await ImportFileAsync(processor, file, "rom");

            Assert.That(Names(folder), Is.EqualTo(new[] { expected }));
        }

        [Test]
        public async Task RenameOn_PlatformOutOfScope_NamesAsBefore()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("pc", "{Title} ({Region})"));

            await ImportFileAsync(processor, "aw (USA).gba", "rom");

            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars.gba" }));
        }

        [Test]
        public async Task DiscToken_InTheTemplate_IsNotRepeated()
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1", "{Title} ({Disc})"));

            await ImportFileAsync(processor, "Final Fantasy VII (USA) (Disc 1).chd", "disc1");

            Assert.That(Names(folder), Is.EqualTo(new[] { "Final Fantasy VII (Disc 1).chd" }));
        }

        [Test]
        public async Task ReleaseGroupSuffix_OnImport()
        {
            var settings = Rename("pc");
            settings.IncludeReleaseGroupInFilename = true;
            var (processor, folder) = await SetUpAsync("Hades", 1, settings);

            var result = await ImportFolderAsync(processor, "Hades [FitGirl Repack]", ("hades.iso", "iso"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Hades [FitGirl].iso" }));
        }

        [Test]
        public async Task CaseOnlyDifference_NeverReplaces()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52);
            File.WriteAllText(Path.Combine(folder, "advance wars.gba"), "old");

            var result = await ImportFileAsync(processor, "Advance Wars (USA).gba", "new");

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Directory.GetFiles(folder).Select(File.ReadAllText), Is.EquivalentTo(new[] { "old", "new" }));
        }

        [TestCase(GameStatus.Missing, GameStatus.Downloaded)]
        [TestCase(GameStatus.Downloaded, GameStatus.Downloaded)]
        [TestCase(GameStatus.InstallerDetected, GameStatus.InstallerDetected)]
        public async Task MainImport_ClearsMissingSince(GameStatus before, GameStatus after)
        {
            var (processor, _) = await SetUpAsync("Advance Wars", 52, seed: g =>
            {
                g.Status = before;
                g.MissingSince = DateTime.UtcNow.AddDays(-3);
            });

            var result = await ImportFileAsync(processor, "Advance Wars (USA).gba", "rom");

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(GameRow().Status, Is.EqualTo(after));
            Assert.That(GameRow().MissingSince, Is.Null);
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task ExtrasOnly_AreNotTheGame(bool folderRelease)
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, seed: g => g.Status = GameStatus.Missing);

            var result = folderRelease
                ? await ImportFolderAsync(processor, "AW Manual", ("manual.pdf", "pdf"), ("readme.txt", "r"))
                : await ImportFileAsync(processor, "manual.pdf", "pdf");

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Directory.GetFiles(folder), Is.Not.Empty);
            Assert.That(GameRow().ExecutablePath, Is.Null);
            Assert.That(GameRow().Status, Is.EqualTo(GameStatus.Missing));
        }

        // "worse" points at a file of the release that now lands with its cue, "other" at a file that is not part of the import
        [TestCase("missing")]
        [TestCase("nfo")]
        [TestCase("worse")]
        [TestCase("other")]
        [TestCase("equal")]
        public async Task ExecutablePath_Repointing(string before)
        {
            string? current = null;
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1"), seed: g =>
            {
                var dir = g.Path!;
                current = before switch
                {
                    "missing" => Path.Combine(dir, "gone.chd"),
                    "nfo" => Path.Combine(dir, "info.nfo"),
                    "worse" => Path.Combine(dir, "Final Fantasy VII.bin"),
                    _ => Path.Combine(dir, "old.chd")
                };
                if (before != "missing") File.WriteAllText(current, "x");
                g.ExecutablePath = current;
            });

            var result = before switch
            {
                "worse" => await ImportFolderAsync(processor, "ff7", ("ff7.cue", Cue("ff7.bin")), ("ff7.bin", "x")),
                "other" => await ImportFileAsync(processor, "ff7.cue", "FILE \"ff7.bin\" BINARY"),
                _ => await ImportFileAsync(processor, "ff7.chd", "rom")
            };

            Assert.That(result.Success, Is.True, result.Reason);
            var expected = before switch
            {
                "worse" => Path.Combine(folder, "Final Fantasy VII.cue"),
                "other" or "equal" => current,
                _ => Path.Combine(folder, "Final Fantasy VII.chd")
            };
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(expected));
        }

        [Test]
        public async Task InstalledGame_KeepsItsExe_WhenAnInstallerIsImported()
        {
            string? exe = null;
            var (processor, _) = await SetUpAsync("Hades", 1, seed: g =>
            {
                exe = Path.Combine(g.Path!, "Hades.exe");
                File.WriteAllText(exe, "game");
                g.ExecutablePath = exe;
                g.Status = GameStatus.Downloaded;
            });

            var result = await ImportFileAsync(processor, "setup_hades_1.38_(64bit)_(48493).exe", "installer", "Hades v1.38 GOG");

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(exe));
        }

        private sealed class NoMetadata : RetroArr.Core.MetadataSource.IGameMetadataServiceFactory
        {
            public RetroArr.Core.MetadataSource.GameMetadataService CreateService() => null!;
            public void RefreshConfiguration() { }
        }

        // "other" and "same" are separate downloads of another or the same file, "itself" is the library file
        // and "folder" the library game folder
        [TestCase("other")]
        [TestCase("same")]
        [TestCase("itself")]
        [TestCase("folder")]
        public async Task GenericImport_NeverReplacesOrDeletesALibraryFile(string download)
        {
            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = Library, DestinationPath = Library });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = false, EnableDeepClean = false });
            var existing = Path.Combine(Directory.CreateDirectory(Path.Combine(Library, "gba", "Advance Wars")).FullName, "Advance Wars.gba");
            File.WriteAllText(existing, "old");
            var source = Path.Combine(Downloads, "Advance Wars.gba");
            File.WriteAllText(source, download == "other" ? "new" : "old");
            _db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            var processor = new PostDownloadProcessor(config, new FileMoverService(),
                new SqliteGameRepository(new DbFactory(_db)), new NoMetadata(), new ArchiveService(), new TitleCleanerService(), new FileRenamer(new TemplateRenderer()));

            var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "x", Name = "Advance Wars", PlatformFolder = "gba", State = DownloadState.Completed,
                DownloadPath = download switch { "itself" => existing, "folder" => Path.GetDirectoryName(existing), _ => source }
            });

            Assert.That(File.ReadAllText(existing), Is.EqualTo("old"));
            Assert.That(Directory.GetFiles(Path.GetDirectoryName(existing)!), Has.Length.EqualTo(1));
            switch (download)
            {
                case "other":
                    Assert.That(result.Success, Is.False);
                    Assert.That(File.ReadAllText(source), Is.EqualTo("new"), "source was removed");
                    break;
                case "same":
                    Assert.That(result.Success, Is.True, result.Reason);
                    Assert.That(File.Exists(source), Is.False, "source was kept");
                    break;
                default:
                    Assert.That(result.Success, Is.True, result.Reason);
                    break;
            }
        }

        // ── Multi-file releases ─────────────────────────────────────────

        [Test]
        public async Task PcRepack_KeepsItsLayout()
        {
            var (processor, folder) = await SetUpAsync("Hades", 1, Rename("windows,pc,linux,macintosh"));

            var result = await ImportFolderAsync(processor, "Hades [FitGirl Repack]",
                ("setup.exe", "exe"), ("setup-fitgirl-01.bin", "one"), ("setup-fitgirl-02.bin", "two"), ("MD5/fitgirl-bins.md5", "md5"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "MD5/fitgirl-bins.md5", "setup-fitgirl-01.bin", "setup-fitgirl-02.bin", "setup.exe" }));
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(Path.Combine(folder, "setup.exe")));
        }

        [TestCase("crlf")]
        [TestCase("bom")]
        public async Task CueBinTracks_RenamedAsSet(string kind)
        {
            var (processor, folder) = await SetUpAsync("Wipeout", 20, Rename("ps1"), torrent: true);
            var text = "REM GENRE \"Caf\u00e9\"\nFILE \"Wipeout (Europe) (Track 1).bin\" BINARY\n  TRACK 01 MODE1/2352\n    INDEX 01 00:00:00\n"
                     + "FILE \"Wipeout (Europe) (Track 2).bin\" BINARY\n  TRACK 02 AUDIO\n    INDEX 01 00:00:00\n";
            // one cue in Latin1 with CRLF (the é is a lone 0xE9 byte), one in UTF-8 with a BOM
            var cue = kind == "crlf"
                ? Encoding.Latin1.GetBytes(text.Replace("\n", "\r\n"))
                : Encoding.UTF8.GetPreamble().Concat(Encoding.UTF8.GetBytes(text)).ToArray();

            var result = await ImportFolderAsync(processor, "Wipeout (Europe)", true, null,
                ("Wipeout (Europe).cue", cue), ("Wipeout (Europe) (Track 1).bin", Encoding.ASCII.GetBytes(Big("t1"))), ("Wipeout (Europe) (Track 2).bin", Encoding.ASCII.GetBytes(Big("t2"))));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Wipeout (Track 01).bin", "Wipeout (Track 02).bin", "Wipeout.cue" }));
            var expected = Encoding.Latin1.GetBytes(Encoding.Latin1.GetString(cue)
                .Replace("Wipeout (Europe) (Track 1).bin", "Wipeout (Track 01).bin")
                .Replace("Wipeout (Europe) (Track 2).bin", "Wipeout (Track 02).bin"));
            Assert.That(File.ReadAllBytes(Path.Combine(folder, "Wipeout.cue")), Is.EqualTo(expected));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout (Track 02).bin")), Is.EqualTo(Big("t2")));
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(Path.Combine(folder, "Wipeout.cue")));
            Assert.That(File.ReadAllBytes(Path.Combine(Downloads, "Wipeout (Europe)", "Wipeout (Europe).cue")), Is.EqualTo(cue), "the seeded cue was changed");
        }

        [Test]
        public async Task SingleTrackCue_BinGetsTheCueStem()
        {
            var (processor, folder) = await SetUpAsync("Crash Bandicoot", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "Crash (USA)",
                ("Crash (USA).cue", Cue("Crash (USA).bin")), ("Crash (USA).bin", "bin"), ("Crash (USA).nfo", "nfo"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Crash Bandicoot.bin", "Crash Bandicoot.cue", "Crash Bandicoot.nfo" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Crash Bandicoot.cue")), Is.EqualTo(Cue("Crash Bandicoot.bin")));
        }

        [Test]
        public async Task M3uWithCues_RenamedAndRewritten()
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "FF7 (USA)",
                ("FF7 (USA).m3u", "#EXTM3U\n# discs\nFF7 (USA) (Disc 1).cue\nFF7 (USA) (Disc 2).cue\n"),
                ("FF7 (USA) (Disc 1).cue", Cue("FF7 (USA) (Disc 1).bin")), ("FF7 (USA) (Disc 1).bin", Big("d1")),
                ("FF7 (USA) (Disc 2).cue", Cue("FF7 (USA) (Disc 2).bin")), ("FF7 (USA) (Disc 2).bin", Big("d2")));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[]
            {
                "Final Fantasy VII (Disc 1).bin", "Final Fantasy VII (Disc 1).cue", "Final Fantasy VII (Disc 2).bin",
                "Final Fantasy VII (Disc 2).cue", "Final Fantasy VII.m3u"
            }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII.m3u")),
                Is.EqualTo("#EXTM3U\n# discs\nFinal Fantasy VII (Disc 1).cue\nFinal Fantasy VII (Disc 2).cue\n"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII (Disc 2).cue")), Is.EqualTo(Cue("Final Fantasy VII (Disc 2).bin")));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII (Disc 2).bin")), Is.EqualTo(Big("d2")));
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(Path.Combine(folder, "Final Fantasy VII.m3u")));
        }

        [Test]
        public async Task M3uEntriesWithoutDiscTokens_GetDiscIndex()
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "ff7", ("ff7.m3u", "a.chd\r\n  b.chd  \r\n"), ("a.chd", "a"), ("b.chd", "b"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Final Fantasy VII (Disc 1).chd", "Final Fantasy VII (Disc 2).chd", "Final Fantasy VII.m3u" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII.m3u")), Is.EqualTo("Final Fantasy VII (Disc 1).chd\r\n  Final Fantasy VII (Disc 2).chd  \r\n"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII (Disc 2).chd")), Is.EqualTo("b"));
        }

        [Test]
        public async Task Gdi_RenamedAsSet()
        {
            var (processor, folder) = await SetUpAsync("Shenmue", 67, Rename("dreamcast"));

            var result = await ImportFolderAsync(processor, "Shenmue (USA)",
                ("Shenmue (USA).gdi", "3\r\n1 0 4 2352 \"Shenmue (USA) (Track 1).bin\" 0\r\n2 756 0 2352 track02.raw 0\r\n3 45000 4 2352 track03.bin 0\r\n"),
                ("Shenmue (USA) (Track 1).bin", Big("t1")), ("track02.raw", Big("t2")), ("track03.bin", Big("t3")));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Shenmue (Track 01).bin", "Shenmue (Track 02).raw", "Shenmue (Track 03).bin", "Shenmue.gdi" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Shenmue.gdi")),
                Is.EqualTo("3\r\n1 0 4 2352 \"Shenmue (Track 01).bin\" 0\r\n2 756 0 2352 \"Shenmue (Track 02).raw\" 0\r\n3 45000 4 2352 \"Shenmue (Track 03).bin\" 0\r\n"));
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(Path.Combine(folder, "Shenmue.gdi")));
        }

        [TestCase("x.cue", "FILE \"missing.bin\" BINARY\n", "x.bin")]
        [TestCase("x.cue", "FILE \"x.bin\"\n", "x.bin")]
        [TestCase("x.cue", "FILE x y.bin BINARY\n", "x y.bin")]
        [TestCase("x.gdi", "2\n1 0 4 2352 x.bin 0\nnot a track\n", "x.bin")]
        public async Task UnparsableSet_KeepsNames(string descriptor, string text, string data)
        {
            var (processor, folder) = await SetUpAsync("Wipeout", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "Wipeout (Europe)", (descriptor, text), (data, "data"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EquivalentTo(new[] { descriptor, data }));
            Assert.That(File.ReadAllText(Path.Combine(folder, descriptor)), Is.EqualTo(text));
        }

        [Test]
        public async Task TwoCuesNamingOneBin_KeepNames()
        {
            var (processor, folder) = await SetUpAsync("Wipeout", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "Wipeout", ("a (Disc 1).cue", Cue("x.bin")), ("b (Disc 2).cue", Cue("x.bin")), ("x.bin", "x"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "a (Disc 1).cue", "b (Disc 2).cue", "x.bin" }));
        }

        [TestCase(20, "Game (Disc 1).chd", "Extra/Game (Disc 2).chd")]
        [TestCase(52, "Sub/AW (USA).gba", "readme.txt")]
        [TestCase(20, "game.7z.001", "game.7z.002")]
        [TestCase(52, "AW (USA).gba", "AW (Europe).gba")]
        [TestCase(20, "x.cue", "x.bin", "x.rar", "x.r00")]
        public async Task SubfolderVolumeOrFlatRomRelease_KeepsNames(int platformId, params string[] files)
        {
            var (processor, folder) = await SetUpAsync("Some Game", platformId, Rename("ps1,gba"));

            var result = await ImportFolderAsync(processor, "Some.Game-GRP", files.Select(f => (f, f.EndsWith(".cue") ? Cue("x.bin") : f)).ToArray());

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EquivalentTo(files));
        }

        [TestCase(true)]
        [TestCase(false)]
        public async Task SingleMainFilePlusExtras_IsRenamed(bool rename)
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, rename ? Rename("gba") : null);

            var result = await ImportFolderAsync(processor, "AW (USA)", ("AW (USA).gba", "rom"), ("readme.txt", "r"), ("AW (USA).nfo", "nfo"), ("README", "r2"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EquivalentTo(rename
                ? new[] { "Advance Wars.gba", "readme.txt", "Advance Wars.nfo", "README" }
                : new[] { "AW (USA).gba", "readme.txt", "AW (USA).nfo", "README" }));
        }

        [Test]
        public async Task DownloadPathWithTrailingSeparator_IsRenamed()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba"));
            var release = MakeRelease("AW (USA)", ("AW (USA).gba", Encoding.ASCII.GetBytes("rom")), ("readme.txt", Encoding.ASCII.GetBytes("r")));

            var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "x", Name = "AW (USA)", DownloadPath = release + Path.DirectorySeparatorChar, GameId = 1, State = DownloadState.Completed
            });

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars.gba", "readme.txt" }));
        }

        [Test]
        public async Task TwoRegionDiscImagesInOneRelease_GetTheirTags()
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "FF7", ("FF7 (USA).chd", "usa"), ("FF7 (Europe).chd", "eur"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EquivalentTo(new[] { "Final Fantasy VII (USA).chd", "Final Fantasy VII (Europe).chd" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Final Fantasy VII (Europe).chd")), Is.EqualTo("eur"));
        }

        [Test]
        public async Task TwoDiscImages_PrimaryIsDiscOne()
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1"));

            var result = await ImportFolderAsync(processor, "FF7", ("FF7 (Disc 1).chd", "d1"), ("FF7 (Disc 2).chd", "disc two is bigger"), ("README", "r"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Final Fantasy VII (Disc 1).chd", "Final Fantasy VII (Disc 2).chd", "README" }));
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(Path.Combine(folder, "Final Fantasy VII (Disc 1).chd")));
        }

        [TestCase(false)]
        [TestCase(true)]
        public async Task IdenticalDiscSetReimport_IsAlreadyThere(bool binDeleted)
        {
            var (processor, folder) = await SetUpAsync("Wipeout", 20, Rename("ps1"));
            (string, string)[] Set() => new[] { ("Wipeout (Europe).cue", Cue("Wipeout (Europe).bin")), ("Wipeout (Europe).bin", "eur") };
            await ImportFolderAsync(processor, "Wipeout (Europe)", Set());
            if (binDeleted) File.Delete(Path.Combine(folder, "Wipeout.bin"));

            var again = await ImportFolderAsync(processor, "Wipeout (Europe)", Set());

            Assert.That(again.Success, Is.True, again.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Wipeout.bin", "Wipeout.cue" }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout.bin")), Is.EqualTo("eur"));
        }

        [Test]
        public async Task SetCollidesWithLibrary()
        {
            var (processor, folder) = await SetUpAsync("Wipeout", 20, Rename("ps1"));

            var usa = await ImportFolderAsync(processor, "Wipeout (USA)", ("Wipeout (USA).cue", Cue("Wipeout (USA).bin")), ("Wipeout (USA).bin", "usa"));
            var eur = await ImportFolderAsync(processor, "WO (Europe)", ("WO (Europe).cue", Cue("WO (Europe).bin")), ("WO (Europe).bin", "eur"));
            var third = await ImportFolderAsync(processor, "Wipeout (Europe) v2", ("Wipeout (Europe).cue", Cue("Wipeout (Europe).bin")), ("Wipeout (Europe).bin", "eur2"));

            Assert.That(usa.Success && eur.Success && third.Success, Is.True, usa.Reason + eur.Reason + third.Reason);
            Assert.That(Names(folder), Is.EquivalentTo(new[]
            {
                "Wipeout.cue", "Wipeout.bin", "Wipeout (Europe).cue", "Wipeout (Europe).bin",
                "Wipeout (Europe) v2/Wipeout (Europe).cue", "Wipeout (Europe) v2/Wipeout (Europe).bin"
            }));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout.bin")), Is.EqualTo("usa"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout.cue")), Is.EqualTo(Cue("Wipeout.bin")));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout (Europe).bin")), Is.EqualTo("eur"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout (Europe).cue")), Is.EqualTo(Cue("Wipeout (Europe).bin")));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Wipeout (Europe) v2", "Wipeout (Europe).bin")), Is.EqualTo("eur2"));
        }

        // Fails the given call to ImportFile
        private sealed class FailingMover : IFileMoverService
        {
            private readonly FileMoverService _inner = new();
            private readonly int _failOn;
            private int _calls;
            public FailingMover(int failOn) => _failOn = failOn;
            public bool ImportFile(string sourceFile, string destinationFile) => ImportFile(sourceFile, destinationFile, out _);
            public bool ImportFile(string sourceFile, string destinationFile, out string? failureReason)
            {
                if (++_calls == _failOn)
                {
                    failureReason = "disk full";
                    return false;
                }
                return _inner.ImportFile(sourceFile, destinationFile, out failureReason);
            }
        }

        // Track 1 lands first. When it is in the library already, the mover fails on its first call, track 2.
        [TestCase(false)]
        [TestCase(true)]
        public async Task SetLandingFailure_RollsBack(bool trackOneThere)
        {
            var (processor, folder) = await SetUpAsync("Wipeout", 20, Rename("ps1"), new FailingMover(trackOneThere ? 1 : 2));
            File.WriteAllText(Path.Combine(folder, "notes.txt"), "mine");
            if (trackOneThere) File.WriteAllText(Path.Combine(folder, "Wipeout (Track 01).bin"), "t1");
            var before = Names(folder);

            var result = await ImportFolderAsync(processor, "Wipeout (Europe)",
                ("Wipeout (Europe).cue", Cue("Wipeout (Europe) (Track 1).bin", "Wipeout (Europe) (Track 2).bin")),
                ("Wipeout (Europe) (Track 1).bin", "t1"), ("Wipeout (Europe) (Track 2).bin", "t2"));

            Assert.That(result.Success, Is.False);
            Assert.That(result.Reason, Does.Contain("disk full"));
            Assert.That(Names(folder), Is.EqualTo(before));
            Assert.That(Names(Path.Combine(Downloads, "Wipeout (Europe)")), Has.Length.EqualTo(3), "source was touched");
        }

        [TestCase(null)]
        [TestCase("Patches")]
        public async Task SecondMultiFileRelease_SameInnerNames_LandsInReleaseFolder(string? subfolder)
        {
            var (processor, folder) = await SetUpAsync("Hades", 1);
            var target = subfolder == null ? folder : Path.Combine(folder, subfolder);

            Task<PostDownloadResult> Import(string release, string exe, string bin) =>
                ImportFolderAsync(processor, release, false, subfolder, ("setup.exe", Encoding.ASCII.GetBytes(exe)), ("setup-1.bin", Encoding.ASCII.GetBytes(bin)));

            var a = await Import("Hades.v1.0-GRP", "a1", "a2");
            var b = await Import("Hades.v1.1-GRP", "b1", "b2");
            Assert.That(Directory.Exists(Path.Combine(Downloads, "Hades.v1.1-GRP")), Is.False, "usenet source was kept");
            // the same release again is already there, another one of that name gets a numbered folder
            var again = await Import("Hades.v1.1-GRP", "b1", "b2");
            var c = await Import("Hades.v1.1-GRP", "c1", "c2");

            Assert.That(a.Success && b.Success && again.Success && c.Success, Is.True, a.Reason + b.Reason + again.Reason + c.Reason);
            Assert.That(Names(target), Is.EqualTo(new[]
            {
                "Hades.v1.1-GRP (2)/setup-1.bin", "Hades.v1.1-GRP (2)/setup.exe", "Hades.v1.1-GRP/setup-1.bin", "Hades.v1.1-GRP/setup.exe",
                "setup-1.bin", "setup.exe"
            }));
            Assert.That(File.ReadAllText(Path.Combine(target, "setup.exe")), Is.EqualTo("a1"));
            Assert.That(File.ReadAllText(Path.Combine(target, "setup-1.bin")), Is.EqualTo("a2"));
            Assert.That(File.ReadAllText(Path.Combine(target, "Hades.v1.1-GRP", "setup.exe")), Is.EqualTo("b1"));
            Assert.That(File.ReadAllText(Path.Combine(target, "Hades.v1.1-GRP", "setup-1.bin")), Is.EqualTo("b2"));
            Assert.That(File.ReadAllText(Path.Combine(target, "Hades.v1.1-GRP (2)", "setup.exe")), Is.EqualTo("c1"));
        }

        [TestCase("off")]
        [TestCase("set")]
        [TestCase("single")]
        public async Task SharedExtra_DoesNotBlock(string mode)
        {
            var platformId = mode == "single" ? 52 : 20;
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", platformId, mode == "off" ? null : Rename("ps1,gba"));
            (string, string)[] Release(string region, string content) => mode switch
            {
                "set" => new[] { ($"FF7 ({region}).cue", Cue($"FF7 ({region}).bin")), ($"FF7 ({region}).bin", content), ("readme.txt", "readme " + region) },
                "single" => new[] { ($"FF7 ({region}).gba", content), ("readme.txt", "readme " + region) },
                _ => new[] { ($"FF7 ({region}).chd", content), ("readme.txt", "readme " + region) }
            };

            var first = await ImportFolderAsync(processor, "FF7 (USA)", Release("USA", "usa"));
            var second = await ImportFolderAsync(processor, "FF7 (Europe)", Release("Europe", "eur"));

            Assert.That(first.Success && second.Success, Is.True, first.Reason + second.Reason);
            Assert.That(File.ReadAllText(Path.Combine(folder, "readme.txt")), Is.EqualTo("readme USA"));
            Assert.That(Names(folder), Is.EquivalentTo(mode switch
            {
                "set" => new[] { "Final Fantasy VII.cue", "Final Fantasy VII.bin", "Final Fantasy VII (Europe).cue", "Final Fantasy VII (Europe).bin", "readme.txt" },
                "single" => new[] { "Final Fantasy VII.gba", "Final Fantasy VII (Europe).gba", "readme.txt" },
                _ => new[] { "FF7 (USA).chd", "FF7 (Europe).chd", "readme.txt" }
            }));
            Assert.That(Directory.GetFiles(folder).Select(File.ReadAllText), Does.Contain("eur"));
        }

        [TestCase("A")]
        [TestCase("B")]
        [TestCase("C")]
        [TestCase("D")]
        public async Task DownloadFolderOverlap(string scenario)
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, Rename("gba"));
            File.WriteAllText(Path.Combine(folder, "aw (USA).gba"), "rom");
            if (scenario == "B") File.WriteAllText(Path.Combine(folder, "aw (Europe).gba"), "eur");
            var before = Names(folder);

            var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "x", Name = "Advance Wars", GameId = 1, State = DownloadState.Completed,
                DownloadPath = scenario == "C" ? Path.GetDirectoryName(folder) : folder,
                ImportSubfolder = scenario == "D" ? "Patches" : null
            });

            Assert.That(Directory.Exists(folder), Is.True);
            switch (scenario)
            {
                case "A":
                    Assert.That(result.Success, Is.True, result.Reason);
                    Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars.gba" }));
                    Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars.gba")), Is.EqualTo("rom"));
                    break;
                case "B":
                    Assert.That(result.Success, Is.True, result.Reason);
                    Assert.That(Names(folder), Is.EqualTo(before));
                    break;
                default:
                    Assert.That(result.Success, Is.False);
                    Assert.That(result.Reason, Does.Contain("overlaps the library"));
                    Assert.That(Names(folder), Is.EqualTo(before));
                    break;
            }
        }

        // The client completed into the game folder, or into a folder inside it
        [TestCase(false)]
        [TestCase(true)]
        public async Task DownloadInTheGameFolder_LibraryDiscsKeepTheirNames(bool inside)
        {
            var (processor, folder) = await SetUpAsync("Final Fantasy VII", 20, Rename("ps1"));
            var download = inside ? Directory.CreateDirectory(Path.Combine(folder, "incoming")).FullName : folder;
            File.WriteAllText(Path.Combine(download, "FF7 CD1 (my dump).chd"), "d1");
            File.WriteAllText(Path.Combine(download, "ff7 (Disc 2).chd"), "d2");

            var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "x", Name = "ff7 (Disc 2)", DownloadPath = download, GameId = 1, State = DownloadState.Completed
            });

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(inside
                ? new[] { "FF7 CD1 (my dump).chd", "ff7 (Disc 2).chd", "incoming/FF7 CD1 (my dump).chd", "incoming/ff7 (Disc 2).chd" }
                : new[] { "FF7 CD1 (my dump).chd", "ff7 (Disc 2).chd" }));
        }

        [TestCase(1, true, true)]
        [TestCase(1, true, false)]
        [TestCase(1, false, true)]
        [TestCase(1, false, false)]
        [TestCase(126, true, true)]
        [TestCase(126, true, false)]
        [TestCase(126, false, true)]
        [TestCase(126, false, false)]
        public async Task GogInstallerParts_KeepNames(int platformId, bool rename, bool exeFirst)
        {
            var (processor, _) = await SetUpAsync("Hades", platformId, rename ? Rename("gog,pc") : null);
            var exe = "setup_hades_1.38_(64bit)_(48493).exe";
            var parts = new[] { exe, "setup_hades_1.38_(64bit)_(48493)-1.bin", "setup_hades_1.38_(64bit)_(48493)-2.bin" };
            foreach (var part in exeFirst ? parts : Enumerable.Reverse(parts))
            {
                var result = await ImportFileAsync(processor, part, part, platformFolder: "gog");
                Assert.That(result.Success, Is.True, result.Reason);
            }

            using var ctx = new RetroArrDbContext(_db);
            var gog = ctx.Games.Single(g => g.PlatformId == 126);
            Assert.That(Names(gog.Path!), Is.EquivalentTo(parts));
            Assert.That(gog.ExecutablePath, Is.EqualTo(Path.Combine(gog.Path!, exe)));
        }

        [TestCase(121, "GAME.EXE", "GAME.DAT")]
        [TestCase(120, "SKY.DSK", "SKY.DNR")]
        [TestCase(121, "GAME.EXE", "GAME.CUE", "GAME.BIN")]
        public async Task SoftwareMultiFile_KeepsNames(int platformId, params string[] files)
        {
            var (processor, folder) = await SetUpAsync("Beneath a Steel Sky", platformId, Rename("dosbox,scummvm"));

            var result = await ImportFolderAsync(processor, "Beneath.a.Steel.Sky-GRP",
                files.Select(f => (f, f.EndsWith(".CUE") ? "FILE \"GAME.BIN\" BINARY\n" : f)).ToArray());

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EquivalentTo(files));
        }

        [Test]
        public async Task PortableRelease_ExecutablePathSkipsJunk()
        {
            var (processor, folder) = await SetUpAsync("Hades", 1, Rename("pc"));

            var result = await ImportFolderAsync(processor, "Hades-Portable",
                ("Game.exe", "x"), ("UnityCrashHandler64.exe", "a much larger file"), ("Game_Data/x.assets", "the largest file of the release"),
                ("Game/Binaries/Win64/Game-Win64-Shipping.exe", "a larger exe further down"));

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(GameRow().ExecutablePath, Is.EqualTo(Path.Combine(folder, "Game.exe")));
        }

        [Test]
        [Platform(Exclude = "Win")]
        public async Task LinkToAnIdenticalFile_IsAlreadyThere()
        {
            var (processor, folder) = await SetUpAsync("Advance Wars", 52);
            var elsewhere = Path.Combine(_root, "elsewhere.gba");
            File.WriteAllText(elsewhere, "rom");
            var link = Path.Combine(folder, "Advance Wars.gba");
            File.CreateSymbolicLink(link, elsewhere);

            var result = await ImportFileAsync(processor, "Advance Wars (USA).gba", "rom");

            Assert.That(result.Success, Is.True, result.Reason);
            Assert.That(Names(folder), Is.EqualTo(new[] { "Advance Wars.gba" }));
            Assert.That(new FileInfo(link).LinkTarget, Is.EqualTo(elsewhere));
        }

        // Holds the first ImportFile call until released, so a second import of the same game could run meanwhile
        private sealed class GatedMover : IFileMoverService
        {
            private readonly FileMoverService _inner = new();
            private int _calls;
            public readonly System.Threading.SemaphoreSlim Entered = new(0);
            public readonly System.Threading.SemaphoreSlim Release = new(0);
            public bool ImportFile(string sourceFile, string destinationFile) => ImportFile(sourceFile, destinationFile, out _);
            public bool ImportFile(string sourceFile, string destinationFile, out string? failureReason)
            {
                var n = System.Threading.Interlocked.Increment(ref _calls);
                Entered.Release();
                if (n == 1) Release.Wait(TimeSpan.FromSeconds(10));
                return _inner.ImportFile(sourceFile, destinationFile, out failureReason);
            }
        }

        [Test]
        public async Task ImportsOfOneGame_AreSerialized()
        {
            var mover = new GatedMover();
            var (processor, folder) = await SetUpAsync("Advance Wars", 52, null, mover);
            var usa = Path.Combine(Downloads, "Advance Wars (USA).gba");
            var eur = Path.Combine(Downloads, "Advance Wars (Europe).gba");
            File.WriteAllText(usa, "usa");
            File.WriteAllText(eur, "eur");
            Task<PostDownloadResult> Import(string file) => Task.Run(() => processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = file, Name = Path.GetFileNameWithoutExtension(file), DownloadPath = file, GameId = 1, State = DownloadState.Completed
            }));

            var first = Import(usa);
            Assert.That(await mover.Entered.WaitAsync(TimeSpan.FromSeconds(10)), Is.True);
            var second = Import(eur);
            var overlapped = await mover.Entered.WaitAsync(TimeSpan.FromMilliseconds(500));
            mover.Release.Release();
            var results = await Task.WhenAll(first, second);

            Assert.That(overlapped, Is.False, "the second import ran while the first was landing");
            Assert.That(results.All(r => r.Success), Is.True, string.Join(" ", results.Select(r => r.Reason)));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars.gba")), Is.EqualTo("usa"));
            Assert.That(File.ReadAllText(Path.Combine(folder, "Advance Wars (Europe).gba")), Is.EqualTo("eur"));
        }

        // ── Naming helpers ──────────────────────────────────────────────

        [TestCase("Rune Factory 4 Special (USA)", null)]
        [TestCase("Hades-RUNE", "RUNE")]
        [TestCase("Hades [FitGirl Repack]", "FitGirl")]
        [TestCase("Game [No-Intro].zip", "No-Intro")]
        [TestCase("The Matrix Reloaded (USA)", null)]
        [TestCase("The.Matrix.Reloaded-RELOADED", "RELOADED")]
        public void ExtractReleaseGroup_IsATokenMatch(string name, string? expected)
        {
            Assert.That(TitleCleanerService.ExtractReleaseGroup(name), Is.EqualTo(expected));
        }

        [Test]
        public void Scope_MatchesSlugOrFolder()
        {
            var ps1 = PlatformDefinitions.AllPlatforms.Single(p => p.Id == 20);
            var mac = PlatformDefinitions.AllPlatforms.Single(p => p.Id == 2);
            Assert.That(FileRenamer.Applies(ps1, Rename("psx")), Is.True);
            Assert.That(FileRenamer.Applies(ps1, Rename("ps1")), Is.True);
            Assert.That(FileRenamer.Applies(mac, Rename("windows,pc,linux,macintosh")), Is.True);
            Assert.That(FileRenamer.Applies(ps1, new MediaSettings { RenameOnImport = false, ApplyRenameToPlatforms = "ps1" }), Is.False);
            Assert.That(FileRenamer.Applies(ps1, Rename("")), Is.False);
        }

        [Test]
        public void TemplateRenderer_EmptyGroupsAndDisc()
        {
            var renderer = new TemplateRenderer();
            Assert.That(renderer.RenderStem("{Title} ({Region}, {Languages})", new Dictionary<string, string?> { ["Title"] = "X", ["Region"] = "", ["Languages"] = null }), Is.EqualTo("X"));
            Assert.That(renderer.RenderStem("{Title} - DLC - {ContentName}", new Dictionary<string, string?> { ["Title"] = "X", ["ContentName"] = "" }), Is.EqualTo("X - DLC"));
            Assert.That(TemplateRenderer.KnownTokens, Does.Contain("Disc"));
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
