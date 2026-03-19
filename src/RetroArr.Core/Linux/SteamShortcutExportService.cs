using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using RetroArr.Core.Games;

namespace RetroArr.Core.Linux
{
    public class SteamShortcutData
    {
        public string AppName { get; set; } = string.Empty;
        public string Exe { get; set; } = string.Empty;
        public string StartDir { get; set; } = string.Empty;
        public string Icon { get; set; } = string.Empty;
        public string LaunchOptions { get; set; } = string.Empty;
        public bool IsHidden { get; set; }
        public bool AllowDesktopConfig { get; set; } = true;
        public bool AllowOverlay { get; set; } = true;
        public List<string> Tags { get; set; } = new List<string>();
    }

    public class SteamShortcutExportService
    {
        /// <summary>
        /// Generates Steam shortcut data for a non-Steam game.
        /// This can be used to add games to Steam's library (visible in Game Mode on Steam Deck).
        /// The output is a JSON-serializable object; actual VDF binary writing requires
        /// the companion script (see docs/LINUX_GAMING.md).
        /// </summary>
        public SteamShortcutData GenerateShortcut(Game game, string? launchOptions = null)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            var execPath = game.ExecutablePath ?? "";
            var startDir = string.IsNullOrEmpty(execPath) ? "" : Path.GetDirectoryName(execPath) ?? "";
            var isWindowsExe = execPath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            var tags = new List<string> { "RetroArr" };
            if (game.Platform != null && !string.IsNullOrEmpty(game.Platform.Name))
            {
                tags.Add(game.Platform.Name);
            }

            var options = launchOptions ?? "";
            if (string.IsNullOrEmpty(options) && isWindowsExe)
            {
                // Suggest Proton launch via Steam compatibility
                options = "STEAM_COMPAT_DATA_PATH=\"%command%\" %command%";
            }

            return new SteamShortcutData
            {
                AppName = game.Title ?? "Unknown Game",
                Exe = QuotePath(execPath),
                StartDir = QuotePath(startDir),
                Icon = game.Images?.CoverUrl ?? "",
                LaunchOptions = options,
                Tags = tags,
                AllowDesktopConfig = true,
                AllowOverlay = true
            };
        }

        /// <summary>
        /// Generates shortcut data for multiple games at once (bulk export).
        /// </summary>
        public List<SteamShortcutData> GenerateShortcuts(IEnumerable<Game> games)
        {
            var shortcuts = new List<SteamShortcutData>();
            foreach (var game in games)
            {
                if (!string.IsNullOrEmpty(game.ExecutablePath))
                {
                    shortcuts.Add(GenerateShortcut(game));
                }
            }
            return shortcuts;
        }

        /// <summary>
        /// Returns the typical Steam shortcut paths on Linux for user discovery.
        /// </summary>
        public static string[] GetSteamShortcutPaths()
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return new[]
            {
                Path.Combine(home, ".steam", "steam", "userdata"),
                Path.Combine(home, ".local", "share", "Steam", "userdata")
            };
        }

        private static string QuotePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return "\"\"";
            return $"\"{path}\"";
        }
    }
}
