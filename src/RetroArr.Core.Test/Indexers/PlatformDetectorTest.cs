using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;
using RetroArr.Core.Indexers;
using RetroArr.Core.Prowlarr;
using RetroArr.Core.Search;

namespace RetroArr.Core.Test.Indexers
{
    [TestFixture]
    public class PlatformDetectorTest
    {
        private static SearchResult Detect(string title, params int[] categories)
        {
            var result = new SearchResult { Title = title, Protocol = "nzb" };
            foreach (var id in categories)
                result.Categories.Add(new ProwlarrCategory { Id = id, Name = id.ToString() });
            PlatformDetector.DetectPlatform(result);
            return result;
        }

        // Trackers without DS / 360 categories file those releases under
        // Console (1000), Console/Other (1090), Wii (1030) or Xbox (1040).
        [TestCase("Pokemon Platinum Version [NDS] (EUR)", 1090, "nds")]
        [TestCase("Pokemon HeartGold Version [NDS] (USA)", 1000, "nds")]
        [TestCase("Professor Layton and the Curious Village [NDS] [USA]", 1030, "nds")]
        [TestCase("Halo 3 X360-COMPLEX", 1040, "xbox360")]
        [TestCase("Forza Motorsport 4 Xbox 360 RF", 1040, "xbox360")]
        [TestCase("Super Smash Bros Melee (GameCube) NTSC", 1030, "gamecube")]
        [TestCase("Final Fantasy Tactics The War of the Lions PlayStation Portable", 1020, "psp")]
        // Category still decides when the title has no tag or names another vendor
        [TestCase("Mario Kart DS (Europe)", 1010, "nds")]
        [TestCase("Fable The Lost Chapters", 1040, "xbox")]
        [TestCase("Sonics Ultimate Genesis Collection", 1050, "xbox360")]
        [TestCase("Forza Horizon 5 Xbox Game Pass PC", 4050, "windows")]
        [TestCase("Genesis Noir-CODEX", 4050, "windows")]
        [TestCase("Pokemon Legends Arceus NSW-BigBlueBox", 1000, "switch")]
        // Plain platform words in a game name don't overrule the category
        [TestCase("Wii Fit U", 1130, "wiiu")]
        [TestCase("NES Remix Pack", 1130, "wiiu")]
        [TestCase("PlayStation All-Stars Battle Royale", 1080, "ps3")]
        [TestCase("Castle Crashers Xbox Live Arcade", 1050, "xbox360")]
        [TestCase("Mario Kart 8 Deluxe (Nintendo Switch)", 1130, "switch")]
        // Retro games repackaged for the category's console stay there
        [TestCase("Super Mario 64 N64 VC WAD", 1060, "wii")]
        [TestCase("EarthBound SNES VC WUP", 1130, "wiiu")]
        [TestCase("Jak and Daxter PS2 Classic FPKG", 1180, "ps4")]
        // Native package formats are no repack
        [TestCase("Bloodborne PS4 FPKG", 1080, "ps4")]
        [TestCase("Mario Kart 8 Wii U WUP", 1030, "wiiu")]
        [TestCase("Pokemon Picross 3DS eShop CIA", 1010, "3ds")]
        [TestCase("Sonic Classic Collection NDS", 1030, "nds")]
        [TestCase("Metal Gear Solid PS1 Classic PKG", 1080, "ps3")]
        // A real tag later in the title beats a plain word earlier on
        [TestCase("1-2-Switch.NSW-BigBlueBox", 1130, "switch")]
        [TestCase("Wii Party (Europe) WBFS", 1000, "wii")]
        [TestCase("Beat Saber PS4 PS5 compatible", 1000, "ps4")]
        [TestCase("Sonic the Hedgehog Sega Genesis", 1000, "megadrive")]
        [TestCase("La Dolce Vita PlayStation Vita", 1020, "vita")]
        // Remakes, ports and compatibility notes name the platform they came from
        [TestCase("Crash Bandicoot N Sane Trilogy PS1 Remake", 1180, "ps4")]
        [TestCase("Chrono Trigger DS (SNES port)", 1010, "nds")]
        [TestCase("Halo 2 PAL Xbox 360 compatible", 1040, "xbox")]
        [TestCase("Bit Trip Beat WiiWare 3DS", 1060, "wii")]
        [TestCase("Jak and Daxter Collection PS2 HD Remaster", 1080, "ps3")]
        [TestCase("Shadow of the Colossus PS2 Remastered", 1180, "ps4")]
        [TestCase("Red Dead Redemption Xbox One Backward Compatible", 1050, "xbox360")]
        [TestCase("Red Dead Redemption 2 Xbox Series X compatible", 1140, "xboxone")]
        [TestCase("Super Mario Odyssey NSW (Nintendo Switch 2 compatible)", 1130, "switch")]
        // ...but the same words elsewhere in a name don't stop the tag
        [TestCase("Final Fantasy VII Remake Intergrade PS5", 1180, "ps5")]
        [TestCase("The Last of Us Remastered PS4", 1080, "ps4")]
        [TestCase("Metroid Prime Remastered NSW", 1030, "switch")]
        [TestCase("Dance Central 3 X360 Kinect Compatible", 1040, "xbox360")]
        [TestCase("Port Royale 3 X360", 1040, "xbox360")]
        [TestCase("Halo 3 ODST X360 Bonus Disc", 1040, "xbox360")]
        [TestCase("ACA NeoGeo Metal Slug NSW", 1130, "switch")]
        [TestCase("Donkey Kong Bananza (Nintendo Switch 2)", 1000, "switch2")]
        // Underscore scene names
        [TestCase("Halo_Reach_PAL_XBOX360-SPARE", 1040, "xbox360")]
        [TestCase("Pokemon_Platinum_Version_USA_NDS-XPA", 1090, "nds")]
        // A title naming the category's own platform keeps it
        [TestCase("Astro Bot PS4 PS5 Upgrade", 1180, "ps4")]
        public void TitleTagAndCategory_Precedence(string title, int category, string expectedFolder)
        {
            Assert.That(Detect(title, category).PlatformFolder, Is.EqualTo(expectedFolder));
        }

        [Test]
        public void SpecificCategory_WinsOverGenericParent()
        {
            Assert.That(Detect("New Super Mario Bros (Europe)", 1000, 1010).PlatformFolder, Is.EqualTo("nds"));
        }

        // Under a generic console category a PC group name, a PC word or a plain platform word
        // in the game name is no answer
        [TestCase("Pokemon Platinum (Europe)", 1000)]
        [TestCase("Pokemon Platinum (Europe)", 1090)]
        [TestCase("Rune Factory 5 (Europe)", 1000)]
        [TestCase("GoldenEye 007 Reloaded", 1090)]
        [TestCase("PC Building Simulator", 1000)]
        [TestCase("Wii Fit U (USA)", 1000)]
        [TestCase("PlayStation All-Stars Battle Royale", 1000)]
        [TestCase("Genesis Noir", 1000)]
        [TestCase("Capcom Arcade Stadium", 1090)]
        [TestCase("Okami HD PS4 Remaster PS2 original", 1000)]
        [TestCase("SEGA Genesis Classics (Switch)", 1000)]
        [TestCase("ACA NeoGeo Metal Slug (EUR)", 1000)]
        [TestCase("Super Mario 64 N64 VC WAD", 1000)]
        [TestCase("Metal Gear Solid PS1 Classic PKG", 1090)]
        public void GenericCategory_WithoutConsoleTag_IsUnknown(string title, int category)
        {
            Assert.That(Detect(title, category).PlatformFolder, Is.EqualTo("unknown"));
        }

        [Test]
        public void ConsoleAndPcCategory_TitleDecides()
        {
            Assert.That(Detect("Hades NSW-VENOM", 1000, 4050).PlatformFolder, Is.EqualTo("switch"));
            Assert.That(Detect("Hades-RUNE", 1000, 4050).PlatformFolder, Is.EqualTo("windows"));
            Assert.That(Detect("Cyberpunk.2077-GOG", 1010, 4050).PlatformFolder, Is.EqualTo("windows"));
            Assert.That(Detect("Cyberpunk.2077-GOG", 4050, 1010).PlatformFolder, Is.EqualTo("windows"));
            // a plain word in the name is no answer here either
            Assert.That(Detect("Genesis Alpha One-CODEX", 1000, 4000).PlatformFolder, Is.EqualTo("unknown"));
        }

        [Test]
        public void NoCategory_TitleStillDecides()
        {
            Assert.That(Detect("Rune Factory 5-RUNE").PlatformFolder, Is.EqualTo("windows"));
            Assert.That(Detect("Rune Factory 5 (Switch)").PlatformFolder, Is.EqualTo("switch"));
            Assert.That(Detect("SEGA Genesis Classics (Switch)").PlatformFolder, Is.EqualTo("switch"));
            Assert.That(Detect("Zelda Tears of the Kingdom NSW (Switch 2 compatible)").PlatformFolder, Is.EqualTo("switch"));
            Assert.That(Detect("Super Mario Odyssey NSW (Nintendo Switch 2 compatible)").PlatformFolder, Is.EqualTo("switch"));
            Assert.That(Detect("Sonic Mania PS4 port of Genesis classics").PlatformFolder, Is.EqualTo("ps4"));
            Assert.That(Detect("PlayStation VR Worlds PS4 Remastered").PlatformFolder, Is.EqualTo("ps4"));
            Assert.That(Detect("Terraria Switch 2.0 Update").PlatformFolder, Is.EqualTo("switch"));
        }

        [Test]
        public void UnknownPlatform_IsKeptForReview_NeverAutoDownloaded()
        {
            var settings = MonitorSettings.CreateDefault();
            settings.AutoDownloadThreshold = 50;
            var game = new Game
            {
                Title = "Pokemon Platinum",
                Platform = PlatformDefinitions.AllPlatforms.First(p => p.FolderName == "nds")
            };
            var scorer = new ReleaseScorer();

            var tagged = scorer.Score(Detect("Pokemon Platinum (Europe) [NDS] No-Intro", 1090), game, settings);
            var untagged = scorer.Score(Detect("Pokemon Platinum (Europe) No-Intro", 1090), game, settings);

            Assert.That(tagged.Decision, Is.EqualTo(ReleaseDecision.AutoDownload), tagged.Reason);
            Assert.That(untagged.Decision, Is.EqualTo(ReleaseDecision.Review), untagged.Reason);
            Assert.That(untagged.Score, Is.EqualTo(tagged.Score));
        }
    }
}
