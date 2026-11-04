using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Enumeration;
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

        private static readonly EnumerationOptions _anyCase = new() { MatchCasing = MatchCasing.CaseInsensitive };

        // Descriptors are mostly written on Windows: backslashes, and names in any case. A name in another
        // case is looked for one folder at a time, and only below the descriptor's folder; the file itself in the
        // folder's listing when one is given.
        private static string? FindFile(string dir, string reference, Dictionary<string, ILookup<string, string>>? listings)
        {
            var relative = reference.Replace('\\', Path.DirectorySeparatorChar);
            var path = Path.Combine(dir, relative);
            if (File.Exists(path)) return path;

            var names = relative.Split(Path.DirectorySeparatorChar, StringSplitOptions.RemoveEmptyEntries).Where(n => n != ".").ToList();
            if (Path.IsPathRooted(relative) || names.Count == 0 || names.Contains("..") || !Directory.Exists(dir)) return null;
            try
            {
                string? found = dir;
                for (var i = 0; i < names.Count && found != null; i++)
                {
                    var name = names[i];
                    var entries = i < names.Count - 1 ? Directory.EnumerateDirectories(found, name, _anyCase)
                        : listings == null ? Directory.EnumerateFiles(found, name, _anyCase)
                        : Listing(found, listings)[Path.GetFileNameWithoutExtension(name)].Where(f => FileSystemName.MatchesSimpleExpression(name, Path.GetFileName(f), ignoreCase: true));
                    found = entries.FirstOrDefault(e => Path.GetFileName(e).Equals(name, StringComparison.OrdinalIgnoreCase));
                }
                return found;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { return null; }
        }

        public static FileSet Resolve(string primaryPath) => Resolve(primaryPath, null);

        // listings: folders read once for the same-stem files of every primary resolved with it, so resolving
        // each file of a big folder does not read the whole folder each time
        public static FileSet Resolve(string primaryPath, Dictionary<string, ILookup<string, string>>? listings)
        {
            if (string.IsNullOrEmpty(primaryPath))
                return new FileSet { PrimaryFile = primaryPath, Type = FileSetType.Single };

            var ext = Path.GetExtension(primaryPath).ToLowerInvariant();

            return ext switch
            {
                ".cue" => ResolveCueBin(primaryPath, listings),
                ".gdi" => ResolveGdi(primaryPath, listings),
                ".m3u" => ResolveM3U(primaryPath, listings),
                _ => ResolveSingleWithCompanions(primaryPath, listings)
            };
        }

        // Directory.GetFiles(dir, stem + ".*", _anyCase), from the folder read once when listings is given
        private static IEnumerable<string> SameStem(string dir, string stem, Dictionary<string, ILookup<string, string>>? listings)
        {
            if (listings == null) return Directory.GetFiles(dir, stem + ".*", _anyCase);
            var pattern = stem + ".*";
            return Listing(dir, listings)[stem].Where(f => FileSystemName.MatchesSimpleExpression(pattern, Path.GetFileName(f), ignoreCase: true));
        }

        // The folder's files by stem, read once
        private static ILookup<string, string> Listing(string dir, Dictionary<string, ILookup<string, string>> listings)
        {
            if (!listings.TryGetValue(dir, out var byStem))
                listings[dir] = byStem = Directory.GetFiles(dir, "*", _anyCase).ToLookup(f => Path.GetFileNameWithoutExtension(f), StringComparer.OrdinalIgnoreCase);
            return byStem;
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

        private static FileSet ResolveCueBin(string cuePath, Dictionary<string, ILookup<string, string>>? listings)
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
                        var found = FindFile(dir, match.Groups[1].Value, listings);
                        if (found != null && !found.Equals(cuePath, StringComparison.OrdinalIgnoreCase))
                        {
                            set.CompanionFiles.Add(Path.GetFullPath(found));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[FileSetResolver] Warning: Could not parse CUE '{cuePath}': {ex.Message}");
            }

            // Fallback when the CUE is empty or its FILE refs don't resolve:
            // sibling files with the same stem are tracks of the same disc.
            if (set.CompanionFiles.Count == 0 && Directory.Exists(dir))
            {
                var stem = Path.GetFileNameWithoutExtension(cuePath);
                if (!string.IsNullOrEmpty(stem))
                {
                    foreach (var sibling in SameStem(dir, stem, listings))
                    {
                        if (sibling.Equals(cuePath, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!Path.GetFileNameWithoutExtension(sibling).Equals(stem, StringComparison.OrdinalIgnoreCase)) continue;
                        set.CompanionFiles.Add(Path.GetFullPath(sibling));
                    }
                }
            }

            return set;
        }

        private static FileSet ResolveGdi(string gdiPath, Dictionary<string, ILookup<string, string>>? listings)
        {
            var set = new FileSet { PrimaryFile = gdiPath, Type = FileSetType.GDI };
            var dir = Path.GetDirectoryName(gdiPath) ?? string.Empty;

            try
            {
                var lines = File.ReadAllLines(gdiPath);
                foreach (var line in lines)
                {
                    // Fifth field is the track file, quoted when it has spaces (Redump)
                    var match = Regex.Match(line, @"^\s*(?:\S+\s+){4}(?:""([^""]+)""|(\S+))");
                    if (!match.Success) continue;

                    var found = FindFile(dir, match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value, listings);
                    if (found != null)
                    {
                        set.CompanionFiles.Add(Path.GetFullPath(found));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[FileSetResolver] Warning: Could not parse GDI '{gdiPath}': {ex.Message}");
            }

            return set;
        }

        private static FileSet ResolveM3U(string m3uPath, Dictionary<string, ILookup<string, string>>? listings)
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

                    // A listed playlist is not a disc, and following it (or this one) never ends
                    var found = FindFile(dir, trimmed, listings);
                    if (found == null || Path.GetExtension(found).Equals(".m3u", StringComparison.OrdinalIgnoreCase)) continue;

                    var referencedSet = Resolve(found, listings);
                    set.CompanionFiles.Add(Path.GetFullPath(found));
                    foreach (var companion in referencedSet.CompanionFiles)
                    {
                        if (!set.CompanionFiles.Contains(companion, StringComparer.OrdinalIgnoreCase))
                            set.CompanionFiles.Add(companion);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[FileSetResolver] Warning: Could not parse M3U '{m3uPath}': {ex.Message}");
            }

            return set;
        }

        // Disc-image primaries. Same-stem siblings are part of the same disc.
        // Single-file ROMs (.smc, .nes, .z64, .gb, .gba) are not listed here.
        private static readonly HashSet<string> _multiFileDiscExts = new(StringComparer.OrdinalIgnoreCase)
        {
            ".cue", ".bin", ".iso", ".gdi", ".m3u", ".toc",
            ".mds", ".mdf", ".ccd", ".img", ".sub", ".sbi"
        };

        private static FileSet ResolveSingleWithCompanions(string filePath, Dictionary<string, ILookup<string, string>>? listings)
        {
            var set = new FileSet { PrimaryFile = filePath, Type = FileSetType.Single };
            var dir = Path.GetDirectoryName(filePath) ?? string.Empty;
            var stem = Path.GetFileNameWithoutExtension(filePath);
            var primaryExt = Path.GetExtension(filePath);

            // Disc-image primary: claim same-stem siblings.
            if (_multiFileDiscExts.Contains(primaryExt) && Directory.Exists(dir) && !string.IsNullOrEmpty(stem))
            {
                foreach (var sibling in SameStem(dir, stem, listings))
                {
                    if (sibling.Equals(filePath, StringComparison.OrdinalIgnoreCase)) continue;
                    if (!Path.GetFileNameWithoutExtension(sibling).Equals(stem, StringComparison.OrdinalIgnoreCase)) continue;
                    set.CompanionFiles.Add(Path.GetFullPath(sibling));
                }
                return set;
            }

            var companionExts = new[] { ".sub", ".sbi", ".ccd", ".img" };
            foreach (var ext in companionExts)
            {
                var companion = Path.Combine(dir, stem + ext);
                if (File.Exists(companion) && !companion.Equals(filePath, StringComparison.OrdinalIgnoreCase))
                {
                    set.CompanionFiles.Add(companion);
                }
            }

            return set;
        }
    }
}
