using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroArr.Core.Games
{
    public static class PlatformDefinitions
    {
        private static Dictionary<int, Platform>? _platformDict;
        public static Dictionary<int, Platform> PlatformDictionary =>
            _platformDict ??= AllPlatforms.ToDictionary(p => p.Id);

        // Walks up a filesystem path and returns the first platform whose
        // folder-name (or alias) matches a segment. Used to heal stale DB rows
        // where a game's Path points into /media/psx/... but PlatformId still
        // says NDS (or whatever the first scan guessed wrong).
        public static Platform? ResolvePlatformFromPath(string? path)
        {
            if (string.IsNullOrWhiteSpace(path)) return null;

            string normalized;
            try { normalized = Path.GetFullPath(path); }
            catch { normalized = path; }

            var segments = normalized.Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar },
                                            System.StringSplitOptions.RemoveEmptyEntries);

            // Walk deepest-to-shallowest so a nested platform folder wins over
            // a parent. Example: /library/retrobat/nds/Game.nds — we want nds,
            // not whatever "retrobat" might match.
            for (int i = segments.Length - 1; i >= 0; i--)
            {
                var seg = segments[i];
                var hit = AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(seg));
                if (hit != null) return hit;
            }
            return null;
        }

        public static readonly List<Platform> AllPlatforms = new()
        {
            // ========== PC / Computer ==========
            new Platform { Id = 1, Name = "PC (Windows)", Slug = "pc", FolderName = "windows", Type = PlatformType.PC, Category = "Computer", IgdbPlatformId = 6, Enabled = true },
            new Platform { Id = 2, Name = "macOS", Slug = "macos", FolderName = "macintosh", Type = PlatformType.MacOS, Category = "Computer", IgdbPlatformId = 14, Enabled = true },
            new Platform { Id = 3, Name = "MS-DOS", Slug = "dos", FolderName = "dos", Type = PlatformType.DOS, Category = "Computer", IgdbPlatformId = 13, Enabled = true },
            new Platform { Id = 4, Name = "Amiga", Slug = "amiga", FolderName = "amiga", Type = PlatformType.Amiga, Category = "Computer", IgdbPlatformId = 16, ScreenScraperSystemId = 64, Enabled = false, RetroBatFolderName = "amiga500", BatoceraFolderName = "amiga500" },
            new Platform { Id = 5, Name = "Amiga CD32", Slug = "cd32", FolderName = "cd32", Type = PlatformType.AmigaCD32, Category = "Computer", IgdbPlatformId = 114, ScreenScraperSystemId = 130, Enabled = false, RetroBatFolderName = "amigacd32", BatoceraFolderName = "amigacd32" },
            new Platform { Id = 6, Name = "Commodore 64", Slug = "c64", FolderName = "c64", Type = PlatformType.Commodore64, Category = "Computer", IgdbPlatformId = 15, ScreenScraperSystemId = 66, Enabled = false },
            new Platform { Id = 7, Name = "Commodore VIC-20", Slug = "vic20", FolderName = "vic20", Type = PlatformType.CommodoreVIC20, Category = "Computer", IgdbPlatformId = 71, ScreenScraperSystemId = 73, Enabled = false, RetroBatFolderName = "c20", BatoceraFolderName = "c20" },
            new Platform { Id = 8, Name = "ZX Spectrum", Slug = "zxspectrum", FolderName = "zxspectrum", Type = PlatformType.ZXSpectrum, Category = "Computer", IgdbPlatformId = 26, ScreenScraperSystemId = 76, Enabled = false },
            new Platform { Id = 9, Name = "MSX", Slug = "msx", FolderName = "msx", Type = PlatformType.MSX, Category = "Computer", IgdbPlatformId = 27, ScreenScraperSystemId = 113, Enabled = false, RetroBatFolderName = "msx1", BatoceraFolderName = "msx1" },
            new Platform { Id = 10, Name = "MSX2", Slug = "msx2", FolderName = "msx2", Type = PlatformType.MSX2, Category = "Computer", IgdbPlatformId = 53, ScreenScraperSystemId = 113, Enabled = false },
            new Platform { Id = 11, Name = "Sharp X68000", Slug = "x68000", FolderName = "x68000", Type = PlatformType.SharpX68000, Category = "Computer", IgdbPlatformId = 121, ScreenScraperSystemId = 79, Enabled = false },
            new Platform { Id = 12, Name = "Apple II", Slug = "apple2", FolderName = "apple2", Type = PlatformType.AppleII, Category = "Computer", IgdbPlatformId = 75, ScreenScraperSystemId = 86, Enabled = false },
            new Platform { Id = 13, Name = "BBC Micro", Slug = "bbcmicro", FolderName = "bbcmicro", Type = PlatformType.BBCMicro, Category = "Computer", IgdbPlatformId = 69, Enabled = false, FolderAliases = new[] { "bbc" } },
            new Platform { Id = 15, Name = "Amiga CDTV", Slug = "amigacdtv", FolderName = "amigacdtv", Type = PlatformType.AmigaCDTV, Category = "Computer", IgdbPlatformId = 16, ScreenScraperSystemId = 129, Enabled = false },
            new Platform { Id = 16, Name = "Amiga 1200 (AGA)", Slug = "amiga1200", FolderName = "amiga1200", Type = PlatformType.Amiga1200, Category = "Computer", IgdbPlatformId = 16, ScreenScraperSystemId = 111, Enabled = false },
            new Platform { Id = 17, Name = "Amiga 4000", Slug = "amiga4000", FolderName = "amiga4000", Type = PlatformType.Amiga4000, Category = "Computer", IgdbPlatformId = 16, Enabled = false },
            new Platform { Id = 18, Name = "Amstrad CPC", Slug = "amstradcpc", FolderName = "amstradcpc", Type = PlatformType.AmstradCPC, Category = "Computer", IgdbPlatformId = 25, ScreenScraperSystemId = 65, Enabled = false },
            new Platform { Id = 19, Name = "Apple IIGS", Slug = "apple2gs", FolderName = "apple2gs", Type = PlatformType.AppleIIGS, Category = "Computer", IgdbPlatformId = 115, Enabled = false },

            // ========== 3DO ==========
            new Platform { Id = 14, Name = "3DO Interactive Multiplayer", Slug = "3do", FolderName = "3do", Type = PlatformType.ThreeDO, Category = "Console", IgdbPlatformId = 50, ScreenScraperSystemId = 29, Enabled = true },

            // ========== Sony PlayStation ==========
            new Platform { Id = 20, Name = "PlayStation 1", Slug = "ps1", FolderName = "psx", Type = PlatformType.PlayStation, Category = "Sony", IgdbPlatformId = 7, ScreenScraperSystemId = 57, Enabled = true },
            new Platform { Id = 21, Name = "PlayStation 2", Slug = "ps2", FolderName = "ps2", Type = PlatformType.PlayStation2, Category = "Sony", IgdbPlatformId = 8, ScreenScraperSystemId = 58, Enabled = true },
            new Platform { Id = 22, Name = "PlayStation 3", Slug = "ps3", FolderName = "ps3", Type = PlatformType.PlayStation3, Category = "Sony", IgdbPlatformId = 9, Enabled = true },
            new Platform { Id = 23, Name = "PlayStation 4", Slug = "ps4", FolderName = "ps4", Type = PlatformType.PlayStation4, Category = "Sony", IgdbPlatformId = 48, Enabled = true },
            new Platform { Id = 24, Name = "PlayStation 5", Slug = "ps5", FolderName = "ps5", Type = PlatformType.PlayStation5, Category = "Sony", IgdbPlatformId = 167, Enabled = true },
            new Platform { Id = 25, Name = "PlayStation Portable", Slug = "psp", FolderName = "psp", Type = PlatformType.PSP, Category = "Sony", IgdbPlatformId = 38, ScreenScraperSystemId = 61, Enabled = true },
            new Platform { Id = 26, Name = "PlayStation Vita", Slug = "vita", FolderName = "vita", Type = PlatformType.PSVita, Category = "Sony", IgdbPlatformId = 46, Enabled = true, RetroBatFolderName = "psvita", BatoceraFolderName = "psvita" },

            // ========== Microsoft Xbox ==========
            new Platform { Id = 30, Name = "Xbox", Slug = "xbox", FolderName = "xbox", Type = PlatformType.Xbox, Category = "Microsoft", IgdbPlatformId = 11, Enabled = false },
            new Platform { Id = 31, Name = "Xbox 360", Slug = "xbox360", FolderName = "xbox360", Type = PlatformType.Xbox360, Category = "Microsoft", IgdbPlatformId = 12, Enabled = false },
            new Platform { Id = 32, Name = "Xbox One", Slug = "xboxone", FolderName = "xboxone", Type = PlatformType.XboxOne, Category = "Microsoft", IgdbPlatformId = 49, Enabled = false },
            new Platform { Id = 33, Name = "Xbox Series X|S", Slug = "xboxseriesx", FolderName = "xboxseriesx", Type = PlatformType.XboxSeriesX, Category = "Microsoft", IgdbPlatformId = 169, Enabled = false },

            // ========== Nintendo Home Consoles ==========
            new Platform { Id = 40, Name = "Nintendo Entertainment System", Slug = "nes", FolderName = "nes", Type = PlatformType.NES, Category = "Nintendo", IgdbPlatformId = 18, ScreenScraperSystemId = 3, Enabled = true },
            new Platform { Id = 41, Name = "Super Nintendo (SNES)", Slug = "snes", FolderName = "snes", Type = PlatformType.SNES, Category = "Nintendo", IgdbPlatformId = 19, ScreenScraperSystemId = 4, Enabled = true, FolderAliases = new[] { "snes-msu", "snes-msu1" } },
            new Platform { Id = 42, Name = "Nintendo 64", Slug = "n64", FolderName = "n64", Type = PlatformType.Nintendo64, Category = "Nintendo", IgdbPlatformId = 4, ScreenScraperSystemId = 14, Enabled = true },
            new Platform { Id = 43, Name = "Nintendo GameCube", Slug = "gamecube", FolderName = "gamecube", Type = PlatformType.GameCube, Category = "Nintendo", IgdbPlatformId = 21, ScreenScraperSystemId = 13, Enabled = true },
            new Platform { Id = 44, Name = "Nintendo Wii", Slug = "wii", FolderName = "wii", Type = PlatformType.Wii, Category = "Nintendo", IgdbPlatformId = 5, ScreenScraperSystemId = 16, Enabled = true },
            new Platform { Id = 45, Name = "Nintendo Wii U", Slug = "wiiu", FolderName = "wiiu", Type = PlatformType.WiiU, Category = "Nintendo", IgdbPlatformId = 41, ScreenScraperSystemId = 18, Enabled = true },
            new Platform { Id = 46, Name = "Nintendo Switch", Slug = "switch", FolderName = "switch", Type = PlatformType.Switch, Category = "Nintendo", IgdbPlatformId = 130, ScreenScraperSystemId = 225, Enabled = true },
            new Platform { Id = 47, Name = "Nintendo Switch 2", Slug = "switch2", FolderName = "switch", Type = PlatformType.Switch2, Category = "Nintendo", IgdbPlatformId = 441, ParentPlatformId = 46, Enabled = true },
            new Platform { Id = 48, Name = "Famicom Disk System", Slug = "fds", FolderName = "fds", Type = PlatformType.FamicomDiskSystem, Category = "Nintendo", IgdbPlatformId = 51, ScreenScraperSystemId = 106, Enabled = false },
            new Platform { Id = 49, Name = "Super Famicom", Slug = "sfc", FolderName = "sfc", Type = PlatformType.SuperFamicom, Category = "Nintendo", IgdbPlatformId = 58, ParentPlatformId = 41, Enabled = false, RetroBatFolderName = "snes", BatoceraFolderName = "snes" },
            new Platform { Id = 57, Name = "Nintendo 64 Disk Drive", Slug = "n64dd", FolderName = "n64dd", Type = PlatformType.Nintendo64DD, Category = "Nintendo", IgdbPlatformId = 4, ParentPlatformId = 42, Enabled = false },
            new Platform { Id = 58, Name = "Satellaview", Slug = "satellaview", FolderName = "satellaview", Type = PlatformType.Satellaview, Category = "Nintendo", IgdbPlatformId = 19, ParentPlatformId = 41, Enabled = false },
            new Platform { Id = 59, Name = "SuFami Turbo", Slug = "sufami", FolderName = "sufami", Type = PlatformType.SuFamiTurbo, Category = "Nintendo", IgdbPlatformId = 19, ParentPlatformId = 41, Enabled = false },

            // ========== Nintendo Handhelds ==========
            new Platform { Id = 50, Name = "Game Boy", Slug = "gb", FolderName = "gb", Type = PlatformType.GameBoy, Category = "Nintendo", IgdbPlatformId = 33, ScreenScraperSystemId = 9, Enabled = true, FolderAliases = new[] { "gb2players", "gb-msu" } },
            new Platform { Id = 51, Name = "Game Boy Color", Slug = "gbc", FolderName = "gbc", Type = PlatformType.GameBoyColor, Category = "Nintendo", IgdbPlatformId = 22, ScreenScraperSystemId = 10, Enabled = true, FolderAliases = new[] { "gbc2players" } },
            new Platform { Id = 52, Name = "Game Boy Advance", Slug = "gba", FolderName = "gba", Type = PlatformType.GameBoyAdvance, Category = "Nintendo", IgdbPlatformId = 24, ScreenScraperSystemId = 12, Enabled = true, FolderAliases = new[] { "gba2players" } },
            new Platform { Id = 53, Name = "Nintendo DS", Slug = "nds", FolderName = "nds", Type = PlatformType.NintendoDS, Category = "Nintendo", IgdbPlatformId = 20, ScreenScraperSystemId = 15, Enabled = true },
            new Platform { Id = 54, Name = "Nintendo 3DS", Slug = "3ds", FolderName = "3ds", Type = PlatformType.Nintendo3DS, Category = "Nintendo", IgdbPlatformId = 37, ScreenScraperSystemId = 17, Enabled = true },
            new Platform { Id = 55, Name = "Virtual Boy", Slug = "virtualboy", FolderName = "virtualboy", Type = PlatformType.VirtualBoy, Category = "Nintendo", IgdbPlatformId = 87, ScreenScraperSystemId = 11, Enabled = false },
            new Platform { Id = 56, Name = "Pokémon Mini", Slug = "pokemini", FolderName = "pokemini", Type = PlatformType.PokemonMini, Category = "Nintendo", IgdbPlatformId = 152, ScreenScraperSystemId = 211, Enabled = false },
            new Platform { Id = 127, Name = "Super Game Boy", Slug = "sgb", FolderName = "sgb", Type = PlatformType.SuperGameBoy, Category = "Nintendo", IgdbPlatformId = 33, ParentPlatformId = 50, Enabled = false, FolderAliases = new[] { "sgb-msu1" } },

            // ========== Sega ==========
            new Platform { Id = 60, Name = "SG-1000", Slug = "sg1000", FolderName = "sg1000", Type = PlatformType.SG1000, Category = "Sega", IgdbPlatformId = 84, ScreenScraperSystemId = 109, Enabled = false },
            new Platform { Id = 61, Name = "Master System", Slug = "mastersystem", FolderName = "mastersystem", Type = PlatformType.MasterSystem, Category = "Sega", IgdbPlatformId = 64, ScreenScraperSystemId = 2, Enabled = true },
            new Platform { Id = 62, Name = "Mega Drive / Genesis", Slug = "megadrive", FolderName = "megadrive", Type = PlatformType.MegaDrive, Category = "Sega", IgdbPlatformId = 29, ScreenScraperSystemId = 1, Enabled = true, FolderAliases = new[] { "megadrive-msu", "msu-md" } },
            new Platform { Id = 63, Name = "Mega CD / Sega CD", Slug = "segacd", FolderName = "segacd", Type = PlatformType.SegaCD, Category = "Sega", IgdbPlatformId = 78, ScreenScraperSystemId = 20, Enabled = true, RetroBatFolderName = "megacd", BatoceraFolderName = "megacd" },
            new Platform { Id = 64, Name = "Sega 32X", Slug = "32x", FolderName = "32x", Type = PlatformType.Sega32X, Category = "Sega", IgdbPlatformId = 30, ScreenScraperSystemId = 19, Enabled = false, RetroBatFolderName = "sega32x", BatoceraFolderName = "sega32x" },
            new Platform { Id = 65, Name = "Game Gear", Slug = "gamegear", FolderName = "gamegear", Type = PlatformType.GameGear, Category = "Sega", IgdbPlatformId = 35, ScreenScraperSystemId = 21, Enabled = true },
            new Platform { Id = 66, Name = "Sega Saturn", Slug = "saturn", FolderName = "saturn", Type = PlatformType.Saturn, Category = "Sega", IgdbPlatformId = 32, ScreenScraperSystemId = 22, Enabled = true },
            new Platform { Id = 67, Name = "Dreamcast", Slug = "dreamcast", FolderName = "dreamcast", Type = PlatformType.Dreamcast, Category = "Sega", IgdbPlatformId = 23, ScreenScraperSystemId = 23, Enabled = true },
            new Platform { Id = 68, Name = "Naomi", Slug = "naomi", FolderName = "naomi", Type = PlatformType.Naomi, Category = "Sega", IgdbPlatformId = 52, ScreenScraperSystemId = 75, Enabled = false },
            new Platform { Id = 69, Name = "Naomi 2", Slug = "naomi2", FolderName = "naomi2", Type = PlatformType.Naomi2, Category = "Sega", IgdbPlatformId = 122, Enabled = false },
            new Platform { Id = 70, Name = "Atomiswave", Slug = "atomiswave", FolderName = "atomiswave", Type = PlatformType.Atomiswave, Category = "Sega", IgdbPlatformId = 123, ScreenScraperSystemId = 75, Enabled = false },
            new Platform { Id = 71, Name = "Sega ST-V", Slug = "segastv", FolderName = "segastv", Type = PlatformType.SegaSTV, Category = "Sega", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 72, Name = "Sega Chihiro", Slug = "chihiro", FolderName = "chihiro", Type = PlatformType.SegaChihiro, Category = "Sega", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 73, Name = "Sega Model 2", Slug = "model2", FolderName = "model2", Type = PlatformType.SegaModel2, Category = "Sega", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 74, Name = "Sega Model 3", Slug = "model3", FolderName = "model3", Type = PlatformType.SegaModel3, Category = "Sega", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 75, Name = "Sega Triforce", Slug = "triforce", FolderName = "triforce", Type = PlatformType.SegaTriforce, Category = "Sega", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 76, Name = "Sega Hikaru", Slug = "hikaru", FolderName = "hikaru", Type = PlatformType.Hikaru, Category = "Sega", IgdbPlatformId = null, Enabled = false },

            // ========== Atari ==========
            new Platform { Id = 80, Name = "Atari 2600", Slug = "atari2600", FolderName = "atari2600", Type = PlatformType.Atari2600, Category = "Atari", IgdbPlatformId = 59, ScreenScraperSystemId = 26, Enabled = false },
            new Platform { Id = 81, Name = "Atari 5200", Slug = "atari5200", FolderName = "atari5200", Type = PlatformType.Atari5200, Category = "Atari", IgdbPlatformId = 66, ScreenScraperSystemId = 40, Enabled = false },
            new Platform { Id = 82, Name = "Atari 7800", Slug = "atari7800", FolderName = "atari7800", Type = PlatformType.Atari7800, Category = "Atari", IgdbPlatformId = 60, ScreenScraperSystemId = 41, Enabled = false },
            new Platform { Id = 83, Name = "Atari Jaguar", Slug = "jaguar", FolderName = "jaguar", Type = PlatformType.Jaguar, Category = "Atari", IgdbPlatformId = 62, ScreenScraperSystemId = 27, Enabled = false },
            new Platform { Id = 84, Name = "Atari Jaguar CD", Slug = "jaguarcd", FolderName = "jaguarcd", Type = PlatformType.JaguarCD, Category = "Atari", IgdbPlatformId = 171, ScreenScraperSystemId = 171, Enabled = false },
            new Platform { Id = 85, Name = "Atari Lynx", Slug = "lynx", FolderName = "lynx", Type = PlatformType.Lynx, Category = "Atari", IgdbPlatformId = 61, ScreenScraperSystemId = 28, Enabled = false },
            new Platform { Id = 86, Name = "Atari ST", Slug = "atarist", FolderName = "atarist", Type = PlatformType.AtariST, Category = "Atari", IgdbPlatformId = 63, ScreenScraperSystemId = 42, Enabled = false },
            new Platform { Id = 87, Name = "Atari 800", Slug = "atari800", FolderName = "atari800", Type = PlatformType.Atari800, Category = "Atari", IgdbPlatformId = 65, ScreenScraperSystemId = 43, Enabled = false },
            new Platform { Id = 88, Name = "Atari XEGS", Slug = "xegs", FolderName = "xegs", Type = PlatformType.AtariXEGS, Category = "Atari", IgdbPlatformId = 65, Enabled = false },

            // ========== NEC / PC Engine ==========
            new Platform { Id = 90, Name = "PC Engine / TurboGrafx-16", Slug = "pcengine", FolderName = "pcengine", Type = PlatformType.PCEngine, Category = "NEC", IgdbPlatformId = 86, ScreenScraperSystemId = 31, Enabled = true },
            new Platform { Id = 91, Name = "PC Engine CD", Slug = "pcenginecd", FolderName = "pcenginecd", Type = PlatformType.PCEngineCD, Category = "NEC", IgdbPlatformId = 150, ScreenScraperSystemId = 114, Enabled = false },
            new Platform { Id = 92, Name = "SuperGrafx", Slug = "supergrafx", FolderName = "supergrafx", Type = PlatformType.SuperGrafx, Category = "NEC", IgdbPlatformId = 128, Enabled = false },
            new Platform { Id = 93, Name = "PC-FX", Slug = "pcfx", FolderName = "pcfx", Type = PlatformType.PCFX, Category = "NEC", IgdbPlatformId = 274, ScreenScraperSystemId = 72, Enabled = false },

            // ========== Arcade ==========
            new Platform { Id = 100, Name = "Arcade (MAME)", Slug = "arcade", FolderName = "arcade", Type = PlatformType.Arcade, Category = "Arcade", IgdbPlatformId = 52, ScreenScraperSystemId = 75, Enabled = true, RetroBatFolderName = "mame", BatoceraFolderName = "mame" },
            new Platform { Id = 101, Name = "FinalBurn Neo", Slug = "fbneo", FolderName = "fbneo", Type = PlatformType.FinalBurnNeo, Category = "Arcade", IgdbPlatformId = null, ScreenScraperSystemId = 75, Enabled = false },
            new Platform { Id = 102, Name = "Neo Geo", Slug = "neogeo", FolderName = "neogeo", Type = PlatformType.NeoGeo, Category = "Arcade", IgdbPlatformId = 79, ScreenScraperSystemId = 142, Enabled = true },
            new Platform { Id = 103, Name = "Neo Geo CD", Slug = "neogeocd", FolderName = "neogeocd", Type = PlatformType.NeoGeoCD, Category = "Arcade", IgdbPlatformId = 136, ScreenScraperSystemId = 70, Enabled = false },
            new Platform { Id = 104, Name = "CPS-1", Slug = "cps1", FolderName = "cps1", Type = PlatformType.CPS1, Category = "Arcade", IgdbPlatformId = null, ScreenScraperSystemId = 68, Enabled = false },
            new Platform { Id = 105, Name = "CPS-2", Slug = "cps2", FolderName = "cps2", Type = PlatformType.CPS2, Category = "Arcade", IgdbPlatformId = null, ScreenScraperSystemId = 69, Enabled = false },
            new Platform { Id = 106, Name = "CPS-3", Slug = "cps3", FolderName = "cps3", Type = PlatformType.CPS3, Category = "Arcade", IgdbPlatformId = null, ScreenScraperSystemId = 70, Enabled = false },
            new Platform { Id = 107, Name = "Daphne (Laserdisc)", Slug = "daphne", FolderName = "daphne", Type = PlatformType.Daphne, Category = "Arcade", IgdbPlatformId = null, ScreenScraperSystemId = 49, Enabled = false },
            new Platform { Id = 108, Name = "MAME Homebrew", Slug = "hbmame", FolderName = "hbmame", Type = PlatformType.MAMEHomebrew, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 109, Name = "Hyper Neo Geo 64", Slug = "neogeo64", FolderName = "neogeo64", Type = PlatformType.NeoGeo64, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 128, Name = "CAVE CV1000", Slug = "cave", FolderName = "cave", Type = PlatformType.CAVE, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 129, Name = "Zinc", Slug = "zinc", FolderName = "zinc", Type = PlatformType.Zinc, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 130, Name = "Namco System 246/256", Slug = "namco2x6", FolderName = "namco2x6", Type = PlatformType.Namco246, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 131, Name = "TeknoParrot", Slug = "teknoparrot", FolderName = "teknoparrot", Type = PlatformType.TeknoParrot, Category = "Arcade", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 132, Name = "Gaelco", Slug = "gaelco", FolderName = "gaelco", Type = PlatformType.Gaelco, Category = "Arcade", IgdbPlatformId = null, Enabled = false },

            // ========== Classic Consoles ==========
            new Platform { Id = 133, Name = "ColecoVision", Slug = "colecovision", FolderName = "colecovision", Type = PlatformType.ColecoVision, Category = "Console", IgdbPlatformId = 68, ScreenScraperSystemId = 48, Enabled = false },
            new Platform { Id = 134, Name = "Intellivision", Slug = "intellivision", FolderName = "intellivision", Type = PlatformType.Intellivision, Category = "Console", IgdbPlatformId = 67, ScreenScraperSystemId = 115, Enabled = false },
            new Platform { Id = 135, Name = "Philips CD-i", Slug = "cdi", FolderName = "cdi", Type = PlatformType.PhilipsCDi, Category = "Console", IgdbPlatformId = 117, Enabled = false },
            new Platform { Id = 136, Name = "Fairchild Channel F", Slug = "channelf", FolderName = "channelf", Type = PlatformType.FairchildChannelF, Category = "Console", IgdbPlatformId = 127, ScreenScraperSystemId = 80, Enabled = false },
            new Platform { Id = 137, Name = "Odyssey² / Videopac", Slug = "o2em", FolderName = "o2em", Type = PlatformType.Odyssey2, Category = "Console", IgdbPlatformId = 133, ScreenScraperSystemId = 104, Enabled = false },
            new Platform { Id = 138, Name = "ActionMax", Slug = "actionmax", FolderName = "actionmax", Type = PlatformType.ActionMax, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 139, Name = "Coleco Adam", Slug = "adam", FolderName = "adam", Type = PlatformType.ColecoAdam, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 140, Name = "Adventure Vision", Slug = "advision", FolderName = "advision", Type = PlatformType.AdventureVision, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 141, Name = "APF M-1000", Slug = "apfm1000", FolderName = "apfm1000", Type = PlatformType.APF1000, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 142, Name = "Arcadia 2001", Slug = "arcadia", FolderName = "arcadia", Type = PlatformType.Arcadia2001, Category = "Console", IgdbPlatformId = null, ScreenScraperSystemId = 94, Enabled = false },
            new Platform { Id = 143, Name = "Bally Astrocade", Slug = "astrocade", FolderName = "astrocade", Type = PlatformType.BallyCastrocade, Category = "Console", IgdbPlatformId = null, ScreenScraperSystemId = 44, Enabled = false, FolderAliases = new[] { "astrocde" } },
            new Platform { Id = 144, Name = "Casio Loopy", Slug = "casloopy", FolderName = "casloopy", Type = PlatformType.CasioLoopy, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 145, Name = "CreatiVision", Slug = "crvision", FolderName = "crvision", Type = PlatformType.CreatiVision, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 146, Name = "Othello Multivision", Slug = "multivision", FolderName = "multivision", Type = PlatformType.OthelloMultivision, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 147, Name = "PV-1000", Slug = "pv1000", FolderName = "pv1000", Type = PlatformType.PV1000, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 148, Name = "Super Cassette Vision", Slug = "scv", FolderName = "scv", Type = PlatformType.SuperCassetteVision, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 149, Name = "Super A'Can", Slug = "supracan", FolderName = "supracan", Type = PlatformType.SuperACan, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 150, Name = "Uzebox", Slug = "uzebox", FolderName = "uzebox", Type = PlatformType.Uzebox, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 151, Name = "VC4000", Slug = "vc4000", FolderName = "vc4000", Type = PlatformType.VC4000, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 152, Name = "Vectrex", Slug = "vectrex", FolderName = "vectrex", Type = PlatformType.Other, Category = "Console", IgdbPlatformId = 70, ScreenScraperSystemId = 102, Enabled = false },
            new Platform { Id = 153, Name = "Vircon32", Slug = "vircon32", FolderName = "vircon32", Type = PlatformType.Vircon32, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 154, Name = "V.Smile", Slug = "vsmile", FolderName = "vsmile", Type = PlatformType.VSmile, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 155, Name = "LowRes NX", Slug = "lowresnx", FolderName = "lowresnx", Type = PlatformType.LowResNX, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 156, Name = "TV Games", Slug = "tvgames", FolderName = "tvgames", Type = PlatformType.TVGames, Category = "Console", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 157, Name = "Amstrad GX4000", Slug = "gx4000", FolderName = "gx4000", Type = PlatformType.AmstradGX4000, Category = "Console", IgdbPlatformId = 25, Enabled = false },

            // ========== Handhelds & Others ==========
            new Platform { Id = 110, Name = "WonderSwan", Slug = "wonderswan", FolderName = "wonderswan", Type = PlatformType.WonderSwan, Category = "Handhelds", IgdbPlatformId = 57, ScreenScraperSystemId = 45, Enabled = false, RetroBatFolderName = "wswan", BatoceraFolderName = "wswan" },
            new Platform { Id = 111, Name = "WonderSwan Color", Slug = "wonderswancolor", FolderName = "wonderswancolor", Type = PlatformType.WonderSwanColor, Category = "Handhelds", IgdbPlatformId = 57, ScreenScraperSystemId = 46, Enabled = false, RetroBatFolderName = "wswanc", BatoceraFolderName = "wswanc" },
            new Platform { Id = 112, Name = "Neo Geo Pocket", Slug = "ngp", FolderName = "ngp", Type = PlatformType.NeoGeoPocket, Category = "Handhelds", IgdbPlatformId = 119, ScreenScraperSystemId = 25, Enabled = false },
            new Platform { Id = 113, Name = "Neo Geo Pocket Color", Slug = "ngpc", FolderName = "ngpc", Type = PlatformType.NeoGeoPocketColor, Category = "Handhelds", IgdbPlatformId = 120, ScreenScraperSystemId = 82, Enabled = false },
            new Platform { Id = 114, Name = "Watara Supervision", Slug = "supervision", FolderName = "supervision", Type = PlatformType.WataraSupervision, Category = "Handhelds", IgdbPlatformId = 95, Enabled = false },
            new Platform { Id = 115, Name = "Nokia N-Gage", Slug = "ngage", FolderName = "ngage", Type = PlatformType.NokiaNGage, Category = "Handhelds", IgdbPlatformId = 161, Enabled = false },
            new Platform { Id = 116, Name = "Arduboy", Slug = "arduboy", FolderName = "arduboy", Type = PlatformType.Arduboy, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 117, Name = "Gamate", Slug = "gamate", FolderName = "gamate", Type = PlatformType.Gamate, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 118, Name = "Game & Watch", Slug = "gameandwatch", FolderName = "gameandwatch", Type = PlatformType.GameAndWatch, Category = "Handhelds", IgdbPlatformId = null, ScreenScraperSystemId = 52, Enabled = false },
            new Platform { Id = 119, Name = "Game.com", Slug = "gamecom", FolderName = "gamecom", Type = PlatformType.TigerGameCom, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 158, Name = "Game Pocket Computer", Slug = "gamepock", FolderName = "gamepock", Type = PlatformType.GamePocketComputer, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 159, Name = "Game Master", Slug = "gmaster", FolderName = "gmaster", Type = PlatformType.GameMaster, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 160, Name = "Game Park 32", Slug = "gp32", FolderName = "gp32", Type = PlatformType.GamePark32, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 161, Name = "LCD Games", Slug = "lcdgames", FolderName = "lcdgames", Type = PlatformType.LCDGames, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 162, Name = "Mega Duck", Slug = "megaduck", FolderName = "megaduck", Type = PlatformType.MegaDuck, Category = "Handhelds", IgdbPlatformId = null, Enabled = false },

            // ========== Computers (additional) ==========
            new Platform { Id = 163, Name = "Commodore 128", Slug = "c128", FolderName = "c128", Type = PlatformType.Commodore128, Category = "Computer", IgdbPlatformId = 15, Enabled = false },
            new Platform { Id = 164, Name = "Commodore Plus/4", Slug = "cplus4", FolderName = "cplus4", Type = PlatformType.CommodorePlus4, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 165, Name = "Commodore PET", Slug = "pet", FolderName = "pet", Type = PlatformType.CommodorePET, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 166, Name = "MSX2+", Slug = "msx2plus", FolderName = "msx2+", Type = PlatformType.MSX2Plus, Category = "Computer", IgdbPlatformId = 53, Enabled = false },
            new Platform { Id = 167, Name = "MSX turbo R", Slug = "msxturbor", FolderName = "msxturbor", Type = PlatformType.MSXTurboR, Category = "Computer", IgdbPlatformId = 53, Enabled = false },
            new Platform { Id = 168, Name = "Sharp X1", Slug = "x1", FolderName = "x1", Type = PlatformType.SharpX1, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 169, Name = "ZX81", Slug = "zx81", FolderName = "zx81", Type = PlatformType.ZX81, Category = "Computer", IgdbPlatformId = null, ScreenScraperSystemId = 77, Enabled = false },
            new Platform { Id = 170, Name = "Acorn Electron", Slug = "electron", FolderName = "electron", Type = PlatformType.AcornElectron, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 171, Name = "Acorn Archimedes", Slug = "archimedes", FolderName = "archimedes", Type = PlatformType.AcornArchimedes, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 172, Name = "Acorn Atom", Slug = "atom", FolderName = "atom", Type = PlatformType.AcornAtom, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 173, Name = "Mattel Aquarius", Slug = "aquarius", FolderName = "aquarius", Type = PlatformType.MattelAquarius, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 174, Name = "Camputers Lynx", Slug = "camplynx", FolderName = "camplynx", Type = PlatformType.CampurtersLynx, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 175, Name = "Dragon 32/64", Slug = "dragon32", FolderName = "dragon32", Type = PlatformType.Dragon32, Category = "Computer", IgdbPlatformId = null, ScreenScraperSystemId = 91, Enabled = false },
            new Platform { Id = 176, Name = "Thomson MO/TO", Slug = "thomson", FolderName = "thomson", Type = PlatformType.Thomson, Category = "Computer", IgdbPlatformId = null, ScreenScraperSystemId = 141, Enabled = false },
            new Platform { Id = 177, Name = "TI-99/4A", Slug = "ti99", FolderName = "ti99", Type = PlatformType.TI99, Category = "Computer", IgdbPlatformId = 129, ScreenScraperSystemId = 205, Enabled = false },
            new Platform { Id = 178, Name = "Tomy Tutor", Slug = "tutor", FolderName = "tutor", Type = PlatformType.TomyTutor, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 179, Name = "SAM Coupé", Slug = "samcoupe", FolderName = "samcoupe", Type = PlatformType.SamCoupe, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 180, Name = "Oric / Atmos", Slug = "oricatmos", FolderName = "oricatmos", Type = PlatformType.OricAtmos, Category = "Computer", IgdbPlatformId = 131, ScreenScraperSystemId = 131, Enabled = false },
            new Platform { Id = 181, Name = "Philips P2000T", Slug = "p2000t", FolderName = "p2000t", Type = PlatformType.PhilipsP2000T, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 182, Name = "Philips VG5000", Slug = "vg5k", FolderName = "vg5k", Type = PlatformType.PhilipsVG5000, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 183, Name = "Aamber Pegasus", Slug = "pegasus", FolderName = "pegasus", Type = PlatformType.Pegasus, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 184, Name = "Spectravideo", Slug = "spectravideo", FolderName = "spectravideo", Type = PlatformType.Spectravideo, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 185, Name = "FM Towns", Slug = "fmtowns", FolderName = "fmtowns", Type = PlatformType.FMTowns, Category = "Computer", IgdbPlatformId = 118, Enabled = false },
            new Platform { Id = 186, Name = "FM-7", Slug = "fm7", FolderName = "fm7", Type = PlatformType.FM7, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 187, Name = "NEC PC-8800", Slug = "pc88", FolderName = "pc88", Type = PlatformType.NECPC88, Category = "Computer", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 188, Name = "NEC PC-9800", Slug = "pc98", FolderName = "pc98", Type = PlatformType.NECPC98, Category = "Computer", IgdbPlatformId = null, ScreenScraperSystemId = 208, Enabled = false },
            new Platform { Id = 189, Name = "TRS-80 Color Computer", Slug = "coco", FolderName = "coco", Type = PlatformType.TRS80CoCo, Category = "Computer", IgdbPlatformId = 144, ScreenScraperSystemId = 144, Enabled = false },

            // ========== Special / Modern ==========
            new Platform { Id = 120, Name = "ScummVM", Slug = "scummvm", FolderName = "scummvm", Type = PlatformType.ScummVM, Category = "Special", IgdbPlatformId = null, ScreenScraperSystemId = 123, Enabled = true },
            new Platform { Id = 121, Name = "DOSBox", Slug = "dosbox", FolderName = "dosbox", Type = PlatformType.DOSBox, Category = "Special", IgdbPlatformId = null, ScreenScraperSystemId = 135, Enabled = false, RetroBatFolderName = "dos", BatoceraFolderName = "dos" },
            new Platform { Id = 122, Name = "OpenBOR", Slug = "openbor", FolderName = "openbor", Type = PlatformType.OpenBOR, Category = "Special", IgdbPlatformId = null, ScreenScraperSystemId = 214, Enabled = false },
            new Platform { Id = 123, Name = "Ports", Slug = "ports", FolderName = "ports", Type = PlatformType.Ports, Category = "Special", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 124, Name = "Moonlight", Slug = "moonlight", FolderName = "moonlight", Type = PlatformType.Moonlight, Category = "Special", IgdbPlatformId = null, Enabled = false },
            new Platform { Id = 125, Name = "Steam", Slug = "steam", FolderName = "steam", Type = PlatformType.Steam, Category = "Special", IgdbPlatformId = 6, Enabled = true },
            new Platform { Id = 126, Name = "GOG Galaxy", Slug = "gog", FolderName = "gog", Type = PlatformType.GOG, Category = "Special", IgdbPlatformId = 6, Enabled = true },
        };
    }
}
