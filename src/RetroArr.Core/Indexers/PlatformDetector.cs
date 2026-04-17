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
            { 1000, ("Console", "console") },
            { 1010, ("Nintendo DS", "nds") },
            { 1020, ("PSP", "psp") },
            { 1030, ("Wii", "wii") },
            { 1040, ("Xbox", "xbox") },
            { 1050, ("Xbox 360", "xbox360") },
            { 1060, ("WiiWare", "wii") },
            { 1070, ("Xbox 360", "xbox360") },
            { 1080, ("PlayStation 3", "ps3") },
            { 1090, ("Other", "other") },
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
            (new Regex(@"\b(NSW|NSP|XCI|Switch)\b", RegexOptions.IgnoreCase), "Nintendo Switch", "switch"),
            (new Regex(@"\bWii\s*U\b", RegexOptions.IgnoreCase), "Wii U", "wiiu"),
            (new Regex(@"\b(Wii|WBFS)\b(?!\s*U)", RegexOptions.IgnoreCase), "Wii", "wii"),
            (new Regex(@"\b(3DS|CIA|3DSX)\b", RegexOptions.IgnoreCase), "Nintendo 3DS", "3ds"),
            (new Regex(@"\b(NDS|Nintendo\s*DS)\b", RegexOptions.IgnoreCase), "Nintendo DS", "nds"),
            (new Regex(@"\b(GBA|Game\s*Boy\s*Advance)\b", RegexOptions.IgnoreCase), "Game Boy Advance", "gba"),
            (new Regex(@"\b(GBC|Game\s*Boy\s*Color)\b", RegexOptions.IgnoreCase), "Game Boy Color", "gbc"),
            (new Regex(@"\b(GB|Game\s*Boy)\b(?!\s*(Advance|Color))", RegexOptions.IgnoreCase), "Game Boy", "gb"),
            (new Regex(@"\b(N64|Nintendo\s*64)\b", RegexOptions.IgnoreCase), "Nintendo 64", "n64"),
            (new Regex(@"\b(SNES|Super\s*Nintendo)\b", RegexOptions.IgnoreCase), "SNES", "snes"),
            (new Regex(@"\b(NES|Famicom)\b(?!\s*Disk)", RegexOptions.IgnoreCase), "NES", "nes"),
            (new Regex(@"\bGameCube\b", RegexOptions.IgnoreCase), "GameCube", "gamecube"),
            
            // Sony
            (new Regex(@"\b(PS5|PlayStation\s*5)\b", RegexOptions.IgnoreCase), "PlayStation 5", "ps5"),
            (new Regex(@"\b(PS4|PlayStation\s*4)\b", RegexOptions.IgnoreCase), "PlayStation 4", "ps4"),
            (new Regex(@"\b(PS3|PlayStation\s*3)\b", RegexOptions.IgnoreCase), "PlayStation 3", "ps3"),
            (new Regex(@"\b(PS2|PlayStation\s*2)\b", RegexOptions.IgnoreCase), "PlayStation 2", "ps2"),
            (new Regex(@"\b(PSX|PS1|PlayStation(?!\s*[2-5]))\b", RegexOptions.IgnoreCase), "PlayStation 1", "psx"),
            (new Regex(@"\b(PSP|PlayStation\s*Portable)\b", RegexOptions.IgnoreCase), "PSP", "psp"),
            (new Regex(@"\b(Vita|PSVita|PS\s*Vita)\b", RegexOptions.IgnoreCase), "PS Vita", "vita"),
            
            // Microsoft
            (new Regex(@"\b(XSX|Xbox\s*Series)\b", RegexOptions.IgnoreCase), "Xbox Series X", "xboxseriesx"),
            (new Regex(@"\b(XONE|Xbox\s*One)\b", RegexOptions.IgnoreCase), "Xbox One", "xboxone"),
            (new Regex(@"\b(X360|Xbox\s*360)\b", RegexOptions.IgnoreCase), "Xbox 360", "xbox360"),
            (new Regex(@"\bXbox\b(?!\s*(One|360|Series))", RegexOptions.IgnoreCase), "Xbox", "xbox"),
            
            // Sega
            (new Regex(@"\bDreamcast\b", RegexOptions.IgnoreCase), "Dreamcast", "dreamcast"),
            (new Regex(@"\b(Saturn|Sega\s*Saturn)\b", RegexOptions.IgnoreCase), "Saturn", "saturn"),
            (new Regex(@"\b(Genesis|Mega\s*Drive)\b", RegexOptions.IgnoreCase), "Mega Drive", "megadrive"),
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
            // First try category-based detection
            if (result.Categories?.Count > 0)
            {
                foreach (var cat in result.Categories)
                {
                    if (CategoryToPlatform.TryGetValue(cat.Id, out var platform))
                    {
                        result.DetectedPlatform = platform.Name;
                        result.PlatformFolder = platform.Folder;
                        return;
                    }
                }
            }

            // Then try title-based detection
            foreach (var (pattern, name, folder) in TitlePatterns)
            {
                if (pattern.IsMatch(result.Title))
                {
                    result.DetectedPlatform = name;
                    result.PlatformFolder = folder;
                    return;
                }
            }

            // Default to PC if nothing detected
            result.DetectedPlatform = "PC";
            result.PlatformFolder = "windows";
        }

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
