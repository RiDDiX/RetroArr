using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Download;
using RetroArr.Core.Games;
using RetroArr.Core.IO;

namespace RetroArr.Core.Test.Download
{
    [TestFixture]
    public class PostDownloadSourceCleanupTest
    {
        private string _root = null!;

        [SetUp]
        public void SetUp()
        {
            _root = Path.Combine(Path.GetTempPath(), "retroarr_pdcleanup_" + Path.GetRandomFileName());
            // Pins ConfigurationService to this tree instead of ~/.config/RetroArr.
            Directory.CreateDirectory(Path.Combine(_root, "config"));
        }

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root))
                Directory.Delete(_root, recursive: true);
        }

        [TestCase("qBittorrent", true)]
        [TestCase("Deluge", true)]
        [TestCase("SABnzbd", false)]
        public async Task Import_LeavesTorrentSourceUntouched_AndStillCleansUsenet(string implementation, bool keepsSource)
        {
            var config = new ConfigurationService(_root);
            var library = Directory.CreateDirectory(Path.Combine(_root, "library", "Test Game")).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library") });
            config.SavePostDownloadSettings(new PostDownloadSettings
            {
                EnableAutoMove = true,
                EnableAutoExtract = true,
                EnableDeepClean = true,
                UnwantedExtensions = new List<string> { ".nfo" }
            });
            config.SaveDownloadClients(new List<DownloadClient> { new DownloadClient { Id = 1, Name = "client", Implementation = implementation } });

            var source = Directory.CreateDirectory(Path.Combine(_root, "downloads", "Test.Game-GRP")).FullName;
            var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
            File.WriteAllText(Path.Combine(staging, "game.iso"), "iso");
            ZipFile.CreateFromDirectory(staging, Path.Combine(source, "game.zip"));
            File.WriteAllText(Path.Combine(source, "release.nfo"), "nfo");

            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Test Game", PlatformId = 40, Path = library });
                await ctx.SaveChangesAsync();
            }

            var processor = new PostDownloadProcessor(config, new FileMoverService(),
                new SqliteGameRepository(new TestDbContextFactory(dbOptions)), null!,
                new ArchiveService(), new TitleCleanerService());

            var result = await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "hash",
                ClientId = 1,
                Name = "Test Game",
                DownloadPath = source,
                GameId = 1,
                State = DownloadState.Completed
            });

            Assert.That(result.Success, Is.True, result.Reason);
            // Library ends up the same either way: only the extracted game, no archive or .nfo.
            var imported = Directory.GetFiles(library).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EqualTo(new[] { "Test Game.iso" }));

            if (keepsSource)
            {
                Assert.That(File.Exists(Path.Combine(source, "game.zip")), Is.True, "seeded archive was deleted");
                Assert.That(File.Exists(Path.Combine(source, "release.nfo")), Is.True, "seeded .nfo was deleted");
                Assert.That(File.Exists(Path.Combine(source, "game.iso")), Is.False, "extracted copy was left in the torrent folder");
            }
            else
            {
                Assert.That(Directory.Exists(source), Is.False, "usenet source should still be cleaned up");
            }
        }

        [Test]
        public async Task Extract_NeverOverwritesASeededFile()
        {
            var config = new ConfigurationService(_root);
            var library = Directory.CreateDirectory(Path.Combine(_root, "library", "Test Game")).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library") });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            config.SaveDownloadClients(new List<DownloadClient> { new DownloadClient { Id = 1, Name = "client", Implementation = "qBittorrent" } });

            // The torrent carries game.iso and a zip whose entry has the same name
            var source = Directory.CreateDirectory(Path.Combine(_root, "downloads", "Test.Game-GRP")).FullName;
            var staging = Directory.CreateDirectory(Path.Combine(_root, "staging")).FullName;
            File.WriteAllText(Path.Combine(staging, "game.iso"), "from the zip");
            ZipFile.CreateFromDirectory(staging, Path.Combine(source, "game.zip"));
            File.WriteAllText(Path.Combine(source, "game.iso"), "seeded");

            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Test Game", PlatformId = 40, Path = library });
                await ctx.SaveChangesAsync();
            }

            var processor = new PostDownloadProcessor(config, new FileMoverService(),
                new SqliteGameRepository(new TestDbContextFactory(dbOptions)), null!,
                new ArchiveService(), new TitleCleanerService());

            await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "hash",
                ClientId = 1,
                Name = "Test Game",
                DownloadPath = source,
                GameId = 1,
                State = DownloadState.Completed
            });

            Assert.That(File.ReadAllText(Path.Combine(source, "game.iso")), Is.EqualTo("seeded"));
            Assert.That(File.Exists(Path.Combine(source, "game.zip")), Is.True);
            // the archive couldn't be fully extracted, so it goes to the library as it is
            Assert.That(Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName), Does.Contain("game.zip"));
        }

        private async Task<(PostDownloadProcessor Processor, string Library, string Source)> TorrentSetup(PostDownloadSettings settings, IFileMoverService? mover = null, string implementation = "Transmission")
        {
            var config = new ConfigurationService(_root);
            var library = Directory.CreateDirectory(Path.Combine(_root, "library", "Test Game")).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library") });
            config.SavePostDownloadSettings(settings);
            config.SaveDownloadClients(new List<DownloadClient> { new DownloadClient { Id = 1, Name = "client", Implementation = implementation } });
            var source = Directory.CreateDirectory(Path.Combine(_root, "downloads", "Test.Game-GRP")).FullName;

            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Test Game", PlatformId = 40, Path = library });
                await ctx.SaveChangesAsync();
            }
            var processor = new PostDownloadProcessor(config, mover ?? new FileMoverService(),
                new SqliteGameRepository(new TestDbContextFactory(dbOptions)), null!,
                new ArchiveService(), new TitleCleanerService());
            return (processor, library, source);
        }

        private static void Zip(string archive, params (string Entry, string Content)[] files)
        {
            using var zip = ZipFile.Open(archive, ZipArchiveMode.Create);
            foreach (var (entry, content) in files)
            {
                using var writer = new StreamWriter(zip.CreateEntry(entry).Open());
                writer.Write(content);
            }
        }

        private static DownloadStatus Torrent(string source) => new DownloadStatus
        {
            Id = "hash", ClientId = 1, Name = "Test Game", DownloadPath = source, GameId = 1, State = DownloadState.Completed
        };

        [Test]
        public async Task ParentFileClash_IsSeen_WithRedundantSeparatorsInThePath()
        {
            Assume.That(Path.DirectorySeparatorChar, Is.EqualTo('/'));
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(source, "x"), "seeded file");
            Zip(Path.Combine(source, "game.zip"), ("aaa.iso", "a"), ("x/s.png", "png"));
            var messyPath = source.Replace("/downloads/", "/downloads/////");

            await processor.ProcessCompletedDownloadAsync(Torrent(messyPath));

            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Does.Contain("game.zip").And.Not.Contain("aaa.iso"));
        }

        [Test]
        public async Task TorrentArchives_SharingAFile_BothExtract_AndLeaveNothingBehind()
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            Zip(Path.Combine(source, "disc1.zip"), ("disc1/game1.iso", "one"), ("readme.txt", "r"));
            Zip(Path.Combine(source, "disc2.zip"), ("disc2/game2.iso", "two"), ("readme.txt", "r"));

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Does.Contain("game1.iso").And.Contain("game2.iso"));
            Assert.That(Directory.GetFileSystemEntries(source).Select(Path.GetFileName), Is.EquivalentTo(new[] { "disc1.zip", "disc2.zip" }));
        }

        // Runs something while the import copies files, like another program writing to the folder
        private sealed class HookedMover : IFileMoverService
        {
            private readonly FileMoverService _inner = new();
            public Action? OnImport { get; set; }
            public bool ImportFile(string sourceFile, string destinationFile) => ImportFile(sourceFile, destinationFile, out _);
            public bool ImportFile(string sourceFile, string destinationFile, out string? failureReason)
            {
                var ok = _inner.ImportFile(sourceFile, destinationFile, out failureReason);
                OnImport?.Invoke();
                OnImport = null;
                return ok;
            }
        }

        [Test]
        public async Task TorrentCleanup_OnlyRemovesWhatWasExtracted()
        {
            var mover = new HookedMover();
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, mover);
            Zip(Path.Combine(source, "game.zip"), ("game.iso", "iso"));
            mover.OnImport = () => File.WriteAllText(Path.Combine(source, "written-meanwhile.txt"), "not ours");

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(File.Exists(Path.Combine(source, "written-meanwhile.txt")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "game.iso")), Is.False);
        }

        [Test]
        public async Task TorrentFolderGoneDuringImport_StillCountsAsImported()
        {
            var mover = new HookedMover();
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, mover);
            Zip(Path.Combine(source, "game.zip"), ("game.iso", "iso"));
            mover.OnImport = () => Directory.Delete(source, true);

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
        }

        [Test]
        public async Task LeftoverStaging_IsRemoved_ButAFolderTheTorrentShipsIsKept()
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(source, "game.iso"), "iso");
            var stale = Directory.CreateDirectory(Path.Combine(source, ".retroarr-extract-" + new string('a', 32))).FullName;
            File.WriteAllText(Path.Combine(stale, "stale.iso"), "old");
            var shipped = Directory.CreateDirectory(Path.Combine(source, ".retroarr-extract")).FullName;
            File.WriteAllText(Path.Combine(shipped, "shipped.txt"), "part of the torrent");

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName), Does.Not.Contain("stale.iso"));
            Assert.That(File.Exists(Path.Combine(shipped, "shipped.txt")), Is.True);
        }

        [Test]
        public async Task FoldersTheTorrentShips_NamedLikeStaging_AreKept()
        {
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(source, "game.iso"), "iso");
            var names = new[] { ".retroarr-extract-foo", ".retroarr-extract-" + new string('g', 32), ".retroarr-extract-" + new string('a', 31) };
            foreach (var name in names)
            {
                File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(source, name)).FullName, "x.txt"), "seeded");
            }

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            foreach (var name in names)
            {
                Assert.That(File.Exists(Path.Combine(source, name, "x.txt")), Is.True, name);
            }
        }

        [Test]
        public async Task ArchiveEntryNamedLikeASeededFolder_IsAClash()
        {
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = false, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(Directory.CreateDirectory(Path.Combine(source, "game.iso")).FullName, "inside.txt"), "seeded");
            Zip(Path.Combine(source, "game.zip"), ("game.iso", "from the zip"));

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Reason, Does.Contain("Auto-move is disabled"));
            Assert.That(File.ReadAllText(Path.Combine(source, "game.iso", "inside.txt")), Is.EqualTo("seeded"));
        }

        [Test]
        public async Task ClashingArchive_IsImportedWhole_NotAlsoAsLooseFiles()
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(source, "game.iso"), "seeded");
            Zip(Path.Combine(source, "game.zip"), ("game.iso", "from the zip"), ("bonus.iso", "bonus"));

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Does.Contain("game.zip").And.Not.Contain("bonus.iso"));
            Assert.That(File.Exists(Path.Combine(source, "bonus.iso")), Is.False);
            Assert.That(File.ReadAllText(Path.Combine(source, "game.iso")), Is.EqualTo("seeded"));
        }

        [Test]
        public async Task ArchiveNeedingAFolderWhereASeededFileIs_IsAClash()
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(source, "extras"), "seeded file");
            Zip(Path.Combine(source, "game.zip"), ("aaa.iso", "a"), ("extras/scan.png", "png"));

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Does.Contain("game.zip").And.Not.Contain("aaa.iso"));
            Assert.That(File.ReadAllText(Path.Combine(source, "extras")), Is.EqualTo("seeded file"));
            Assert.That(File.Exists(Path.Combine(source, "aaa.iso")), Is.False);
        }

        [Test]
        public async Task CaseVariantFromALaterArchive_NeverReplacesASeededFile()
        {
            var probe = Path.Combine(_root, "CaseProbe");
            File.WriteAllText(probe, "");
            Assume.That(File.Exists(Path.Combine(_root, "caseprobe")), Is.False, "needs a case-sensitive file system");

            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            File.WriteAllText(Path.Combine(source, "GAME.ISO"), "seeded");
            // the subfolder makes this archive come second
            Zip(Path.Combine(source, "a.zip"), ("game.iso", "from a"));
            Zip(Path.Combine(Directory.CreateDirectory(Path.Combine(source, "zz")).FullName, "b.zip"), ("GAME.ISO", "from b"));

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(File.ReadAllText(Path.Combine(source, "GAME.ISO")), Is.EqualTo("seeded"));
            Assert.That(File.Exists(Path.Combine(source, "game.iso")), Is.False);
        }

        [Test]
        public async Task FolderThatExistedBefore_IsKept_EvenWhenEmptyAgain()
        {
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            Directory.CreateDirectory(Path.Combine(source, "extras"));
            Zip(Path.Combine(source, "game.zip"), ("game.iso", "iso"), ("extras/scan.png", "png"), ("new/deep/x.txt", "x"));

            await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(Directory.Exists(Path.Combine(source, "extras")), Is.True);
            Assert.That(Directory.Exists(Path.Combine(source, "new")), Is.False);
            Assert.That(File.Exists(Path.Combine(source, "extras", "scan.png")), Is.False);
        }

        [Test]
        public async Task TorrentExtract_WithoutAutoMove_KeepsTheExtractedFiles()
        {
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = false, EnableAutoExtract = true });
            Zip(Path.Combine(source, "game.zip"), ("game.iso", "iso"));

            await processor.ProcessCompletedDownloadAsync(Torrent(source));
            var second = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(File.Exists(Path.Combine(source, "game.iso")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "game.zip")), Is.True);
            Assert.That(second.Reason, Does.Contain("Auto-move is disabled"));
            Assert.That(Directory.GetDirectories(source, ".retroarr-extract*"), Is.Empty);
        }

        [Test]
        public async Task Import_KeepsUsenetSource_WhenAFileFailedToImport()
        {
            var config = new ConfigurationService(_root);
            var library = Directory.CreateDirectory(Path.Combine(_root, "library", "Test Game")).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library") });
            config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true });
            config.SaveDownloadClients(new List<DownloadClient> { new DownloadClient { Id = 1, Name = "client", Implementation = "SABnzbd" } });

            var source = Directory.CreateDirectory(Path.Combine(_root, "downloads", "Test.Game-GRP")).FullName;
            File.WriteAllText(Path.Combine(source, "disc1.iso"), "one");
            File.WriteAllText(Path.Combine(source, "disc2.iso"), "two");
            // A directory in the way makes disc2 fail to import
            Directory.CreateDirectory(Path.Combine(library, "disc2.iso"));

            var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>()
                .UseInMemoryDatabase(Guid.NewGuid().ToString())
                .Options;
            using (var ctx = new RetroArrDbContext(dbOptions))
            {
                ctx.Games.Add(new Game { Id = 1, Title = "Test Game", PlatformId = 40, Path = library });
                await ctx.SaveChangesAsync();
            }

            var processor = new PostDownloadProcessor(config, new FileMoverService(),
                new SqliteGameRepository(new TestDbContextFactory(dbOptions)), null!,
                new ArchiveService(), new TitleCleanerService());

            await processor.ProcessCompletedDownloadAsync(new DownloadStatus
            {
                Id = "nzo",
                ClientId = 1,
                Name = "Test Game",
                DownloadPath = source,
                GameId = 1,
                State = DownloadState.Completed
            });

            Assert.That(File.Exists(Path.Combine(library, "disc1.iso")), Is.True);
            Assert.That(File.Exists(Path.Combine(source, "disc2.iso")), Is.True, "the only copy of disc2 was deleted");
        }

        // No rar tool ships with the tests, so this writes stored (uncompressed) RAR 4 volumes by hand; unrar t accepts them
        private static void RarVolumes(string entry, string content, params string[] volumes)
        {
            var data = System.Text.Encoding.ASCII.GetBytes(content);
            var name = System.Text.Encoding.ASCII.GetBytes(entry);
            var chunk = (data.Length + volumes.Length - 1) / volumes.Length;
            for (var i = 0; i < volumes.Length; i++)
            {
                var part = data.Skip(i * chunk).Take(chunk).ToArray();
                var last = i == volumes.Length - 1;
                using var w = new BinaryWriter(File.Create(volumes[i]));
                w.Write(new byte[] { 0x52, 0x61, 0x72, 0x21, 0x1A, 0x07, 0x00 });
                // volume, first volume, name.partN.rar numbering
                RarBlock(w, 0x73, (ushort)(0x0001 | (i == 0 ? 0x0100 : 0) | (volumes[0].Contains(".part") ? 0x0010 : 0)), new byte[6]);
                using var header = new MemoryStream();
                using (var h = new BinaryWriter(header))
                {
                    h.Write((uint)part.Length);
                    h.Write((uint)data.Length);
                    h.Write((byte)2); // Windows
                    h.Write(Crc32(last ? data : part));
                    h.Write(0x50210000u); // 2020-01-01
                    h.Write((byte)29);
                    h.Write((byte)0x30); // stored
                    h.Write((ushort)name.Length);
                    h.Write(0x20u);
                    h.Write(name);
                }
                // continued from the previous volume / continues in the next one
                RarBlock(w, 0x74, (ushort)(0x8000 | (i > 0 ? 0x01 : 0) | (last ? 0 : 0x02)), header.ToArray());
                w.Write(part);
                RarBlock(w, 0x7B, (ushort)(last ? 0 : 0x0001), Array.Empty<byte>());
            }
        }

        private static void RarBlock(BinaryWriter w, byte type, ushort flags, byte[] body)
        {
            var head = new byte[5 + body.Length];
            head[0] = type;
            BitConverter.GetBytes(flags).CopyTo(head, 1);
            BitConverter.GetBytes((ushort)(head.Length + 2)).CopyTo(head, 3);
            body.CopyTo(head, 5);
            w.Write((ushort)Crc32(head));
            w.Write(head);
        }

        private static uint Crc32(byte[] bytes)
        {
            var crc = 0xFFFFFFFFu;
            foreach (var b in bytes)
            {
                crc ^= b;
                for (var k = 0; k < 8; k++) crc = (crc >> 1) ^ (0xEDB88320u & (0u - (crc & 1)));
            }
            return ~crc;
        }

        private static readonly string[][] VolumeSets =
        {
            new[] { "game.part1.rar", "game.part2.rar", "game.part3.rar" },
            new[] { "game.part01.rar", "game.part02.rar", "game.part03.rar" },
            new[] { "game.part001.rar", "game.part002.rar", "game.part003.rar" },
            new[] { "game.rar", "game.r00", "game.r01" },
        };

        [Test]
        public async Task UsenetVolumeSet_IsDeleted_OnceExtracted([ValueSource(nameof(VolumeSets))] string[] volumes)
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, implementation: "SABnzbd");
            RarVolumes("game.iso", "a game image spread over three volumes", volumes.Select(v => Path.Combine(source, v)).ToArray());
            // an N64 dump, not a volume
            File.WriteAllText(Path.Combine(source, "game.v64"), "rom");

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EquivalentTo(new[] { "game.iso", "game.v64" }));
            Assert.That(File.ReadAllText(Path.Combine(library, "game.iso")), Is.EqualTo("a game image spread over three volumes"));
        }

        [Test]
        public async Task TorrentVolumeSet_IsKept_AndNotImported([ValueSource(nameof(VolumeSets))] string[] volumes)
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true });
            RarVolumes("game.iso", "a game image spread over three volumes", volumes.Select(v => Path.Combine(source, v)).ToArray());

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EqualTo(new[] { "Test Game.iso" }));
            Assert.That(Directory.GetFileSystemEntries(source).Select(Path.GetFileName), Is.EquivalentTo(volumes));
        }

        [Test]
        public async Task UsenetVolumeSet_MissingAVolume_DeletesNothing()
        {
            var (processor, _, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = false, EnableAutoExtract = true }, implementation: "SABnzbd");
            RarVolumes("game.iso", "a game image spread over three volumes", Path.Combine(source, "game.part1.rar"), Path.Combine(source, "game.part2.rar"), Path.Combine(source, "game.part3.rar"));
            File.Delete(Path.Combine(source, "game.part2.rar"));

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Reason, Does.Contain("Extraction failed"));
            Assert.That(Directory.GetFiles(source).Select(Path.GetFileName), Is.SupersetOf(new[] { "game.part1.rar", "game.part3.rar" }));
        }

        [TestCase("SABnzbd")]
        [TestCase("Transmission")]
        public async Task ArchiveNamedWithPart_IsNotMistakenForALaterVolume(string implementation)
        {
            var (processor, library, _) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, implementation: implementation);
            var source = Directory.CreateDirectory(Path.Combine(_root, "downloads", "Mario.Party.NSW-GRP")).FullName;
            Zip(Path.Combine(source, "Mario.Party.zip"), ("mario.nsp", "nsp"));

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EqualTo(new[] { "Test Game.nsp" }));
        }

        // Real RAR 5 archives from rar 7.23: "rar a -m0 -tl discN.rar discN.iso" and "rar a -m0 -v1k game.rar game.iso"
        private static readonly byte[] Disc1Rar = Convert.FromBase64String(
            "UmFyIRoHAQAzkrXlCgEFBgAFAQGAgABwtaGBJwIDC4MABIMApIMC8YZseoAAAQlkaXNjMS5pc28KAxP1SrlqGTY6Mm9uZR13VlEDBQQA");
        private static readonly byte[] Disc2Rar = Convert.FromBase64String(
            "UmFyIRoHAQAzkrXlCgEFBgAFAQGAgACIRSk+JwIDC4MABIMApIMCZorKEYAAAQlkaXNjMi5pc28KAxP1SrlqGTY6MnR3bx13VlEDBQQA");
        private static readonly string GameIso = string.Concat(Enumerable.Repeat("a game image spread over two volumes. ", 30));
        private static readonly byte[][] GameRarVolumes =
        {
            Convert.FromBase64String(
                "UmFyIRoHAQBt4SgnCwEFBwEGAQGAgIAAtPHzFiYCEwvlBgT0CKSDAgTB876AAAEIZ2FtZS5pc28KAxP1SrlqSUq0MmEgZ2Ft" +
                "ZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1bWVzLiBh" +
                "IGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1l" +
                "cy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZv" +
                "bHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3" +
                "byB2b2x1bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3Zl" +
                "ciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFk" +
                "IG92ZXIgdHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNw" +
                "cmVhZCBvdmVyIHR3byB2b2x1bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFn" +
                "ZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1bWVzLiBhIGdhbWUg" +
                "aW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBn" +
                "YW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZvbHVtZXMu" +
                "IGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1" +
                "i0dRJgMFBAEAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA" +
                "AAAAAAAAAAAAAAAAAAAAAA=="),
            Convert.FromBase64String(
                "UmFyIRoHAQCpwsTKDAEFBwMBBgEBgICAAE03ZQkmAgsLjwIE9AikgwKIJm1xgAABCGdhbWUuaXNvCgMT9Uq5aklKtDJtZXMu" +
                "IGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1" +
                "bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIgdHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28g" +
                "dm9sdW1lcy4gYSBnYW1lIGltYWdlIHNwcmVhZCBvdmVyIHR3byB2b2x1bWVzLiBhIGdhbWUgaW1hZ2Ugc3ByZWFkIG92ZXIg" +
                "dHdvIHZvbHVtZXMuIGEgZ2FtZSBpbWFnZSBzcHJlYWQgb3ZlciB0d28gdm9sdW1lcy4gHXdWUQMFBAA="),
        };

        [TestCase("SABnzbd")]
        [TestCase("Transmission")]
        public async Task Rar5VolumeSet_IsExtractedAsOneArchive(string implementation)
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, implementation: implementation);
            File.WriteAllBytes(Path.Combine(source, "game.part1.rar"), GameRarVolumes[0]);
            File.WriteAllBytes(Path.Combine(source, "game.part2.rar"), GameRarVolumes[1]);

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EqualTo(new[] { "Test Game.iso" }));
            Assert.That(File.ReadAllText(Path.Combine(library, "Test Game.iso")), Is.EqualTo(GameIso));
        }

        // SharpCompress reads on into name.part2.rar or name.r00 whether or not they are real volumes, and extracts both
        [TestCase("SABnzbd", "Game.part1.rar", "Game.part2.rar")]
        [TestCase("SABnzbd", "Game.rar", "Game.r00")]
        [TestCase("Transmission", "Game.part1.rar", "Game.part2.rar")]
        public async Task SeparateArchivesNamedLikeVolumes_AreBothExtracted(string implementation, string first, string second)
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, implementation: implementation);
            File.WriteAllBytes(Path.Combine(source, first), Disc1Rar);
            File.WriteAllBytes(Path.Combine(source, second), Disc2Rar);

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EquivalentTo(new[] { "disc1.iso", "disc2.iso" }));
        }

        // Names SharpCompress doesn't follow from the first archive, and a zip it can't read as a RAR volume
        [TestCase("SABnzbd", "Game.part1.rar", "Game.part02.rar", false)]
        [TestCase("SABnzbd", "Game.part1.rar", "Game.part3.rar", false)]
        [TestCase("SABnzbd", "Game.rar", "Game.r01", false)]
        [TestCase("SABnzbd", "Game.part1.rar", "Game.part2.rar", true)]
        [TestCase("Transmission", "Game.part1.rar", "Game.part02.rar", false)]
        [TestCase("Transmission", "Game.rar", "Game.r01", false)]
        public async Task FileNamedLikeAVolume_ButNotRead_IsImportedAsItIs(string implementation, string first, string second, bool secondIsZip)
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, implementation: implementation);
            File.WriteAllBytes(Path.Combine(source, first), Disc1Rar);
            if (secondIsZip) Zip(Path.Combine(source, second), ("disc2.iso", "two"));
            else File.WriteAllBytes(Path.Combine(source, second), Disc2Rar);
            var secondBytes = File.ReadAllBytes(Path.Combine(source, second));

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EquivalentTo(new[] { "disc1.iso", second }), $"{second} was never extracted, so it has to be imported as it is");
            Assert.That(File.ReadAllBytes(Path.Combine(library, second)), Is.EqualTo(secondBytes));
        }

        [Test]
        public async Task UsenetVolumeSets_InSeparateFolders_AreEachExtracted()
        {
            var (processor, library, source) = await TorrentSetup(new PostDownloadSettings { EnableAutoMove = true, EnableAutoExtract = true }, implementation: "SABnzbd");
            var cd1 = Directory.CreateDirectory(Path.Combine(source, "CD1")).FullName;
            var cd2 = Directory.CreateDirectory(Path.Combine(source, "CD2")).FullName;
            RarVolumes("disc1.iso", "the first disc", Path.Combine(cd1, "Game.rar"), Path.Combine(cd1, "Game.r00"));
            RarVolumes("disc2.iso", "the second disc", Path.Combine(cd2, "Game.rar"), Path.Combine(cd2, "Game.r00"));

            var result = await processor.ProcessCompletedDownloadAsync(Torrent(source));

            Assert.That(result.Success, Is.True, result.Reason);
            var imported = Directory.GetFiles(library, "*", SearchOption.AllDirectories).Select(Path.GetFileName).ToArray();
            Assert.That(imported, Is.EquivalentTo(new[] { "disc1.iso", "disc2.iso" }));
            Assert.That(File.ReadAllText(Path.Combine(library, "disc2.iso")), Is.EqualTo("the second disc"));
        }

        private sealed class TestDbContextFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public TestDbContextFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
