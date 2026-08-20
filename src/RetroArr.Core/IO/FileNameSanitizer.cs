using System;
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
        // A maximal run of Windows-reserved chars (< > : " / \ | ? *) or control
        // chars, together with any surrounding spaces, becomes a " - " separator.
        // Doing it as one run keeps "A:B" -> "A - B" and "a<>b" -> "a - b" (not a
        // double dash), and — because we only eat spaces *adjacent to an illegal
        // char* — a legitimate hyphen like "Spider-Man" is left untouched.
        private static readonly Regex IllegalRun = new(@"\s*[<>:""/\\|?*\x00-\x1F]+\s*", RegexOptions.Compiled);
        private static readonly Regex MultiSpace = new(@"\s{2,}", RegexOptions.Compiled);

        private static readonly System.Collections.Generic.HashSet<string> ReservedNames =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "CON", "PRN", "AUX", "NUL",
                "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9",
                "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9"
            };

        // Sanitize a single path segment (file or folder name, no separators).
        // Illegal characters become a " - " separator, so
        // "Diablo II: Resurrected" -> "Diablo II - Resurrected".
        public static string Sanitize(string? input, string fallback = "unknown")
        {
            if (string.IsNullOrWhiteSpace(input)) return fallback;

            // Replace every run of illegal characters with a readable " - " separator
            // ("Diablo II: Resurrected" -> "Diablo II - Resurrected").
            var result = IllegalRun.Replace(input, " - ");
            result = MultiSpace.Replace(result, " ").Trim();
            // Strip separators/space/dots that ended up leading or trailing.
            result = result.Trim('-', ' ', '.').Trim();

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
