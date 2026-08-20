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
        
        // "native" | "retrobat" | "batocera"
        public string FolderNamingMode { get; set; } = "native";

        // relative to FolderPath, so default resolves to {Library}/gog/downloads
        public string GogDownloadsPath { get; set; } = "gog/downloads";

        // variables: {Platform}, {Title}, {Year}
        public string DestinationPathPattern { get; set; } = "{Platform}/{Title}";

        public bool UseDestinationPattern { get; set; } = true;

        // ---- File renaming on import ----
        // Off by default to keep existing installs unchanged. Once enabled,
        // newly imported files (PostDownloadProcessor path) get a canonical
        // name driven by the templates below. Existing files stay untouched.
        public bool RenameOnImport { get; set; }

        // Templates for the canonical filename, without extension.
        // Variables: {Title}, {Year}, {Version}, {ContentName}, {ReleaseGroup}, {Region}, {Platform}
        // Empty variables are dropped together with their surrounding
        // " - " separators by the renamer, so a missing ContentName leaves
        // a clean "{Title} - DLC" instead of "{Title} - DLC - ".
        public string MainFileTemplate { get; set; } = "{Title}";
        public string UpdateFileTemplate { get; set; } = "{Title} - Update {Version}";
        public string DlcFileTemplate { get; set; } = "{Title} - DLC - {ContentName}";

        // When true, the configured ReleaseGroupSuffix is appended to ANY of
        // the templates if a group was detected. Default off because most
        // users want a clean library, not "Chrono Trigger [No-Intro].zip".
        public bool IncludeReleaseGroupInFilename { get; set; }
        public string ReleaseGroupSuffix { get; set; } = "[{ReleaseGroup}]";

        // Auto-rename is SCOPED to desktop platforms by default. Console and
        // handheld platforms (Switch, PS4/5, PS Vita, Wii etc.) embed
        // load-bearing TitleID/version markers in their filenames that
        // emulators parse - blindly renaming those breaks the games. CSV
        // of Platform.Slug values that the renamer is allowed to touch.
        public string ApplyRenameToPlatforms { get; set; } = "windows,pc,linux,macintosh";

        // What to do when the target filename already exists on disk:
        //   "Skip"      - leave the source alone, log it (safest default)
        //   "Overwrite" - replace target (caller should know what they want)
        //   "Suffix"    - append " (1)", " (2)" until unique
        public string FileConflictBehavior { get; set; } = "Skip";

        public bool IsConfigured => !string.IsNullOrWhiteSpace(FolderPath);

        // Splits the platform CSV into a normalized, lowercase set for
        // membership checks. Cached for the rare case this is hot.
        public System.Collections.Generic.HashSet<string> GetRenameTargetPlatformSlugs()
        {
            var set = new System.Collections.Generic.HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(ApplyRenameToPlatforms)) return set;
            foreach (var raw in ApplyRenameToPlatforms.Split(new[] { ',', ';', '|' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var trimmed = raw.Trim();
                if (trimmed.Length > 0) set.Add(trimmed);
            }
            return set;
        }

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

        // resolves to {Library}/gog/downloads/{GameTitle}
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
            // Always strip the Windows-reserved set (not just the runtime's, which on
            // Linux is only '/'), so folder names stay valid on SMB/NTFS shares.
            return RetroArr.Core.IO.FileNameSanitizer.Sanitize(input, "unknown");
        }
    }
}
