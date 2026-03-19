using System;
using System.IO;
using System.Text;
using RetroArr.Core.Games;

namespace RetroArr.Core.Linux
{
    public class DesktopEntryExportService
    {
        /// <summary>
        /// Generates an XDG .desktop file for a game.
        /// Spec: https://specifications.freedesktop.org/desktop-entry-spec/latest/
        /// </summary>
        public string GenerateDesktopEntry(Game game, string? iconPath = null, string? runnerPrefix = null)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            var title = game.Title ?? "Unknown Game";
            var execPath = game.ExecutablePath ?? "";
            var workDir = string.IsNullOrEmpty(execPath) ? "" : Path.GetDirectoryName(execPath) ?? "";
            var icon = iconPath ?? game.Images?.CoverUrl ?? "applications-games";
            var comment = $"Launch {title} via RetroArr";

            var execLine = BuildExecLine(execPath, runnerPrefix);

            var categories = "Game;";
            if (game.Platform != null)
            {
                var cat = game.Platform.Category;
                if (!string.IsNullOrEmpty(cat))
                {
                    if (cat.Contains("Retro", StringComparison.OrdinalIgnoreCase))
                        categories = "Game;Emulator;";
                    else if (cat.Contains("PC", StringComparison.OrdinalIgnoreCase))
                        categories = "Game;";
                }
            }

            var sb = new StringBuilder();
            sb.AppendLine("[Desktop Entry]");
            sb.AppendLine("Type=Application");
            sb.AppendLine($"Name={EscapeDesktopValue(title)}");
            sb.AppendLine($"Comment={EscapeDesktopValue(comment)}");
            sb.AppendLine($"Exec={execLine}");
            sb.AppendLine($"Icon={icon}");
            sb.AppendLine($"Path={workDir}");
            sb.AppendLine($"Categories={categories}");
            sb.AppendLine("Terminal=false");
            sb.AppendLine("StartupNotify=true");

            return sb.ToString();
        }

        public static string BuildExecLine(string executablePath, string? runnerPrefix)
        {
            if (string.IsNullOrEmpty(executablePath))
                return "";

            var isWindowsExe = executablePath.EndsWith(".exe", StringComparison.OrdinalIgnoreCase);

            if (!string.IsNullOrEmpty(runnerPrefix))
            {
                return $"{runnerPrefix} \"{executablePath}\"";
            }

            if (isWindowsExe)
            {
                return $"wine \"{executablePath}\"";
            }

            return $"\"{executablePath}\"";
        }

        private static string EscapeDesktopValue(string value)
        {
            // Per XDG spec, backslash-escape special chars
            return value
                .Replace("\\", "\\\\")
                .Replace("\n", "\\n")
                .Replace("\t", "\\t")
                .Replace("\r", "\\r");
        }
    }
}
