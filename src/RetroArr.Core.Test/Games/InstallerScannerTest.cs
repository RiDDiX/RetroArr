using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // GET /rename/installers and /rename/updates: the extras on disk matched to the games of their platform
    [TestFixture]
    public class InstallerScannerTest
    {
        private const int Snes = 41;
        private const int Switch = 46;
        private string _root = null!;

        [SetUp]
        public void SetUp() => _root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "retroarr_installers_" + Path.GetRandomFileName())).FullName;

        [TearDown]
        public void TearDown()
        {
            if (Directory.Exists(_root)) Directory.Delete(_root, true);
        }

        [Test]
        public async Task Extras_MatchOnlyAGameOfTheirPlatform()
        {
            var lib = Path.Combine(_root, "library");
            foreach (var file in new[] { Path.Combine(lib, "switch", "updates", "Zelda Tears [0100F2C0115B6000][v1.2.0].nsp"), Path.Combine(lib, "gog", "downloads", "Quake", "setup_quake_1.0.exe") })
            {
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, "data");
            }
            Directory.CreateDirectory(Path.Combine(_root, "config"));
            var config = new ConfigurationService(_root);
            config.SaveMediaSettings(new MediaSettings { FolderPath = lib });

            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
            using (var ctx = new RetroArrDbContext(options))
            {
                ctx.Games.AddRange(
                    new Game { Id = 1, Title = "Zelda Tears", PlatformId = Snes },
                    new Game { Id = 2, Title = "Zelda Tears", PlatformId = Switch, Path = Path.Combine(lib, "switch", "Zelda Tears [0100F2C0115B6000]") },
                    new Game { Id = 3, Title = "Quake", PlatformId = Snes },
                    new Game { Id = 4, Title = "Quake", PlatformId = PlatformDefinitions.GogPlatformId });
                await ctx.SaveChangesAsync();
            }
            var scanner = new InstallerScannerService(config, new PooledDbContextFactory<RetroArrDbContext>(options));

            var updates = await scanner.ScanSwitchUpdatesAsync();
            Assert.That(updates.Select(u => (u.TitleId, u.MatchedGame?.Id)), Is.EqualTo(new[] { ((string?)"0100F2C0115B6000", (int?)2) }));
            var installers = await scanner.ScanGogInstallersAsync();
            Assert.That(installers.Select(i => (i.FolderName, i.MatchedGame?.Id)), Is.EqualTo(new[] { ("Quake", (int?)4) }));
        }
    }
}
