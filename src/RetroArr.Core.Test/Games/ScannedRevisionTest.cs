using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class ScannedRevisionTest
    {
        private const int Snes = 41;

        private sealed class DbFactory : IDbContextFactory<RetroArrDbContext>
        {
            private readonly DbContextOptions<RetroArrDbContext> _options;
            public DbFactory(DbContextOptions<RetroArrDbContext> options) => _options = options;
            public RetroArrDbContext CreateDbContext() => new RetroArrDbContext(_options);
        }

        // Both games already exist, so no candidate needs metadata
        private sealed class NoMetadata : IGameMetadataServiceFactory
        {
            public GameMetadataService CreateService() => null!;
            public void RefreshConfiguration() { }
        }

        private string _root = null!;

        [SetUp]
        public void SetUp() => _root = Path.Combine(Path.GetTempPath(), "retroarr_revision_" + Path.GetRandomFileName());

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public async Task Scan_NumericRevisionTag_FillsOnlyAnEmptyRevision()
        {
            var lib = Directory.CreateDirectory(Path.Combine(_root, "library")).FullName;
            var snes = Directory.CreateDirectory(Path.Combine(lib, "snes")).FullName;
            var mario = Path.Combine(snes, "Super Mario World (USA) (Rev 1).sfc");
            var zelda = Path.Combine(snes, "Zelda (USA) (Rev 1).sfc");
            File.WriteAllText(mario, "rom");
            File.WriteAllText(zelda, "rom");

            var db = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using (var ctx = new RetroArrDbContext(db))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Super Mario World", PlatformId = Snes, Path = mario, ExecutablePath = mario, Status = GameStatus.Downloaded },
                    new Game { Id = 2, Title = "Zelda", PlatformId = Snes, Path = zelda, ExecutablePath = zelda, Status = GameStatus.Downloaded, Revision = "Rev A" });
                await ctx.SaveChangesAsync();
            }

            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = lib, DestinationPath = lib });
            await new MediaScannerService(config, new NoMetadata(), new SqliteGameRepository(new DbFactory(db)), new TitleCleanerService()).ScanAsync();

            using var check = new RetroArrDbContext(db);
            var rows = check.Games.AsNoTracking().OrderBy(g => g.Id).ToList();
            Assert.That(rows.Select(g => g.Revision), Is.EqualTo(new[] { "Rev 1", "Rev A" }));
        }
    }
}
