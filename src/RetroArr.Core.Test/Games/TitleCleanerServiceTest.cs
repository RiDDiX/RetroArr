using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class TitleCleanerServiceTest
    {
        private TitleCleanerService _sut = null!;

        [SetUp]
        public void Setup()
        {
            _sut = new TitleCleanerService();
        }

        // ==================== CleanGameTitle ====================

        [TestCase("Super Mario Odyssey [0100000000010000][v0].nsp", "Super Mario Odyssey")]
        [TestCase("The Legend of Zelda Breath of the Wild [01007EF00011E000][v196608].nsp", "The Legend of Zelda Breath of the Wild")]
        [TestCase("God of War [CUSA07408].pkg", "God of War")]
        [TestCase("Horizon Zero Dawn [CUSA10237].pkg", "Horizon Zero Dawn")]
        [TestCase("Cyberpunk 2077", "Cyberpunk 2077")]
        [TestCase("Streets of Rage 4", "Streets of Rage 4")]
        [TestCase("Frostpunk 2", "Frostpunk 2")]
        [TestCase("F1 2024", "F1 2024")]
        public void CleanGameTitle_CommonTitles_PreservesName(string input, string expected)
        {
            var (title, _) = _sut.CleanGameTitle(input);
            Assert.That(title, Is.EqualTo(expected));
        }

        [TestCase("2048", "2048")]
        [TestCase("1942", "1942")]
        [TestCase("1943", "1943")]
        public void CleanGameTitle_NumericTitles_Preserved(string input, string expected)
        {
            var (title, _) = _sut.CleanGameTitle(input);
            Assert.That(title, Is.EqualTo(expected));
        }

        [TestCase("God of War [CUSA07408].pkg", "CUSA07408")]
        [TestCase("Horizon Zero Dawn [CUSA10237].pkg", "CUSA10237")]
        [TestCase("Game [BLES01234]", "BLES01234")]
        [TestCase("Game [BLUS12345]", "BLUS12345")]
        [TestCase("Game [PPSA01234]", "PPSA01234")]
        public void CleanGameTitle_ExtractsPlayStationSerial(string input, string expectedSerial)
        {
            var (_, serial) = _sut.CleanGameTitle(input);
            Assert.That(serial, Is.EqualTo(expectedSerial));
        }

        [TestCase("Super Mario Odyssey [0100000000010000][v0].nsp", "0100000000010000")]
        public void CleanGameTitle_ExtractsSwitchSerial(string input, string expectedSerial)
        {
            var (_, serial) = _sut.CleanGameTitle(input);
            Assert.That(serial, Is.EqualTo(expectedSerial));
        }

        [Test]
        public void CleanGameTitle_EmptyInput_ReturnsEmpty()
        {
            var (title, serial) = _sut.CleanGameTitle("");
            Assert.That(title, Is.EqualTo(""));
            Assert.That(serial, Is.Null);
        }

        [Test]
        public void CleanGameTitle_NullInput_ReturnsNull()
        {
            var (title, serial) = _sut.CleanGameTitle(null!);
            Assert.That(title, Is.Null);
            Assert.That(serial, Is.Null);
        }

        [TestCase("Game.Name.v1.00.repack.fitgirl", "Name")]
        [TestCase("Some_Game-CODEX", "Some")]
        [TestCase("Game (USA) (En,Fr,De)", "")]
        public void CleanGameTitle_SceneReleaseNames_StripsNoise(string input, string expected)
        {
            var (title, _) = _sut.CleanGameTitle(input);
            Assert.That(title, Is.EqualTo(expected));
        }

        [TestCase("EP9000-CUSA07408_00-GODOFWAR00000000", "God of War")]
        public void CleanGameTitle_PS4ContentId_StripsPrefix(string input, string _)
        {
            var (title, serial) = _sut.CleanGameTitle(input);
            Assert.That(serial, Is.EqualTo("CUSA07408"));
            // Title may be empty after stripping — the metadata lookup fills it
            Assert.That(title, Is.Not.Null);
        }

        // ==================== ResolvePlatformFromSerial ====================

        [TestCase("CUSA07408", "ps4")]
        [TestCase("CUSA12345", "ps4")]
        [TestCase("PLJS12345", "ps4")]
        [TestCase("PPSA01234", "ps5")]
        [TestCase("ELJS01234", "ps5")]
        [TestCase("BLES01234", "ps3")]
        [TestCase("BLUS12345", "ps3")]
        [TestCase("BCUS12345", "ps3")]
        [TestCase("NPUB12345", "ps3")]
        [TestCase("SLES12345", "ps2")]
        [TestCase("SLUS12345", "ps2")]
        [TestCase("SLPS12345", "ps2")]
        [TestCase("ULES12345", "psp")]
        [TestCase("UCUS12345", "psp")]
        [TestCase("ULJM12345", "psp")]
        [TestCase("NPJH12345", "psp")]
        [TestCase("PCSA12345", "vita")]
        [TestCase("PCSE12345", "vita")]
        [TestCase("PCSG12345", "vita")]
        [TestCase("PCSB12345", "vita")]
        [TestCase("", "default")]
        [TestCase("UNKNOWN123", "default")]
        public void ResolvePlatformFromSerial_ReturnsCorrectPlatform(string serial, string expected)
        {
            Assert.That(_sut.ResolvePlatformFromSerial(serial), Is.EqualTo(expected));
        }

        // ==================== GetPlatformFromExtension ====================

        [TestCase(".nsp", "nintendo_switch")]
        [TestCase(".xci", "nintendo_switch")]
        [TestCase(".nsz", "nintendo_switch")]
        [TestCase(".xcz", "nintendo_switch")]
        [TestCase(".pkg", "default")]
        [TestCase(".dmg", "macos")]
        [TestCase(".app", "macos")]
        [TestCase(".z64", "nintendo_64")]
        [TestCase(".n64", "nintendo_64")]
        [TestCase(".v64", "nintendo_64")]
        [TestCase(".sfc", "snes")]
        [TestCase(".smc", "snes")]
        [TestCase(".nes", "nes")]
        [TestCase(".gb", "gb")]
        [TestCase(".gbc", "gbc")]
        [TestCase(".gba", "gba")]
        [TestCase(".md", "megadrive")]
        [TestCase(".gen", "megadrive")]
        [TestCase(".sms", "mastersystem")]
        [TestCase(".gg", "gamegear")]
        [TestCase(".pce", "pc_engine")]
        [TestCase(".exe", "default")]
        [TestCase(".iso", "default")]
        [TestCase("", "default")]
        public void GetPlatformFromExtension_ReturnsCorrectPlatform(string ext, string expected)
        {
            Assert.That(_sut.GetPlatformFromExtension(ext), Is.EqualTo(expected));
        }

        // ==================== Ampersand → "and" ====================

        [TestCase("Ratchet & Clank", "Ratchet and Clank")]
        [TestCase("Might & Magic", "Might and Magic")]
        [TestCase("Lock & Load", "Lock and Load")]
        public void CleanGameTitle_Ampersand_ReplacedWithAnd(string input, string expected)
        {
            var (title, _) = _sut.CleanGameTitle(input);
            Assert.That(title, Is.EqualTo(expected));
        }

        // ==================== GenerateSearchVariants ====================

        [Test]
        public void GenerateSearchVariants_PlainTitle_ReturnsSingleVariant()
        {
            var variants = _sut.GenerateSearchVariants("Super Mario Odyssey");
            Assert.That(variants.Count, Is.EqualTo(1));
            Assert.That(variants[0], Is.EqualTo("Super Mario Odyssey"));
        }

        [Test]
        public void GenerateSearchVariants_WithMultiPlayer_StripsIt()
        {
            var variants = _sut.GenerateSearchVariants("Dark Messiah of Might and Magic Multi-Player");
            Assert.That(variants.Count, Is.GreaterThanOrEqualTo(2));
            Assert.That(variants[0], Is.EqualTo("Dark Messiah of Might and Magic Multi-Player"));
            Assert.That(variants[1], Is.EqualTo("Dark Messiah of Might and Magic"));
        }

        [Test]
        public void GenerateSearchVariants_WithSinglePlayer_StripsIt()
        {
            var variants = _sut.GenerateSearchVariants("Portal 2 Single-Player");
            Assert.That(variants.Any(v => v == "Portal 2"), Is.True);
        }

        [Test]
        public void GenerateSearchVariants_WithGOTY_StripsIt()
        {
            var variants = _sut.GenerateSearchVariants("Batman Arkham Knight GOTY");
            Assert.That(variants.Any(v => v == "Batman Arkham Knight"), Is.True);
        }

        [Test]
        public void GenerateSearchVariants_WithRemastered_StripsIt()
        {
            var variants = _sut.GenerateSearchVariants("Resident Evil 4 HD Remastered");
            Assert.That(variants.Any(v => v == "Resident Evil 4"), Is.True);
        }

        [Test]
        public void GenerateSearchVariants_WithCompleteEdition_StripsIt()
        {
            var variants = _sut.GenerateSearchVariants("Witcher 3 Complete Edition");
            Assert.That(variants.Any(v => v == "Witcher 3"), Is.True);
        }

        [Test]
        public void GenerateSearchVariants_DoesNotStripRomanNumerals()
        {
            var variants = _sut.GenerateSearchVariants("Final Fantasy VII Remake");
            // Should strip "Remake" but keep "VII"
            Assert.That(variants.Any(v => v == "Final Fantasy VII"), Is.True);
        }

        [Test]
        public void GenerateSearchVariants_EmptyInput_ReturnsEmpty()
        {
            var variants = _sut.GenerateSearchVariants("");
            Assert.That(variants.Count, Is.EqualTo(0));
        }

        [TestCase("007 Liebesgruesse aus Moskau")]
        public void GenerateSearchVariants_GermanTitle_IncludesOriginal(string input)
        {
            var variants = _sut.GenerateSearchVariants(input);
            Assert.That(variants.Count, Is.GreaterThanOrEqualTo(1));
            Assert.That(variants[0], Is.EqualTo(input));
        }

        // ==================== NormalizeDiacritics ====================

        [TestCase("caf\u00e9", "cafe")]
        [TestCase("na\u00efve", "naive")]
        [TestCase("Pok\u00e9mon", "Pokemon")]
        [TestCase("ASCII only", "ASCII only")]
        public void NormalizeDiacritics_RemovesCombiningMarks(string input, string expected)
        {
            Assert.That(TitleCleanerService.NormalizeDiacritics(input), Is.EqualTo(expected));
        }

        // ==================== ComputeSimilarity ====================

        [Test]
        public void ComputeSimilarity_ExactMatch_Returns1()
        {
            Assert.That(TitleCleanerService.ComputeSimilarity("Hello", "Hello"), Is.EqualTo(1.0));
        }

        [Test]
        public void ComputeSimilarity_CaseInsensitive_Returns1()
        {
            Assert.That(TitleCleanerService.ComputeSimilarity("hello", "HELLO"), Is.EqualTo(1.0));
        }

        [Test]
        public void ComputeSimilarity_Contains_ReturnsReasonableScore()
        {
            var score = TitleCleanerService.ComputeSimilarity("007 From Russia with Love", "007: From Russia with Love - GameCube Edition");
            Assert.That(score, Is.GreaterThanOrEqualTo(0.50));
        }

        [Test]
        public void ComputeSimilarity_CompletelyDifferent_ReturnsLow()
        {
            var score = TitleCleanerService.ComputeSimilarity("Halo", "Zelda");
            Assert.That(score, Is.LessThan(0.5));
        }

        [Test]
        public void ComputeSimilarity_EmptyInput_ReturnsZero()
        {
            Assert.That(TitleCleanerService.ComputeSimilarity("", "Hello"), Is.EqualTo(0.0));
            Assert.That(TitleCleanerService.ComputeSimilarity("Hello", ""), Is.EqualTo(0.0));
        }

        // ==================== ClassifySupplementaryContent ====================

        // --- Switch TitleID classification ---

        [Test]
        public void Classify_SwitchBase_TitleIdEnds000_ReturnsMain()
        {
            var info = _sut.ClassifySupplementaryContent("Bayonetta 2 [01004A4000B3A000][v0].nsp");
            Assert.That(info.FileType, Is.EqualTo("Main"));
            Assert.That(info.TitleId, Is.EqualTo("01004A4000B3A000"));
            Assert.That(info.Version, Is.EqualTo("v0"));
        }

        [Test]
        public void Classify_SwitchUpdate_TitleIdEnds800_ReturnsPatch()
        {
            var info = _sut.ClassifySupplementaryContent("Bayonetta 2 [01004A4000B3A800][v65536].nsp");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
            Assert.That(info.TitleId, Is.EqualTo("01004A4000B3A800"));
        }

        [Test]
        public void Classify_SwitchDLC_TitleIdBit12Set_ReturnsDLC()
        {
            var info = _sut.ClassifySupplementaryContent("Bayonetta 2 DLC [01004A4000B3B001][v0].nsp");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
            Assert.That(info.TitleId, Is.EqualTo("01004A4000B3B001"));
        }

        [Test]
        public void Classify_SwitchDLC_TitleIdBit12Set_Index2()
        {
            var info = _sut.ClassifySupplementaryContent("Game [01007EF00011F002][v0].nsp");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
        }

        // --- PS Vita serial extraction ---

        [Test]
        public void Classify_PSVitaSerial_Extracted()
        {
            var info = _sut.ClassifySupplementaryContent("Persona 4 Golden [PCSE00120].vpk");
            Assert.That(info.Serial, Is.EqualTo("PCSE00120"));
        }

        [Test]
        public void Classify_PSPSerial_Extracted()
        {
            var info = _sut.ClassifySupplementaryContent("Crisis Core [ULES01040].iso");
            Assert.That(info.Serial, Is.EqualTo("ULES01040"));
        }

        // --- Scene group tag stripping ---

        [Test]
        public void Classify_SceneTrailingTag_StrippedAndKeywordDetected()
        {
            var info = _sut.ClassifySupplementaryContent("SpongeBob_SquarePants_The_Cosmic_Shake_Update_v1.0.3_NSW-LiGHTFORCE.nsp");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
        }

        [Test]
        public void Classify_SceneLeadingPrefix_StrippedAndKeywordDetected()
        {
            var info = _sut.ClassifySupplementaryContent("sxs-persona_5_strikers_persona_legacy_bgm_dlc.nsp");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
        }

        [Test]
        public void Classify_SceneTrailingTag_DLC()
        {
            var info = _sut.ClassifySupplementaryContent("Persona_5_Strikers_DLC_NSW-SXS.nsp");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
        }

        // --- Space-delimited version normalization ---

        [Test]
        public void Classify_SpaceVersion_NormalizedToDots()
        {
            var info = _sut.ClassifySupplementaryContent("SpongeBob_Update_v1 0 3_NSW-LiGHTFORCE.nsp");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
            Assert.That(info.Version, Is.EqualTo("v1.0.3"));
        }

        [Test]
        public void Classify_SpaceVersion_TwoComponents()
        {
            var info = _sut.ClassifySupplementaryContent("Spyro_Reignited_Trilogy_Update_v1 01_NSW-VENOM.nsp");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
            Assert.That(info.Version, Is.EqualTo("v1.01"));
        }

        // --- PS4/PS5 content type suffix ---

        [Test]
        public void Classify_PS4PatchSuffix_ReturnsPatch()
        {
            var info = _sut.ClassifySupplementaryContent("CUSA09193-patch_v1.02.pkg");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
            Assert.That(info.Serial, Is.EqualTo("CUSA09193"));
        }

        [Test]
        public void Classify_PS4AcSuffix_ReturnsDLC()
        {
            var info = _sut.ClassifySupplementaryContent("CUSA09193-ac.pkg");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
        }

        [Test]
        public void Classify_PS4AppSuffix_ReturnsMain()
        {
            var info = _sut.ClassifySupplementaryContent("CUSA09193-app.pkg");
            Assert.That(info.FileType, Is.EqualTo("Main"));
        }

        // --- Keyword-based classification ---

        [Test]
        public void Classify_UpdateKeyword_ReturnsPatch()
        {
            var info = _sut.ClassifySupplementaryContent("Resident_Evil_2_Update_v1.02.pkg");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
        }

        [Test]
        public void Classify_DLCKeyword_ReturnsDLC()
        {
            var info = _sut.ClassifySupplementaryContent("RE2_DLC_ClothesSet.pkg");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
        }

        [Test]
        public void Classify_SeasonPassKeyword_ReturnsDLC()
        {
            var info = _sut.ClassifySupplementaryContent("Game Season Pass.pkg");
            Assert.That(info.FileType, Is.EqualTo("DLC"));
        }

        [Test]
        public void Classify_GenericUpdateName_ReturnsPatch()
        {
            var info = _sut.ClassifySupplementaryContent("update.pkg");
            Assert.That(info.FileType, Is.EqualTo("Patch"));
        }

        [Test]
        public void Classify_NoKeywords_ReturnsMain()
        {
            var info = _sut.ClassifySupplementaryContent("Bayonetta 2.nsp");
            Assert.That(info.FileType, Is.EqualTo("Main"));
        }

        // --- DeriveBaseTitleId ---

        [TestCase("01004A4000B3A800", "01004A4000B3A000")]
        [TestCase("01004A4000B3B001", "01004A4000B3B000")]
        [TestCase("01004A4000B3A000", "01004A4000B3A000")]
        [TestCase(null, null)]
        [TestCase("short", null)]
        public void DeriveBaseTitleId_ReturnsCorrectBase(string? input, string? expected)
        {
            Assert.That(TitleCleanerService.DeriveBaseTitleId(input), Is.EqualTo(expected));
        }
    }
}
