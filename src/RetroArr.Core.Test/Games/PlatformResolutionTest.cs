using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class PlatformResolutionTest
    {
        // ==================== MatchesFolderName - Folder-based detection ====================

        [TestCase("gamecube", 43, "Nintendo GameCube")]
        [TestCase("gb", 50, "Game Boy")]
        [TestCase("gba", 52, "Game Boy Advance")]
        [TestCase("gbc", 51, "Game Boy Color")]
        [TestCase("nes", 40, "Nintendo Entertainment System")]
        [TestCase("snes", 41, "Super Nintendo (SNES)")]
        [TestCase("n64", 42, "Nintendo 64")]
        [TestCase("wii", 44, "Nintendo Wii")]
        [TestCase("wiiu", 45, "Nintendo Wii U")]
        [TestCase("switch", 46, "Nintendo Switch")]
        [TestCase("nds", 53, "Nintendo DS")]
        [TestCase("3ds", 54, "Nintendo 3DS")]
        [TestCase("ps1", 20, "PlayStation 1")]
        [TestCase("ps2", 21, "PlayStation 2")]
        [TestCase("ps3", 22, "PlayStation 3")]
        [TestCase("ps4", 23, "PlayStation 4")]
        [TestCase("ps5", 24, "PlayStation 5")]
        [TestCase("psp", 25, "PlayStation Portable")]
        [TestCase("megadrive", 62, "Mega Drive / Genesis")]
        [TestCase("dreamcast", 67, "Dreamcast")]
        [TestCase("saturn", 66, "Sega Saturn")]
        [TestCase("mastersystem", 61, "Master System")]
        [TestCase("xbox360", 31, "Xbox 360")]
        [TestCase("arcade", 100, "Arcade (MAME)")]
        public void FolderName_ResolvesToCorrectPlatformId(string folderName, int expectedId, string expectedName)
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(folderName));
            Assert.That(platform, Is.Not.Null, $"No platform matched folder '{folderName}'");
            Assert.That(platform!.Id, Is.EqualTo(expectedId), $"Folder '{folderName}' resolved to '{platform.Name}' (Id={platform.Id}) instead of '{expectedName}' (Id={expectedId})");
        }

        // ==================== Batocera / RetroBat folder names ====================

        [TestCase("megacd", 63)]
        [TestCase("amiga500", 4)]
        [TestCase("amigacd32", 5)]
        [TestCase("c20", 7)]
        [TestCase("msx1", 9)]
        [TestCase("psvita", 26)]
        [TestCase("mame", 100)]
        [TestCase("sega32x", 64)]
        public void BatoceraRetroBatFolderName_ResolvesToCorrectPlatformId(string folderName, int expectedId)
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(folderName));
            Assert.That(platform, Is.Not.Null, $"No platform matched Batocera/RetroBat folder '{folderName}'");
            Assert.That(platform!.Id, Is.EqualTo(expectedId));
        }

        // ==================== REGRESSION: Switch and Switch 2 must resolve to separate platforms ====================

        [Test]
        public void Switch_FolderName_ResolvesToSwitch_NotSwitch2()
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName("switch"));
            Assert.That(platform, Is.Not.Null);
            Assert.That(platform!.Id, Is.EqualTo(46), "Folder 'switch' must resolve to Nintendo Switch (Id=46), not Switch 2");
        }

        [Test]
        public void Switch2_FolderName_ResolvesToSwitch2()
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName("switch2"));
            Assert.That(platform, Is.Not.Null, "No platform matched folder 'switch2'");
            Assert.That(platform!.Id, Is.EqualTo(47), "Folder 'switch2' must resolve to Nintendo Switch 2 (Id=47)");
        }

        [Test]
        public void Switch2_HasOwnFolderName_NotSharedWithSwitch()
        {
            var sw = PlatformDefinitions.AllPlatforms.First(p => p.Id == 46);
            var sw2 = PlatformDefinitions.AllPlatforms.First(p => p.Id == 47);
            Assert.That(sw.FolderName, Is.EqualTo("switch"));
            Assert.That(sw2.FolderName, Is.EqualTo("switch2"));
            Assert.That(sw.FolderName, Is.Not.EqualTo(sw2.FolderName), "Switch and Switch 2 must not share the same FolderName");
        }

        // ==================== REGRESSION: GameCube must NEVER resolve to PS4 ====================

        [Test]
        public void GameCube_FolderName_NeverResolvesToPS4()
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName("gamecube"));
            Assert.That(platform, Is.Not.Null);
            Assert.That(platform!.Id, Is.Not.EqualTo(23), "GameCube folder resolved to PS4 (Id=23)!");
            Assert.That(platform.Id, Is.EqualTo(43), "GameCube folder must resolve to GameCube (Id=43)");
            Assert.That(platform.Type, Is.EqualTo(PlatformType.GameCube));
        }

        [Test]
        public void GameCube_Slug_NeverResolvesToPS4()
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "gamecube");
            Assert.That(platform, Is.Not.Null);
            Assert.That(platform!.Id, Is.EqualTo(43));
            Assert.That(platform.Type, Is.EqualTo(PlatformType.GameCube));
        }

        // ==================== No folder name collisions between unrelated platforms ====================

        [Test]
        public void FolderNames_NoCrossPlatformCollisions()
        {
            var allPlatforms = PlatformDefinitions.AllPlatforms;

            // Known intentional overlaps: DOSBox uses RetroBat folder "dos" which matches MS-DOS
            var knownOverlaps = new System.Collections.Generic.HashSet<(int, int)>
            {
                (3, 121), (121, 3) // MS-DOS <-> DOSBox
            };

            foreach (var p in allPlatforms)
            {
                var matches = allPlatforms.Where(other => other.MatchesFolderName(p.FolderName)).ToList();
                
                foreach (var match in matches)
                {
                    bool isSameOrRelated = match.Id == p.Id 
                        || match.ParentPlatformId == p.Id 
                        || p.ParentPlatformId == match.Id
                        || match.FolderName == p.FolderName
                        || knownOverlaps.Contains((p.Id, match.Id));
                    Assert.That(isSameOrRelated, Is.True,
                        $"Folder '{p.FolderName}' for {p.Name} (Id={p.Id}) also matches unrelated platform {match.Name} (Id={match.Id})");
                }
            }
        }

        // ==================== Extension-based platform guessing ====================

        [Test]
        public void GetPlatformFromExtension_Pkg_DoesNotReturnPs4()
        {
            var sut = new TitleCleanerService();
            var result = sut.GetPlatformFromExtension(".pkg");
            Assert.That(result, Is.Not.EqualTo("ps4"), ".pkg must not hardcode to ps4 (used by PS3, PS5, macOS)");
            Assert.That(result, Is.EqualTo("default"), ".pkg should return 'default' (ambiguous extension)");
        }

        [Test]
        public void GetPlatformFromExtension_Iso_ReturnsDefault()
        {
            var sut = new TitleCleanerService();
            var result = sut.GetPlatformFromExtension(".iso");
            Assert.That(result, Is.EqualTo("default"), ".iso is ambiguous (GameCube, PS2, Saturn, etc.)");
        }

        [TestCase(".nsp", "nintendo_switch")]
        [TestCase(".xci", "nintendo_switch")]
        [TestCase(".nsz", "nintendo_switch")]
        [TestCase(".xcz", "nintendo_switch")]
        [TestCase(".dmg", "macos")]
        [TestCase(".z64", "nintendo_64")]
        [TestCase(".n64", "nintendo_64")]
        [TestCase(".sfc", "snes")]
        [TestCase(".smc", "snes")]
        [TestCase(".nes", "nes")]
        [TestCase(".gb", "gb")]
        [TestCase(".gbc", "gbc")]
        [TestCase(".gba", "gba")]
        [TestCase(".pce", "pc_engine")]
        public void GetPlatformFromExtension_UnambiguousExtensions_ResolveCorrectly(string ext, string expectedKey)
        {
            var sut = new TitleCleanerService();
            Assert.That(sut.GetPlatformFromExtension(ext), Is.EqualTo(expectedKey));
        }

        [TestCase(".iso")]
        [TestCase(".bin")]
        [TestCase(".cue")]
        [TestCase(".chd")]
        [TestCase(".pkg")]
        [TestCase(".zip")]
        [TestCase(".7z")]
        public void GetPlatformFromExtension_AmbiguousExtensions_ReturnDefault(string ext)
        {
            var sut = new TitleCleanerService();
            Assert.That(sut.GetPlatformFromExtension(ext), Is.EqualTo("default"),
                $"Ambiguous extension '{ext}' should return 'default', not a specific platform");
        }

        // ==================== Case insensitivity ====================

        [TestCase("GameCube")]
        [TestCase("GAMECUBE")]
        [TestCase("Gamecube")]
        [TestCase("gamecube")]
        public void MatchesFolderName_CaseInsensitive(string folderName)
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(folderName));
            Assert.That(platform, Is.Not.Null, $"Folder '{folderName}' should match GameCube");
            Assert.That(platform!.Id, Is.EqualTo(43));
        }

        [TestCase("PS4")]
        [TestCase("Ps4")]
        [TestCase("ps4")]
        public void MatchesFolderName_PS4_CaseInsensitive(string folderName)
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(folderName));
            Assert.That(platform, Is.Not.Null, $"Folder '{folderName}' should match PS4");
            Assert.That(platform!.Id, Is.EqualTo(23));
        }
    }
}
