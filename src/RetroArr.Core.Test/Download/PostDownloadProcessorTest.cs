using System;
using System.IO;
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
    public class PostDownloadProcessorTest
    {
        // ── Import with the release detector's "unknown" platform ─────

        [Test]
        public async Task GameTargetedImport_WithUnknownPlatformHint_UsesTheGamesPlatform()
        {
            var root = Path.Combine(Path.GetTempPath(), "retroarr_pdunknown_" + Path.GetRandomFileName());
            Directory.CreateDirectory(Path.Combine(root, "config"));
            try
            {
                var config = new ConfigurationService(root);
                var library = Directory.CreateDirectory(Path.Combine(root, "library", "nds", "Pokemon Platinum")).FullName;
                config.SaveMediaSettings(new MediaSettings { FolderPath = Path.Combine(root, "library") });
                config.SavePostDownloadSettings(new PostDownloadSettings { EnableAutoMove = true });

                var source = Directory.CreateDirectory(Path.Combine(root, "downloads", "Pokemon.Platinum")).FullName;
                File.WriteAllText(Path.Combine(source, "Pokemon Platinum.nds"), "rom");

                var dbOptions = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
                using (var ctx = new RetroArrDbContext(dbOptions))
                {
                    ctx.Games.Add(new Game { Id = 1, Title = "Pokemon Platinum", PlatformId = 53, Path = library });
                    await ctx.SaveChangesAsync();
                }

                var processor = new PostDownloadProcessor(config, new FileMoverService(),
                    new SqliteGameRepository(new DbFactory(dbOptions)), null!, new ArchiveService(), new TitleCleanerService());
                await processor.ProcessCompletedDownloadAsync(new DownloadStatus
                {
                    Id = "x", Name = "Pokemon Platinum", DownloadPath = source, GameId = 1,
                    PlatformFolder = "unknown", State = DownloadState.Completed
                });

                using var check = new RetroArrDbContext(dbOptions);
                Assert.That(check.Games.Count(), Is.EqualTo(1), "a second game was created on the Unknown platform");
                Assert.That(Directory.GetFiles(library), Has.Length.EqualTo(1));
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // ── DetectContentType: Patch with version ─────────────────────

        [TestCase("Street Fighter x Tekken Update v1.02 PS3", "1.02")]
        [TestCase("Game.Title.Patch.1.05.PS3", "1.05")]
        [TestCase("MyGame Hotfix 2.01", "2.01")]
        [TestCase("SomeGame Update v3.0.1", "3.0.1")]
        [TestCase("GameTitle Fix 1.0.2", "1.0.2")]
        public void DetectContentType_Patch_WithVersion(string input, string expectedVersion)
        {
            var (type, version, _) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.Patch));
            Assert.That(version, Is.EqualTo(expectedVersion));
        }

        [TestCase("GameTitle Update PS3")]
        [TestCase("SomeGame Patch")]
        [TestCase("MyGame Hotfix")]
        public void DetectContentType_Patch_WithoutVersion(string input)
        {
            var (type, version, _) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.Patch));
            Assert.That(version, Is.Null);
        }

        // ── DetectContentType: DLC ────────────────────────────────────

        [TestCase("GameTitle DLC Season Pass", "Season Pass")]
        [TestCase("SomeGame DLC Map Pack", "Map Pack")]
        [TestCase("MyGame Expansion The Frozen North", "The Frozen North")]
        [TestCase("Game Add-on Extra Content", "Extra Content")]
        public void DetectContentType_DLC(string input, string expectedName)
        {
            var (type, _, dlcName) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.DLC));
            Assert.That(dlcName, Is.EqualTo(expectedName));
        }

        // ── DetectContentType: MainGame ───────────────────────────────

        [TestCase("Street Fighter x Tekken PS3")]
        [TestCase("Gran Turismo 7")]
        [TestCase("The Last of Us Part II")]
        [TestCase("")]
        public void DetectContentType_MainGame(string input)
        {
            var (type, version, dlcName) = PostDownloadProcessor.DetectContentType(input);
            Assert.That(type, Is.EqualTo(PostDownloadProcessor.DownloadContentType.MainGame));
            Assert.That(version, Is.Null);
            Assert.That(dlcName, Is.Null);
        }

        // ── BuildPatchFileName ────────────────────────────────────────

        [TestCase("Street Fighter x Tekken", "1.02", ".pkg", "Street Fighter x Tekken-Patch-v1.02.pkg")]
        [TestCase("Gran Turismo 7", "3.0.1", ".pkg", "Gran Turismo 7-Patch-v3.0.1.pkg")]
        [TestCase("MyGame", null, ".iso", "MyGame-Patch.iso")]
        public void BuildPatchFileName_Correct(string gameTitle, string? version, string ext, string expected)
        {
            var result = PostDownloadProcessor.BuildPatchFileName(gameTitle, version, ext);
            Assert.That(result, Is.EqualTo(expected));
        }

        // ── BuildDlcFileName ──────────────────────────────────────────

        [TestCase("Street Fighter x Tekken", "Season Pass", ".pkg", "Street Fighter x Tekken-DLC-Season Pass.pkg")]
        [TestCase("MyGame", "Map Pack", ".iso", "MyGame-DLC-Map Pack.iso")]
        [TestCase("MyGame", null, ".pkg", "MyGame-DLC.pkg")]
        public void BuildDlcFileName_Correct(string gameTitle, string? dlcName, string ext, string expected)
        {
            var result = PostDownloadProcessor.BuildDlcFileName(gameTitle, dlcName, ext);
            Assert.That(result, Is.EqualTo(expected));
        }
    }
}
