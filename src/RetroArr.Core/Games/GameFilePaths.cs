using System;
using System.IO;

namespace RetroArr.Core.Games
{
    // Where the GameFiles rows of a game point. RelativePath is relative to the game's folder, or to the folder a
    // single-file game lies in, with '/' between folders; a file outside that folder is stored by its full path.
    public static class GameFilePaths
    {
        // The folder the game's rows are relative to; null for a game without a path
        public static string? FilesFolder(string? gamePath) =>
            string.IsNullOrEmpty(gamePath) ? null : File.Exists(gamePath) ? Path.GetDirectoryName(Path.GetFullPath(gamePath)) : gamePath;

        // What a row of the game stores for the file
        public static string StoredPath(string? gamePath, string file) => StoredPathIn(FilesFolder(gamePath), file);

        // The full path a row of the game points at; null when it is relative and the game has no path
        public static string? Resolve(string? gamePath, string storedPath) => ResolveIn(FilesFolder(gamePath), storedPath);

        // The same for many files of one game, with the folder FilesFolder gave
        public static string StoredPathIn(string? folder, string file)
        {
            var path = folder == null ? null : Path.GetRelativePath(folder, file);
            if (path == null || Path.IsPathRooted(path) || path.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                path = Path.GetFullPath(file);
            return path.Replace(Path.DirectorySeparatorChar, '/');
        }

        public static string? ResolveIn(string? folder, string storedPath)
        {
            if (Path.IsPathRooted(storedPath)) return Path.GetFullPath(storedPath);
            return folder == null ? null : Path.GetFullPath(Path.Combine(folder, storedPath));
        }
    }
}
