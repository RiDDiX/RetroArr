using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Games
{
    // Where a path lies in the library (MediaSettings.FolderPath and DestinationPath)
    public static class LibraryPaths
    {
        public static List<string> Roots(MediaSettings settings) => MediaScannerService.LibraryRoots(settings);

        // The part of the path below the library root it lies in (the deepest one); null outside every root
        public static string? BelowRoot(string? path, IReadOnlyList<string> roots)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                var full = Path.GetFullPath(path);
                var root = roots.Where(r => full.Equals(r, StringComparison.OrdinalIgnoreCase)
                        || full.StartsWith(Path.EndsInDirectorySeparator(r) ? r : r + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    .OrderByDescending(r => r.Length)
                    .FirstOrDefault();
                return root == null ? null : Path.GetRelativePath(root, full);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
            {
                return null;
            }
        }
    }
}
