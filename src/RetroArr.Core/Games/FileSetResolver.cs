using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace RetroArr.Core.Games
{
    public class FileSet
    {
        public string PrimaryFile { get; set; } = string.Empty;
        public FileSetType Type { get; set; }
        public List<string> CompanionFiles { get; set; } = new();

        public IEnumerable<string> AllFiles
        {
            get
            {
                yield return PrimaryFile;
                foreach (var f in CompanionFiles) yield return f;
            }
        }
    }

    public enum FileSetType
    {
        Single,
        CueBin,
        M3U,
        GDI,
        FolderROM
    }

    public static class FileSetResolver
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMedia);

        public static FileSet Resolve(string primaryPath)
        {
            if (string.IsNullOrEmpty(primaryPath))
                return new FileSet { PrimaryFile = primaryPath, Type = FileSetType.Single };

            var ext = Path.GetExtension(primaryPath).ToLowerInvariant();

            return ext switch
            {
                ".cue" => ResolveCueBin(primaryPath),
                ".gdi" => ResolveGdi(primaryPath),
                ".m3u" => ResolveM3U(primaryPath),
                _ => ResolveSingleWithCompanions(primaryPath)
            };
        }

        public static List<FileSet> ResolveDirectory(string directoryPath)
        {
            if (!Directory.Exists(directoryPath))
                return new List<FileSet>();

            var sets = new List<FileSet>();
            var claimed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var files = Directory.GetFiles(directoryPath)
                .OrderBy(f => Path.GetExtension(f).ToLowerInvariant() switch
                {
                    ".m3u" => 0,
                    ".cue" => 1,
                    ".gdi" => 2,
                    _ => 3
                })
                .ToList();

            foreach (var file in files)
            {
                if (claimed.Contains(file)) continue;

                var set = Resolve(file);
                sets.Add(set);
                foreach (var f in set.AllFiles)
                    claimed.Add(f);
            }

            return sets;
        }

        private static FileSet ResolveCueBin(string cuePath)
        {
            var set = new FileSet { PrimaryFile = cuePath, Type = FileSetType.CueBin };
            var dir = Path.GetDirectoryName(cuePath) ?? string.Empty;

            try
            {
                var lines = File.ReadAllLines(cuePath);
                foreach (var line in lines)
                {
                    var match = Regex.Match(line, @"FILE\s+""([^""]+)""", RegexOptions.IgnoreCase);
                    if (!match.Success)
                        match = Regex.Match(line, @"FILE\s+(\S+)\s+", RegexOptions.IgnoreCase);

                    if (match.Success)
                    {
                        var referenced = match.Groups[1].Value;
                        var fullPath = Path.IsPathRooted(referenced)
                            ? referenced
                            : Path.Combine(dir, referenced);

                        if (File.Exists(fullPath) && !fullPath.Equals(cuePath, StringComparison.OrdinalIgnoreCase))
                        {
                            set.CompanionFiles.Add(Path.GetFullPath(fullPath));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[FileSetResolver] Warning: Could not parse CUE '{cuePath}': {ex.Message}");
            }

            // Fallback when the CUE was empty, unparseable, or pointed at a path
            // that no longer resolves on disk (renamed .bin, copied folder, etc.).
            // Anything in the same directory that shares the cue's exact stem is
            // a track of the same disc — claim it as a companion so the scanner
            // doesn't promote it to a second game entry.
            if (set.CompanionFiles.Count == 0 && Directory.Exists(dir))
            {
                var stem = Path.GetFileNameWithoutExtension(cuePath);
                if (!string.IsNullOrEmpty(stem))
                {
                    foreach (var sibling in Directory.GetFiles(dir, stem + ".*"))
                    {
                        if (sibling.Equals(cuePath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!Path.GetFileNameWithoutExtension(sibling).Equals(stem, StringComparison.OrdinalIgnoreCase)) continue;
                        set.CompanionFiles.Add(Path.GetFullPath(sibling));
                    }
                }
            }

            return set;
        }

        private static FileSet ResolveGdi(string gdiPath)
        {
            var set = new FileSet { PrimaryFile = gdiPath, Type = FileSetType.GDI };
            var dir = Path.GetDirectoryName(gdiPath) ?? string.Empty;

            try
            {
                var lines = File.ReadAllLines(gdiPath);
                foreach (var line in lines)
                {
                    var parts = line.Trim().Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 5)
                    {
                        var trackFile = parts[4];
                        var fullPath = Path.Combine(dir, trackFile);
                        if (File.Exists(fullPath))
                        {
                            set.CompanionFiles.Add(Path.GetFullPath(fullPath));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[FileSetResolver] Warning: Could not parse GDI '{gdiPath}': {ex.Message}");
            }

            return set;
        }

        private static FileSet ResolveM3U(string m3uPath)
        {
            var set = new FileSet { PrimaryFile = m3uPath, Type = FileSetType.M3U };
            var dir = Path.GetDirectoryName(m3uPath) ?? string.Empty;

            try
            {
                var lines = File.ReadAllLines(m3uPath);
                foreach (var line in lines)
                {
                    var trimmed = line.Trim();
                    if (string.IsNullOrEmpty(trimmed) || trimmed.StartsWith("#")) continue;

                    var fullPath = Path.IsPathRooted(trimmed)
                        ? trimmed
                        : Path.Combine(dir, trimmed);

                    if (File.Exists(fullPath))
                    {
                        var referencedSet = Resolve(fullPath);
                        set.CompanionFiles.Add(Path.GetFullPath(fullPath));
                        foreach (var companion in referencedSet.CompanionFiles)
                        {
                            if (!set.CompanionFiles.Contains(companion, StringComparer.OrdinalIgnoreCase))
                                set.CompanionFiles.Add(companion);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[FileSetResolver] Warning: Could not parse M3U '{m3uPath}': {ex.Message}");
            }

            return set;
        }

        private static FileSet ResolveSingleWithCompanions(string filePath)
        {
            var set = new FileSet { PrimaryFile = filePath, Type = FileSetType.Single };
            var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            var nameWithoutExt = Path.GetFileNameWithoutExtension(filePath);

            var companionExts = new[] { ".sub", ".sbi", ".ccd", ".img" };
            foreach (var ext in companionExts)
            {
                var companion = Path.Combine(dir, nameWithoutExt + ext);
                if (File.Exists(companion) && !companion.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    set.CompanionFiles.Add(companion);
                }
            }

            return set;
        }
    }
}
