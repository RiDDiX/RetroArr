using System;
using System.IO;
using System.Text;
using RetroArr.Core.Games;

namespace RetroArr.Core.Linux
{
    public class LutrisExportService
    {
        /// <summary>
        /// Generates a Lutris installer YAML for a given game.
        /// Format follows https://github.com/lutris/lutris/blob/master/docs/installers.rst
        /// </summary>
        public string GenerateInstallerYaml(Game game, string? runnerOverride = null)
        {
            if (game == null) throw new ArgumentNullException(nameof(game));

            var title = SanitizeYamlString(game.Title ?? "Unknown Game");
            var slug = GenerateSlug(game.Title ?? "unknown-game");
            var execPath = game.ExecutablePath ?? "";
            var workDir = string.IsNullOrEmpty(execPath) ? "" : Path.GetDirectoryName(execPath) ?? "";

            var runner = DetermineRunner(execPath, runnerOverride);

            var sb = new StringBuilder();
            sb.AppendLine($"name: {title}");
            sb.AppendLine($"game_slug: {slug}");
            sb.AppendLine($"version: RetroArr Export");
            sb.AppendLine($"slug: {slug}-retroarr");
            sb.AppendLine($"runner: {runner}");
            sb.AppendLine();
            sb.AppendLine("game:");

            if (runner == "linux")
            {
                sb.AppendLine($"  exe: {execPath}");
                if (!string.IsNullOrEmpty(workDir))
                    sb.AppendLine($"  working_dir: {workDir}");
            }
            else if (runner == "wine")
            {
                sb.AppendLine($"  exe: {execPath}");
                if (!string.IsNullOrEmpty(workDir))
                    sb.AppendLine($"  prefix: {workDir}");
            }
            else if (runner == "steam")
            {
                sb.AppendLine($"  appid: {game.SteamId}");
            }

            return sb.ToString();
        }

        public static string DetermineRunner(string executablePath, string? runnerOverride)
        {
            if (!string.IsNullOrEmpty(runnerOverride))
            {
                return runnerOverride.ToLowerInvariant() switch
                {
                    "wine" => "wine",
                    "proton" => "steam", // Proton games launch through Steam
                    "linux" or "native" => "linux",
                    "steam" => "steam",
                    _ => "wine"
                };
            }

            if (string.IsNullOrEmpty(executablePath))
                return "linux";

            var ext = Path.GetExtension(executablePath);
            if (ext.Equals(".exe", StringComparison.OrdinalIgnoreCase) ||
                ext.Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                return "wine";
            }

            return "linux";
        }

        public static string GenerateSlug(string title)
        {
            if (string.IsNullOrEmpty(title)) return "unknown";

            var slug = title.ToLowerInvariant();
            var sb = new StringBuilder();
            foreach (var c in slug)
            {
                if (char.IsLetterOrDigit(c))
                    sb.Append(c);
                else if (c == ' ' || c == '-' || c == '_')
                    sb.Append('-');
            }

            // Collapse multiple dashes
            var result = sb.ToString();
            while (result.Contains("--"))
                result = result.Replace("--", "-");

            return result.Trim('-');
        }

        private static string SanitizeYamlString(string input)
        {
            // Escape characters that could break YAML
            if (input.Contains(':') || input.Contains('#') || input.Contains('\'') || input.Contains('"'))
            {
                return $"\"{input.Replace("\"", "\\\"")}\"";
            }
            return input;
        }
    }
}
