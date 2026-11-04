using System;
using System.IO;

namespace RetroArr.Core.IO
{
    public static class PathGuard
    {
        // A drive or filesystem root, or a system folder: never a game's folder, never deleted
        public static bool IsCriticalPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var full = Path.GetFullPath(path);
            var root = Path.GetPathRoot(full) ?? string.Empty;
            if (full.Length <= root.Length) return true;
            full = full.TrimEnd(Path.DirectorySeparatorChar);

            var sensitive = new[]
            {
                "/", "/bin", "/boot", "/dev", "/etc", "/home", "/lib", "/proc", "/root",
                "/run", "/sbin", "/sys", "/tmp", "/usr", "/var", "/Users",
                "C:\\", "C:\\Windows", "C:\\Program Files", "C:\\Users"
            };

            foreach (var s in sensitive)
            {
                if (full.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
