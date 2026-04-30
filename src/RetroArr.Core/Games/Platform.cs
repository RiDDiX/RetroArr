using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Games
{
    public class Platform
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Slug { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public PlatformType Type { get; set; }
        public bool Enabled { get; set; } = true;
        public string? Category { get; set; }
        public int? IgdbPlatformId { get; set; }
        public int? ScreenScraperSystemId { get; set; }
        public int? ParentPlatformId { get; set; }
        public string? RetroBatFolderName { get; set; }
        public string? BatoceraFolderName { get; set; }
        public string[]? FolderAliases { get; set; }
        
        public string GetEffectiveFolderName(string? mode = null)
        {
            return mode?.ToLowerInvariant() switch
            {
                "retrobat" => RetroBatFolderName ?? FolderName,
                "batocera" => BatoceraFolderName ?? FolderName,
                _ => FolderName
            };
        }
        
        public bool MatchesFolderName(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            var cmp = System.StringComparison.OrdinalIgnoreCase;
            if (FolderName.Equals(name, cmp) || Slug.Equals(name, cmp))
                return true;
            if (!string.IsNullOrEmpty(RetroBatFolderName) && RetroBatFolderName.Equals(name, cmp))
                return true;
            if (!string.IsNullOrEmpty(BatoceraFolderName) && BatoceraFolderName.Equals(name, cmp))
                return true;
            if (FolderAliases != null)
            {
                foreach (var alias in FolderAliases)
                    if (alias.Equals(name, cmp)) return true;
            }
            return false;
        }

        // EF inverse navigation for Game.Platform. Don't remove, don't enumerate.
        // Use IGameRepository for game lookups.
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<Game> Games { get; set; } = new();
    }

    public enum PlatformType
    {
        // PC / Computer
        PC,
        MacOS,
        DOS,
        Windows,
        Amiga,
        AmigaCD32,
        AmigaCDTV,
        Amiga1200,
        Amiga4000,
        Commodore64,
        Commodore128,
        CommodoreVIC20,
        CommodorePET,
        CommodorePlus4,
        ZXSpectrum,
        ZX81,
        MSX,
        MSX2,
        MSX2Plus,
        MSXTurboR,
        SharpX68000,
        SharpX1,
        AppleII,
        AppleIIGS,
        Macintosh,
        BBCMicro,
        AcornElectron,
        AcornArchimedes,
        AcornAtom,
        AmstradCPC,
        AmstradGX4000,
        Atari800,
        AtariXEGS,
        ColecoAdam,
        MattelAquarius,
        CampurtersLynx,
        Dragon32,
        Thomson,
        TI99,
        TomyTutor,
        SamCoupe,
        OricAtmos,
        PhilipsP2000T,
        PhilipsVG5000,
        Spectravideo,
        Pegasus,
        FMTowns,
        FM7,
        NECPC88,
        NECPC98,
        TRS80CoCo,
        
        // Sony PlayStation
        PlayStation,
        PlayStation2,
        PlayStation3,
        PlayStation4,
        PlayStation5,
        PSP,
        PSVita,
        
        // Microsoft Xbox
        Xbox,
        Xbox360,
        XboxOne,
        XboxSeriesX,
        
        // Nintendo Home Consoles
        NES,
        SNES,
        Nintendo64,
        Nintendo64DD,
        GameCube,
        Wii,
        WiiU,
        Switch,
        Switch2,
        FamicomDiskSystem,
        Satellaview,
        SuFamiTurbo,
        SuperFamicom,
        SuperGameBoy,
        
        // Nintendo Handhelds
        GameBoy,
        GameBoyColor,
        GameBoyAdvance,
        NintendoDS,
        Nintendo3DS,
        VirtualBoy,
        PokemonMini,
        
        // 3DO
        ThreeDO,
        
        // Sega
        SG1000,
        MasterSystem,
        MegaDrive,
        Genesis,
        SegaCD,
        Sega32X,
        GameGear,
        Saturn,
        Dreamcast,
        Naomi,
        Naomi2,
        Atomiswave,
        SegaSTV,
        SegaChihiro,
        SegaModel2,
        SegaModel3,
        SegaTriforce,
        Hikaru,
        
        // Atari
        Atari2600,
        Atari5200,
        Atari7800,
        Jaguar,
        JaguarCD,
        Lynx,
        AtariST,
        
        // NEC / PC Engine
        PCEngine,
        TurboGrafx16,
        PCEngineCD,
        SuperGrafx,
        PCFX,
        
        // Arcade
        Arcade,
        MAME,
        MAMEHomebrew,
        FinalBurnNeo,
        NeoGeo,
        NeoGeoCD,
        NeoGeo64,
        CPS1,
        CPS2,
        CPS3,
        Daphne,
        CAVE,
        Zinc,
        Namco246,
        TeknoParrot,
        Gaelco,
        
        // Classic Consoles
        ColecoVision,
        Intellivision,
        PhilipsCDi,
        FairchildChannelF,
        Odyssey2,
        ActionMax,
        AdventureVision,
        APF1000,
        Arcadia2001,
        BallyCastrocade,
        CasioLoopy,
        CreatiVision,
        OthelloMultivision,
        PV1000,
        SuperCassetteVision,
        SuperACan,
        Uzebox,
        VC4000,
        Vircon32,
        VSmile,
        LowResNX,
        TVGames,
        
        // Handhelds & Others
        WonderSwan,
        WonderSwanColor,
        NeoGeoPocket,
        NeoGeoPocketColor,
        NokiaNGage,
        Arduboy,
        Gamate,
        GameAndWatch,
        BandaiPlaydia,
        BandaiPippin,
        WataraSupervision,
        TigerGameCom,
        GamePocketComputer,
        GameMaster,
        GamePark32,
        LCDGames,
        MegaDuck,
        
        // Special / Modern
        ScummVM,
        DOSBox,
        OpenBOR,
        Ports,
        Moonlight,
        Steam,
        GOG,
        EpicGames,
        
        Other
    }
}
