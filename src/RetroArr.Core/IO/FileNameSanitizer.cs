using System;
using System.Text;
using System.Text.RegularExpressions;

namespace RetroArr.Core.IO
{
    // Cross-platform-safe file/folder name sanitizer.
    //
    // RetroArr runs on Linux (Docker), where Path.GetInvalidFileNameChars() only
    // returns '/' and NUL. But libraries usually live on SMB / NTFS / exFAT shares
    // that forbid the Windows-reserved set (< > : " / \ | ? * and control chars) and
    // trailing dots/spaces. Names built with the runtime's invalid-char list therefore
    // keep e.g. the ':' in "Diablo II: Resurrected", which Windows then mangles into an
    // 8.3 short name like "DTWOE3~0". So we always strip the Windows superset, making
    // names portable regardless of where the library is mounted.
    public static class FileNameSanitizer
    {
        // < > : " / \ | ? *  (plus control chars 0-31 handled below)
        private const string WindowsReserved = "<>:\"/\\|?*";
        private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

        private static readonly System.Collections.Generic.HashSet<string> ReservedNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };

        // Sanitize a single path segment (file or folder name, no separators).
        // Illegal characters are replaced with a space and collapsed, so
        // "Diablo II: Resurrected" -> "Diablo II Resurrected".
        public static string Sanitize(string? input, string fallback = "unknown")
        {
            if (string.IsNullOrWhiteSpace(input)) return fallback;

            var sb = new StringBuilder(input.Length);
            foreach (var ch in input)
            {
                if (ch < 32 || WindowsReserved.IndexOf(ch) >= 0)
                    sb.Append(' ');
                else
                    sb.Append(ch);
            }

            var result = MultiSpace.Replace(sb.ToString(), " ").Trim();
            // Windows forbids trailing dots and spaces on a name.
            result = result.TrimEnd('.', ' ').Trim();

            if (result.Length == 0) return fallback;

            // Avoid reserved device names (compared without extension).
            var withoutExt = result;
            var dot = result.IndexOf('.');
            if (dot > 0) withoutExt = result.Substring(0, dot);
            if (ReservedNames.Contains(withoutExt)) result = "_" + result;

            return result;
        }
    }
}
