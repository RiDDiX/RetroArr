using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace RetroArr.Core.Games
{
    public class TitleCleanerService
    {
        private static readonly HashSet<string> _noiseWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "setup", "install", "installer", "gog", "repack", "fitgirl", "dodi", "cracked", 
            "unpacked", "steamrip", "portable", "multi10", "multi5", "multi2", "v1", "v2",
            "xatab", "codex", "skidrow", "reloaded", "razor1911", "plaza", "cpy", "dlpsgame",
            "nsw2u", "egold", "quacked", "venom", "inc", "rpgonly", "gamesfull", "bitsearch",
            "www", "app", "com", "net", "org", "iso", "bin", "decepticon", "empress", 
            "tenoke", "rune", "goldberg", "ali213", "p2p", "fairlight",
            "xyz", "dot", "v0", "v196608", "v65536", "v131072", "dlc", "update", "upd", "collection", "anniversary", "edition",
            "us", "eu", "es", "uk", "asia", "cn", "ru", "gb", "mb", "kb", "romslab", "madloader", "usa", "eur", "jp", "region",
            "eng", "english", "spa", "spanish", "fra", "french", "ger", "german", "ita", "italian", "kor", "korean", "chi", "chinese", "tw", "hk",
            "rpgarchive", "gamesmega", "nxdump", "nx", "switch", "game",
            "opoisso893", "cyb1k", "pppwn", "pppwngo", "goldhen", "ps3", "ps4", "ps5", "psp", "psvita", "vita", "playstation", "sony",
            "definitive", "remastered", "remake",
            "nsp", "xci", "nsz", "xcz", "vpk", "pkg", "iso", "nla", "zip", "rar", "7z"
        };

        private static readonly HashSet<string> _regionCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "US", "EU", "JP", "UK"
        };

        // folder is the game, files inside belong to it
        private static readonly HashSet<string> _containerExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            ".ps3", ".ps3dir", ".psn", ".ps4", ".psvita",
            ".xbox360", ".daphne", ".gog"
        };

        public static bool IsContainerExtension(string? ext) =>
            !string.IsNullOrEmpty(ext) && _containerExtensions.Contains(ext);

        // Region tokens found in filenames — ordered by specificity (longest first)
        private static readonly (string Token, string Region)[] _regionTokens = new[]
        {
            // Full names
            ("USA", "USA"), ("Europe", "Europe"), ("Japan", "Japan"), ("Germany", "Germany"),
            ("France", "France"), ("Spain", "Spain"), ("Italy", "Italy"), ("Brazil", "Brazil"),
            ("Australia", "Australia"), ("Korea", "Korea"), ("China", "China"), ("Taiwan", "Taiwan"),
            ("Sweden", "Sweden"), ("Netherlands", "Netherlands"), ("Denmark", "Denmark"),
            ("Canada", "Canada"), ("Greece", "Greece"), ("Hong Kong", "Hong Kong"),
            ("Russia", "Russia"), ("Norway", "Norway"), ("Finland", "Finland"), ("Poland", "Poland"),
            ("Portugal", "Portugal"), ("India", "India"),
            ("World", "World"), ("Asia", "Asia"),
            // TV system codes (Dreamcast, older consoles)
            ("PAL", "Europe"), ("NTSC", "USA"),
            // Language adjectives
            ("German", "Germany"), ("French", "France"), ("Spanish", "Spain"), ("Italian", "Italy"),
            ("English", "USA"), ("Korean", "Korea"), ("Chinese", "China"),
            // 3-letter codes
            ("EUR", "Europe"),
            // 2-letter codes
            ("EU", "Europe"), ("US", "USA"), ("JP", "Japan"), ("UK", "UK"),
            ("DE", "Germany"), ("FR", "France"), ("ES", "Spain"), ("IT", "Italy"),
            ("BR", "Brazil"), ("AU", "Australia"), ("KR", "Korea"), ("CN", "China"),
            ("TW", "Taiwan"), ("HK", "Hong Kong"), ("RU", "Russia"), ("NL", "Netherlands"),
            ("PT", "Portugal"), ("SE", "Sweden"), ("NO", "Norway"), ("DK", "Denmark"),
            ("FI", "Finland"), ("PL", "Poland"), ("CZ", "Czech Republic"), ("GR", "Greece"),
            ("CA", "Canada"), ("GE", "Germany"), ("IN", "India"), ("GB", "UK"),
            // Single-letter codes (rare, No-Intro style)
            ("U", "USA"), ("E", "Europe")
        };

        // Known 2–3 letter language codes found in ROM filenames
        private static readonly HashSet<string> _knownLanguageCodes = new(StringComparer.OrdinalIgnoreCase)
        {
            "En", "Fr", "De", "Es", "It", "Nl", "Sv", "No", "Da", "Fi", "Pt", "Ja", "Zh",
            "Ko", "Ru", "Pl", "Cs", "Hu", "Ca", "El", "Tr", "Ge", "Ro", "Hr", "Sk", "Bg",
            "Uk", "Lt", "Lv", "Et", "Sl"
        };

        // Language code → display name for frontend
        private static readonly Dictionary<string, string> _languageCodeToName = new(StringComparer.OrdinalIgnoreCase)
        {
            ["En"] = "English", ["Fr"] = "French", ["De"] = "German", ["Es"] = "Spanish",
            ["It"] = "Italian", ["Nl"] = "Dutch", ["Sv"] = "Swedish", ["No"] = "Norwegian",
            ["Da"] = "Danish", ["Fi"] = "Finnish", ["Pt"] = "Portuguese", ["Ja"] = "Japanese",
            ["Zh"] = "Chinese", ["Ko"] = "Korean", ["Ru"] = "Russian", ["Pl"] = "Polish",
            ["Cs"] = "Czech", ["Hu"] = "Hungarian", ["Ca"] = "Catalan", ["El"] = "Greek",
            ["Tr"] = "Turkish", ["Ge"] = "German", ["Ro"] = "Romanian", ["Hr"] = "Croatian",
            ["Sk"] = "Slovak", ["Bg"] = "Bulgarian", ["Uk"] = "Ukrainian", ["Lt"] = "Lithuanian",
            ["Lv"] = "Latvian", ["Et"] = "Estonian", ["Sl"] = "Slovenian"
        };

        private static readonly Regex _regionBracketRegex = new Regex(
            @"[\[\(\{]\s*([A-Za-z][A-Za-z,. \-]+?)\s*[\]\)\}]",
            RegexOptions.Compiled);

        // Revision patterns: (Rev A), (Rev-A), (Rev 1), (Rev 2)
        private static readonly Regex _revisionRegex = new Regex(
            @"^Rev[\s\-]?([A-Z0-9]+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Disc patterns: (Disc 1), (Disc 2)
        private static readonly Regex _discRegex = new Regex(
            @"^Disc\s+(\d+)$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Version inside brackets: (v1.0), (v2.00), (v1.000)
        private static readonly Regex _bracketVersionRegex = new Regex(
            @"^v\d+(\.\d+)*$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Known variant/build status tokens
        private static readonly HashSet<string> _knownVariantTokens = new(StringComparer.OrdinalIgnoreCase)
        {
            "Beta", "Alpha", "Proto", "Prototype", "Sample", "Unknown", "Alternate", "Alt",
            "Unl", "Unlicensed", "Demo", "Promo", "Kiosk", "Prerelease", "Rerelease",
            "NDSi Enhanced", "SGB Enhanced", "GBC Enhanced", "Rumble Version"
        };

        // PlayStation serial prefixes covering PS1 through PS5 across all regions
        private static readonly Regex _psSerialRegex = new Regex(
            @"(CUSA|PPSA|BLES|BLUS|BCES|BCUS|NPEB|NPUB|NPEA|NPUA|SLES|SLUS|SCES|SCUS|SLPS|SLPM|SCCS|SLKA|BCAS|BLAS|BCJM|BLJM|BCJS|BLJS|PLJS|PLJM|PCJS|ELJS|ELJM|PCSA|PCSE|PCSG|PCSB|PCSH|PCSD|ULES|ULUS|UCES|UCUS|UCAS|ULJM|ULJS|UCJS|NPJH|NPEH|NPUH|SCED|SCUD|PAPX|PCPX)[-_]?(?:\d{3}[.]\d{2}|\d{4,5})",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Safety-net: strip PS serial prefix + digits from the start of a cleaned title
        private static readonly Regex _psSerialPrefixCleanup = new Regex(
            @"^(?:CUSA|PPSA|BLES|BLUS|BCES|BCUS|NPEB|NPUB|NPEA|NPUA|SLES|SLUS|SCES|SCUS|SLPS|SLPM|SCCS|SLKA|BCAS|BLAS|BCJM|BLJM|BCJS|BLJS|PLJS|PLJM|PCJS|ELJS|ELJM|PCSA|PCSE|PCSG|PCSB|PCSH|PCSD|ULES|ULUS|UCES|UCUS|UCAS|ULJM|ULJS|UCJS|NPJH|NPEH|NPUH|SCED|SCUD|PAPX|PCPX)[\s._-]*(?:\d{3}[\s._-]*\d{2}|\d{4,5})\s*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Switch Title ID (16-char hex)
        private static readonly Regex _switchSerialRegex = new Regex(
            @"[0-9a-fA-F]{16}",
            RegexOptions.Compiled);

        // PS4 Content ID prefix
        private static readonly Regex _ps4ContentIdRegex = new Regex(
            @"[EU]P\d{4}-",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Version patterns: v1.00, v1.0, v1, v05g
        private static readonly Regex _versionRegex = new Regex(
            @"v\d+([a-zA-Z0-9._-]+)*",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // PS4 specific codes: A0100, V0100
        private static readonly Regex _ps4CodeRegex = new Regex(
            @"\b[AV]\d{4}\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Size patterns: 2.90GB, 500MB
        private static readonly Regex _sizeRegex = new Regex(
            @"\d+(\.\d+)?\s*(gb|mb|kb|gr|mg)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Bracket content
        private static readonly Regex _squareBracketRegex = new Regex(
            @"[\[［].*?[\]］]",
            RegexOptions.Compiled);

        private static readonly Regex _parenRegex = new Regex(
            @"\(.*?\)",
            RegexOptions.Compiled);

        private static readonly Regex _curlyBracketRegex = new Regex(
            @"\{.*?\}",
            RegexOptions.Compiled);

        private static readonly Regex _multiSpaceRegex = new Regex(
            @"\s+",
            RegexOptions.Compiled);

        private static readonly char[] _separators = { ' ', '.', '[', ']', '(', ')', '{', '}', '\uFF3B', '\uFF3D', '+', ',', '!', '?', '#', '&', ';' };

        public (string Title, string? Serial) CleanGameTitle(string originalTitle)
        {
            if (string.IsNullOrWhiteSpace(originalTitle)) return (originalTitle, null);
            
            // 0. Pre-clean URL encoded brackets often found in scene releases
            string workingTitle = originalTitle.Replace("5B", "[", StringComparison.OrdinalIgnoreCase)
                                               .Replace("5D", "]", StringComparison.OrdinalIgnoreCase);

            // 0. Aggressive Noise Stripping (Content in brackets/parens)
            workingTitle = workingTitle.Replace('\u00A0', ' ').Replace('_', ' ').Replace('+', ' ');
            // Preserve '&' as 'and' for semantic meaning (e.g. "Might & Magic" → "Might and Magic")
            workingTitle = workingTitle.Replace("&", " and ");
            workingTitle = workingTitle.Replace('-', ' ');
            workingTitle = _squareBracketRegex.Replace(workingTitle, " ");
            workingTitle = _parenRegex.Replace(workingTitle, " ");
            workingTitle = _curlyBracketRegex.Replace(workingTitle, " ");

            // 1. Size patterns (e.g. 2.90GB, 500MB)
            workingTitle = _sizeRegex.Replace(workingTitle, " ");

            string? serial = null;
            
            // 0a. Try to find PlayStation Serial
            var psSerialMatch = _psSerialRegex.Match(originalTitle);
            if (psSerialMatch.Success)
            {
                serial = psSerialMatch.Value.ToUpper().Replace("-", "").Replace("_", "").Replace(".", "");
                workingTitle = workingTitle.Replace(psSerialMatch.Value, " ", StringComparison.OrdinalIgnoreCase);
                // Also try with underscores/dashes normalised to spaces (preprocessing already did this)
                var normalizedSerial = psSerialMatch.Value.Replace('_', ' ').Replace('-', ' ');
                workingTitle = workingTitle.Replace(normalizedSerial, " ", StringComparison.OrdinalIgnoreCase);
            }

            // 0b. Try to find Switch Serial (16-char hex)
            if (string.IsNullOrEmpty(serial))
            {
                var hexMatch = _switchSerialRegex.Match(originalTitle); 
                if (hexMatch.Success) serial = hexMatch.Value.ToUpper();
            }

            // 0c. Strip common PS4 content ID prefixes
            workingTitle = _ps4ContentIdRegex.Replace(workingTitle, " ");
            
            // Strip standard version patterns
            workingTitle = _versionRegex.Replace(workingTitle, " ");
            
            // Strip PS4 specific codes like A0100 (App), V0100 (Version)
            workingTitle = _ps4CodeRegex.Replace(workingTitle, " ");

            // 2. Split and filter words
            var words = workingTitle.Split(_separators, StringSplitOptions.RemoveEmptyEntries);
            var cleanWords = new List<string>();

            foreach (var word in words)
            {
                if (_noiseWords.Contains(word)) continue;
                
                // Explicitly kill "00", "01" artifacts mostly left over from versions
                if (word == "00" || word == "01") continue;

                // Explicit check for common 2-letter region codes
                if (word.Length == 2 && _regionCodes.Contains(word)) continue;

                // Skip words with 6 or more digits (hex IDs, content IDs, long version numbers).
                // Threshold raised from 4 to 6 to preserve legitimate game titles like
                // "2048", "1942", "1943" and year components in "F1 2024", "Cyberpunk 2077".
                int digitCount = word.Count(char.IsDigit);
                if (digitCount >= 6) continue;

                cleanWords.Add(word);
            }

            string title = string.Join(" ", cleanWords).Trim();
            
            // Safety-net: strip any remaining PS serial prefix from the start of the title
            title = _psSerialPrefixCleanup.Replace(title, "").Trim();
            
            // Remove lingering noise
            title = _multiSpaceRegex.Replace(title, " ");
            
            return (title, serial);
        }

        public string ResolvePlatformFromSerial(string serial)
        {
            if (string.IsNullOrEmpty(serial)) return "default";
            
            // PlayStation 4 / 5
            if (serial.StartsWith("CUSA") || serial.StartsWith("PLAS") || serial.StartsWith("PLJS") || serial.StartsWith("PLJM") || serial.StartsWith("PCJS")) return "ps4";
            if (serial.StartsWith("PPSA") || serial.StartsWith("ELJS") || serial.StartsWith("ELJM")) return "ps5";

            // PlayStation 3
            if (serial.StartsWith("BLES") || serial.StartsWith("BLUS") || 
                serial.StartsWith("BCES") || serial.StartsWith("BCUS") ||
                serial.StartsWith("NPEB") || serial.StartsWith("NPUB") ||
                serial.StartsWith("NPEA") || serial.StartsWith("NPUA") ||
                serial.StartsWith("BCAS") || serial.StartsWith("BLAS") ||
                serial.StartsWith("BCJM") || serial.StartsWith("BLJM") ||
                serial.StartsWith("BCJS") || serial.StartsWith("BLJS")) return "ps3";
            
            // PlayStation 2 / 1
            if (serial.StartsWith("SLES") || serial.StartsWith("SLUS") || 
                serial.StartsWith("SCES") || serial.StartsWith("SCUS") ||
                serial.StartsWith("SLPS") || serial.StartsWith("SLPM") ||
                serial.StartsWith("SCCS") || serial.StartsWith("SLKA") ||
                serial.StartsWith("SCED") || serial.StartsWith("SCUD") ||
                serial.StartsWith("PAPX") || serial.StartsWith("PCPX"))
            {
               return "ps2"; 
            }
            
            // PlayStation Vita
            if (serial.StartsWith("PCSA") || serial.StartsWith("PCSE") ||
                serial.StartsWith("PCSG") || serial.StartsWith("PCSB") ||
                serial.StartsWith("PCSH") || serial.StartsWith("PCSD")) return "vita";

            // PlayStation Portable (PSP)
            if (serial.StartsWith("ULES") || serial.StartsWith("ULUS") ||
                serial.StartsWith("UCES") || serial.StartsWith("UCUS") ||
                serial.StartsWith("UCAS") || serial.StartsWith("ULJM") ||
                serial.StartsWith("ULJS") || serial.StartsWith("UCJS") ||
                serial.StartsWith("NPJH") || serial.StartsWith("NPEH") ||
                serial.StartsWith("NPUH")) return "psp";

            return "default";
        }

        public string GetPlatformFromExtension(string ext)
        {
            if (string.IsNullOrEmpty(ext)) return "default";
            ext = ext.ToLower();

            if (ext == ".nsp" || ext == ".xci" || ext == ".nsz" || ext == ".xcz") return "nintendo_switch";
            if (ext == ".dmg" || ext == ".app") return "macos";
            
            // Retro Mappings
            if (ext == ".z64" || ext == ".n64" || ext == ".v64") return "nintendo_64";
            if (ext == ".sfc" || ext == ".smc") return "snes";
            if (ext == ".nes") return "nes";
            if (ext == ".gba") return "gba";
            if (ext == ".gbc") return "gbc";
            if (ext == ".gb") return "gb";
            if (ext == ".md" || ext == ".gen" || ext == ".smd") return "megadrive";
            if (ext == ".sms") return "mastersystem";
            if (ext == ".gg") return "gamegear";
            if (ext == ".pce") return "pc_engine";

            return "default";
        }

        private static readonly Regex _suffixPattern = new Regex(
            @"\b(Multi[- ]?Player|Single[- ]?Player|Co[- ]?op|GOTY|Game of the Year|Complete Edition|" +
            @"Definitive Edition|Deluxe Edition|Gold Edition|Enhanced Edition|" +
            @"Special Edition|Premium Edition|Ultimate Edition|Legendary Edition|" +
            @"HD|Remastered|Remake|Demo|Beta|Trial|Early Access|" +
            @"Director'?s Cut|Extended Edition|Collector'?s Edition)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _trailingNoise = new Regex(
            @"\s+$|^\s+|\s{2,}",
            RegexOptions.Compiled);

        /// <summary>
        /// Generate multiple search query variants from a cleaned title.
        /// Returns an ordered list: original, suffix-stripped, diacritics-normalized.
        /// Duplicates are removed.
        /// </summary>
        public List<string> GenerateSearchVariants(string cleanedTitle)
        {
            var variants = new List<string>();
            if (string.IsNullOrWhiteSpace(cleanedTitle)) return variants;

            // Variant 1: the cleaned title as-is
            variants.Add(cleanedTitle.Trim());

            // Variant 2: suffix-stripped
            var stripped = _suffixPattern.Replace(cleanedTitle, " ");
            stripped = _trailingNoise.Replace(stripped, " ").Trim();
            if (!string.IsNullOrWhiteSpace(stripped) && !stripped.Equals(cleanedTitle.Trim(), StringComparison.OrdinalIgnoreCase))
            {
                variants.Add(stripped);
            }

            // Variant 3: German diacritics expanded (ue→ü etc. reversed: ü→ue already in input)
            var diacriticsNormalized = NormalizeDiacritics(cleanedTitle);
            if (!variants.Any(v => v.Equals(diacriticsNormalized, StringComparison.OrdinalIgnoreCase)))
            {
                variants.Add(diacriticsNormalized);
            }

            // Variant 4: suffix-stripped + diacritics
            if (!string.IsNullOrWhiteSpace(stripped))
            {
                var strippedNormalized = NormalizeDiacritics(stripped);
                if (!variants.Any(v => v.Equals(strippedNormalized, StringComparison.OrdinalIgnoreCase)))
                {
                    variants.Add(strippedNormalized);
                }
            }

            return variants;
        }

        /// <summary>
        /// Normalize diacritics: decompose Unicode and remove combining marks.
        /// Also handles common German transliterations (ue→ü, oe→ö, ae→ä, ss→ß reversed).
        /// </summary>
        public static string NormalizeDiacritics(string input)
        {
            if (string.IsNullOrEmpty(input)) return input;

            // Unicode NFC decomposition → remove combining marks
            var normalized = input.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder(normalized.Length);
            foreach (var c in normalized)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark)
                    sb.Append(c);
            }
            return sb.ToString().Normalize(NormalizationForm.FormC);
        }

        /// <summary>
        /// Compute similarity between two strings (0.0 to 1.0).
        /// Uses a combination of contains-check and Levenshtein distance.
        /// </summary>
        public static double ComputeSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

            var la = a.ToLower(CultureInfo.InvariantCulture).Trim();
            var lb = b.ToLower(CultureInfo.InvariantCulture).Trim();

            if (la == lb) return 1.0;
            if (la.Contains(lb) || lb.Contains(la)) return 0.90;

            int distance = LevenshteinDistance(la, lb);
            int maxLen = Math.Max(la.Length, lb.Length);
            if (maxLen == 0) return 1.0;

            return 1.0 - ((double)distance / maxLen);
        }

        /// <summary>
        /// Strip container extensions (.ps3, .ps4, .psn) from folder names.
        /// Returns the base name without extension and the container extension found (or null).
        /// </summary>
        public static (string BaseName, string? ContainerExt) StripContainerExtension(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return (folderName, null);
            var ext = Path.GetExtension(folderName);
            if (!string.IsNullOrEmpty(ext) && _containerExtensions.Contains(ext))
            {
                return (Path.GetFileNameWithoutExtension(folderName), ext);
            }
            return (folderName, null);
        }

        public static string? ExtractRegion(string originalName)
        {
            var (region, _, _) = ExtractFilenameMetadata(originalName);
            return region;
        }

        public static (string? Region, string? Languages) ExtractRegionAndLanguages(string originalName)
        {
            var (region, languages, _) = ExtractFilenameMetadata(originalName);
            return (region, languages);
        }

        /// <summary>
        /// Extract region, languages AND revision from an original filename/folder name BEFORE title cleaning.
        /// Parses bracketed tokens like (USA), (Europe) (En,Fr,De,Es,It), (Rev 1), (Beta), [JP]
        /// Also handles dash-separated PSX/PSP style: (EU - AU), (EN - ES - FR)
        /// Returns:
        ///   Region   — comma-separated region names (e.g. "USA", "USA, Europe", "Japan")
        ///   Languages — comma-separated language codes (e.g. "En, Fr, De, Es, It")
        ///   Revision  — revision/variant tag (e.g. "Rev A", "Beta", "Disc 1", "v2.00")
        /// Returns (null, null, null) if nothing detected.
        /// </summary>
        public static (string? Region, string? Languages, string? Revision) ExtractFilenameMetadata(string originalName)
        {
            if (string.IsNullOrEmpty(originalName)) return (null, null, null);

            string? region = null;
            string? languages = null;
            var revisions = new List<string>();

            var matches = _regionBracketRegex.Matches(originalName);
            foreach (Match m in matches)
            {
                var content = m.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(content)) continue;

                // Try revision/variant first (most specific patterns)
                var rev = TryParseRevisionToken(content);
                if (rev != null)
                {
                    revisions.Add(rev);
                    continue;
                }

                // Try to parse as region(s)
                if (region == null)
                {
                    var parsed = TryParseRegionToken(content);
                    if (parsed != null)
                    {
                        region = parsed;
                        continue;
                    }
                }

                // Try to parse as a language list
                if (languages == null)
                {
                    var parsed = TryParseLanguageToken(content);
                    if (parsed != null)
                    {
                        languages = parsed;
                        continue;
                    }
                }
            }

            // Fallback: look for unbracketed region tokens as whole words (>= 3 chars)
            if (region == null)
            {
                foreach (var (regionToken, regionName) in _regionTokens)
                {
                    if (regionToken.Length >= 3)
                    {
                        var pattern = @"\b" + Regex.Escape(regionToken) + @"\b";
                        if (Regex.IsMatch(originalName, pattern, RegexOptions.IgnoreCase))
                        {
                            region = regionName;
                            break;
                        }
                    }
                }
            }

            string? revision = revisions.Count > 0 ? string.Join(", ", revisions) : null;
            return (region, languages, revision);
        }

        /// <summary>
        /// Try to parse a bracketed token as a revision/variant/disc tag.
        /// Returns normalized revision string, or null if not a revision.
        /// </summary>
        private static string? TryParseRevisionToken(string content)
        {
            // Exact variant match: Beta, Alpha, Proto, Sample, Unknown, etc.
            if (_knownVariantTokens.Contains(content))
                return content;

            // Rev patterns: Rev A, Rev-A, Rev 1, Rev-B
            var revMatch = _revisionRegex.Match(content);
            if (revMatch.Success)
                return "Rev " + revMatch.Groups[1].Value.ToUpper();

            // Disc patterns: Disc 1, Disc 2
            var discMatch = _discRegex.Match(content);
            if (discMatch.Success)
                return "Disc " + discMatch.Groups[1].Value;

            // Version inside brackets: v1.0, v2.00
            if (_bracketVersionRegex.IsMatch(content))
                return content;

            return null;
        }

        /// <summary>
        /// Try to parse a bracketed token as one or more region names.
        /// Handles single ("USA"), multi comma-separated ("USA, Europe"),
        /// multi dash-separated PSX/PSP style ("EU - AU"), and short codes ("U", "E").
        /// Returns normalized comma-separated region string, or null if not a region.
        /// </summary>
        private static string? TryParseRegionToken(string content)
        {
            // Check for exact single-region match first
            foreach (var (token, name) in _regionTokens)
            {
                if (content.Equals(token, StringComparison.OrdinalIgnoreCase))
                    return name;
            }

            // Detect separator: comma or dash
            char separator = content.Contains(',') ? ',' : content.Contains('-') ? '-' : '\0';
            if (separator == '\0') return null;

            // Check for multi-region: "USA, Europe", "EU - AU", "EU - FR - DE"
            var parts = content.Split(separator);
            var regionParts = new List<string>();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                string? match = null;
                foreach (var (token, name) in _regionTokens)
                {
                    if (trimmed.Equals(token, StringComparison.OrdinalIgnoreCase))
                    {
                        match = name;
                        break;
                    }
                }
                if (match == null) return null; // Not all parts are regions → not a region token
                regionParts.Add(match);
            }
            if (regionParts.Count > 0)
                return string.Join(", ", regionParts.Distinct());

            return null;
        }

        /// <summary>
        /// Try to parse a bracketed token as a comma-separated or dash-separated language list.
        /// Handles: (En,Fr,De,Es,It) and (EN - ES - FR)
        /// Returns normalized comma-separated language codes, or null if not a language list.
        /// </summary>
        private static string? TryParseLanguageToken(string content)
        {
            // Detect separator: comma or dash
            char separator = content.Contains(',') ? ',' : content.Contains('-') ? '-' : '\0';
            if (separator == '\0') return null;

            var parts = content.Split(separator);
            if (parts.Length < 2) return null;

            var codes = new List<string>();
            foreach (var part in parts)
            {
                var trimmed = part.Trim();
                if (string.IsNullOrEmpty(trimmed)) continue;
                if (trimmed.Length < 2 || trimmed.Length > 3) return null;
                if (!_knownLanguageCodes.Contains(trimmed)) return null;
                // Normalize: capitalize first letter
                codes.Add(char.ToUpper(trimmed[0]) + trimmed.Substring(1).ToLower());
            }

            if (codes.Count < 2) return null;
            return string.Join(", ", codes);
        }

        /// <summary>
        /// Result of classifying a supplementary content file (Update, DLC, Patch).
        /// </summary>
        public class SupplementaryContentInfo
        {
            public string FileType { get; set; } = "Main"; // "Main", "Patch", or "DLC"
            public string? Serial { get; set; }
            public string? TitleId { get; set; }
            public string? BaseTitleId { get; set; } // Base game TitleId derived from update/DLC mask
            public string? Version { get; set; }
            public string? ContentName { get; set; }
            public string? CleanParentTitle { get; set; }
            public bool IsGeneric { get; set; }
        }

        public static string? ExtractBaseSwitchTitleId(string? titleId)
        {
            if (string.IsNullOrEmpty(titleId) || titleId.Length != 16) return null;
            if (!ulong.TryParse(titleId, System.Globalization.NumberStyles.HexNumber,
                System.Globalization.CultureInfo.InvariantCulture, out var value))
            {
                return null;
            }
            const ulong BaseMask = 0xFFFFFFFFFFFFE000UL;
            var baseId = value & BaseMask;
            return baseId.ToString("X16", System.Globalization.CultureInfo.InvariantCulture);
        }

        // Keywords indicating DLC content
        private static readonly string[] _dlcKeywords = new[]
        {
            "dlc", "add-on", "addon", "season pass", "seasonpass", "expansion",
            "bonus content", "content pack", "map pack", "character pack",
            "skin pack", "weapon pack", "costume pack"
        };

        // Keywords indicating Update/Patch content
        private static readonly string[] _updateKeywords = new[]
        {
            "update", "patch", "fix", "hotfix", "hot fix", "bugfix", "day one",
            "day-one", "backport", "firmwar"
        };

        // Generic filenames that are always updates
        private static readonly HashSet<string> _genericUpdateNames = new(StringComparer.OrdinalIgnoreCase)
        {
            "update", "patch", "update.pkg", "patch.pkg", "update.nsp", "patch.nsp",
            "UP", "UP.pkg"
        };

        // Switch TitleID suffix patterns: base ends 000, patch ends 800, DLC has bit 12 set
        private static readonly Regex _switchTitleIdRegex = new Regex(
            @"\[?([0-9a-fA-F]{16})\]?",
            RegexOptions.Compiled);

        private static readonly Regex _switchVersionBracketRegex = new Regex(
            @"\[v(\d+)\]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Scene release group tags: trailing _NSW-GROUPNAME, _PS4-GROUPNAME, etc.
        private static readonly Regex _sceneGroupTrailingRegex = new Regex(
            @"[_\-](NSW|PS4|PS5|XBOX|PC|3DS|WiiU)[_\-][A-Za-z0-9]+$",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Scene release group prefix: sxs-, venom-, etc. (lowercase prefix ending with hyphen)
        private static readonly Regex _sceneGroupLeadingRegex = new Regex(
            @"^[a-z0-9]{2,10}-",
            RegexOptions.Compiled);

        // Version with spaces instead of dots: v1 0 3, v1 01 (scene release convention)
        private static readonly Regex _spaceVersionRegex = new Regex(
            @"\bv(\d+(?:\s+\d+)+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // PS4/PS5 content type suffix in filename: -app (base), -patch (update), -ac (DLC)
        private static readonly Regex _ps4ContentTypeSuffixRegex = new Regex(
            @"-(app|patch|ac)(?:[_\.]|$)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        /// <summary>
        /// Classify a file as Main, Patch, or DLC based on its filename, folder context, and content identifiers.
        /// Extracts serial, titleId, version, content name, and a cleaned parent title for matching.
        /// </summary>
        public SupplementaryContentInfo ClassifySupplementaryContent(string fileName, string? parentFolderName = null)
        {
            var result = new SupplementaryContentInfo();
            if (string.IsNullOrWhiteSpace(fileName)) return result;

            var baseName = Path.GetFileNameWithoutExtension(fileName);
            if (string.IsNullOrEmpty(baseName)) baseName = fileName;

            // Check for generic update filenames first
            if (_genericUpdateNames.Contains(baseName) || _genericUpdateNames.Contains(fileName))
            {
                result.FileType = "Patch";
                result.IsGeneric = true;
                // For generic names, parent title comes from folder context
                if (!string.IsNullOrEmpty(parentFolderName))
                {
                    var (stripped, _) = StripContainerExtension(parentFolderName);
                    var (cleanParent, parentSerial) = CleanGameTitle(stripped);
                    result.CleanParentTitle = cleanParent;
                    result.Serial = parentSerial;
                }
                return result;
            }

            // Extract PlayStation serial
            var psMatch = _psSerialRegex.Match(fileName);
            if (psMatch.Success)
            {
                result.Serial = psMatch.Value.ToUpper().Replace("-", "").Replace("_", "").Replace(".", "");
            }

            // Extract Switch TitleID
            var switchIdMatch = _switchTitleIdRegex.Match(fileName);
            if (switchIdMatch.Success)
            {
                result.TitleId = switchIdMatch.Groups[1].Value.ToUpper();
            }

            // Extract version from [vNNNNN] pattern (Switch style)
            var switchVerMatch = _switchVersionBracketRegex.Match(fileName);
            if (switchVerMatch.Success)
            {
                result.Version = "v" + switchVerMatch.Groups[1].Value;
            }
            else
            {
                // Try general version pattern
                var verMatch = _versionRegex.Match(baseName);
                if (verMatch.Success)
                {
                    result.Version = verMatch.Value;
                }
            }

            // Determine content type from TitleID pattern (Switch-specific)
            // Bit layout of last 4 hex digits: bit 11 (0x800) = Patch, bit 12 (0x1000) = DLC
            if (!string.IsNullOrEmpty(result.TitleId) && result.TitleId.Length == 16)
            {
                var last4Hex = result.TitleId.Substring(12, 4);
                if (int.TryParse(last4Hex, System.Globalization.NumberStyles.HexNumber, null, out int last4))
                {
                    bool isPatch = (last4 & 0x800) != 0 && (last4 & 0x1000) == 0;
                    bool isDLC = (last4 & 0x1000) != 0;

                    if (isPatch)
                        result.FileType = "Patch";
                    else if (isDLC)
                        result.FileType = "DLC";

                    if (isPatch || isDLC)
                    {
                        result.BaseTitleId = ExtractBaseSwitchTitleId(result.TitleId);
                    }
                    else
                    {
                        result.BaseTitleId = result.TitleId;
                    }
                }
            }

            // Normalize scene release conventions before keyword matching
            var normalizedName = baseName;

            // Strip scene group tags: trailing _NSW-GROUPNAME or -GROUPNAME
            normalizedName = _sceneGroupTrailingRegex.Replace(normalizedName, "");
            // Strip scene group prefix: sxs-, venom-, etc.
            normalizedName = _sceneGroupLeadingRegex.Replace(normalizedName, "");
            // Normalize underscores to spaces for keyword matching
            var lowerName = normalizedName.Replace('_', ' ').ToLowerInvariant();

            // Normalize space-delimited versions: "v1 0 3" → "v1.0.3"
            var spaceVerMatch = _spaceVersionRegex.Match(lowerName);
            if (spaceVerMatch.Success)
            {
                var normalized = "v" + spaceVerMatch.Groups[1].Value.Replace(' ', '.');
                lowerName = lowerName.Replace(spaceVerMatch.Value, normalized);
                result.Version = normalized;
            }

            // PS4/PS5 content type suffix: -patch → Update, -ac → DLC, -app → Base
            if (result.FileType == "Main")
            {
                var ps4SuffixMatch = _ps4ContentTypeSuffixRegex.Match(baseName);
                if (ps4SuffixMatch.Success)
                {
                    var suffix = ps4SuffixMatch.Groups[1].Value.ToLowerInvariant();
                    if (suffix == "patch") result.FileType = "Patch";
                    else if (suffix == "ac") result.FileType = "DLC";
                }
            }

            // Check filename keywords if not already classified by TitleID or PS4 suffix
            if (result.FileType == "Main")
            {
                foreach (var kw in _dlcKeywords)
                {
                    if (lowerName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        result.FileType = "DLC";
                        break;
                    }
                }
            }
            if (result.FileType == "Main")
            {
                foreach (var kw in _updateKeywords)
                {
                    if (lowerName.Contains(kw, StringComparison.OrdinalIgnoreCase))
                    {
                        result.FileType = "Patch";
                        break;
                    }
                }
            }

            // Build content name (the descriptive part, e.g. "ALL DLC", "Season Pass")
            result.ContentName = baseName;

            // Clean parent title for matching (use scene-stripped name)
            var (cleanTitle, serial) = CleanGameTitle(normalizedName);
            result.CleanParentTitle = cleanTitle;
            if (string.IsNullOrEmpty(result.Serial) && !string.IsNullOrEmpty(serial))
            {
                result.Serial = serial;
            }

            return result;
        }

        /// <summary>
        /// Derive the base TitleID for a Switch game from an update/DLC TitleID.
        /// Base games end in 000, updates keep the same base, DLCs have base+800.
        /// </summary>
        public static string? DeriveBaseTitleId(string? titleId)
        {
            if (string.IsNullOrEmpty(titleId) || titleId.Length != 16) return null;
            // Zero out the last 3 hex digits to get the application base
            return titleId.Substring(0, 13) + "000";
        }

        private static int LevenshteinDistance(string s, string t)
        {
            int n = s.Length, m = t.Length;
            var d = new int[n + 1, m + 1];

            for (int i = 0; i <= n; i++) d[i, 0] = i;
            for (int j = 0; j <= m; j++) d[0, j] = j;

            for (int i = 1; i <= n; i++)
            {
                for (int j = 1; j <= m; j++)
                {
                    int cost = s[i - 1] == t[j - 1] ? 0 : 1;
                    d[i, j] = Math.Min(
                        Math.Min(d[i - 1, j] + 1, d[i, j - 1] + 1),
                        d[i - 1, j - 1] + cost);
                }
            }

            return d[n, m];
        }
    }
}
