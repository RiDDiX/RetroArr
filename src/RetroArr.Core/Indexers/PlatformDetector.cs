using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using RetroArr.Core.Games;
using RetroArr.Core.Prowlarr;

namespace RetroArr.Core.Indexers
{
    // Sonarr/Radarr-style platform detection from release titles + Newznab categories
    public static class PlatformDetector
    {
        // Category mappings (Newznab standard)
        private static readonly Dictionary<int, (string Name, string Folder)> CategoryToPlatform = new()
        {
            // PC Games (4000-4999)
            { 4000, ("PC", "windows") },
            { 4010, ("PC", "windows") },
            { 4020, ("PC", "windows") },
            { 4030, ("Mac", "macintosh") },
            { 4040, ("Mobile", "mobile") },
            { 4050, ("PC", "windows") },
            
            // Console Games (1000-1999)
            { 1010, ("Nintendo DS", "nds") },
            { 1020, ("PSP", "psp") },
            { 1030, ("Wii", "wii") },
            { 1040, ("Xbox", "xbox") },
            { 1050, ("Xbox 360", "xbox360") },
            { 1060, ("WiiWare", "wii") },
            { 1070, ("Xbox 360", "xbox360") },
            { 1080, ("PlayStation 3", "ps3") },
            { 1110, ("Nintendo 3DS", "3ds") },
            { 1120, ("PS Vita", "vita") },
            { 1130, ("Wii U", "wiiu") },
            { 1140, ("Xbox One", "xboxone") },
            { 1180, ("PlayStation 4", "ps4") },
        };

        // Title patterns for platform detection
        private static readonly List<(Regex Pattern, string Name, string Folder)> TitlePatterns = new()
        {
            // Nintendo
            (new Regex(@"\b(NSW2|Nintendo\s*Switch\s*2)\b", RegexOptions.IgnoreCase), "Nintendo Switch 2", "switch2"),
            (new Regex(@"\b(NSW|NSP|XCI|Nintendo\s*Switch|Switch)\b", RegexOptions.IgnoreCase), "Nintendo Switch", "switch"),
            (new Regex(@"\bWii\s*U\b", RegexOptions.IgnoreCase), "Wii U", "wiiu"),
            (new Regex(@"\b(Wii|WBFS)\b(?!\s*U)", RegexOptions.IgnoreCase), "Wii", "wii"),
            (new Regex(@"\b(3DS|CIA|3DSX)\b", RegexOptions.IgnoreCase), "Nintendo 3DS", "3ds"),
            (new Regex(@"\b(NDS|Nintendo\s*DS)\b", RegexOptions.IgnoreCase), "Nintendo DS", "nds"),
            (new Regex(@"\b(GBA|Game\s*Boy\s*Advance)\b", RegexOptions.IgnoreCase), "Game Boy Advance", "gba"),
            (new Regex(@"\b(GBC|Game\s*Boy\s*Color)\b", RegexOptions.IgnoreCase), "Game Boy Color", "gbc"),
            (new Regex(@"\bGame\s*Boy\b(?!\s*(Advance|Color))", RegexOptions.IgnoreCase), "Game Boy", "gb"),
            (new Regex(@"\b(N64|Nintendo\s*64)\b", RegexOptions.IgnoreCase), "Nintendo 64", "n64"),
            (new Regex(@"\b(SNES|Super\s*Nintendo)\b", RegexOptions.IgnoreCase), "SNES", "snes"),
            (new Regex(@"\b(NES|Famicom)\b(?!\s*Disk)", RegexOptions.IgnoreCase), "NES", "nes"),
            (new Regex(@"\bGameCube\b", RegexOptions.IgnoreCase), "GameCube", "gamecube"),
            
            // Sony
            (new Regex(@"\b(PS5|PlayStation\s*5)\b", RegexOptions.IgnoreCase), "PlayStation 5", "ps5"),
            (new Regex(@"\b(PS4|PlayStation\s*4)\b", RegexOptions.IgnoreCase), "PlayStation 4", "ps4"),
            (new Regex(@"\b(PS3|PlayStation\s*3)\b", RegexOptions.IgnoreCase), "PlayStation 3", "ps3"),
            (new Regex(@"\b(PS2|PlayStation\s*2)\b", RegexOptions.IgnoreCase), "PlayStation 2", "ps2"),
            (new Regex(@"\b(PSP|PlayStation\s*Portable)\b", RegexOptions.IgnoreCase), "PSP", "psp"),
            (new Regex(@"\b(PlayStation\s*Vita|PSVita|PS\s*Vita|Vita)\b", RegexOptions.IgnoreCase), "PS Vita", "vita"),
            (new Regex(@"\b(PSX|PS1|PlayStation(?!\s*[2-5]))\b", RegexOptions.IgnoreCase), "PlayStation 1", "psx"),
            
            // Microsoft
            (new Regex(@"\b(XSX|Xbox\s*Series)\b", RegexOptions.IgnoreCase), "Xbox Series X", "xboxseriesx"),
            (new Regex(@"\b(XONE|Xbox\s*One)\b", RegexOptions.IgnoreCase), "Xbox One", "xboxone"),
            (new Regex(@"\b(X360|Xbox\s*360)\b", RegexOptions.IgnoreCase), "Xbox 360", "xbox360"),
            (new Regex(@"\bXbox\b(?!\s*(One|360|Series))", RegexOptions.IgnoreCase), "Xbox", "xbox"),
            
            // Sega
            (new Regex(@"\bDreamcast\b", RegexOptions.IgnoreCase), "Dreamcast", "dreamcast"),
            (new Regex(@"\b(Saturn|Sega\s*Saturn)\b", RegexOptions.IgnoreCase), "Saturn", "saturn"),
            (new Regex(@"\b(Sega\s*Genesis|Genesis|Mega\s*Drive)\b", RegexOptions.IgnoreCase), "Mega Drive", "megadrive"),
            (new Regex(@"\bGame\s*Gear\b", RegexOptions.IgnoreCase), "Game Gear", "gamegear"),
            (new Regex(@"\bMaster\s*System\b", RegexOptions.IgnoreCase), "Master System", "mastersystem"),
            
            // PC
            (new Regex(@"\b(GOG|CODEX|PLAZA|SKIDROW|RELOADED|FLT|HOODLUM|TENOKE|RUNE)\b", RegexOptions.IgnoreCase), "PC", "windows"),
            (new Regex(@"\b(Windows|Win32|Win64|x86|x64|PC)\b", RegexOptions.IgnoreCase), "PC", "windows"),
            (new Regex(@"\b(macOS|OSX|Mac)\b", RegexOptions.IgnoreCase), "Mac", "macintosh"),
            (new Regex(@"\bLinux\b", RegexOptions.IgnoreCase), "Linux", "linux"),
            
            // Retro/Arcade
            (new Regex(@"\b(MAME|Arcade)\b", RegexOptions.IgnoreCase), "Arcade", "arcade"),
            (new Regex(@"\bNeo\s*Geo\b", RegexOptions.IgnoreCase), "Neo Geo", "neogeo"),
            (new Regex(@"\bDOS\b", RegexOptions.IgnoreCase), "DOS", "dos"),
            (new Regex(@"\bAmiga\b", RegexOptions.IgnoreCase), "Amiga", "amiga"),
        };

        public static void DetectPlatform(SearchResult result)
        {
            // Scene names often use underscores, which \b doesn't treat as a separator
            var text = (result.Title ?? string.Empty).Replace('_', ' ');
            // A platform only named as what the release also runs on ("Nintendo Switch 2 compatible")
            // doesn't count when another platform in the title does
            bool IsOriginNote(Match m) => OriginNote.IsMatch(text.Substring(m.Index + m.Length));
            bool IsCompatNote(Match m) => CompatNote.IsMatch(text.Substring(m.Index + m.Length));
            var found = TitlePatterns.Where(p => p.Pattern.IsMatch(text)).ToList();
            var title = found.FirstOrDefault(p => p.Pattern.Matches(text).Any(m => !IsCompatNote(m)));
            if (title.Pattern == null) title = found.FirstOrDefault();
            // Plain words like "Wii" or "PlayStation" also turn up in game names (Wii Fit U, PlayStation All-Stars),
            // so only count it as a tag when the pattern also found an explicit one (1-2-Switch NSW, Wii Party WBFS)
            var matches = title.Pattern?.Matches(text).ToList() ?? new List<Match>();
            var isTag = matches.Any(m => !PlainWords.Contains(Regex.Replace(m.Value, @"\s+", "")));
            // A platform only named as where the game came from ("PS1 Remake", "SNES port"), repacks for another
            // console (Virtual Console, PS2 Classics) and Arcade Archives of a NeoGeo game aren't the release's platform
            var isOrigin = RepackMarkers.IsMatch(text)
                || (matches.Count > 0 && matches.All(IsOriginNote))
                || (title.Pattern != null && Vendor(title.Folder) == "Arcade" && ArcadePort.IsMatch(text));
            var catId = result.Categories?.Select(c => c.Id).FirstOrDefault(CategoryToPlatform.ContainsKey) ?? 0;
            var consoleCategory = result.Categories?.Any(c => c.Id >= 1000 && c.Id < 2000) == true;
            // A console and a PC category on the same release contradict each other, only an explicit tag decides
            var conflicting = consoleCategory && result.Categories!.Any(c => c.Id >= 4000 && c.Id < 5000);
            if (conflicting)
            {
                catId = 0;
            }

            if (catId != 0)
            {
                var platform = CategoryToPlatform[catId];
                // Console categories are coarse buckets (DS filed under Wii, 360 under Xbox), so an explicit
                // platform tag from the same vendor is the better answer
                if (isTag && catId < 2000
                    && !isOrigin
                    && !TitlePatterns.Any(p => p.Folder == platform.Folder && p.Pattern.IsMatch(text))
                    && Vendor(platform.Folder) is { } vendor && vendor == Vendor(title.Folder))
                {
                    platform = (title.Name, title.Folder);
                }
                result.DetectedPlatform = platform.Name;
                result.PlatformFolder = platform.Folder;
                return;
            }

            // Under a generic console category only a console tag counts, a PC group name (RUNE, RELOADED)
            // or a plain word would otherwise turn a console release into a confident wrong match
            var titleDecides = !consoleCategory
                || (conflicting && isTag && !isOrigin)
                || (isTag && !isOrigin && Vendor(title.Folder) is { } v && v != "Computer");
            if (title.Pattern != null && titleDecides)
            {
                result.DetectedPlatform = title.Name;
                result.PlatformFolder = title.Folder;
                return;
            }

            // Nothing matched - mark as unknown so callers can route to review
            // instead of silently filing every unresolved release as PC.
            result.DetectedPlatform = "Unknown";
            result.PlatformFolder = "unknown";
        }

        private static readonly HashSet<string> PlainWords = new(StringComparer.OrdinalIgnoreCase)
        {
            "Wii", "NES", "Famicom", "Switch", "GameBoy", "PlayStation", "Vita", "Xbox", "Genesis", "Saturn", "Arcade"
        };

        private static readonly Regex RepackMarkers = new(@"\b(VC|Virtual\s*Console|WAD|WiiWare|(PS1|PSX|PS2|PSP)\s*Classics?)\b", RegexOptions.IgnoreCase);

        // Right after the platform tag: "PS1 Remake", "PS2 HD Remaster", "(SNES port)", plus the compatibility notes below
        private static readonly Regex OriginNote = new(
            @"^\W*((HD\W*)?(Remake|Remaster(ed)?|port)|(X\|S|X|S)?\W*(Backwards?\s*)?(compatible|BC))\b",
            RegexOptions.IgnoreCase);

        // "Xbox 360 compatible", "Xbox One Backward Compatible", "Xbox Series X compatible", "Switch 2 compatible"
        private static readonly Regex CompatNote = new(@"^\W*(X\|S|X|S)?\W*(Backwards?\s*)?(compatible|BC)\b", RegexOptions.IgnoreCase);

        private static readonly Regex ArcadePort = new(@"\b(ACA|Arcade\s*Archives)\b", RegexOptions.IgnoreCase);

        private static string? Vendor(string folder) =>
            PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.FolderName == folder)?.Category;

        public static List<(string Name, string Folder)> GetAllPlatforms()
        {
            return PlatformDefinitions.AllPlatforms
                .Where(p => p.Enabled)
                .Select(p => (p.Name, p.FolderName))
                .OrderBy(p => p.Name)
                .ToList();
        }

        public static string? GetPlatformFolder(string platformName)
        {
            var platform = PlatformDefinitions.AllPlatforms
                .FirstOrDefault(p => p.Name.Equals(platformName, StringComparison.OrdinalIgnoreCase) ||
                                     p.Slug.Equals(platformName, StringComparison.OrdinalIgnoreCase));
            return platform?.FolderName;
        }
    }
}
