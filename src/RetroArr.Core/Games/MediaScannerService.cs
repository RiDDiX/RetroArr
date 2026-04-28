using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Text.RegularExpressions;
using RetroArr.Core.Configuration;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.Debug;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RetroArr.Core.Games
{
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1307:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Performance", "CA1860:AvoidUsingAnyWhenUseCount")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1003:UseGenericEventHandlerInstances")]
    public class MediaScannerService
    {
        private readonly ConfigurationService _configService;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly IGameRepository _gameRepository;
        private readonly DebugLogService? _debugLog;
        private readonly TitleCleanerService _titleCleaner;
        
        // Sentinel used when platform resolution from key/slug/path fails.
        // Must match an existing row in Platforms; PostDownloadProcessor uses the same id.
        private const int UnresolvedPlatformIdFallback = 1; // PC (Windows)

        // Scan State Tracking (thread-safe via volatile + lock)
        private readonly object _stateLock = new();
        private volatile bool _isScanning;
        private volatile string? _lastGameFound;
        private volatile int _gamesAddedCount;
        private volatile string? _currentScanDirectory;
        private volatile string? _currentScanFile;
        private volatile int _filesScannedCount;
        private System.Threading.CancellationTokenSource? _scanCts;

        public bool IsScanning => _isScanning;
        public string? LastGameFound => _lastGameFound;
        public int GamesAddedCount => _gamesAddedCount;
        public string? CurrentScanDirectory => _currentScanDirectory;
        public string? CurrentScanFile => _currentScanFile;
        public int FilesScannedCount => _filesScannedCount;

        public delegate void GameAddedHandler(Game game);
        public event GameAddedHandler? OnGameAdded;
        public event Action? OnScanStarted;

        private static readonly HashSet<string> _noiseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "setup", "install", "installer", "gog", "repack", "fitgirl", "dodi", "cracked", 
            "unpacked", "steamrip", "portable", "multi10", "multi5", "multi2", "v1", "v2",
            "xatab", "codex", "skidrow", "reloaded", "razor1911", "plaza", "cpy", "dlpsgame",
            "nsw2u", "egold", "quacked", "venom", "inc", "rpgonly", "gamesfull", "bitsearch",
            "www", "app", "com", "net", "org", "iso", "bin", "decepticon", "empress", 
            "tenoke", "rune", "goldberg", "ali213", "p2p", "fairlight",
            "xyz", "dot", "v0", "v196608", "v65536", "v131072", "dlc", "update", "upd", "collection", "anniversary", "edition",
            "us", "eu", "es", "uk", "asia", "cn", "ru", "gb", "mb", "kb", "romslab", "nsw2u", "madloader", "rpgonly", "usa", "eur", "jp", "region",
            "eng", "english", "spa", "spanish", "fra", "french", "ger", "german", "ita", "italian", "kor", "korean", "chi", "chinese", "tw", "hk",
            "repack", "fitgirl", "dodi", "xatab", "codex", "skidrow", "reloaded", "plaza", "cpy", "dlpsgame", "egold", "quacked", "venom", "inc",
            "rpgarchive", "gamesmega", "gamesfull", "bitsearch", "nxdump", "nx", "switch", "game",
            "opoisso893", "cyb1k", "dlpsgame", "pppwn", "pppwngo", "goldhen", "ps4", "ps5", "playstation", "sony",
            "definitive", "edition", "collection", "remastered", "remake",
            "nsp", "xci", "nsz", "xcz", "vpk", "pkg", "iso", "bin", "nla", "zip", "rar", "7z"
        };
        public event Action<int>? OnScanFinished;
        public event Action? OnBatchFinished;

        // 1. Valid Extensions (Whitelist)
        private static readonly Dictionary<string, PlatformRule> _platformRules = new(StringComparer.OrdinalIgnoreCase)
        {
            // ========== 3DO ==========
            ["3do"] = new() { Extensions = new[] { ".iso", ".chd", ".cue" } },
            // ========== Sony PlayStation ==========
            ["nintendo_switch"] = new() { Extensions = new[] { ".nsp", ".xci", ".nsz", ".xcz", ".nso", ".nro", ".nca", ".kip" } },
            ["switch"] = new() { Extensions = new[] { ".nsp", ".xci", ".nsz", ".xcz", ".nso", ".nro", ".nca", ".kip" } },
            ["switch2"] = new() { Extensions = new[] { ".nsp", ".xci", ".nsz", ".xcz", ".nso", ".nro", ".nca", ".kip" } },
            ["ps4"] = new() { Extensions = new[] { ".ps4", ".m3u", ".lnk" }, IsFolderMode = true },
            ["ps5"] = new() { Extensions = new[] { ".pkg" } },
            ["ps3"] = new() { Extensions = new[] { ".iso", ".pkg", ".bin", ".psn", ".squashfs", ".m3u", ".ps3", ".lnk", ".7z", ".zip", ".rar" }, IsFolderMode = true },
            ["ps2"] = new() { Extensions = new[] { ".iso", ".bin", ".cue", ".chd", ".cso", ".gz", ".mdf", ".nrg", ".img", ".dump" } },
            ["psx"] = new() { Extensions = new[] { ".bin", ".cue", ".chd", ".pbp", ".iso", ".img", ".mdf", ".toc", ".cbn", ".m3u", ".ccd", ".zip", ".7z", ".cso" } },
            ["psp"] = new() { Extensions = new[] { ".iso", ".cso", ".pbp", ".chd", ".elf", ".prx", ".zip", ".7z", ".squashfs" } },
            ["vita"] = new() { Extensions = new[] { ".vpk", ".mai", ".psvita", ".m3u", ".zip" }, IsFolderMode = true },
            ["psvita"] = new() { Extensions = new[] { ".vpk", ".mai", ".psvita", ".m3u", ".zip" }, IsFolderMode = true },
            ["pc"] = new() { Extensions = new[] { ".iso", ".exe", ".zip", ".rar", ".7z", ".setup" }, IsFolderMode = true },
            ["pc_windows"] = new() { Extensions = new[] { ".iso", ".exe", ".zip", ".rar", ".7z", ".setup" }, IsFolderMode = true },
            ["windows"] = new() { Extensions = new[] { ".iso", ".exe", ".zip", ".rar", ".7z", ".setup" }, IsFolderMode = true },
            ["macos"] = new() { Extensions = new[] { ".dmg", ".pkg", ".app", ".zip", ".rar", ".7z" }, IsFolderMode = true },
            ["macintosh"] = new() { Extensions = new[] { ".dmg", ".pkg", ".app", ".zip", ".rar", ".7z" }, IsFolderMode = true },
            // ========== Microsoft Xbox ==========
            ["xbox"] = new() { Extensions = new[] { ".iso", ".xiso", ".zip", ".7z" } },
            ["xbox360"] = new() { Extensions = new[] { ".iso", ".xex", ".god", ".zip", ".7z", ".rar" }, IsFolderMode = true },
            ["xboxone"] = new() { Extensions = new[] { ".xvd", ".zip", ".7z" } },
            ["xboxseriesx"] = new() { Extensions = new[] { ".xvd", ".zip", ".7z" } },
            // ========== Nintendo ==========
            ["nes"] = new() { Extensions = new[] { ".nes", ".unf", ".unif", ".zip", ".7z" } },
            ["snes"] = new() { Extensions = new[] { ".sfc", ".smc", ".fig", ".gd3", ".gd7", ".dx2", ".bsx", ".swc", ".zip", ".7z" } },
            ["n64"] = new() { Extensions = new[] { ".z64", ".n64", ".v64", ".ndd", ".zip", ".7z" } },
            ["gamecube"] = new() { Extensions = new[] { ".iso", ".gcm", ".rvz", ".gcz", ".ciso", ".wbfs", ".elf", ".dol", ".m3u", ".nkit.iso" } },
            ["wii"] = new() { Extensions = new[] { ".iso", ".wbfs", ".rvz", ".gcz", ".ciso", ".wad", ".gcm", ".elf", ".dol", ".m3u", ".json", ".nkit.iso" } },
            ["wiiu"] = new() { Extensions = new[] { ".wud", ".wux", ".rpx", ".wua", ".wup", ".squashfs", ".iso" }, IsFolderMode = true },
            ["gb"] = new() { Extensions = new[] { ".gb", ".zip", ".7z" } },
            ["gbc"] = new() { Extensions = new[] { ".gbc", ".gb", ".zip", ".7z" } },
            ["gba"] = new() { Extensions = new[] { ".gba", ".zip", ".7z" } },
            ["nds"] = new() { Extensions = new[] { ".nds", ".bin", ".zip", ".7z" } },
            ["3ds"] = new() { Extensions = new[] { ".3ds", ".cia", ".cxi", ".cci", ".axf", ".elf", ".zip", ".7z" } },
            // ========== Sega ==========
            ["megadrive"] = new() { Extensions = new[] { ".md", ".gen", ".smd", ".bin", ".sg", ".zip", ".7z" } },
            ["mastersystem"] = new() { Extensions = new[] { ".sms", ".bin", ".zip", ".7z" } },
            ["gamegear"] = new() { Extensions = new[] { ".gg", ".bin", ".zip", ".7z" } },
            ["saturn"] = new() { Extensions = new[] { ".iso", ".bin", ".cue", ".chd", ".ccd", ".m3u", ".zip" } },
            ["dreamcast"] = new() { Extensions = new[] { ".gdi", ".cdi", ".chd", ".cue", ".bin", ".m3u" } },
            ["segacd"] = new() { Extensions = new[] { ".iso", ".bin", ".cue", ".chd", ".m3u" } },
            ["megacd"] = new() { Extensions = new[] { ".iso", ".bin", ".cue", ".chd", ".m3u" } },
            ["32x"] = new() { Extensions = new[] { ".32x", ".bin", ".smd", ".md", ".zip", ".7z" } },
            ["sega32x"] = new() { Extensions = new[] { ".32x", ".bin", ".smd", ".md", ".zip", ".7z" } },
            // ========== PC / Computer (additional) ==========
            ["dos"] = new() { Extensions = new[] { ".exe", ".com", ".bat", ".zip", ".7z" }, IsFolderMode = true },
            ["linux"] = new() { Extensions = new[] { ".AppImage", ".sh", ".deb", ".tar.gz", ".zip", ".7z" }, IsFolderMode = true },
            ["scummvm"] = new() { Extensions = null, IsFolderMode = true },
            ["dosbox"] = new() { Extensions = new[] { ".exe", ".com", ".bat", ".zip", ".7z" }, IsFolderMode = true },
            ["amiga"] = new() { Extensions = new[] { ".adf", ".adz", ".dms", ".dmz", ".ipf", ".hdf", ".uae", ".lha", ".exe", ".m3u", ".zip", ".7z" } },
            ["amiga500"] = new() { Extensions = new[] { ".adf", ".adz", ".dms", ".dmz", ".ipf", ".hdf", ".uae", ".lha", ".exe", ".m3u", ".zip", ".7z" } },
            ["c64"] = new() { Extensions = new[] { ".d64", ".d81", ".t64", ".tap", ".crt", ".prg", ".m3u", ".zip", ".7z" } },
            // ========== NEC / PC Engine ==========
            ["pcengine"] = new() { Extensions = new[] { ".pce", ".bin", ".cue", ".chd", ".zip", ".7z" } },
            ["pcenginecd"] = new() { Extensions = new[] { ".cue", ".chd", ".bin", ".iso", ".pce", ".ccd", ".img" } },
            ["supergrafx"] = new() { Extensions = new[] { ".pce", ".sgx", ".cue", ".ccd", ".chd", ".zip", ".7z" } },
            // ========== Arcade ==========
            ["arcade"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["mame"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["fbneo"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["neogeo"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["neogeocd"] = new() { Extensions = new[] { ".cue", ".chd", ".bin", ".iso" } },
            ["cps1"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["cps2"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["cps3"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["naomi"] = new() { Extensions = new[] { ".zip", ".7z", ".bin", ".dat", ".lst" } },
            ["naomi2"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["atomiswave"] = new() { Extensions = new[] { ".zip", ".7z", ".bin", ".dat", ".lst" } },
            ["daphne"] = new() { Extensions = new[] { ".daphne", ".zip", ".7z" }, IsFolderMode = true },
            // ========== Sega (additional) ==========
            ["sg1000"] = new() { Extensions = new[] { ".sg", ".bin", ".zip", ".7z" } },
            // ========== Nintendo (additional) ==========
            ["fds"] = new() { Extensions = new[] { ".fds", ".zip", ".7z" } },
            ["sfc"] = new() { Extensions = new[] { ".sfc", ".smc", ".fig", ".gd3", ".gd7", ".dx2", ".bsx", ".swc", ".zip", ".7z" } },
            ["virtualboy"] = new() { Extensions = new[] { ".vb", ".zip", ".7z" } },
            ["pokemini"] = new() { Extensions = new[] { ".min", ".zip", ".7z" } },
            // ========== Atari ==========
            ["atari2600"] = new() { Extensions = new[] { ".a26", ".bin", ".zip", ".7z" } },
            ["atari5200"] = new() { Extensions = new[] { ".a52", ".bin", ".rom", ".xfd", ".atr", ".atx", ".cdm", ".cas", ".car", ".xex", ".zip", ".7z" } },
            ["atari7800"] = new() { Extensions = new[] { ".a78", ".bin", ".zip", ".7z" } },
            ["jaguar"] = new() { Extensions = new[] { ".j64", ".jag", ".cue", ".cof", ".abs", ".cdi", ".rom", ".zip", ".7z" } },
            ["jaguarcd"] = new() { Extensions = new[] { ".cue", ".chd", ".bin", ".iso" } },
            ["lynx"] = new() { Extensions = new[] { ".lnx", ".bll", ".lyx", ".o", ".zip", ".7z" } },
            ["atarist"] = new() { Extensions = new[] { ".st", ".stx", ".msa", ".dim", ".ipf", ".m3u", ".zip", ".7z" } },
            // ========== Handhelds ==========
            ["wonderswan"] = new() { Extensions = new[] { ".ws", ".zip", ".7z" } },
            ["wswan"] = new() { Extensions = new[] { ".ws", ".zip", ".7z" } },
            ["wonderswancolor"] = new() { Extensions = new[] { ".wsc", ".ws", ".zip", ".7z" } },
            ["wswanc"] = new() { Extensions = new[] { ".wsc", ".ws", ".zip", ".7z" } },
            ["ngp"] = new() { Extensions = new[] { ".ngp", ".zip", ".7z" } },
            ["ngpc"] = new() { Extensions = new[] { ".ngc", ".ngp", ".zip", ".7z" } },
            ["supervision"] = new() { Extensions = new[] { ".sv", ".bin", ".zip", ".7z" } },
            // ========== Computer (missing platforms) ==========
            ["cd32"] = new() { Extensions = new[] { ".iso", ".cue", ".chd", ".bin", ".zip", ".7z" } },
            ["amigacd32"] = new() { Extensions = new[] { ".iso", ".cue", ".chd", ".bin", ".zip", ".7z" } },
            ["vic20"] = new() { Extensions = new[] { ".prg", ".crt", ".d64", ".d81", ".tap", ".t64", ".a0", ".b0", ".m3u", ".zip", ".7z" } },
            ["c20"] = new() { Extensions = new[] { ".prg", ".crt", ".d64", ".d81", ".tap", ".t64", ".a0", ".b0", ".m3u", ".zip", ".7z" } },
            ["zxspectrum"] = new() { Extensions = new[] { ".tzx", ".tap", ".z80", ".sna", ".dsk", ".rzx", ".scl", ".trd", ".zip", ".7z" } },
            ["msx"] = new() { Extensions = new[] { ".rom", ".mx1", ".mx2", ".dsk", ".cas", ".m3u", ".zip", ".7z" } },
            ["msx1"] = new() { Extensions = new[] { ".rom", ".mx1", ".mx2", ".dsk", ".cas", ".m3u", ".zip", ".7z" } },
            ["msx2"] = new() { Extensions = new[] { ".rom", ".mx2", ".dsk", ".cas", ".m3u", ".zip", ".7z" } },
            ["x68000"] = new() { Extensions = new[] { ".dim", ".img", ".d88", ".88d", ".hdm", ".dup", ".2hd", ".xdf", ".hdf", ".cmd", ".m3u", ".zip", ".7z" } },
            ["apple2"] = new() { Extensions = new[] { ".dsk", ".do", ".po", ".nib", ".woz", ".mfi", ".dfi", ".rti", ".edd", ".wav", ".zip", ".7z" } },
            ["bbcmicro"] = new() { Extensions = new[] { ".ssd", ".dsd", ".uef", ".zip", ".7z" } },
            // ========== Sega (additional) ==========
            ["segastv"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["chihiro"] = new() { Extensions = new[] { ".iso", ".xbe", ".zip", ".7z" } },
            ["model2"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["model3"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["triforce"] = new() { Extensions = new[] { ".iso", ".zip", ".7z" } },
            ["hikaru"] = new() { Extensions = new[] { ".zip", ".7z" } },
            // ========== Nintendo (additional) ==========
            ["n64dd"] = new() { Extensions = new[] { ".ndd", ".z64", ".n64", ".v64", ".zip", ".7z" } },
            ["satellaview"] = new() { Extensions = new[] { ".sfc", ".smc", ".bs", ".zip", ".7z" } },
            ["sufami"] = new() { Extensions = new[] { ".sfc", ".smc", ".zip", ".7z" } },
            ["sgb"] = new() { Extensions = new[] { ".gb", ".gbc", ".zip", ".7z" } },
            // ========== Atari (additional) ==========
            ["atari800"] = new() { Extensions = new[] { ".xfd", ".atr", ".atx", ".cdm", ".cas", ".bin", ".a52", ".xex", ".zip", ".7z" } },
            ["xegs"] = new() { Extensions = new[] { ".xfd", ".atr", ".atx", ".cdm", ".cas", ".bin", ".a52", ".xex", ".zip", ".7z" } },
            // ========== NEC (additional) ==========
            ["pcfx"] = new() { Extensions = new[] { ".cue", ".chd", ".bin", ".iso" } },
            // ========== Arcade (additional) ==========
            ["hbmame"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["neogeo64"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["cave"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["zinc"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["namco2x6"] = new() { Extensions = new[] { ".iso", ".bin", ".zip", ".7z" } },
            ["teknoparrot"] = new() { Extensions = null, IsFolderMode = true },
            ["gaelco"] = new() { Extensions = new[] { ".zip", ".7z" } },
            // ========== Classic Consoles ==========
            ["colecovision"] = new() { Extensions = new[] { ".col", ".rom", ".bin", ".zip", ".7z" } },
            ["intellivision"] = new() { Extensions = new[] { ".int", ".rom", ".bin", ".zip", ".7z" } },
            ["cdi"] = new() { Extensions = new[] { ".cdi", ".chd", ".iso" } },
            ["channelf"] = new() { Extensions = new[] { ".chf", ".bin", ".zip", ".7z" } },
            ["o2em"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["actionmax"] = new() { Extensions = new[] { ".singedata" }, IsFolderMode = true },
            ["adam"] = new() { Extensions = new[] { ".dsk", ".ddp", ".rom", ".col", ".zip", ".7z" } },
            ["advision"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["apfm1000"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["arcadia"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["astrocade"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["casloopy"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["crvision"] = new() { Extensions = new[] { ".rom", ".bin", ".zip", ".7z" } },
            ["multivision"] = new() { Extensions = new[] { ".sg", ".bin", ".zip", ".7z" } },
            ["pv1000"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["scv"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["supracan"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["uzebox"] = new() { Extensions = new[] { ".uze", ".hex", ".zip", ".7z" } },
            ["vc4000"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["vectrex"] = new() { Extensions = new[] { ".vec", ".bin", ".gam", ".zip", ".7z" } },
            ["vircon32"] = new() { Extensions = new[] { ".v32", ".zip", ".7z" } },
            ["vsmile"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["lowresnx"] = new() { Extensions = new[] { ".nx", ".zip", ".7z" } },
            ["tvgames"] = new() { Extensions = new[] { ".zip", ".7z" } },
            ["gx4000"] = new() { Extensions = new[] { ".cpr", ".dsk", ".sna", ".zip", ".7z" } },
            // ========== Handhelds (additional) ==========
            ["ngage"] = new() { Extensions = new[] { ".ngage", ".app", ".sis", ".sisx", ".n-gage", ".zip", ".7z" } },
            ["arduboy"] = new() { Extensions = new[] { ".hex", ".arduboy", ".zip", ".7z" } },
            ["gamate"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["gameandwatch"] = new() { Extensions = new[] { ".mgw", ".zip", ".7z" } },
            ["gamecom"] = new() { Extensions = new[] { ".tgc", ".bin", ".zip", ".7z" } },
            ["gamepock"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["gmaster"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["gp32"] = new() { Extensions = new[] { ".gxb", ".smc", ".bin", ".zip", ".7z" } },
            ["lcdgames"] = new() { Extensions = new[] { ".mgw", ".zip", ".7z" } },
            ["megaduck"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            // ========== Computers (additional) ==========
            ["amigacdtv"] = new() { Extensions = new[] { ".iso", ".cue", ".chd", ".adf", ".zip", ".7z" } },
            ["amiga1200"] = new() { Extensions = new[] { ".adf", ".adz", ".dms", ".dmz", ".ipf", ".hdf", ".uae", ".lha", ".exe", ".m3u", ".zip", ".7z" } },
            ["amiga4000"] = new() { Extensions = new[] { ".adf", ".adz", ".dms", ".dmz", ".ipf", ".hdf", ".uae", ".lha", ".exe", ".m3u", ".zip", ".7z" } },
            ["amstradcpc"] = new() { Extensions = new[] { ".dsk", ".sna", ".cdt", ".voc", ".kcr", ".m3u", ".zip", ".7z" } },
            ["apple2gs"] = new() { Extensions = new[] { ".2mg", ".po", ".dsk", ".do", ".nib", ".woz", ".zip", ".7z" } },
            ["c128"] = new() { Extensions = new[] { ".d64", ".d71", ".d81", ".t64", ".tap", ".crt", ".prg", ".m3u", ".zip", ".7z" } },
            ["cplus4"] = new() { Extensions = new[] { ".d64", ".d81", ".t64", ".tap", ".prg", ".m3u", ".zip", ".7z" } },
            ["pet"] = new() { Extensions = new[] { ".d64", ".d81", ".t64", ".tap", ".prg", ".zip", ".7z" } },
            ["msx2+"] = new() { Extensions = new[] { ".rom", ".mx2", ".dsk", ".cas", ".m3u", ".zip", ".7z" } },
            ["msxturbor"] = new() { Extensions = new[] { ".rom", ".mx2", ".dsk", ".cas", ".m3u", ".zip", ".7z" } },
            ["x1"] = new() { Extensions = new[] { ".dx1", ".2d", ".2hd", ".tfd", ".d88", ".cmd", ".zip", ".7z" } },
            ["zx81"] = new() { Extensions = new[] { ".p", ".81", ".tzx", ".zip", ".7z" } },
            ["electron"] = new() { Extensions = new[] { ".uef", ".csw", ".ssd", ".dsd", ".zip", ".7z" } },
            ["archimedes"] = new() { Extensions = new[] { ".adf", ".jfd", ".zip", ".7z" } },
            ["atom"] = new() { Extensions = new[] { ".atm", ".tap", ".uef", ".zip", ".7z" } },
            ["aquarius"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["camplynx"] = new() { Extensions = new[] { ".tap", ".zip", ".7z" } },
            ["dragon32"] = new() { Extensions = new[] { ".cas", ".ccc", ".rom", ".dsk", ".wav", ".zip", ".7z" } },
            ["thomson"] = new() { Extensions = new[] { ".fd", ".sap", ".k7", ".m7", ".m5", ".rom", ".zip", ".7z" } },
            ["ti99"] = new() { Extensions = new[] { ".rpk", ".ctg", ".bin", ".zip", ".7z" } },
            ["tutor"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["samcoupe"] = new() { Extensions = new[] { ".dsk", ".mgt", ".sbt", ".zip", ".7z" } },
            ["oricatmos"] = new() { Extensions = new[] { ".dsk", ".tap", ".wav", ".zip", ".7z" } },
            ["p2000t"] = new() { Extensions = new[] { ".cas", ".zip", ".7z" } },
            ["vg5k"] = new() { Extensions = new[] { ".k7", ".zip", ".7z" } },
            ["pegasus"] = new() { Extensions = new[] { ".bin", ".zip", ".7z" } },
            ["spectravideo"] = new() { Extensions = new[] { ".rom", ".mx1", ".dsk", ".cas", ".zip", ".7z" } },
            ["fmtowns"] = new() { Extensions = new[] { ".iso", ".cue", ".chd", ".ccd", ".bin", ".zip", ".7z" } },
            ["fm7"] = new() { Extensions = new[] { ".d77", ".d88", ".t77", ".zip", ".7z" } },
            ["pc88"] = new() { Extensions = new[] { ".d88", ".88d", ".cmt", ".t88", ".zip", ".7z" } },
            ["pc98"] = new() { Extensions = new[] { ".fdi", ".hdi", ".hdd", ".d88", ".hdm", ".xdf", ".dup", ".cmd", ".nfd", ".zip", ".7z" } },
            ["coco"] = new() { Extensions = new[] { ".cas", ".ccc", ".rom", ".dsk", ".wav", ".zip", ".7z" } },
            // ========== Special / Modern ==========
            ["openbor"] = new() { Extensions = new[] { ".pak", ".zip", ".7z" }, IsFolderMode = true },
            ["ports"] = new() { Extensions = null, IsFolderMode = true },
            ["moonlight"] = new() { Extensions = null, IsFolderMode = true },
            ["steam"] = new() { Extensions = null, IsFolderMode = true },
            ["gog"] = new() { Extensions = null, IsFolderMode = true },
            // ========== Generic fallback ==========
            ["retro_emulation"] = new() { Extensions = new[] { 
                ".iso", ".bin", ".cue", ".chd", ".rvz", ".wbfs",
                ".z64", ".n64", ".v64",
                ".sfc", ".smc", ".nes",
                ".gb", ".gba", ".gbc",
                ".md", ".gen", ".smd",
                ".sms", ".gg", ".pce",
                ".zip", ".7z", ".rar"
            } },
            ["default"] = new() { Extensions = null, IsFolderMode = false }
        };

        private static string[] _allExtensions = _platformRules.Values
            .Where(r => r.Extensions != null)
            .SelectMany(r => r.Extensions)
            .Distinct()
            .Where(ext => !ext.Equals(".bin", StringComparison.OrdinalIgnoreCase)) // Exclude .bin from Auto-Scan (too generic, matches system files)
            .ToArray();

        // 2. Exclusion Rules (Global Blacklist)
        // Non-game files to ignore during scanning
        private static readonly HashSet<string> _globalBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            // Documents & Text
            ".nfo", ".txt", ".url", ".website", ".html", ".htm", ".md", ".rtf",
            ".pdf", ".doc", ".docx", ".xls", ".xlsx", ".ppt", ".pptx", ".odt", ".ods", ".odp",
            ".csv", ".xml", ".json", ".yaml", ".yml", ".log", ".ini", ".cfg", ".conf",
            
            // Checksums & Metadata
            ".sfv", ".md5", ".sha1", ".sha256", ".crc", ".par", ".par2",
            
            // Images
            ".jpg", ".jpeg", ".png", ".bmp", ".gif", ".webp", ".tiff", ".tif",
            ".ico", ".svg", ".heic", ".heif", ".raw", ".psd", ".ai", ".eps",
            ".xcf", ".dds", ".tga", ".exr", ".hdr",
            
            // Videos
            ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".m4v",
            ".mpg", ".mpeg", ".3gp", ".3g2", ".vob", ".ogv", ".mts", ".m2ts",
            ".ts", ".divx", ".xvid", ".rm", ".rmvb", ".asf",
            
            // Audio
            ".mp3", ".wav", ".flac", ".ogg", ".aac", ".wma", ".m4a", ".opus",
            ".ape", ".alac", ".aiff", ".mid", ".midi", ".mka",
            
            // Subtitles
            ".srt", ".sub", ".ass", ".ssa", ".vtt", ".idx",
            
            // System & Libraries
            ".ds_store", ".db", ".sqlite", ".sqlite3",
            ".dll", ".so", ".lib", ".a", ".bin", ".dat", ".bak",
            ".tmp", ".temp", ".cache", ".thumbs"
        };

        private static readonly HashSet<string> _keywordBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "steam_api", "crashpad", "unitycrash", "unins000", "uninstall", "update", "config", "dxsetup", "redist", "vcredist", "fna", "mono", "bios", "firmware", "retroarch", "overlay", "shdr", "slang", "glsl", "cg", "dlc", "update"
        };

        private readonly HashSet<string> _filenameBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "steam_api.dll", "steam_api64.dll", "openvr_api.dll", "nvapi.dll", "nvapi64.dll",
            "d3dcompiler_47.dll", "d3dcompiler_43.dll", "xinput1_3.dll", "xinput9_1_0.dll",
            "msvcp140.dll", "vcruntime140.dll", "unityplayer.dll", "crashpad_handler.exe", "unitycrashhandler64.exe",
            "unins000.exe", "uninstall.exe", "update.exe", "updater.exe", "config.exe", "settings.exe",
            "wmplayer.exe"
        };

        private static readonly HashSet<string> _folderBlacklist = new(StringComparer.OrdinalIgnoreCase)
        {
            "_CommonRedist", "CommonRedist", "Redist", "DirectX", "Support", 
            "Prerequisites", "Launcher", "Ship", "Shipping", 
            "Retail", "x64", "x86", "System", "Binaries", "Engine", "Content", "Asset", "Resource",
            "shadercache", "compatdata", "depotcache", ".steam", ".local", ".cache", "temp", "tmp", "node_modules",
            "windows", "system32", "syswow64", "Microsoft.NET", "Framework", "Framework64", "Internet Explorer", "Accessories", "Windows NT", "INF", "WinSxS", "SysARM32", "Sysnative", "command",
            "retroarch", "autoconfig", "assets", "overlays", "database", "cursors", "cheats", "filters", "libretro", "thumbnails", "config", "remaps", "playlists", "cores", "screenshots",
            "images", "videos", "manuals", "snaps", "marquees", "wheels", "boxart", "fanart",
            "z:", "d:"
        };

        private static readonly HashSet<string> _containerNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "common", "steamapps", "games", "juegos", "roms", "emulators", "others", "downloads", "library", "biblioteca", "collection"
        };

        private static readonly HashSet<string> _supplementaryFolderNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "Updates+DLCs", "Patches+DLCs", "Updates", "Patches", "DLC", "DLCs",
            "Addons", "Add-ons", "Updates+DLC", "Patches+DLC",
            "updates+dlcs", "patches+dlcs"
        };

        private static readonly HashSet<string> _noClusterExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".iso", ".bin", ".cue", ".pkg",
            ".nsp", ".xci", ".nsz", ".xcz",
            ".z64", ".n64", ".v64",
            ".sfc", ".smc", ".nes",
            ".gb", ".gbc", ".gba",
            ".md", ".gen", ".smd", ".sms", ".gg",
            ".pce"
        };

        [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
        private class PlatformRule
        {
            public string[] Extensions { get; set; } = Array.Empty<string>();
            public bool IsFolderMode { get; set; }
        }

        // Walk all platform name variants (folder, slug, retrobat/batocera/aliases)
        // and return the first matching rule, or the caller fallback.
        private PlatformRule ResolveRuleForPlatform(Platform platform, PlatformRule fallback)
        {
            if (_platformRules.TryGetValue(platform.GetEffectiveFolderName(), out var pr)) return pr;
            if (!string.IsNullOrEmpty(platform.Slug) && _platformRules.TryGetValue(platform.Slug, out pr)) return pr;
            if (!string.IsNullOrEmpty(platform.FolderName) && _platformRules.TryGetValue(platform.FolderName, out pr)) return pr;
            if (!string.IsNullOrEmpty(platform.RetroBatFolderName) && _platformRules.TryGetValue(platform.RetroBatFolderName, out pr)) return pr;
            if (!string.IsNullOrEmpty(platform.BatoceraFolderName) && _platformRules.TryGetValue(platform.BatoceraFolderName, out pr)) return pr;
            if (platform.FolderAliases != null)
            {
                foreach (var alias in platform.FolderAliases)
                {
                    if (!string.IsNullOrEmpty(alias) && _platformRules.TryGetValue(alias, out pr)) return pr;
                }
            }
            return fallback;
        }

        [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
        private class GameCandidate
        {
            public string Title { get; set; } = string.Empty;
            public string Path { get; set; } = string.Empty;
            public string? PlatformKey { get; set; }
            public string? Serial { get; set; }
            public string? ExecutablePath { get; set; }
            public bool IsInstaller { get; set; }
            public bool IsExternal { get; set; }
            public string? Region { get; set; }
            public string? Languages { get; set; }
            public string? Revision { get; set; }
        }

        private readonly ReviewItemService? _reviewService;

        private readonly DuplicateGameMergeService? _mergeService;

        public MediaScannerService(
            ConfigurationService configService,
            IGameMetadataServiceFactory metadataServiceFactory,
            IGameRepository gameRepository,
            TitleCleanerService titleCleaner,
            DebugLogService? debugLog = null,
            ReviewItemService? reviewService = null,
            DuplicateGameMergeService? mergeService = null)
        {
            _configService = configService;
            _metadataServiceFactory = metadataServiceFactory;
            _gameRepository = gameRepository;
            _titleCleaner = titleCleaner;
            _debugLog = debugLog;
            _reviewService = reviewService;
            _mergeService = mergeService;
        }

        public void StopScan()
        {
            if (IsScanning && _scanCts != null)
            {
                Log("Cancellation requested by user.");
                _scanCts.Cancel();
            }
        }

        public async Task CleanLibraryAsync()
        {
            Log("Cleaning library...");
            await _gameRepository.DeleteAllAsync();
            _gamesAddedCount = 0;
            _lastGameFound = null;
            Log("Library cleaned.");
        }

        public async Task<int> ScanAsync(string? overridePath = null, string? overridePlatform = null)
        {
            if (IsScanning)
            {
                _nlog.Warn("Scan skipped: Another scan is already in progress.");
                return 0;
            }

            var settings = _configService.LoadMediaSettings();
            var folderPath = overridePath ?? settings.FolderPath;
            var platformKey = overridePlatform ?? (string.IsNullOrWhiteSpace(settings.Platform) ? "default" : settings.Platform);

            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                var skipMsg = $"Media scanner skip: Path not configured or doesn't exist: '{folderPath}'";
                if (folderPath != null && folderPath.StartsWith("smb://", StringComparison.OrdinalIgnoreCase))
                {
                    skipMsg = "The address starts with 'smb://'. This is a protocol, not a path. Please mount the drive in Finder and use the path in '/Volumes/'.";
                }
                _nlog.Warn(skipMsg);
                Log(skipMsg);
                return 0;
            }

            if (!_platformRules.TryGetValue(platformKey, out var rule))
            {
                _nlog.Warn($"Unknown platform '{platformKey}', defaulting to standard rules.");
                rule = _platformRules["default"];
            }

            // Auto-enable platforms based on existing folders
            AutoEnablePlatformsFromFolders(folderPath);

            var logMsg = $"Starting scan. Platform: {platformKey}, FolderMode: {rule.IsFolderMode}, Path: {folderPath}";
            Log(logMsg);

            int gamesAdded = 0;
            var existingGames = await _gameRepository.GetAllLightAsync();
            var metadataService = _metadataServiceFactory.CreateService();

            OnScanStarted?.Invoke();
            _isScanning = true;
            _gamesAddedCount = 0;
            _lastGameFound = null;
            _filesScannedCount = 0;
            _currentScanDirectory = folderPath;
            _currentScanFile = null;
            _scanCts = new System.Threading.CancellationTokenSource();
            _debugLog?.StartScan();
            _debugLog?.UpdateScanProgress(currentPlatform: platformKey);

            try
            {
                // Auto-detect platform subfolders (e.g., /Library/3ds/, /Library/switch/, /Library/ps4/)
                var platformSubfolders = DetectPlatformSubfolders(folderPath);

                // If a specific platform was requested, filter to only that platform
                if (!string.IsNullOrEmpty(overridePlatform) && overridePlatform != "default" && platformSubfolders.Count > 0)
                {
                    platformSubfolders = platformSubfolders
                        .Where(p => p.Platform.MatchesFolderName(overridePlatform))
                        .ToList();
                    Log($"[PlatformFilter] Filtered to platform '{overridePlatform}': {platformSubfolders.Count} match(es)");
                }

                // If no platform subfolders detected, check if the root folder ITSELF is a platform
                // (e.g., user configured /media/gamecube as library root instead of /media)
                if (platformSubfolders.Count == 0 && platformKey == "default")
                {
                    var rootFolderName = new DirectoryInfo(folderPath).Name;
                    var rootPlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(rootFolderName));
                    if (rootPlatform != null)
                    {
                        Log($"[AutoPlatform] Root folder '{rootFolderName}' IS platform '{rootPlatform.Name}'. Treating as single-platform scan.");
                        platformSubfolders.Add((folderPath, rootPlatform));
                    }
                }

                if (platformSubfolders.Count > 0)
                {
                    Log($"[AutoPlatform] Detected {platformSubfolders.Count} platform subfolder(s): {string.Join(", ", platformSubfolders.Select(p => p.Platform.FolderName))}");
                    
                    foreach (var (platformFolder, platform) in platformSubfolders)
                    {
                        _scanCts.Token.ThrowIfCancellationRequested();

                        try
                        {
                            var platformRule = ResolveRuleForPlatform(platform, rule);
                            Log($"[AutoPlatform] Scanning '{platformFolder}' as {platform.Name}...");
                            UpdateScanProgress(directory: platformFolder);
                            _debugLog?.UpdateScanProgress(currentPlatform: platform.Name);
                            
                            if (platformRule.IsFolderMode)
                            {
                                var folderAdded = await ScanFolderModeAsync(platformFolder, platformRule, existingGames, platform.FolderName, metadataService, _scanCts.Token);
                                gamesAdded += folderAdded;

                                // Always also scan loose files in the platform root.
                                // Many platforms (Xbox 360, Wii U, PS3) mix folder-based
                                // games with loose .iso/.wua files in the same directory.
                                if (!_scanCts.Token.IsCancellationRequested)
                                {
                                    var looseAdded = await ScanFileModeAsync(platformFolder, platformRule, existingGames, platform.FolderName, metadataService, _scanCts.Token);
                                    if (looseAdded > 0)
                                        Log($"[AutoPlatform] FileMode picked up {looseAdded} loose file(s) for '{platform.Name}'.");
                                    gamesAdded += looseAdded;
                                }
                            }
                            else
                            {
                                gamesAdded += await ScanFileModeAsync(platformFolder, platformRule, existingGames, platform.FolderName, metadataService, _scanCts.Token);
                            }

                            // Scan supplementary folders (Updates+DLCs, Patches+DLCs, etc.) and link content to existing games
                            await ScanSupplementaryFoldersAsync(platformFolder, platform.FolderName, platformRule, existingGames, _scanCts.Token);

                            // Cleanup stale games (paths no longer on disk) and resync existing games' files
                            await CleanupAndResyncPlatformAsync(platformFolder, platform.FolderName, existingGames, _scanCts.Token);
                        }
                        catch (OperationCanceledException) { throw; }
                        catch (Exception ex)
                        {
                            Log($"[AutoPlatform] Error scanning platform '{platform.Name}' at '{platformFolder}': {ex.Message}. Continuing with next platform.", LogLevel.Error);
                        }
                    }
                }
                else if (rule.IsFolderMode)
                {
                    gamesAdded = await ScanFolderModeAsync(folderPath, rule, existingGames, platformKey, metadataService, _scanCts.Token);

                    // Also scan loose files — same reason as the platform-subfolder path above.
                    if (!_scanCts.Token.IsCancellationRequested)
                    {
                        var looseAdded = await ScanFileModeAsync(folderPath, rule, existingGames, platformKey, metadataService, _scanCts.Token);
                        if (looseAdded > 0)
                            Log($"FileMode picked up {looseAdded} loose file(s) for '{platformKey}'.");
                        gamesAdded += looseAdded;
                    }

                    await CleanupAndResyncPlatformAsync(folderPath, platformKey, existingGames, _scanCts.Token);
                }
                else
                {
                    gamesAdded = await ScanFileModeAsync(folderPath, rule, existingGames, platformKey, metadataService, _scanCts.Token);
                    await CleanupAndResyncPlatformAsync(folderPath, platformKey, existingGames, _scanCts.Token);
                }

                // Wine/Whisky integration (external library, scanned separately if configured)
                var winePath = settings.WinePrefixPath;
                // Only scan external libraries if we are doing a FULL library scan (no override path)
                if (string.IsNullOrEmpty(overridePath) && !string.IsNullOrEmpty(winePath) && Directory.Exists(winePath))
                {
                    Log($"Scanning Wine/Whisky External Path: {winePath}");
                    var externalGames = await ScanExternalLibraryAsync(winePath, existingGames, metadataService, _scanCts.Token);
                    gamesAdded += externalGames;
                }

                // Full sweep runs only on full-library scans (the Missing/purge
                // pass depends on seeing every platform). Per-platform scans
                // still get a cheap heal pass so wrong PlatformId rows get
                // fixed without waiting for the next full scan.
                if (string.IsNullOrEmpty(overridePath))
                {
                    await GlobalOrphanSweepAsync(settings, _scanCts.Token);
                }
                else
                {
                    try { await HealWrongPlatformsAsync(_scanCts.Token); }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex) { Log($"[Heal] skipped: {ex.Message}", LogLevel.Warning); }
                }
            }
            catch (OperationCanceledException)
            {
                Log("Scan was cancelled by user.");
            }
            catch (Exception ex)
            {
                Log($"Error during scan: {ex.Message}");
            }
            finally
            {
                _isScanning = false;
                _currentScanDirectory = null;
                _currentScanFile = null;
                _scanCts?.Dispose();
                _scanCts = null;
                _gamesAddedCount = gamesAdded;
                _debugLog?.EndScan();
                Log($"Scan Finished/Stopped. Added: {gamesAdded}");
                OnScanFinished?.Invoke(gamesAdded);
            }

            return gamesAdded;
        }

        private async Task<int> ScanFolderModeAsync(string rootPath, PlatformRule rule, List<Game> existingGames, string platformKey, GameMetadataService metadataService, System.Threading.CancellationToken ct)
        {
            var candidates = new List<GameCandidate>();
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(rootPath);
            }
            catch (Exception ex)
            {
                Log($"Error accessing root path: {ex.Message}");
                return 0;
            }

            var pcPlatforms = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                { "pc", "pc_windows", "windows", "macos", "macintosh", "linux", "dos", "dosbox" };

            // Resolve platform ID once outside the loop
            int scanPlatformId = 0;
            var scanPlatDef = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                p => p.MatchesFolderName(platformKey));
            if (scanPlatDef != null) scanPlatformId = scanPlatDef.Id;

            foreach (var dir in directories)
            {
                ct.ThrowIfCancellationRequested();
                var folderName = Path.GetFileName(dir);
                if (_folderBlacklist.Contains(folderName) || folderName.StartsWith(".")) continue;
                if (_supplementaryFolderNames.Contains(folderName))
                {
                    Log($"[Scanner] Skipping supplementary folder (will scan later): {folderName}");
                    continue;
                }

                try
                {
                    // Advanced Scanner Logic V2: Find the best executable in the folder structure
                    var (bestExePath, isInstaller) = FindBestExecutable(dir, rule.Extensions, isExternal: false);
                    
                    // For console folder-mode platforms the folder IS the game — no executable required.
                    // PC platforms still need an executable to avoid picking up random folders.
                    if (string.IsNullOrEmpty(bestExePath) && pcPlatforms.Contains(platformKey))
                        continue;

                    // Strip container extensions (.ps3, .ps4) before title cleaning
                    var (baseFolderName, _) = TitleCleanerService.StripContainerExtension(folderName);
                    var (region, langs, revision) = TitleCleanerService.ExtractFilenameMetadata(baseFolderName);
                    var (cleanName, serial) = _titleCleaner.CleanGameTitle(baseFolderName);
                    
                    // Check if game exists for THIS platform (allow same title on different platforms)
                    if (!existingGames.Any(g => g.Title.Equals(cleanName, StringComparison.OrdinalIgnoreCase) &&
                        (scanPlatformId == 0 || g.PlatformId == scanPlatformId)))
                    {
                        var candidate = new GameCandidate 
                        { 
                            Title = cleanName, 
                            Path = dir,
                            PlatformKey = platformKey, 
                            Serial = serial,
                            ExecutablePath = bestExePath,
                            IsInstaller = isInstaller,
                            Region = region,
                            Languages = langs,
                            Revision = revision
                        };
                        candidates.Add(candidate);
                    }
                    else
                    {
                        var existingGame = existingGames.FirstOrDefault(g => g.Title.Equals(cleanName, StringComparison.OrdinalIgnoreCase) &&
                            (scanPlatformId == 0 || g.PlatformId == scanPlatformId));
                        if (existingGame != null)
                        {
                            await SyncGameFilesFromDisk(existingGame.Id, dir);
                            Log($"Game already exists in DB (resynced files): {cleanName}");
                        }
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    Log($"[Scanner] Error processing folder '{folderName}': {ex.Message}. Continuing.", LogLevel.Warning);
                }
            }

            return await ProcessCandidatesBatchAsync(candidates, existingGames, metadataService, ct);
        }

        // Updated Helper for V2 Scanner
        // Returns: (Path to best executable, IsInstaller)
        private (string? Path, bool IsInstaller) FindBestExecutable(string folderPath, string[]? allowedExtensions, bool isExternal = false)
        {
            try
            {
                var root = new DirectoryInfo(folderPath);
                if (!root.Exists) return (null, false);

                var candidates = new List<(FileInfo File, int Score, bool IsInstaller)>();
                
                // Recursive search with depth limit (e.g. 3-4 levels) to avoid scanning too deep
                // And explicitly skipping blacklist folders
                var allFiles = GetFilesSafe(root, 0, isExternal ? 5 : 3); 

                foreach (var file in allFiles)
                {
                    bool isAllowedByPlatform = allowedExtensions?.Contains(file.Extension, StringComparer.OrdinalIgnoreCase) == true;
                    if (allowedExtensions != null && !isAllowedByPlatform) continue;
                    if (!isAllowedByPlatform && _globalBlacklist.Contains(file.Extension)) continue;

                    int score = 0;
                    bool isInstaller = false;
                    string name = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant();
                    if (string.IsNullOrEmpty(name)) name = file.Name.ToLowerInvariant(); // Support extensionless files (v0.4.2)
                    string folderName = file.Directory?.Name.ToLowerInvariant() ?? "";
                    
                    // --- SCORING RULES (v0.4.2 Winner Takes All) ---
                    
                    // 1. Blacklist Filtering (TAREA 1)
                    if (IsBlacklistedFile(file.Name, isExternal)) continue;

                    // 2. Name Match (+100) (TAREA 3)
                    var rootFolderName = root.Name.ToLowerInvariant();
                    if (name == rootFolderName) score += 100;
                    else if (name.Replace(" ", "").Replace("-", "") == rootFolderName.Replace(" ", "").Replace("-", "")) score += 90;
                    else if (rootFolderName.Contains(name) && name.Length > 4) score += 30;

                    // 3. Priority Names (+50) (TAREA 2 & 3)
                    if (file.Name.Equals("AppRun", StringComparison.OrdinalIgnoreCase) || 
                        file.Name.Equals("Start.sh", StringComparison.OrdinalIgnoreCase))
                    {
                        score += 50;
                    }

                    // 4. Installer/Config Penalty (-50) (TAREA 3)
                    if (name.Contains("launch") || name.Contains("settings") || name.Contains("server") || 
                        name.Contains("config") || name.Contains("setup") || name.Contains("install"))
                    {
                        score -= 50;
                        if (name.Contains("setup") || name.Contains("install")) isInstaller = true;
                    }

                    // 5. Folder Location Bonus
                    if (folderName == "binaries" || folderName == "win64" || folderName == "release" || folderName == "shipping" || folderName == "retail") score += 25;
                    
                    // 6. Native Linux executable bonus (on Linux systems)
                    if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) && string.IsNullOrEmpty(file.Extension))
                    {
                        score += 10;
                    }
                    
                    candidates.Add((file, score, isInstaller));
                }

                if (!candidates.Any()) return (null, false);

                // 7. Largest File Bonus (+20) (TAREA 3)
                var largestFile = candidates.OrderByDescending(x => x.File.Length).FirstOrDefault();
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (candidates[i].File.FullName == largestFile.File.FullName)
                    {
                        candidates[i] = (candidates[i].File, candidates[i].Score + 20, candidates[i].IsInstaller);
                    }
                }

                // Final Selection
                var winner = candidates.OrderByDescending(x => x.Score).FirstOrDefault();
                
                // --- Linux Executable Intelligence (v0.4.4) ---
                if (System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux) && 
                    winner.File != null && string.IsNullOrEmpty(winner.File.Extension))
                {
                    if (!IsBinaryExecutable(winner.File.FullName))
                    {
                        Log($"[Scanner] Skipping non-binary Linux candidate: {winner.File.Name}");
                        return (null, false);
                    }
                }

                return (winner.File.FullName, winner.IsInstaller);
            }
            catch (Exception ex)
            {
                Log($"Error discovering executable in {folderPath}: {ex.Message}");
                return (null, false);
            }
        }

        private bool IsBinaryExecutable(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read))
                {
                    if (fs.Length < 4) return false;
                    var buffer = new byte[4];
                    fs.Read(buffer, 0, 4);

                    // 1. ELF Header: 0x7F 'E' 'L' 'F'
                    if (buffer[0] == 0x7F && buffer[1] == 0x45 && buffer[2] == 0x4C && buffer[3] == 0x46) return true;

                    // 2. Shebang: #!
                    if (buffer[0] == 0x23 && buffer[1] == 0x21) return true;
                }
            }
            catch { }
            return false;
        }

        private List<FileInfo> GetFilesSafe(DirectoryInfo root, int currentDepth, int maxDepth)
        {
            var results = new List<FileInfo>();
            
            // Intelligence: If we are in a "Bridge" folder (common, SteamApps), reset depth to allow finding games inside
            if (root.Name.Equals("common", StringComparison.OrdinalIgnoreCase) || root.Name.Equals("SteamApps", StringComparison.OrdinalIgnoreCase))
            {
                maxDepth += 2; // Allow 2 more levels for Steam nested games
            }

            if (currentDepth > maxDepth) return results;

            // blacklist folders
            if (root.Name.StartsWith(".") || _folderBlacklist.Contains(root.Name) || IsMetadataSubfolder(root.Name))
            {
                return results;
            }

            try
            {
                results.AddRange(root.GetFiles().OrderBy(f => f.FullName, StringComparer.Ordinal));
                foreach (var dir in root.GetDirectories().OrderBy(d => d.FullName, StringComparer.Ordinal))
                {
                    results.AddRange(GetFilesSafe(dir, currentDepth + 1, maxDepth));
                }
            }
            catch { } // Ignore permission errors

            return results;
        }

        private async Task<int> ScanFileModeAsync(string rootPath, PlatformRule rule, List<Game> existingGames, string platformKey, GameMetadataService metadataService, System.Threading.CancellationToken ct)
        {
            var candidates = new List<GameCandidate>();
            var extensionsToUse = platformKey == "default" ? _allExtensions : rule.Extensions;
            Log($"Scanning (Fast FileMode) Root: {rootPath}. Valid Extensions: {(extensionsToUse != null ? string.Join(", ", extensionsToUse) : "ALL")}");

            // Resolve platform ID once for dedup checks
            int fileScanPlatformId = 0;
            var fileScanPlatDef = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                p => p.MatchesFolderName(platformKey));
            if (fileScanPlatDef != null) fileScanPlatformId = fileScanPlatDef.Id;

            try
            {
                var validFilesByFolder = new Dictionary<string, List<string>>();
                
                // Fast Hierarchical Discovery (v0.4.2)
                // Instead of SearchOption.AllDirectories (Slow), we recurse manually and skip blacklisted branches
                DiscoverFilesHierarchical(new DirectoryInfo(rootPath), extensionsToUse, validFilesByFolder, ct, 0, 4); 

                Log($"[FileMode] Discovery phase finished. Found {validFilesByFolder.Count} candidate folders items. Applying clustering...");

                foreach (var folderEntry in validFilesByFolder)
                {
                    ct.ThrowIfCancellationRequested();
                    var folderPath = folderEntry.Key;
                    var filePaths = folderEntry.Value;

                    var folderName = new DirectoryInfo(folderPath).Name;
                    bool isContainer = _containerNames.Contains(folderName);
                    bool isConsole = platformKey != "pc_windows" && platformKey != "pc" && platformKey != "windows" 
                                     && platformKey != "macos" && platformKey != "macintosh" 
                                     && platformKey != "dos" && platformKey != "linux" && platformKey != "default";

                    // TAREA 2: Skip clustering for specific extensions (ROMs, ISOs, PKG, etc.)
                    // Check if ANY file in the folder has an extension that skips clustering
                    bool hasNoClusterExtension = filePaths.Any(f => _noClusterExtensions.Contains(Path.GetExtension(f)));

                    // LOGIC SWITCH: If it's a container, a console platform, or has "No-Cluster" extensions, DO NOT cluster.
                    if (isContainer || isConsole || hasNoClusterExtension)
                    {
                        // Pair multi-file ROMs (cue+bin, gdi+bins, m3u+cues,
                        // identical-stem siblings) into one candidate.
                        var orderedPaths = filePaths
                            .OrderBy(f => Path.GetExtension(f).ToLowerInvariant() switch
                            {
                                ".m3u" => 0,
                                ".cue" => 1,
                                ".gdi" => 2,
                                _ => 3
                            })
                            .ToList();
                        var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                        foreach (var filePath in orderedPaths)
                        {
                            if (claimed.Contains(filePath)) continue;

                            var fileSet = FileSetResolver.Resolve(filePath);
                            foreach (var member in fileSet.AllFiles)
                                claimed.Add(Path.GetFullPath(member));

                            // Catches formats FileSetResolver doesn't model
                            // (.iso+.mds, .ccd+.img+.sub) by exact stem match.
                            var stem = Path.GetFileNameWithoutExtension(filePath);
                            if (!string.IsNullOrEmpty(stem))
                            {
                                foreach (var sibling in orderedPaths)
                                {
                                    if (sibling.Equals(filePath, StringComparison.OrdinalIgnoreCase)) continue;
                                    if (claimed.Contains(sibling)) continue;
                                    if (Path.GetFileNameWithoutExtension(sibling).Equals(stem, StringComparison.OrdinalIgnoreCase))
                                        claimed.Add(sibling);
                                }
                            }

                            var rawFileName = Path.GetFileNameWithoutExtension(filePath);
                            if (string.IsNullOrEmpty(rawFileName)) rawFileName = Path.GetFileName(filePath);

                            var (fileRegion, fileLangs, fileRevision) = TitleCleanerService.ExtractFilenameMetadata(rawFileName);
                            var (cleanTitle, serial) = _titleCleaner.CleanGameTitle(rawFileName);

                            if (!existingGames.Any(g => g.Title.Equals(cleanTitle, StringComparison.OrdinalIgnoreCase) &&
                                (fileScanPlatformId == 0 || g.PlatformId == fileScanPlatformId)))
                            {
                                var candidate = new GameCandidate 
                                { 
                                    Title = cleanTitle, 
                                    Path = filePath,
                                    PlatformKey = platformKey == "default" ? _titleCleaner.GetPlatformFromExtension(Path.GetExtension(filePath)) : platformKey, 
                                    Serial = serial,
                                    ExecutablePath = filePath,
                                    IsInstaller = false,
                                    Region = fileRegion,
                                    Languages = fileLangs,
                                    Revision = fileRevision
                                };

                                // TAREA: Ambiguity Resolution for ISO/BIN/PKG in console mode too
                                if (candidate.PlatformKey == "default" || candidate.PlatformKey == "ps4")
                                {
                                    var refined = _titleCleaner.ResolvePlatformFromSerial(serial ?? "");
                                    if (refined != "default") candidate.PlatformKey = refined;
                                }

                                candidates.Add(candidate);
                            }
                        }
                        continue;
                    }

                    // TAREA 3: PC/Desktop Logic: Only apply clustering if we have executable scripts/binaries
                    // Otherwise treat them as one-file-one-game if they are unique enough (or let the filter handle it)
                    var (bestExePath, isInstaller) = FindBestExecutableInList(folderPath, filePaths);

                    if (!string.IsNullOrEmpty(bestExePath))
                    {
                        var rawFileName = Path.GetFileNameWithoutExtension(bestExePath);
                        if (string.IsNullOrEmpty(rawFileName)) rawFileName = Path.GetFileName(bestExePath);
                        
                        var dirInfo = new DirectoryInfo(folderPath);
                        var rawFolderName = dirInfo.Name;

                        // Intelligence: If folder name is generic (e.g. "bin", "x64", "x64.gog"), climb up
                        var genericFolders = new HashSet<string>(StringComparer.OrdinalIgnoreCase) 
                        { 
                            "bin", "binaries", "data", "system", "system32", "x64", "x86", "win64", "win32", 
                            "release", "retail", "debug", "shipping", "gog", "game", "games"
                        };

                        if ((genericFolders.Contains(rawFolderName) || rawFolderName.EndsWith(".gog", StringComparison.OrdinalIgnoreCase)) && dirInfo.Parent != null)
                        {
                            // Check if parent is also generic (could happen in nested structures like bin/x64)
                             if (genericFolders.Contains(dirInfo.Parent.Name))
                             {
                                 if (dirInfo.Parent.Parent != null) rawFolderName = dirInfo.Parent.Parent.Name;
                             }
                             else
                             {
                                 rawFolderName = dirInfo.Parent.Name;
                             }
                        }
                        
                        // Clean both to compare their "quality"
                        var (cleanFile, _) = _titleCleaner.CleanGameTitle(rawFileName);
                        var (cleanFolder, _) = _titleCleaner.CleanGameTitle(rawFolderName);

                        // Selection Heuristic
                        bool isGenericFile = rawFileName.Equals("setup", StringComparison.OrdinalIgnoreCase) || 
                                             rawFileName.Equals("install", StringComparison.OrdinalIgnoreCase) || 
                                             rawFileName.Equals("game", StringComparison.OrdinalIgnoreCase);

                        string selectedName;
                        string source;

                        if (isGenericFile)
                        {
                            selectedName = cleanFolder;
                            source = "Folder (Generic File)";
                        }
                        else if (cleanFolder.Equals(cleanFile, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedName = cleanFolder;
                            source = "Folder (Exact Match)";
                        }
                        else if (cleanFolder.Contains(cleanFile, StringComparison.OrdinalIgnoreCase) && !_noiseWords.Any(nw => cleanFolder.Contains(nw, StringComparison.OrdinalIgnoreCase)))
                        {
                            selectedName = cleanFolder;
                            source = "Folder (Subset Match)";
                        }
                        else if (cleanFile.Contains(cleanFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            // If filename contains folder name, usually filename has MORE noise (e.g. "Streets of Rage 4 g" vs "Streets of Rage 4")
                            // Prefer folder if it's cleaner/shorter
                            if (cleanFile.Length > cleanFolder.Length + 3)
                            {
                                selectedName = cleanFolder;
                                source = "Folder (Cleaner Substring)";
                            }
                            else
                            {
                                selectedName = cleanFile;
                                source = "Filename (Subset Match)";
                            }
                        }
                        else if (cleanFolder.Length > 3 && cleanFile.StartsWith(cleanFolder, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedName = cleanFolder;
                            source = "Folder (Cleaner Prefix)";
                        }
                        else if (cleanFile.Length > 3 && cleanFolder.StartsWith(cleanFile, StringComparison.OrdinalIgnoreCase))
                        {
                            selectedName = cleanFile;
                            source = "Filename (Cleaner Prefix)";
                        }
                        else
                        {
                            selectedName = cleanFolder.Length > 0 ? cleanFolder : cleanFile;
                            source = cleanFolder.Length > 0 ? "Folder (Context Default)" : "Filename (Fallback)";
                        }

                        var finalTitle = selectedName;
                        var (_, serial) = _titleCleaner.CleanGameTitle(rawFileName); 
                        
                        Log($"[Scanner] Title Resolution: File('{cleanFile}') vs Folder('{cleanFolder}') -> Selected: '{finalTitle}' (Source: {source})");

                        if (!existingGames.Any(g => g.Title.Equals(finalTitle, StringComparison.OrdinalIgnoreCase) &&
                            (fileScanPlatformId == 0 || g.PlatformId == fileScanPlatformId)))
                        {
                            string finalPlatformKey = platformKey;
                            if (platformKey == "default") 
                            {
                                finalPlatformKey = _titleCleaner.GetPlatformFromExtension(Path.GetExtension(bestExePath));
                                
                                if (finalPlatformKey == "default" || finalPlatformKey == "ps4")
                                {
                                    var refined = _titleCleaner.ResolvePlatformFromSerial(serial ?? "");
                                    if (refined != "default") finalPlatformKey = refined;
                                }
                            }

                            var (clusterRegion, clusterLangs, clusterRevision) = TitleCleanerService.ExtractFilenameMetadata(rawFolderName);
                            candidates.Add(new GameCandidate 
                            { 
                                Title = finalTitle, 
                                Path = folderPath,
                                PlatformKey = finalPlatformKey, 
                                Serial = serial,
                                ExecutablePath = bestExePath,
                                IsInstaller = isInstaller,
                                Region = clusterRegion,
                                Languages = clusterLangs,
                                Revision = clusterRevision
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error during Fast FileMode scan: {ex.Message}");
            }

            return await ProcessCandidatesBatchAsync(candidates, existingGames, metadataService, ct);
        }

        private void DiscoverFilesHierarchical(DirectoryInfo root, string[]? allowedExtensions, Dictionary<string, List<string>> results, System.Threading.CancellationToken ct, int currentDepth, int maxDepth)
        {
            ct.ThrowIfCancellationRequested();

            if (!root.Exists) return;

            bool isContainer = _containerNames.Contains(root.Name);

            // Intelligence: If we are in a "Bridge" (Container) folder, allow deeper scan
            if (isContainer)
            {
                maxDepth += 2;
            }

            if (currentDepth > maxDepth) return;

            if (currentDepth > 0 && (root.Name.StartsWith(".") || _folderBlacklist.Contains(root.Name) || IsMetadataSubfolder(root.Name))) return;
            if (_supplementaryFolderNames.Contains(root.Name)) return;

            try
            {
                // If it's a container, we still look for files (e.g., ROMs in 'Juegos')
                // but we give them a chance to be clustered differently.
                // Sort by full path so rescans pick the same primary file.
                var files = root.EnumerateFiles().OrderBy(f => f.FullName, StringComparer.Ordinal);
                foreach (var file in files)
                {
                    if (IsBlacklistedFile(file.Name, isExternal: false)) continue;

                    if (IsValidFile(file.FullName, allowedExtensions))
                    {
                        if (!results.ContainsKey(root.FullName))
                            results[root.FullName] = new List<string>();

                        results[root.FullName].Add(file.FullName);
                    }
                }

                foreach (var subDir in root.EnumerateDirectories().OrderBy(d => d.FullName, StringComparer.Ordinal))
                {
                    // TAREA: PS3 Folder Detection
                    if (subDir.Name.Equals("PS3_GAME", StringComparison.OrdinalIgnoreCase))
                    {
                        // If we find PS3_GAME, the root folder is a PS3 game
                        if (!results.ContainsKey(root.FullName))
                            results[root.FullName] = new List<string>();
                        
                        // Treat the folder as a single game entry for ScanFileMode.
                        if (!results[root.FullName].Contains(subDir.FullName))
                            results[root.FullName].Add(subDir.FullName); 
                        
                        continue; // No need to recurse into PS3_GAME for files
                    }

                    DiscoverFilesHierarchical(subDir, allowedExtensions, results, ct, currentDepth + 1, maxDepth);
                }
            }
            catch { /* Skip permission errors */ }
        }

        // New Helper for clustering in File Mode
        private (string? Path, bool IsInstaller) FindBestExecutableInList(string folderPath, List<string> filePaths)
        {
            var candidates = new List<(string FilePath, int Score, bool IsInstaller)>();
            var root = new DirectoryInfo(folderPath);

            foreach (var filePath in filePaths)
            {
                var file = new FileInfo(filePath);
                int score = 0;
                bool isInstaller = false;
                string name = Path.GetFileNameWithoutExtension(file.Name).ToLowerInvariant();
                if (string.IsNullOrEmpty(name)) name = file.Name.ToLowerInvariant();
                
                // Scoring (Same logic as FindBestExecutable but for a specific list)
                var rootFolderName = root.Name.ToLowerInvariant();
                if (name == rootFolderName) score += 100;
                else if (name.Replace(" ", "").Replace("-", "") == rootFolderName.Replace(" ", "").Replace("-", "")) score += 90;
                else if (rootFolderName.Contains(name) && name.Length > 4) score += 30;

                if (file.Name.Equals("AppRun", StringComparison.OrdinalIgnoreCase) || 
                    file.Name.Equals("Start.sh", StringComparison.OrdinalIgnoreCase)) score += 50;

                if (name.Contains("launch") || name.Contains("settings") || name.Contains("server") || 
                    name.Contains("config") || name.Contains("setup") || name.Contains("install"))
                {
                    score -= 50;
                    if (name.Contains("setup") || name.Contains("install")) isInstaller = true;
                }

                candidates.Add((filePath, score, isInstaller));
            }

            if (!candidates.Any()) return (null, false);

            // TAREA 3: Only cluster if we found typical PC executables/scripts
            // If we only have data files or other extensions, the selection might be weaker
            var winner = candidates.OrderByDescending(c => c.Score).First();
            
            // Check if winner extension is PC-specific for clustering
            var winnerExt = Path.GetExtension(winner.FilePath).ToLowerInvariant();
            bool isPcExec = winnerExt == ".exe" || winnerExt == ".bat" || winnerExt == ".sh" || string.IsNullOrEmpty(winnerExt);
            
            if (!isPcExec)
            {
                // If the "best" file isn't an executable, we might be mis-clustering.
                // FindBestExecutableInList is only called if clustering is allowed.
                // We already filtered No-Cluster extensions in ScanFileModeAsync.
            }

            // Tie-break with size if scores are equal
            var topScorers = candidates.Where(c => c.Score == winner.Score).ToList();
            if (topScorers.Count > 1)
            {
                 winner = topScorers.OrderByDescending(c => new FileInfo(c.FilePath).Length).First();
            }

            return (winner.FilePath, winner.IsInstaller);
        }

        private async Task<int> ScanExternalLibraryAsync(string rootPath, List<Game> existingGames, GameMetadataService metadataService, System.Threading.CancellationToken ct)
        {
            var candidates = new List<GameCandidate>();
            Log($"Starting Hierarchical External Scan: {rootPath}");
            
            await ScanDirectoryHierarchicalAsync(new DirectoryInfo(rootPath), candidates, existingGames, ct);
            
            return await ProcessCandidatesBatchAsync(candidates, existingGames, metadataService, ct);
        }

        private async Task ScanDirectoryHierarchicalAsync(DirectoryInfo dir, List<GameCandidate> candidates, List<Game> existingGames, System.Threading.CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();

            if (!dir.Exists || IsBlacklistedFolder(dir)) return;
            if (IsMetadataSubfolder(dir.Name)) return;

            // 1. Is this folder a game candidate?
            // Criteria: Not generic AND has a "local" executable.
            bool isGeneric = IsGenericFolderName(dir.Name);
            
            if (!isGeneric)
            {
                // We use FindBestExecutable with depth 5 for external libraries, 
                // but we check if the found EXE actually "belongs" to this folder level.
                var (bestExe, isInstaller) = FindBestExecutable(dir.FullName, new[] { ".exe" }, isExternal: true);

                if (!string.IsNullOrEmpty(bestExe))
                {
                    // Logic: If the EXE is found deep inside another NON-GENERIC folder, 
                    // then this current folder is just a container, and we should recurse.
                    if (IsExeBelongingToFolder(dir.FullName, bestExe))
                    {
                        var folderName = dir.Name;
                        var (cleanName, serial) = _titleCleaner.CleanGameTitle(folderName);

                        if (!IsBlacklistedTitle(cleanName))
                        {
                            bool alreadyInLibrary = existingGames.Any(g => g.Title.Equals(cleanName, StringComparison.OrdinalIgnoreCase));
                            bool alreadyInBatch = candidates.Any(c => c.Title.Equals(cleanName, StringComparison.OrdinalIgnoreCase));

                            if (!alreadyInLibrary && !alreadyInBatch)
                            {
                                candidates.Add(new GameCandidate
                                {
                                    Title = cleanName,
                                    Path = dir.FullName,
                                    ExecutablePath = bestExe,
                                    IsInstaller = isInstaller,
                                    IsExternal = true,
                                    PlatformKey = "pc_windows",
                                    Serial = serial
                                });
                                
                                Log($"[ExternalScan] Identified new game at: {dir.FullName} -> {cleanName}. Following subdirectories skipped.");
                            }
                            else
                            {
                                Log($"[ExternalScan] Folder {dir.FullName} identified as existing game '{cleanName}'. Skipping subdirectories.");
                            }

                            return; // STOP recursion here because this folder IS a game
                        }
                    }
                }
            }

            // 2. Otherwise (generic folder or no local EXE), continue searching subdirectories
            try
            {
                foreach (var subDir in dir.GetDirectories())
                {
                    await ScanDirectoryHierarchicalAsync(subDir, candidates, existingGames, ct);
                }
            }
            catch (Exception ex)
            {
                Log($"Error accessing subdirectories of {dir.FullName}: {ex.Message}");
            }
        }

        private bool IsExeBelongingToFolder(string folderPath, string exePath)
        {
            var relative = Path.GetRelativePath(folderPath, exePath);
            var parts = relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            
            // If it's more than 3 levels deep, it's likely a sub-game or too nested to be "the" game of this folder.
            if (parts.Length > 4) return false; 
            
            // If any folder in the path between current folder and EXE is NOT generic,
            // then the EXE probably belongs to that sub-folder instead.
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (!IsGenericFolderName(parts[i])) return false;
            }
            
            return true;
        }

        private bool IsGenericFolderName(string name)
        {
            var generics = new[] 
            { 
                "Ship", "Shipping", "Retail", "Binaries", "x64", "x86", "Win64", "Win32", "Release", 
                "drive_c", "Program Files", "Program Files (x86)", "Users", "Games", "Juegos", "My Games", "Mis Juegos",
                "FitGirl", "FitGirl Repack", "DODI", "DODI Repack", "KaOs", "ElAmigos", "Repack", "Bottles", "drive_c/Games",
                "GOG Games", "Epic Games", "SteamLibrary", "common", "Games_Installed", "Installer",
                "windows", "system32", "syswow64", "Microsoft.NET", "Internet Explorer", "Windows NT"
            };
            return generics.Any(g => name.Equals(g, StringComparison.OrdinalIgnoreCase) || name.Contains(g, StringComparison.OrdinalIgnoreCase));
        }
        
        private bool IsBlacklistedFile(string fileName, bool isExternal)
        {
            if (string.IsNullOrEmpty(fileName)) return false;

            // 1. Exact Filename Blacklist
            if (_filenameBlacklist.Contains(fileName)) return true;

            // 2. Keyword/Substring Blacklist (TAREA 1)
            string lowered = fileName.ToLowerInvariant();
            foreach (var keyword in _keywordBlacklist)
            {
                if (lowered.Contains(keyword)) return true;
            }

            return false;
        }

        private bool IsMetadataSubfolder(string name)
        {
            var metadataFolders = new[] { "artworks", "soundtrack", "avatars", "manual", "wallpapers", "Goodies",            "Common", "Prerequisites", "Support", "Redist", "DirectX", "DotNet", "VCRedist", "PhysX",
            "Windows Media Player", "MD5", "z:"
        };
    return metadataFolders.Any(f => name.EndsWith(f, StringComparison.OrdinalIgnoreCase) || name.Contains(f, StringComparison.OrdinalIgnoreCase));
        }

        private bool IsBlacklistedTitle(string title)
        {
             var block = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { 
                 "Windows", "Program Files", "Program Files (x86)", "Common Files", "Users", 
                 "drive_c", "dosdevices", "Binaries", "Win64", "Win32", "Common", "Engine", "Content",
                 "System32", "syswow64", "Microsoft.NET", "Accessories", "Command",
                 "x64", "x86", "Windows Media Player"
             };
             return block.Contains(title) || _folderBlacklist.Contains(title) || IsMetadataSubfolder(title) || Regex.IsMatch(title, @"^\d+$");
        }

        private bool IsBlacklistedFolder(DirectoryInfo dir)
        {
             return _folderBlacklist.Contains(dir.Name) || dir.Name.StartsWith(".");
        }

        private async Task<int> ProcessCandidatesBatchAsync(List<GameCandidate> candidates, List<Game> existingGames, GameMetadataService metadataService, System.Threading.CancellationToken ct)
        {
            int added = 0;
            const int batchSize = 20;
            const int delayMs = 500;

            Log($"Processing {candidates.Count} candidates in batches of {batchSize}...");

            for (int i = 0; i < candidates.Count; i += batchSize)
            {
                ct.ThrowIfCancellationRequested();
                var batch = candidates.Skip(i).Take(batchSize).ToList();

                foreach (var candidate in batch)
                {
                    ct.ThrowIfCancellationRequested();

                    try
                    {
                        // Re-evaluate executable for Folder candidates (Lazy evaluation)
                        string? exePath = candidate.ExecutablePath;
                        bool isInstaller = candidate.IsInstaller;

                        // If it's a folder (no extension) AND no explicit exe path set, try to find it
                        if (string.IsNullOrEmpty(exePath) && Directory.Exists(candidate.Path) && !File.Exists(candidate.Path))
                        {
                             // Folder Mode: Find best exe
                             var result = FindBestExecutable(candidate.Path, null, candidate.IsExternal); // Pass null for exts or assume defaults
                             exePath = result.Path;
                             isInstaller = result.IsInstaller;
                        }
                        else if (string.IsNullOrEmpty(exePath))
                        {
                            // File Mode: Path isExe
                            exePath = candidate.Path;
                            string fileName = Path.GetFileName(exePath);
                            var name = Path.GetFileNameWithoutExtension(exePath);
                            
                            // Check if file is blacklisted in File Mode (re-check with context if needed)
                            if (IsBlacklistedFile(fileName, candidate.IsExternal)) continue;

                            isInstaller = name.StartsWith("setup", StringComparison.OrdinalIgnoreCase) || name.StartsWith("install", StringComparison.OrdinalIgnoreCase);
                        }

                        // Safety gate: route ambiguous candidates to review instead of auto-adding
                        if (_reviewService != null && candidate.PlatformKey == "default" && string.IsNullOrEmpty(candidate.Serial))
                        {
                            var reviewItem = new ReviewItem
                            {
                                FilePaths = new List<string> { candidate.Path },
                                DetectedPlatformKey = candidate.PlatformKey,
                                DetectedTitle = candidate.Title,
                                DiskName = Path.GetFileName(candidate.Path),
                                Region = candidate.Region,
                                Serial = candidate.Serial,
                                Reason = ReviewReason.PlatformAmbiguous,
                                ReasonDetail = $"Platform could not be determined for '{candidate.Title}'. File: {candidate.Path}"
                            };
                            _reviewService.Add(reviewItem);
                            Log($"[Scanner] Routed to review (ambiguous platform): '{candidate.Title}' at {candidate.Path}");
                            continue;
                        }

                        if (await ProcessPotentialGame(candidate.Title, existingGames, metadataService, candidate.Path, candidate.PlatformKey, candidate.Serial, exePath, isInstaller, candidate.IsExternal, candidate.Region, candidate.Languages, candidate.Revision))
                        {
                            added++;
                        }
                    }
                    catch (OperationCanceledException) { throw; }
                    catch (Exception ex)
                    {
                        Log($"[Scanner] Error processing candidate '{candidate.Title}' at '{candidate.Path}': {ex.Message}. Continuing.", LogLevel.Warning);
                    }
                }

                if (i + batchSize < candidates.Count) await Task.Delay(delayMs, ct);
            }
            
            OnBatchFinished?.Invoke();
            return added;
        }

        private async Task<bool> ProcessPotentialGame(string gameTitle, List<Game> existingGames, GameMetadataService metadataService, string? localPath = null, string? platformKey = null, string? serial = null, string? executablePath = null, bool isInstaller = false, bool isExternal = false, string? region = null, string? languages = null, string? revision = null)
        {
            Log($"[Scanner-Trace] Processing Candidate: '{localPath}' (Title: {gameTitle})");
            
            var existingByPath = existingGames.FirstOrDefault(g => g.Path == localPath);
            if (existingByPath != null)
            {
                bool needsUpdate = false;

                // Rediscovered on disk: drop the Missing flag so it stops showing as stale.
                if (existingByPath.MissingSince != null)
                {
                    await _gameRepository.ClearMissingAsync(existingByPath.Id);
                    existingByPath.MissingSince = null;
                    if (existingByPath.Status == GameStatus.Missing)
                        existingByPath.Status = GameStatus.Released;
                }

                // Path is authoritative: the file lives where it lives, so the
                // folder dictates the platform. If a rival row already owns
                // this title on the target platform, the current row is a
                // stale duplicate — drop it and let the rival keep the slot.
                if (!string.IsNullOrEmpty(platformKey) && platformKey != "default")
                {
                    var correctPlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(platformKey));
                    if (correctPlatform != null && existingByPath.PlatformId != correctPlatform.Id)
                    {
                        var rival = existingGames.FirstOrDefault(g =>
                            g.Id != existingByPath.Id &&
                            g.Title.Equals(existingByPath.Title, StringComparison.OrdinalIgnoreCase) &&
                            g.PlatformId == correctPlatform.Id);

                        if (rival == null)
                        {
                            Log($"[Scanner] Correcting platform for '{existingByPath.Title}': {existingByPath.PlatformId} → {correctPlatform.Id} ({correctPlatform.Name})");
                            existingByPath.PlatformId = correctPlatform.Id;
                            needsUpdate = true;
                        }
                        else
                        {
                            Log($"[Scanner] Dropping miscategorized '{existingByPath.Title}' (id={existingByPath.Id}, platform={existingByPath.PlatformId}) — rival id={rival.Id} already owns title on platform {correctPlatform.Id}");
                            try
                            {
                                await _gameRepository.DeleteAsync(existingByPath.Id);
                                existingGames.Remove(existingByPath);
                            }
                            catch (Exception ex) { Log($"[Scanner] Dupe drop failed id={existingByPath.Id}: {ex.Message}", LogLevel.Warning); }
                            return false; // nothing more to do with the deleted row
                        }
                    }
                }

                // Backfill: fill in missing region/languages/revision from filename
                if (!string.IsNullOrEmpty(region) && string.IsNullOrEmpty(existingByPath.Region))
                {
                    existingByPath.Region = region;
                    needsUpdate = true;
                }
                if (!string.IsNullOrEmpty(languages) && string.IsNullOrEmpty(existingByPath.Languages))
                {
                    existingByPath.Languages = languages;
                    needsUpdate = true;
                }
                if (!string.IsNullOrEmpty(revision) && string.IsNullOrEmpty(existingByPath.Revision))
                {
                    existingByPath.Revision = revision;
                    needsUpdate = true;
                }

                if (needsUpdate)
                {
                    try { await _gameRepository.UpdateAsync(existingByPath.Id, existingByPath); }
                    catch (Exception ex) { Log($"[Scanner] Backfill update failed for '{existingByPath.Title}': {ex.Message}"); }
                }
                return false;
            }

            // Resolve numeric platform ID for this candidate so we can match by title+platform
            int candidatePlatformId = 0;
            if (!string.IsNullOrEmpty(platformKey))
            {
                var platDef = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                    p => p.MatchesFolderName(platformKey));
                if (platDef != null) candidatePlatformId = platDef.Id;
            }

            // Match by title AND same platform first; fall back to title-only if no platform match
            var existingByTitle = existingGames.FirstOrDefault(g =>
                g.Title.Equals(gameTitle, StringComparison.OrdinalIgnoreCase) &&
                (candidatePlatformId == 0 || g.PlatformId == candidatePlatformId));

            if (existingByTitle == null)
            {
                // Check title-only match but on a DIFFERENT platform — skip update, allow new entry
                var crossPlatformMatch = existingGames.FirstOrDefault(g =>
                    g.Title.Equals(gameTitle, StringComparison.OrdinalIgnoreCase));
                if (crossPlatformMatch != null && candidatePlatformId > 0 && crossPlatformMatch.PlatformId != candidatePlatformId)
                {
                    Log($"[Scanner] '{gameTitle}' exists on platform {crossPlatformMatch.PlatformId} but scanning platform {candidatePlatformId} — creating separate entry");
                    existingByTitle = null; // Force new entry
                }
                else
                {
                    existingByTitle = crossPlatformMatch;
                }
            }

            if (existingByTitle != null && existingByTitle.IgdbId.HasValue && existingByTitle.IgdbId != 0)
            {
                return await UpdateExistingGamePaths(existingByTitle, localPath, executablePath, isExternal, isInstaller, platformKey, region, languages, revision);
            }

            Game? finalGame = await TryFetchMetadata(gameTitle, existingGames, metadataService, localPath, platformKey, serial, executablePath, isExternal);

            // TryFetchMetadata may have matched an existing game by IgdbId and already updated it.
            // In that case finalGame.Id > 0 — do not try to insert again.
            if (finalGame != null && finalGame.Id > 0)
                return true;

            if (finalGame == null)
                finalGame = await CreateOfflineFallback(gameTitle, localPath, executablePath, isExternal, platformKey);

            // Set region, languages, and revision from filename detection
            if (!string.IsNullOrEmpty(region) && string.IsNullOrEmpty(finalGame.Region))
                finalGame.Region = region;
            if (!string.IsNullOrEmpty(languages) && string.IsNullOrEmpty(finalGame.Languages))
                finalGame.Languages = languages;
            if (!string.IsNullOrEmpty(revision) && string.IsNullOrEmpty(finalGame.Revision))
                finalGame.Revision = revision;

            if (existingByTitle != null)
                return await MergeMetadataIntoExisting(existingByTitle, finalGame, platformKey);

            return await PersistNewGame(finalGame, existingGames, localPath, executablePath, isExternal, isInstaller, platformKey, gameTitle);
        }

        private async Task<bool> UpdateExistingGamePaths(Game existing, string? localPath, string? executablePath, bool isExternal, bool isInstaller, string? platformKey = null, string? region = null, string? languages = null, string? revision = null)
        {
            Log($"Updating existing game '{existing.Title}' path to: {localPath}");
            existing.Path = localPath;
            existing.ExecutablePath = executablePath;
            existing.IsExternal = isExternal;
            if (isInstaller) existing.Status = GameStatus.InstallerDetected;

            // Rediscovered on disk: drop the Missing flag.
            if (existing.MissingSince != null)
            {
                existing.MissingSince = null;
                if (existing.Status == GameStatus.Missing) existing.Status = GameStatus.Released;
            }

            // Correct platform if folder-based detection disagrees
            if (!string.IsNullOrEmpty(platformKey) && platformKey != "default")
            {
                var correctPlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(platformKey));
                if (correctPlatform != null && existing.PlatformId != correctPlatform.Id)
                {
                    Log($"[Scanner] Correcting platform for '{existing.Title}': {existing.PlatformId} → {correctPlatform.Id} ({correctPlatform.Name})");
                    existing.PlatformId = correctPlatform.Id;
                }
            }

            // Backfill missing metadata from filename
            if (!string.IsNullOrEmpty(region) && string.IsNullOrEmpty(existing.Region))
                existing.Region = region;
            if (!string.IsNullOrEmpty(languages) && string.IsNullOrEmpty(existing.Languages))
                existing.Languages = languages;
            if (!string.IsNullOrEmpty(revision) && string.IsNullOrEmpty(existing.Revision))
                existing.Revision = revision;

            try
            {
                await _gameRepository.UpdateAsync(existing.Id, existing);
                await SyncGameFilesFromDisk(existing.Id, localPath);
            }
            catch (Exception ex) { Log($"[Scanner] Update failed for '{existing.Title}': {ex.Message}"); }
            return true;
        }

        private const double AutoAcceptThreshold = 0.85;

        private async Task<Game?> TryFetchMetadata(string gameTitle, List<Game> existingGames, GameMetadataService metadataService, string? localPath, string? platformKey, string? serial, string? executablePath, bool isExternal)
        {
            // Resolve internal platform ID for cross-platform checks
            int scanPlatformId = 0;
            if (!string.IsNullOrEmpty(platformKey))
            {
                var scanPlat = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                    p => p.MatchesFolderName(platformKey));
                if (scanPlat != null) scanPlatformId = scanPlat.Id;
            }

            try
            {
                Log($"Searching metadata for: {gameTitle}");

                // Generate search variants (original, suffix-stripped, diacritics-normalized)
                var searchVariants = _titleCleaner.GenerateSearchVariants(gameTitle);
                if (searchVariants.Count == 0) searchVariants.Add(gameTitle);

                Log($"[Scanner] Search variants: {string.Join(" | ", searchVariants)}");

                // Check per-platform metadata source preference
                var preferScreenScraper = scanPlatformId > 0
                    && PlatformService.GetMetadataSource(scanPlatformId)
                        .Equals(PlatformService.MetadataSourceScreenScraper, StringComparison.OrdinalIgnoreCase);

                if (preferScreenScraper)
                {
                    Log($"[Scanner] Platform {scanPlatformId} prefers ScreenScraper — trying ScreenScraper first");
                    var ssResults = await metadataService.SearchScreenScraperAsync(searchVariants.First(), platformKey);
                    if (ssResults.Count > 0)
                    {
                        var best = ssResults.First();
                        best.Path = localPath;
                        best.ExecutablePath = executablePath;
                        best.IsExternal = isExternal;
                        best.MetadataSource = "ScreenScraper";
                        best.NeedsMetadataReview = false;
                        if (best.PlatformId == 0 && scanPlatformId > 0)
                            best.PlatformId = scanPlatformId;
                        Log($"[Scanner] ScreenScraper match: '{best.Title}'");
                        return best;
                    }
                    Log($"[Scanner] ScreenScraper returned no results, falling back to IGDB");
                }

                // Multi-variant search with platform fallback (IGDB)
                var igdbCandidates = await metadataService.SearchWithVariantsAsync(searchVariants, platformKey, null, serial);

                if (igdbCandidates.Count > 0)
                {
                    int? expectedPlatformId = null;
                    if (!string.IsNullOrEmpty(platformKey))
                    {
                        var plat = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                            p => p.MatchesFolderName(platformKey));
                        expectedPlatformId = plat?.IgdbPlatformId;
                    }

                    // Score all candidates and sort by confidence
                    var scored = igdbCandidates
                        .Select(c => new { Game = c, Score = metadataService.ScoreCandidate(c, searchVariants, expectedPlatformId) })
                        .OrderByDescending(x => x.Score)
                        .ToList();

                    var best = scored.First();
                    Log($"[Scanner] Best match: '{best.Game.Name}' (IGDB {best.Game.Id}) confidence={best.Score:F2}");

                    // Check if already in library — but only for the SAME platform
                    var existing = existingGames.FirstOrDefault(g => g.IgdbId == best.Game.Id);
                    if (existing != null)
                    {
                        if (scanPlatformId > 0 && existing.PlatformId != scanPlatformId)
                        {
                            Log($"[Scanner] IGDB {best.Game.Id} exists on platform {existing.PlatformId}, scanning for {scanPlatformId} — skipping update, will create new entry");
                            // Don't update the cross-platform game; fall through to fetch fresh metadata
                        }
                        else
                        {
                            existing.Path = localPath;
                            existing.ExecutablePath = executablePath;
                            existing.IsExternal = isExternal;
                            await _gameRepository.UpdateAsync(existing.Id, existing);
                            return existing;
                        }
                    }

                    // Fetch full metadata for the best candidate
                    var fullMetadata = await metadataService.GetGameMetadataAsync(best.Game.Id, null, platformKey);
                    if (fullMetadata != null)
                    {
                        fullMetadata.MatchConfidence = best.Score;

                        if (best.Score >= AutoAcceptThreshold)
                        {
                            fullMetadata.NeedsMetadataReview = false;
                            Log($"[Scanner] Auto-accepted '{best.Game.Name}' (confidence={best.Score:F2} >= {AutoAcceptThreshold})");
                        }
                        else
                        {
                            fullMetadata.NeedsMetadataReview = true;
                            fullMetadata.MetadataReviewReason = best.Score < 0.40
                                ? "Low confidence"
                                : scored.Count > 1 && scored[1].Score > best.Score - 0.10
                                    ? "Multiple close matches"
                                    : "Below auto-accept threshold";
                            Log($"[Scanner] Needs review: '{best.Game.Name}' (confidence={best.Score:F2}, reason={fullMetadata.MetadataReviewReason})");
                        }

                        return fullMetadata;
                    }
                }

                // Fallback: try legacy single-query search (covers ScreenScraper fallback)
                var legacyResults = await metadataService.SearchGamesAsync(searchVariants.First(), platformKey, null, serial);
                if (legacyResults != null && legacyResults.Any())
                {
                    foreach (var gameData in legacyResults)
                    {
                        if (!gameData.IgdbId.HasValue) continue;

                        var match = existingGames.FirstOrDefault(g => g.IgdbId == gameData.IgdbId);
                        if (match != null)
                        {
                            if (scanPlatformId > 0 && match.PlatformId != scanPlatformId)
                            {
                                Log($"[Scanner] Legacy: IGDB {gameData.IgdbId} exists on platform {match.PlatformId}, scanning for {scanPlatformId} — skipping");
                                continue;
                            }
                            match.Path = localPath;
                            match.ExecutablePath = executablePath;
                            match.IsExternal = isExternal;
                            await _gameRepository.UpdateAsync(match.Id, match);
                            return match;
                        }

                        var fullMetadata = await metadataService.GetGameMetadataAsync(gameData.IgdbId.Value, null, platformKey);
                        if (fullMetadata != null)
                        {
                            if (fullMetadata.Year == 0 && string.IsNullOrEmpty(fullMetadata.Images.CoverUrl))
                            {
                                Log($"[Scanner] Metadata for Id {gameData.IgdbId} is empty (No Year/Cover). Trying next search result...");
                                continue;
                            }
                            fullMetadata.NeedsMetadataReview = true;
                            fullMetadata.MetadataReviewReason = "ScreenScraper fallback";
                            return fullMetadata;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"Error processing metadata for {gameTitle}: {ex.Message}.");
                if (ex.Message.Contains("Forbidden") || ex.Message.Contains("authenticate") || ex.Message.Contains("Unauthorized"))
                {
                    Log("Skipping game addition due to authentication failure. Please check IGDB credentials.");
                    return null;
                }
                Log("Proceeding with offline fallback.");
            }
            return null;
        }

        private async Task<Game> CreateOfflineFallback(string gameTitle, string? localPath, string? executablePath, bool isExternal, string? platformKey)
        {
            Log($"[Scanner] No metadata found for: '{gameTitle}'. Creating offline entry.");
            var offlinePlatformId = await ResolvePlatformIdAsync(platformKey);
            var hitPlatformFallback = false;
            // Defense in depth: if platformKey didn't resolve, try the file path
            if (offlinePlatformId == UnresolvedPlatformIdFallback && (string.IsNullOrEmpty(platformKey) || platformKey == "default"))
            {
                var pathPlatformId = ResolvePlatformFromPath(localPath);
                if (pathPlatformId.HasValue && pathPlatformId.Value > 0)
                {
                    offlinePlatformId = pathPlatformId.Value;
                }
                else
                {
                    hitPlatformFallback = true;
                }
            }

            // Surface unanchored rows (no scraper, no platform) for manual review.
            var reviewReason = hitPlatformFallback
                ? "Offline fallback — no scraper match and no platform folder match"
                : "Offline fallback — no scraper match";

            return new Game
            {
                Title = gameTitle,
                Path = localPath,
                ExecutablePath = executablePath,
                IsExternal = isExternal,
                PlatformId = offlinePlatformId,
                Status = GameStatus.Released,
                Year = 0,
                Overview = "Metadata not found. Added via offline fallback.",
                Images = new GameImages(),
                NeedsMetadataReview = true,
                MetadataReviewReason = reviewReason
            };
        }

        private async Task<bool> MergeMetadataIntoExisting(Game existing, Game freshData, string? platformKey)
        {
            if (existing.MetadataConfirmedByUser)
            {
                Log($"[Scanner] Skip metadata overwrite for '{existing.Title}' (user-confirmed match — backfilling paths only).");
                if (string.IsNullOrEmpty(existing.Path) && !string.IsNullOrEmpty(freshData.Path))
                    existing.Path = freshData.Path;
                if (string.IsNullOrEmpty(existing.ExecutablePath) && !string.IsNullOrEmpty(freshData.ExecutablePath))
                    existing.ExecutablePath = freshData.ExecutablePath;
                existing.IsExternal = freshData.IsExternal;
                if (existing.MissingSince != null)
                {
                    existing.MissingSince = null;
                    if (existing.Status == GameStatus.Missing) existing.Status = GameStatus.Released;
                }
                await _gameRepository.UpdateAsync(existing.Id, existing);
                await SyncGameFilesFromDisk(existing.Id, existing.Path);
                return true;
            }

            existing.Title = freshData.Title;
            existing.Overview = freshData.Overview;
            existing.Year = freshData.Year;
            existing.Images = freshData.Images;
            existing.IgdbId = freshData.IgdbId;
            // Folder-based platform always wins over IGDB metadata
            var folderPlatId = await ResolvePlatformIdAsync(platformKey);
            // Defense in depth: if platformKey didn't resolve, try the file path
            if (folderPlatId == UnresolvedPlatformIdFallback && (string.IsNullOrEmpty(platformKey) || platformKey == "default"))
            {
                var pathPlatformId = ResolvePlatformFromPath(existing.Path ?? freshData.Path);
                if (pathPlatformId.HasValue && pathPlatformId.Value > 0)
                    folderPlatId = pathPlatformId.Value;
            }
            existing.PlatformId = folderPlatId > 0 ? folderPlatId : freshData.PlatformId;
            if (string.IsNullOrEmpty(existing.Path) && !string.IsNullOrEmpty(freshData.Path))
                existing.Path = freshData.Path;
            if (string.IsNullOrEmpty(existing.ExecutablePath) && !string.IsNullOrEmpty(freshData.ExecutablePath))
                existing.ExecutablePath = freshData.ExecutablePath;
            existing.IsExternal = freshData.IsExternal;

            // Rediscovered on disk: drop the Missing flag so retention purge
            // doesn't wipe this row next sweep.
            if (existing.MissingSince != null)
            {
                existing.MissingSince = null;
                if (existing.Status == GameStatus.Missing) existing.Status = GameStatus.Released;
            }

            await _gameRepository.UpdateAsync(existing.Id, existing);
            await SyncGameFilesFromDisk(existing.Id, existing.Path);
            return true;
        }

        private async Task<bool> PersistNewGame(Game finalGame, List<Game> existingGames, string? localPath, string? executablePath, bool isExternal, bool isInstaller, string? platformKey, string gameTitle)
        {
            finalGame.Path = localPath;
            finalGame.ExecutablePath = executablePath;
            finalGame.IsExternal = isExternal;
            
            // Folder-based platform always wins over IGDB metadata
            var folderPlatformId = await ResolvePlatformIdAsync(platformKey);
            // Defense in depth: if platformKey didn't resolve to a real platform, try the file path
            if (folderPlatformId == UnresolvedPlatformIdFallback && (string.IsNullOrEmpty(platformKey) || platformKey == "default"))
            {
                var pathPlatformId = ResolvePlatformFromPath(localPath);
                if (pathPlatformId.HasValue && pathPlatformId.Value > 0)
                    folderPlatformId = pathPlatformId.Value;
            }
            if (folderPlatformId > 0)
                finalGame.PlatformId = folderPlatformId;

            if (isInstaller) finalGame.Status = GameStatus.InstallerDetected;

            // Final dedup guard: IGDB metadata may have changed the title from the cleaned
            // folder name, so two different folders can resolve to the same Title+PlatformId.
            var duplicate = existingGames.FirstOrDefault(g =>
                g.Title.Equals(finalGame.Title, StringComparison.OrdinalIgnoreCase) &&
                g.PlatformId == finalGame.PlatformId);
            if (duplicate != null)
            {
                Log($"[Scanner] Dedup guard: '{finalGame.Title}' (Platform {finalGame.PlatformId}) already exists (ID {duplicate.Id}). Updating path instead of inserting.");
                if (string.IsNullOrEmpty(duplicate.Path) && !string.IsNullOrEmpty(localPath))
                    duplicate.Path = localPath;
                if (string.IsNullOrEmpty(duplicate.ExecutablePath) && !string.IsNullOrEmpty(executablePath))
                    duplicate.ExecutablePath = executablePath;
                try
                {
                    await _gameRepository.UpdateAsync(duplicate.Id, duplicate);
                }
                catch (Exception ex)
                {
                    Log($"[Scanner] Dedup path-merge failed for ID {duplicate.Id}: {ex.Message}");
                }
                return false;
            }

            try 
            {
                var newGame = await _gameRepository.AddAsync(finalGame);
                existingGames.Add(newGame);
                
                Log($"Added new game: {newGame.Title} (Exe: {executablePath ?? "None"}, Ext: {isExternal})");
                _lastGameFound = newGame.Title;
                _gamesAddedCount++;
                OnGameAdded?.Invoke(newGame);
                await SyncGameFilesFromDisk(newGame.Id, localPath);
                return true;
            }
            catch (Exception ex)
            {
                 var innerParam = ex.InnerException != null ? $" Inner: {ex.InnerException.Message}" : "";
                 Log($"Error saving game to DB {gameTitle} (with Metadata: {finalGame.IgdbId.HasValue}): {ex.Message}{innerParam}");
                 return false;
            }
        }
        
        private async Task SyncGameFilesFromDisk(int gameId, string? gamePath)
        {
            if (string.IsNullOrEmpty(gamePath) || gameId <= 0) return;

            try
            {
                var files = new List<GameFile>();
                bool isFile = File.Exists(gamePath) && !Directory.Exists(gamePath);
                bool isDir = Directory.Exists(gamePath);

                if (isFile)
                {
                    var fi = new FileInfo(gamePath);
                    var parentDir = fi.DirectoryName ?? gamePath;
                    var fileType = parentDir.EndsWith("Patches", StringComparison.OrdinalIgnoreCase) || parentDir.EndsWith("Updates", StringComparison.OrdinalIgnoreCase) ? "Patch"
                                 : parentDir.EndsWith("DLC", StringComparison.OrdinalIgnoreCase) ? "DLC"
                                 : "Main";

                    // Pull cue/gdi/m3u track refs and same-stem siblings (cue+bin etc.)
                    // so the game owns the whole set, not just the primary file.
                    var fileSet = FileSetResolver.Resolve(gamePath);
                    var seenStems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in fileSet.AllFiles)
                    {
                        if (!File.Exists(member)) continue;
                        if (!seenStems.Add(Path.GetFullPath(member))) continue;
                        var mi = new FileInfo(member);
                        files.Add(new GameFile
                        {
                            GameId = gameId,
                            RelativePath = mi.Name,
                            Size = mi.Length,
                            DateAdded = DateTime.UtcNow,
                            FileType = fileType
                        });
                    }
                }
                else if (isDir)
                {
                    var rootDir = new DirectoryInfo(gamePath);
                    var gameFolderName = rootDir.Name;
                    foreach (var fi in rootDir.EnumerateFiles("*", SearchOption.AllDirectories).OrderBy(f => f.FullName, StringComparer.Ordinal))
                    {
                        if (fi.Name.StartsWith(".")) continue;
                        var relativePath = Path.GetRelativePath(gamePath, fi.FullName).Replace('\\', '/');

                        // Classify file type from path context
                        string fileType = "Main";
                        string? version = null;
                        string? contentName = null;
                        string? titleId = null;
                        string? serial = null;

                        // Check explicit subfolder prefixes first
                        if (relativePath.StartsWith("Patches/", StringComparison.OrdinalIgnoreCase) ||
                            relativePath.StartsWith("Updates/", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "Patch";
                        }
                        else if (relativePath.StartsWith("DLC/", StringComparison.OrdinalIgnoreCase) ||
                                 relativePath.StartsWith("DLCs/", StringComparison.OrdinalIgnoreCase))
                        {
                            fileType = "DLC";
                        }
                        else
                        {
                            // Check if file is inside a subfolder with DLC/Update keywords
                            var parentRelDir = Path.GetDirectoryName(relativePath)?.Replace('\\', '/');
                            if (!string.IsNullOrEmpty(parentRelDir))
                            {
                                var info = _titleCleaner.ClassifySupplementaryContent(fi.Name, gameFolderName);
                                var folderInfo = _titleCleaner.ClassifySupplementaryContent(parentRelDir);
                                if (folderInfo.FileType != "Main")
                                {
                                    fileType = folderInfo.FileType;
                                    contentName = parentRelDir;
                                }
                                else if (info.FileType != "Main")
                                {
                                    fileType = info.FileType;
                                }
                                version = info.Version ?? folderInfo.Version;
                                serial = info.Serial ?? folderInfo.Serial;
                                titleId = info.TitleId ?? folderInfo.TitleId;
                                if (fileType != "Main") contentName ??= info.ContentName;
                            }
                            else
                            {
                                // Root-level file: check if it's a generic update
                                var info = _titleCleaner.ClassifySupplementaryContent(fi.Name, gameFolderName);
                                if (info.FileType != "Main")
                                {
                                    fileType = info.FileType;
                                    version = info.Version;
                                    serial = info.Serial;
                                    titleId = info.TitleId;
                                    contentName = info.ContentName;
                                }
                            }
                        }

                        files.Add(new GameFile
                        {
                            GameId = gameId,
                            RelativePath = relativePath,
                            Size = fi.Length,
                            DateAdded = DateTime.UtcNow,
                            FileType = fileType,
                            Version = version,
                            ContentName = contentName,
                            TitleId = titleId,
                            Serial = serial
                        });
                    }
                }

                if (files.Count > 0)
                {
                    await _gameRepository.SyncGameFilesAsync(gameId, files);
                    Log($"Synced {files.Count} file(s) for game ID {gameId}");
                }
            }
            catch (Exception ex)
            {
                Log($"Error syncing game files for ID {gameId}: {ex.Message}", LogLevel.Warning);
            }
        }

        private static readonly NLog.Logger _nlog = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMedia);

        private void Log(string message, LogLevel level = LogLevel.Info)
        {
            _debugLog?.Log(level, "Scanner", message);

            var nlogLevel = level switch
            {
                LogLevel.Debug => NLog.LogLevel.Debug,
                LogLevel.Warning => NLog.LogLevel.Warn,
                LogLevel.Error => NLog.LogLevel.Error,
                _ => NLog.LogLevel.Info
            };
            _nlog.Log(nlogLevel, message);
        }

        private void UpdateScanProgress(string? directory = null, string? file = null, int? filesScanned = null)
        {
            if (directory != null) _currentScanDirectory = directory;
            if (file != null) _currentScanFile = file;
            if (filesScanned.HasValue) _filesScannedCount = filesScanned.Value;
            
            _debugLog?.UpdateScanProgress(
                currentDirectory: CurrentScanDirectory,
                currentFile: CurrentScanFile,
                filesScanned: FilesScannedCount,
                gamesFound: GamesAddedCount,
                lastGameFound: LastGameFound
            );
        }

        private bool IsValidFile(string filePath, string[]? validExtensions)
        {
            var fileName = Path.GetFileName(filePath);
            if (fileName.StartsWith("._")) return false; // Skip macOS metadata
            
            // Ignore common tool directories for PS4 exploits to prevent false positives
            if (filePath.Contains("PPPwnGo", StringComparison.OrdinalIgnoreCase) || 
                filePath.Contains("GoldHEN", StringComparison.OrdinalIgnoreCase)) return false;

            // Explicitly Ignore known PS4 tools that are often in the folder
            if (fileName.Contains("PS4.Remote.PKG.Sender", StringComparison.OrdinalIgnoreCase) ||
                fileName.Contains("npcap", StringComparison.OrdinalIgnoreCase)) return false;

            var ext = Path.GetExtension(filePath);

            // TAREA 2: Linux support for extensionless binaries
            bool isLinux = System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Linux);
            if (isLinux && string.IsNullOrEmpty(ext))
            {
                // On Linux, we allow files without extensions as valid candidates
                // provided they aren't hidden files AND they pass magic byte check.
                if (fileName.StartsWith(".")) return false;
                
                return IsExecutableBinary(filePath);
            }

            if (string.IsNullOrEmpty(ext)) return false;

            // Platform whitelist beats the global blacklist — .bin is garbage
            // on PC but a real ROM on PS1/PS2/Saturn/MegaDrive.
            if (validExtensions != null)
                return validExtensions.Contains(ext, StringComparer.OrdinalIgnoreCase);
            
            // Default mode (no platform extensions): reject blacklisted extensions
            if (_globalBlacklist.Contains(ext)) return false;

            return true;
        }


        private async Task<int> ResolvePlatformIdAsync(string? platformKey)
        {
            // 1. Try to match by FolderName from PlatformDefinitions first
            if (!string.IsNullOrEmpty(platformKey))
            {
                var platformByFolder = PlatformDefinitions.AllPlatforms
                    .FirstOrDefault(p => p.MatchesFolderName(platformKey));
                
                if (platformByFolder != null)
                {
                    Log($"[Platform] Resolved '{platformKey}' -> '{platformByFolder.Name}' (ID: {platformByFolder.Id})");
                    return platformByFolder.Id;
                }
            }

            // 2. Fallback: Map internal scanner key to PlatformDefinitions slug
            //    Internal keys from TitleCleanerService.GetPlatformFromExtension use
            //    non-standard names that don't match MatchesFolderName. Map them here.
            string resolvedSlug = platformKey?.ToLowerInvariant() switch
            {
                "pc_windows" => "pc",
                "nintendo_switch" => "switch",
                "xbox_series" => "xboxseriesx",
                "nintendo_64" => "n64",
                "gameboy_advance" => "gba",
                "gameboy_color" => "gbc",
                "gameboy" => "gb",
                "sega_genesis" => "megadrive",
                "master_system" => "mastersystem",
                "game_gear" => "gamegear",
                "pc_engine" => "pcengine",
                "retro_emulation" => null,
                "default" => null,
                _ => null
            };

            if (!string.IsNullOrEmpty(resolvedSlug))
            {
                var resolved = PlatformDefinitions.AllPlatforms
                    .FirstOrDefault(p => p.Slug.Equals(resolvedSlug, StringComparison.OrdinalIgnoreCase)
                                      || p.MatchesFolderName(resolvedSlug));
                if (resolved != null)
                {
                    Log($"[Platform] Resolved internal key '{platformKey}' -> '{resolved.Name}' (ID: {resolved.Id})");
                    return resolved.Id;
                }
            }

            // 3. Try dynamic lookup from DB by slug
            string dbSlug = resolvedSlug ?? platformKey ?? "pc";
            try 
            {
                var dbId = await _gameRepository.GetPlatformIdBySlugAsync(dbSlug);
                if (dbId.HasValue) 
                {
                    return dbId.Value;
                }
            }
            catch (Exception ex)
            {
                Log($"[Platform] Error looking up slug '{dbSlug}': {ex.Message}");
            }

            // 4. Ultimate fallback: file the row under PC (Windows) so the scan keeps running,
            //    but log loudly so it shows up in review.
            Log($"[Platform] WARNING: Could not resolve platform for key '{platformKey}'. Falling back to PC (Windows).");
            return UnresolvedPlatformIdFallback;
        }

        /// <summary>
        /// Walk parent directories of a file path to find a platform folder match.
        /// Returns the platform ID or null if no match found.
        /// </summary>
        private static int? ResolvePlatformFromPath(string? filePath)
        {
            if (string.IsNullOrEmpty(filePath)) return null;
            try
            {
                var dir = Directory.Exists(filePath) ? new DirectoryInfo(filePath) : new DirectoryInfo(Path.GetDirectoryName(filePath)!);
                while (dir != null)
                {
                    var match = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(dir.Name));
                    if (match != null) return match.Id;
                    dir = dir.Parent;
                }
            }
            catch { }
            return null;
        }
        private bool IsExecutableBinary(string filePath)
        {
            try
            {
                using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    if (fs.Length < 4) return false;

                    byte[] buffer = new byte[4];
                    fs.Read(buffer, 0, 4);

                    // Check for ELF Header (0x7F, 'E', 'L', 'F')
                    if (buffer[0] == 0x7F && buffer[1] == 0x45 && buffer[2] == 0x4C && buffer[3] == 0x46)
                        return true;

                    // Check for Shebang '#!' (0x23, 0x21) - Common for shell scripts wrapper
                    if (buffer[0] == 0x23 && buffer[1] == 0x21)
                        return true;
                }
            }
            catch 
            {
                // If we can't read it (permissions, etc.), assume false to remain safe.
                return false;
            }

            return false;
        }

        private void AutoEnablePlatformsFromFolders(string libraryPath)
        {
            try
            {
                if (!Directory.Exists(libraryPath))
                {
                    return;
                }

                var directories = Directory.GetDirectories(libraryPath);
                var folderNames = directories.Select(d => Path.GetFileName(d)).ToList();

                if (folderNames.Count == 0)
                {
                    return;
                }

                Log($"[AutoPlatform] Checking {folderNames.Count} folders for platform auto-enable...");

                // Get all platforms from static definitions
                var allPlatforms = PlatformDefinitions.AllPlatforms;

                foreach (var folderName in folderNames)
                {
                    PlatformService.EnablePlatformByFolderName(folderName, allPlatforms);
                }

                Log($"[AutoPlatform] Platform auto-enable check completed.");
            }
            catch (Exception ex)
            {
                Log($"[AutoPlatform] Error during auto-enable: {ex.Message}");
            }
        }

        private async Task ScanSupplementaryFoldersAsync(string platformFolderPath, string platformKey, PlatformRule rule, List<Game> existingGames, System.Threading.CancellationToken ct)
        {
            try
            {
                var directories = Directory.GetDirectories(platformFolderPath);
                foreach (var dir in directories)
                {
                    ct.ThrowIfCancellationRequested();
                    var folderName = Path.GetFileName(dir);
                    if (!_supplementaryFolderNames.Contains(folderName)) continue;

                    Log($"[Supplementary] Scanning supplementary folder: {dir}");

                    // Determine default file type from folder name
                    var lowerFolder = folderName.ToLowerInvariant();
                    string defaultType = lowerFolder.Contains("dlc") && !lowerFolder.Contains("update") && !lowerFolder.Contains("patch")
                        ? "DLC"
                        : lowerFolder.Contains("update") || lowerFolder.Contains("patch")
                            ? "Patch"
                            : "Patch"; // Mixed folders like "Updates+DLCs" — classify per-file

                    // Collect all files (flat + subfolders up to 3 levels)
                    var allFiles = new List<string>();
                    CollectSupplementaryFiles(new DirectoryInfo(dir), allFiles, rule.Extensions, 0, 3);

                    // Also collect immediate subdirectories as potential DLC folders (e.g. "Call of Duty Ghosts - ALL DLC [BLES01945]")
                    var subDirs = new List<string>();
                    try { subDirs.AddRange(Directory.GetDirectories(dir)); } catch { }

                    int linked = 0;

                    // Process individual files
                    foreach (var filePath in allFiles)
                    {
                        ct.ThrowIfCancellationRequested();
                        var fileName = Path.GetFileName(filePath);
                        var info = _titleCleaner.ClassifySupplementaryContent(fileName);

                        // Override type if classification returned Main but folder context says otherwise
                        if (info.FileType == "Main" && defaultType != "Main")
                        {
                            // Mixed folder: use folder context default
                            info.FileType = defaultType;
                        }

                        // Try to find the parent game
                        var parentGame = FindParentGame(info, existingGames, platformKey);
                        if (parentGame != null)
                        {
                            var fi = new FileInfo(filePath);
                            var relativePath = Path.GetRelativePath(platformFolderPath, filePath).Replace('\\', '/');
                            var gameFile = new GameFile
                            {
                                GameId = parentGame.Id,
                                RelativePath = relativePath,
                                Size = fi.Length,
                                DateAdded = DateTime.UtcNow,
                                FileType = info.FileType,
                                Version = info.Version,
                                ContentName = info.ContentName,
                                TitleId = info.TitleId,
                                Serial = info.Serial
                            };

                            try
                            {
                                await _gameRepository.SyncGameFilesAsync(parentGame.Id, new List<GameFile> { gameFile });
                                linked++;
                                Log($"[Supplementary] Linked '{fileName}' -> '{parentGame.Title}' as {info.FileType}");
                            }
                            catch (Exception ex)
                            {
                                Log($"[Supplementary] Error linking '{fileName}': {ex.Message}", LogLevel.Warning);
                            }
                        }
                        else
                        {
                            Log($"[Supplementary] No parent game found for '{fileName}' (Serial: {info.Serial}, TitleId: {info.TitleId}, Title: {info.CleanParentTitle})");

                            if (_reviewService != null)
                            {
                                _reviewService.Add(new ReviewItem
                                {
                                    FilePaths = new List<string> { filePath },
                                    DetectedPlatformKey = platformKey,
                                    DetectedTitle = info.CleanParentTitle ?? fileName,
                                    DiskName = fileName,
                                    Serial = info.Serial,
                                    Reason = ReviewReason.PlatformAmbiguous,
                                    ReasonDetail = $"Supplementary content could not be matched to any game. Type: {info.FileType}, File: {filePath}"
                                });
                            }
                        }
                    }

                    // Process subdirectories as DLC bundles (e.g. "Game Name - ALL DLC [SERIAL]")
                    foreach (var subDir in subDirs)
                    {
                        ct.ThrowIfCancellationRequested();
                        var subFolderName = Path.GetFileName(subDir);
                        var info = _titleCleaner.ClassifySupplementaryContent(subFolderName);
                        if (info.FileType == "Main") info.FileType = "DLC"; // Subfolder in supplementary = DLC

                        var parentGame = FindParentGame(info, existingGames, platformKey);
                        if (parentGame != null)
                        {
                            // Add all files in this DLC subfolder to the parent game
                            var dlcFiles = new List<GameFile>();
                            try
                            {
                                foreach (var fi in new DirectoryInfo(subDir).EnumerateFiles("*", SearchOption.AllDirectories))
                                {
                                    if (fi.Name.StartsWith(".")) continue;
                                    var relativePath = Path.GetRelativePath(platformFolderPath, fi.FullName).Replace('\\', '/');
                                    dlcFiles.Add(new GameFile
                                    {
                                        GameId = parentGame.Id,
                                        RelativePath = relativePath,
                                        Size = fi.Length,
                                        DateAdded = DateTime.UtcNow,
                                        FileType = info.FileType,
                                        Version = info.Version,
                                        ContentName = subFolderName,
                                        TitleId = info.TitleId,
                                        Serial = info.Serial
                                    });
                                }
                            }
                            catch (Exception ex)
                            {
                                Log($"[Supplementary] Error enumerating DLC folder '{subFolderName}': {ex.Message}", LogLevel.Warning);
                            }

                            if (dlcFiles.Count > 0)
                            {
                                try
                                {
                                    await _gameRepository.SyncGameFilesAsync(parentGame.Id, dlcFiles);
                                    linked += dlcFiles.Count;
                                    Log($"[Supplementary] Linked DLC folder '{subFolderName}' ({dlcFiles.Count} files) -> '{parentGame.Title}'");
                                }
                                catch (Exception ex)
                                {
                                    Log($"[Supplementary] Error syncing DLC folder: {ex.Message}", LogLevel.Warning);
                                }
                            }
                        }
                        else
                        {
                            Log($"[Supplementary] No parent game found for DLC folder '{subFolderName}'");
                        }
                    }

                    Log($"[Supplementary] Finished '{folderName}': linked {linked} file(s) to existing games");
                }
            }
            catch (Exception ex)
            {
                Log($"[Supplementary] Error scanning supplementary folders in '{platformFolderPath}': {ex.Message}", LogLevel.Warning);
            }
        }

        private void CollectSupplementaryFiles(DirectoryInfo dir, List<string> results, string[]? extensions, int depth, int maxDepth)
        {
            if (depth > maxDepth || !dir.Exists) return;
            if (dir.Name.StartsWith(".") || _folderBlacklist.Contains(dir.Name)) return;

            try
            {
                foreach (var file in dir.EnumerateFiles())
                {
                    if (file.Name.StartsWith(".")) continue;
                    if (extensions != null && !extensions.Contains(file.Extension, StringComparer.OrdinalIgnoreCase)) continue;
                    if (_globalBlacklist.Contains(file.Extension)) continue;
                    results.Add(file.FullName);
                }

                // Only recurse into subdirectories at depth 0 (the supplementary folder itself)
                // Subdirectories at depth 0 are DLC bundles handled separately
                if (depth > 0)
                {
                    foreach (var sub in dir.EnumerateDirectories())
                    {
                        CollectSupplementaryFiles(sub, results, extensions, depth + 1, maxDepth);
                    }
                }
            }
            catch { }
        }

        private Game? FindParentGame(TitleCleanerService.SupplementaryContentInfo info, List<Game> existingGames, string platformKey)
        {
            // Resolve platform ID for this scan context
            int platformId = 0;
            var platDef = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(platformKey));
            if (platDef != null) platformId = platDef.Id;

            var platformGames = platformId > 0
                ? existingGames.Where(g => g.PlatformId == platformId).ToList()
                : existingGames;

            // Strategy 1: Serial match (PS3/PS4/PS5 — highest confidence)
            if (!string.IsNullOrEmpty(info.Serial))
            {
                // Check if any existing game has this serial in its path or GameFiles
                foreach (var game in platformGames)
                {
                    if (!string.IsNullOrEmpty(game.Path) &&
                        game.Path.Contains(info.Serial, StringComparison.OrdinalIgnoreCase))
                    {
                        return game;
                    }
                }

                // Also try serial extracted from the game's own title cleaning
                foreach (var game in platformGames)
                {
                    var (_, gameSerial) = _titleCleaner.CleanGameTitle(Path.GetFileName(game.Path ?? game.Title));
                    if (!string.IsNullOrEmpty(gameSerial) &&
                        gameSerial.Equals(info.Serial, StringComparison.OrdinalIgnoreCase))
                    {
                        return game;
                    }
                }
            }

            // Strategy 2: Switch TitleID match (derive base TitleID)
            if (!string.IsNullOrEmpty(info.TitleId))
            {
                var baseTitleId = TitleCleanerService.DeriveBaseTitleId(info.TitleId);
                if (!string.IsNullOrEmpty(baseTitleId))
                {
                    foreach (var game in platformGames)
                    {
                        if (!string.IsNullOrEmpty(game.Path) &&
                            game.Path.Contains(baseTitleId, StringComparison.OrdinalIgnoreCase))
                        {
                            return game;
                        }

                        // Check existing GameFiles for TitleID
                        if (game.GameFiles != null)
                        {
                            foreach (var gf in game.GameFiles)
                            {
                                if (!string.IsNullOrEmpty(gf.TitleId) &&
                                    gf.TitleId.Equals(baseTitleId, StringComparison.OrdinalIgnoreCase))
                                {
                                    return game;
                                }
                                // Also check the file path for the base TitleID
                                if (gf.RelativePath.Contains(baseTitleId, StringComparison.OrdinalIgnoreCase))
                                {
                                    return game;
                                }
                            }
                        }
                    }
                }
            }

            // Strategy 3: Fuzzy title match
            if (!string.IsNullOrEmpty(info.CleanParentTitle) && info.CleanParentTitle.Length >= 3)
            {
                Game? bestMatch = null;
                double bestScore = 0;

                foreach (var game in platformGames)
                {
                    var score = TitleCleanerService.ComputeSimilarity(info.CleanParentTitle, game.Title);
                    if (score > bestScore)
                    {
                        bestScore = score;
                        bestMatch = game;
                    }
                }

                if (bestMatch != null && bestScore >= 0.75)
                {
                    return bestMatch;
                }
            }

            return null;
        }

        private async Task CleanupAndResyncPlatformAsync(string platformFolderPath, string platformKey, List<Game> existingGames, System.Threading.CancellationToken ct)
        {
            int platformId = 0;
            var platDef = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(platformKey));
            if (platDef != null) platformId = platDef.Id;
            if (platformId == 0) return;

            var normalizedFolder = Path.GetFullPath(platformFolderPath).TrimEnd(Path.DirectorySeparatorChar);

            var platformGames = existingGames.Where(g =>
                g.PlatformId == platformId &&
                !string.IsNullOrEmpty(g.Path)
            ).ToList();

            int flagged = 0;
            int cleared = 0;
            int resynced = 0;
            var missingIds = new List<int>();
            var now = DateTime.UtcNow;

            foreach (var game in platformGames)
            {
                ct.ThrowIfCancellationRequested();

                // Only process games whose paths are under this platform folder
                string gamePath;
                try { gamePath = Path.GetFullPath(game.Path!); }
                catch { continue; }

                if (!gamePath.StartsWith(normalizedFolder, StringComparison.OrdinalIgnoreCase))
                    continue;

                bool pathExists = Directory.Exists(game.Path) || File.Exists(game.Path);

                if (!pathExists)
                {
                    if (game.MissingSince == null)
                    {
                        missingIds.Add(game.Id);
                        flagged++;
                    }
                    // Already flagged earlier — retention sweep at scan end handles final purge.
                }
                else
                {
                    if (game.MissingSince != null)
                    {
                        await _gameRepository.ClearMissingAsync(game.Id);
                        cleared++;
                    }
                    await SyncGameFilesFromDisk(game.Id, game.Path);
                    resynced++;
                }
            }

            if (missingIds.Count > 0)
                await _gameRepository.FlagMissingAsync(missingIds, now);

            if (flagged > 0 || cleared > 0 || resynced > 0)
                Log($"[Cleanup] Platform '{platformKey}': flagged {flagged} missing, cleared {cleared}, resynced {resynced}");
        }

        // Walks every DB row, compares stored PlatformId against what the path
        // says, and fixes mismatches. Returns (healed, dupesDropped, dropped-set).
        // Split out from the full orphan sweep so it can be called ad-hoc
        // without touching the Missing-flag lifecycle.
        public async Task<(int healed, int dupesDropped, int mergedDuplicates)> HealWrongPlatformsAsync(System.Threading.CancellationToken ct = default)
        {
            var all = await _gameRepository.GetAllLightAsync();
            int healed = 0;
            int dupesDropped = 0;

            var live = new Dictionary<(string, int), HashSet<int>>();
            foreach (var g in all)
            {
                var key = (g.Title?.ToLowerInvariant() ?? string.Empty, g.PlatformId);
                if (!live.TryGetValue(key, out var set)) { set = new HashSet<int>(); live[key] = set; }
                set.Add(g.Id);
            }
            var dropped = new HashSet<int>();

            foreach (var g in all)
            {
                ct.ThrowIfCancellationRequested();
                if (dropped.Contains(g.Id)) continue;
                if (string.IsNullOrEmpty(g.Path)) continue;

                bool pathExists;
                try { pathExists = Directory.Exists(g.Path) || File.Exists(g.Path); }
                catch { continue; }
                if (!pathExists) continue;

                var pathPlatform = PlatformDefinitions.ResolvePlatformFromPath(g.Path);
                if (pathPlatform == null || pathPlatform.Id == g.PlatformId) continue;

                var titleKey = g.Title?.ToLowerInvariant() ?? string.Empty;
                var hasLiveCollision = live.TryGetValue((titleKey, pathPlatform.Id), out var set)
                    && set.Any(id => id != g.Id);

                if (!hasLiveCollision)
                {
                    Log($"[Heal] Correcting platform for '{g.Title}' (id={g.Id}): {g.PlatformId} → {pathPlatform.Id} ({pathPlatform.Name}) based on path '{g.Path}'");
                    var fresh = await _gameRepository.GetByIdAsync(g.Id);
                    if (fresh != null)
                    {
                        fresh.PlatformId = pathPlatform.Id;
                        try
                        {
                            await _gameRepository.UpdateAsync(fresh.Id, fresh);
                            healed++;
                            if (live.TryGetValue((titleKey, g.PlatformId), out var oldSet)) oldSet.Remove(g.Id);
                            if (!live.TryGetValue((titleKey, pathPlatform.Id), out var newSet))
                            { newSet = new HashSet<int>(); live[(titleKey, pathPlatform.Id)] = newSet; }
                            newSet.Add(g.Id);
                        }
                        catch (Exception ex) { Log($"[Heal] Update failed id={g.Id}: {ex.Message}", LogLevel.Warning); }
                    }
                }
                else
                {
                    Log($"[Heal] Dropping duplicate '{g.Title}' (id={g.Id}, platform={g.PlatformId}) — correct entry on platform {pathPlatform.Id} already exists");
                    try
                    {
                        await _gameRepository.DeleteAsync(g.Id);
                        dupesDropped++;
                        dropped.Add(g.Id);
                        if (live.TryGetValue((titleKey, g.PlatformId), out var oldSet)) oldSet.Remove(g.Id);
                    }
                    catch (Exception ex) { Log($"[Heal] Delete failed id={g.Id}: {ex.Message}", LogLevel.Warning); }
                }
            }

            if (healed > 0 || dupesDropped > 0)
                Log($"[Heal] Platform heal pass: healed {healed}, duplicates dropped {dupesDropped}");
            // Same-platform duplicates (cue/bin pairs etc.) skip the loop
            // above. Catch them via path-stem / title / igdb clustering.
            int mergedDuplicates = 0;
            if (_mergeService != null)
            {
                try
                {
                    var mergeResult = await _mergeService.MergeAsync(ct);
                    mergedDuplicates = mergeResult.RowsMerged;
                    if (mergeResult.RowsMerged > 0)
                        Log($"[Heal] Merged {mergeResult.RowsMerged} duplicate game row(s) across {mergeResult.ClustersFound} cluster(s).");
                }
                catch (OperationCanceledException) { throw; }
                catch (System.Exception ex) { Log($"[Heal] Duplicate merge skipped: {ex.Message}", LogLevel.Warning); }
            }

            return (healed, dupesDropped, mergedDuplicates);
        }

        private async Task GlobalOrphanSweepAsync(Configuration.MediaSettings settings, System.Threading.CancellationToken ct)
        {
            try
            {
                // Platform heal runs first so the Missing pass sees corrected rows.
                var (healed, dupesDropped, _) = await HealWrongPlatformsAsync(ct);

                var all = await _gameRepository.GetAllLightAsync();
                var now = DateTime.UtcNow;
                var toFlag = new List<int>();
                int cleared = 0;

                foreach (var g in all)
                {
                    ct.ThrowIfCancellationRequested();
                    if (string.IsNullOrEmpty(g.Path)) continue;

                    bool pathExists;
                    try { pathExists = Directory.Exists(g.Path) || File.Exists(g.Path); }
                    catch { continue; }

                    if (!pathExists && g.MissingSince == null)
                    {
                        toFlag.Add(g.Id);
                    }
                    else if (pathExists && g.MissingSince != null)
                    {
                        await _gameRepository.ClearMissingAsync(g.Id);
                        cleared++;
                    }
                }

                int flagged = 0;
                if (toFlag.Count > 0)
                    flagged = await _gameRepository.FlagMissingAsync(toFlag, now);

                int purged = 0;
                if (settings.MissingRetentionDays > 0)
                {
                    var threshold = now.AddDays(-settings.MissingRetentionDays);
                    purged = await _gameRepository.DeleteMissingOlderThanAsync(threshold);
                }

                if (flagged > 0 || cleared > 0 || purged > 0 || healed > 0 || dupesDropped > 0)
                    Log($"[OrphanSweep] flagged {flagged}, cleared {cleared}, purged {purged}, healed {healed}, duplicates dropped {dupesDropped} (retention={settings.MissingRetentionDays}d)");
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                Log($"[OrphanSweep] skipped: {ex.Message}", LogLevel.Warning);
            }
        }

        private List<(string FolderPath, Platform Platform)> DetectPlatformSubfolders(string libraryPath)
        {
            var result = new List<(string, Platform)>();
            
            try
            {
                if (!Directory.Exists(libraryPath))
                {
                    return result;
                }

                var directories = Directory.GetDirectories(libraryPath);
                var allPlatforms = PlatformDefinitions.AllPlatforms;

                foreach (var dir in directories)
                {
                    var folderName = Path.GetFileName(dir);
                    
                    // Check if folder name matches a platform FolderName or Slug
                    var matchedPlatform = allPlatforms.FirstOrDefault(p => 
                        p.MatchesFolderName(folderName));
                    
                    if (matchedPlatform != null)
                    {
                        result.Add((dir, matchedPlatform));
                    }
                }
            }
            catch (Exception ex)
            {
                Log($"[AutoPlatform] Error detecting platform subfolders: {ex.Message}");
            }

            return result;
        }
    }
}
