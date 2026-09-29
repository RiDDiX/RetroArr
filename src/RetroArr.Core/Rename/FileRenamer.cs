using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Rename
{
    // Works out the names imported files get. PostDownloadProcessor lands them under
    // those names, so nothing here moves, replaces or deletes a file.
    public sealed class FileRenamer
    {
        private readonly TemplateRenderer _renderer;

        public FileRenamer(TemplateRenderer renderer)
        {
            _renderer = renderer;
        }

        // Emulators look romsets up by their set name.
        private static readonly HashSet<PlatformType> RomsetTypes = new()
        {
            PlatformType.Arcade, PlatformType.FinalBurnNeo, PlatformType.NeoGeo, PlatformType.CPS1, PlatformType.CPS2,
            PlatformType.CPS3, PlatformType.Daphne, PlatformType.MAMEHomebrew, PlatformType.NeoGeo64, PlatformType.CAVE,
            PlatformType.Zinc, PlatformType.Namco246, PlatformType.Gaelco,
            PlatformType.Naomi, PlatformType.Naomi2, PlatformType.Atomiswave, PlatformType.SegaSTV, PlatformType.SegaModel2, PlatformType.SegaModel3,
            PlatformType.Hikaru, PlatformType.SegaChihiro
        };

        // Installers and program folders find their files by name.
        private static readonly HashSet<PlatformType> SoftwareTypes = new()
        {
            PlatformType.PC, PlatformType.MacOS, PlatformType.DOS, PlatformType.Windows, PlatformType.GOG, PlatformType.Steam,
            PlatformType.EpicGames, PlatformType.DOSBox, PlatformType.ScummVM, PlatformType.OpenBOR, PlatformType.Ports,
            PlatformType.TeknoParrot, PlatformType.Moonlight
        };

        internal static readonly HashSet<string> ExtraExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".nfo", ".txt", ".diz", ".sfv", ".md5", ".sha1", ".sha256", ".crc", ".url", ".website", ".html", ".htm",
            ".pdf", ".log", ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp"
        };

        // .bin is left out, Genesis ROMs and installer parts use it too
        private static readonly HashSet<string> DiscImageExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".chd", ".iso", ".cso", ".pbp", ".rvz", ".wbfs", ".gcz", ".cdi", ".ccd", ".img", ".sub", ".sbi"
        };

        private static readonly Regex _discTokenRegex = new(
            @"[(\[]\s*(Dis[ck]\s*\d+(?:\s*of\s*\d+)?)\s*[)\]]",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _hasDiscRegex = new(@"\bDis[ck]\s*\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _bracketTagRegex = new(@"\([^()]+\)|\[[^\[\]]+\]", RegexOptions.Compiled);

        private static readonly Regex _installerRegex = new(@"^(setup|install|patch)|-\d+\.bin$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _volumeRegex = new(@"\.(part\d+\.rar|r\d{2,3}|z\d{2}|\d{3})$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Descriptors are read as Latin1 so every byte survives the rewrite; a UTF-8 BOM shows up as three chars.
        private const string Bom = "\u00EF\u00BB\u00BF";
        private static readonly Regex _cueFileLine = new(@"^(?:\u00EF\u00BB\u00BF)?\s*FILE\b", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _cueFileRef = new(@"^((?:\u00EF\u00BB\u00BF)?\s*FILE\s+)(?:""([^""]*)""|(\S+))(\s+\S+\s*)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);
        private static readonly Regex _gdiCount = new(@"^(?:\u00EF\u00BB\u00BF)?\s*\d+\s*$", RegexOptions.Compiled);
        private static readonly Regex _gdiTrack = new(@"^\s*(\d+)\s+(\d+)\s+(\d+)\s+(\d+)\s+(?:""([^""]+)""|(\S+))\s+(-?\d+)\s*$", RegexOptions.Compiled);

        public static bool Applies(Platform? platform, MediaSettings settings) =>
            settings.RenameOnImport && platform != null && settings.GetRenameTargetPlatformSlugs().Any(platform.MatchesFolderName);

        internal static bool IsRomsetPlatform(Platform? p) => p != null && RomsetTypes.Contains(p.Type);

        internal static bool IsSoftwarePlatform(Platform? p) => p != null && SoftwareTypes.Contains(p.Type);

        internal static bool IsInstallerName(string fileName) => _installerRegex.IsMatch(fileName);

        internal static bool IsExtra(string path) => ExtraExtensions.Contains(Path.GetExtension(path));

        internal static string? DiscToken(string name)
        {
            var m = _discTokenRegex.Match(name ?? "");
            return m.Success ? m.Groups[1].Value : null;
        }

        internal static bool HasDiscToken(string stem) => _hasDiscRegex.IsMatch(stem);

        internal static string[] BracketTags(string name) =>
            _bracketTagRegex.Matches(name ?? "").Select(m => m.Value).Where(t => !_discTokenRegex.IsMatch(t)).Distinct().ToArray();

        // Region, languages and revision describe the file, so they come from its name (else the release name),
        // never from the game. Only bracket tags are read, so "Super Mario World" isn't taken for a region.
        internal string Render(string template, Game game, Platform? platform, string sourceName, string releaseName,
                               string? disc, string? version, string? contentName)
        {
            var file = TitleCleanerService.ExtractFilenameMetadata(string.Join(" ", BracketTags(Path.GetFileNameWithoutExtension(sourceName))));
            var release = TitleCleanerService.ExtractFilenameMetadata(string.Join(" ", BracketTags(releaseName)));
            var variables = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["Title"]        = game.Title,
                ["Year"]         = game.Year > 0 ? game.Year.ToString() : null,
                ["Platform"]     = platform?.Name,
                ["Version"]      = version,
                ["ContentName"]  = contentName,
                ["ReleaseGroup"] = TitleCleanerService.ExtractReleaseGroup(releaseName) ?? TitleCleanerService.ExtractReleaseGroup(sourceName),
                ["Region"]       = file.Region ?? release.Region,
                ["Languages"]    = file.Languages ?? release.Languages,
                ["Revision"]     = file.Revision ?? release.Revision,
                ["Disc"]         = disc,
                ["Edition"]      = null
            };
            return _renderer.RenderStem(string.IsNullOrWhiteSpace(template) ? "{Title}" : template, variables);
        }

        internal string GroupSuffix(string sourceName, string releaseName, MediaSettings settings)
        {
            if (!settings.IncludeReleaseGroupInFilename) return "";
            var group = TitleCleanerService.ExtractReleaseGroup(releaseName) ?? TitleCleanerService.ExtractReleaseGroup(sourceName);
            if (string.IsNullOrWhiteSpace(group)) return "";
            var template = string.IsNullOrWhiteSpace(settings.ReleaseGroupSuffix) ? "[{ReleaseGroup}]" : settings.ReleaseGroupSuffix;
            return " " + _renderer.RenderStem(template, new Dictionary<string, string?>(StringComparer.Ordinal) { ["ReleaseGroup"] = group });
        }

        internal sealed class PlannedFile
        {
            public string Source { get; init; } = string.Empty;
            public string Name { get; init; } = string.Empty;
            public byte[]? Content { get; init; }
        }

        // Names a disc set (cue/bin, gdi, m3u, or disc images only) as a whole and rewrites the descriptors
        // to the new names. Null means the release is not a set this can rename safely, so it keeps its names.
        // stemFor(file name, disc) gives the new stem; the source files are only read.
        internal static List<PlannedFile>? PlanDiscRelease(string sourceDir, IReadOnlyList<string> files, Func<string, string?, string> stemFor)
        {
            var dir = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceDir));
            if (files.Any(f => !string.Equals(Path.GetDirectoryName(Path.GetFullPath(f)), dir, StringComparison.Ordinal))) return null;
            if (files.Any(f => Ext(f) is ".mds" or ".mdf" or ".toc" || _volumeRegex.IsMatch(Path.GetFileName(f)))) return null;
            var descriptors = files.Where(f => Ext(f) is ".cue" or ".gdi" or ".m3u").ToList();
            if (descriptors.Count == 0 && !files.Where(f => !IsExtra(f) && Path.HasExtension(f)).All(f => DiscImageExtensions.Contains(Path.GetExtension(f)))) return null;

            var stems = new Dictionary<string, string>(StringComparer.Ordinal);
            var contents = new Dictionary<string, byte[]>(StringComparer.Ordinal);
            bool Assign(string file, string stem) => stems.TryAdd(file, stem);
            string NewName(string file) => stems[file] + Path.GetExtension(file);

            string? Resolve(string reference)
            {
                var name = Encoding.UTF8.GetString(Encoding.Latin1.GetBytes(reference));
                if (name.Length == 0 || Path.IsPathRooted(name)) return null;
                string full;
                try { full = Path.GetFullPath(Path.Combine(dir, name)); }
                catch (Exception) { return null; }
                var exact = files.FirstOrDefault(f => string.Equals(Path.GetFullPath(f), full, StringComparison.Ordinal));
                if (exact != null) return exact;
                var loose = files.Where(f => string.Equals(Path.GetFullPath(f), full, StringComparison.OrdinalIgnoreCase)).ToList();
                return loose.Count == 1 ? loose[0] : null;
            }

            bool PlanCue(string cue, string stem)
            {
                if (!Assign(cue, stem)) return false;
                var text = ReadDescriptor(cue);
                if (text == null) return false;
                var lines = text.Split('\n');
                var refs = new List<(int Line, Match M, string Target)>();
                for (var i = 0; i < lines.Length; i++)
                {
                    if (!_cueFileLine.IsMatch(lines[i])) continue;
                    var m = _cueFileRef.Match(lines[i]);
                    if (!m.Success) return false;
                    var target = Resolve(m.Groups[2].Success ? m.Groups[2].Value : m.Groups[3].Value);
                    if (target == null) return false;
                    refs.Add((i, m, target));
                }
                if (refs.Count == 0) return false;
                for (var j = 0; j < refs.Count; j++)
                {
                    var (line, m, target) = refs[j];
                    if (!Assign(target, refs.Count == 1 ? stem : $"{stem} (Track {j + 1:00})")) return false;
                    lines[line] = m.Groups[1].Value + "\"" + Latin1(NewName(target)) + "\"" + m.Groups[4].Value;
                }
                contents[cue] = Encoding.Latin1.GetBytes(string.Join("\n", lines));
                return true;
            }

            bool PlanGdi(string gdi, string stem)
            {
                if (!Assign(gdi, stem)) return false;
                var text = ReadDescriptor(gdi);
                if (text == null) return false;
                var lines = text.Split('\n');
                var first = true;
                for (var i = 0; i < lines.Length; i++)
                {
                    if (string.IsNullOrWhiteSpace(lines[i])) continue;
                    if (first)
                    {
                        if (!_gdiCount.IsMatch(lines[i])) return false;
                        first = false;
                        continue;
                    }
                    var m = _gdiTrack.Match(lines[i]);
                    if (!m.Success) return false;
                    var quoted = m.Groups[5].Success;
                    var target = Resolve(quoted ? m.Groups[5].Value : m.Groups[6].Value);
                    if (target == null || !int.TryParse(m.Groups[1].Value, out var track) || !Assign(target, $"{stem} (Track {track:00})")) return false;
                    var name = Latin1(NewName(target));
                    var token = quoted || name.Contains(' ') ? "\"" + name + "\"" : name;
                    var start = quoted ? m.Groups[5].Index - 1 : m.Groups[6].Index;
                    var end = quoted ? m.Groups[5].Index + m.Groups[5].Length + 1 : m.Groups[6].Index + m.Groups[6].Length;
                    lines[i] = lines[i].Substring(0, start) + token + lines[i].Substring(end);
                }
                if (first) return false;
                contents[gdi] = Encoding.Latin1.GetBytes(string.Join("\n", lines));
                return true;
            }

            foreach (var m3u in descriptors.Where(f => Ext(f) == ".m3u"))
            {
                var text = ReadDescriptor(m3u);
                if (text == null || !Assign(m3u, stemFor(Path.GetFileName(m3u), null))) return null;
                var lines = text.Split('\n');
                var index = 0;
                for (var i = 0; i < lines.Length; i++)
                {
                    var skip = i == 0 && lines[i].StartsWith(Bom, StringComparison.Ordinal) ? Bom.Length : 0;
                    var entry = lines[i].Substring(skip).Trim();
                    if (entry.Length == 0 || entry.StartsWith('#')) continue;
                    index++;
                    var target = Resolve(entry);
                    if (target == null || Ext(target) == ".m3u") return null;
                    var name = Path.GetFileName(target);
                    var stem = stemFor(name, DiscToken(name) ?? $"Disc {index}");
                    var planned = Ext(target) switch
                    {
                        ".cue" => PlanCue(target, stem),
                        ".gdi" => PlanGdi(target, stem),
                        _ => Assign(target, stem)
                    };
                    if (!planned) return null;
                    var at = lines[i].IndexOf(entry, skip, StringComparison.Ordinal);
                    lines[i] = lines[i].Substring(0, at) + Latin1(NewName(target)) + lines[i].Substring(at + entry.Length);
                }
                contents[m3u] = Encoding.Latin1.GetBytes(string.Join("\n", lines));
            }

            foreach (var cue in descriptors.Where(f => Ext(f) == ".cue" && !stems.ContainsKey(f)).ToList())
            {
                var name = Path.GetFileName(cue);
                if (!PlanCue(cue, stemFor(name, DiscToken(name)))) return null;
            }
            foreach (var gdi in descriptors.Where(f => Ext(f) == ".gdi" && !stems.ContainsKey(f)).ToList())
            {
                var name = Path.GetFileName(gdi);
                if (!PlanGdi(gdi, stemFor(name, DiscToken(name)))) return null;
            }
            foreach (var file in files.Where(f => !stems.ContainsKey(f) && !IsExtra(f) && Path.HasExtension(f)).ToList())
            {
                var name = Path.GetFileName(file);
                Assign(file, stemFor(name, DiscToken(name)));
            }

            return files.Select(f =>
            {
                string name;
                if (stems.ContainsKey(f)) name = NewName(f);
                else if (IsExtra(f)) name = ExtraName(f, stems.Where(s => !string.Equals(NewName(s.Key), Path.GetFileName(s.Key), StringComparison.Ordinal))
                                                                 .Select(s => (s.Key, s.Value)));
                else name = Path.GetFileName(f);
                return new PlannedFile { Source = f, Name = name, Content = contents.GetValueOrDefault(f) };
            }).ToList();
        }

        // An extra named like a renamed file follows it ("Crash (USA).nfo" next to "Crash (USA).cue"), any other keeps its name.
        internal static string ExtraName(string extra, IEnumerable<(string Source, string NewStem)> renamed)
        {
            var stem = Path.GetFileNameWithoutExtension(extra);
            foreach (var (source, newStem) in renamed)
            {
                if (string.Equals(Path.GetFileNameWithoutExtension(source), stem, StringComparison.OrdinalIgnoreCase))
                    return newStem + Path.GetExtension(extra);
            }
            return Path.GetFileName(extra);
        }

        // Descriptors are a few lines of text; anything big is not one this can rewrite
        private static string? ReadDescriptor(string path) =>
            new FileInfo(path).Length > 1 << 20 ? null : Encoding.Latin1.GetString(File.ReadAllBytes(path));

        private static string Ext(string file) => Path.GetExtension(file).ToLowerInvariant();

        private static string Latin1(string name) => Encoding.Latin1.GetString(Encoding.UTF8.GetBytes(name));
    }
}
