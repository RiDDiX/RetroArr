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

        private async Task<(PostDownloadProcessor Processor, string Library, string Source)> TorrentSetup(PostDownloadSettings settings, IFileMoverService? mover = null)
        {
            var config = new ConfigurationService(_root);
            var library = Directory.CreateDirectory(Path.Combine(_root, "library", "Test Game")).FullName;
            config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(_root, "library") });
            config.SavePostDownloadSettings(settings);
            config.SaveDownloadClients(new List<DownloadClient> { new DownloadClient { Id = 1, Name = "client", Implementation = "Transmission" } });
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

        private sealed class TestDbContextFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public TestDbContextFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }
    }
}
