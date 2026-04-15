using System;
using System.Text.RegularExpressions;

namespace RetroArr.Core.Configuration
{
    public class MediaSettings
    {
        public string FolderPath { get; set; } = string.Empty;
        public string DownloadPath { get; set; } = string.Empty;
        public string DestinationPath { get; set; } = string.Empty;
        public string WinePrefixPath { get; set; } = string.Empty;
        public string BiosPath { get; set; } = string.Empty;

        // Deleted game files land here instead of being wiped immediately.
        // Empty = use <configDir>/trash as the default.
        public string TrashPath { get; set; } = string.Empty;

        // 0 = never auto-purge (manual-empty only). Positive = days before
        // background service purges an entry.
        public int TrashRetentionDays { get; set; } = 14;

        // Days a game keeps its Missing flag before the scanner drops the DB
        // row. 0 = never auto-purge (keep flagged forever, user decides).
        public int MissingRetentionDays { get; set; } = 14;

        public string Platform { get; set; } = "default";
        
        /// <summary>
        /// Controls platform folder naming convention.
        /// "native" = RetroArr defaults, "retrobat" = RetroBat-compatible, "batocera" = Batocera-compatible
        /// </summary>
        public string FolderNamingMode { get; set; } = "native";
        
        /// <summary>
        /// GOG downloads folder path pattern. Defaults to {Library}/gog/downloads
        /// </summary>
        public string GogDownloadsPath { get; set; } = "gog/downloads";
        
        /// <summary>
        /// Path pattern with variables: {Platform}, {Title}, {Year}
        /// Example: /library/{Platform}/{Title}
        /// </summary>
        public string DestinationPathPattern { get; set; } = "{Platform}/{Title}";
        
        /// <summary>
        /// Whether to use the pattern-based path for organizing downloads
        /// </summary>
        public bool UseDestinationPattern { get; set; } = true;
        
        public bool IsConfigured => !string.IsNullOrWhiteSpace(FolderPath);

        /// <summary>
        /// Resolves the destination path pattern with actual values
        /// </summary>
        public string ResolveDestinationPath(string baseFolder, string? platform, string? title, int? year = null)
        {
            if (!UseDestinationPattern || string.IsNullOrEmpty(DestinationPathPattern))
            {
                return !string.IsNullOrEmpty(DestinationPath) ? DestinationPath : baseFolder;
            }

            var resolvedPath = DestinationPathPattern;
            
            // Replace variables
            resolvedPath = resolvedPath.Replace("{Platform}", SanitizePath(platform ?? "unknown"));
            resolvedPath = resolvedPath.Replace("{Title}", SanitizePath(title ?? "Unknown"));
            resolvedPath = resolvedPath.Replace("{Year}", year?.ToString() ?? "");
            
            // Clean up any double slashes or trailing slashes
            resolvedPath = Regex.Replace(resolvedPath, @"[/\\]+", System.IO.Path.DirectorySeparatorChar.ToString());
            resolvedPath = resolvedPath.Trim(System.IO.Path.DirectorySeparatorChar);

            return System.IO.Path.Combine(baseFolder, resolvedPath);
        }

        /// <summary>
        /// Resolves the GOG downloads path. Creates: {Library}/gog/downloads/{GameTitle}
        /// </summary>
        public string ResolveGogDownloadPath(string? gameTitle = null)
        {
            var basePath = !string.IsNullOrEmpty(FolderPath) ? FolderPath : DestinationPath;
            if (string.IsNullOrEmpty(basePath)) return string.Empty;

            var gogPath = System.IO.Path.Combine(basePath, GogDownloadsPath);
            
            if (!string.IsNullOrEmpty(gameTitle))
            {
                gogPath = System.IO.Path.Combine(gogPath, SanitizePath(gameTitle));
            }

            return gogPath;
        }

        private static string SanitizePath(string input)
        {
            if (string.IsNullOrEmpty(input)) return "unknown";
            
            // Remove invalid path characters
            var invalid = System.IO.Path.GetInvalidFileNameChars();
            foreach (var c in invalid)
            {
                input = input.Replace(c.ToString(), "");
            }
            return input.Trim();
        }
    }
}
