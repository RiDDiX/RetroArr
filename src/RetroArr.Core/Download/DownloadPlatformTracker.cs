using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Web;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DownloadPlatformTracker
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadsImport);
        private readonly string _trackingFile;
        private readonly object _lock = new();
        private List<TrackedDownload> _entries = new();
        private static readonly HashSet<string> GenericWords = new() { "retroarr", "download", "api", "get", "getnzb", "dl", "nzb", "torrent" };
        // A file extension after a name with spaces ('Doom 64 (USA).z64'). Dotted release names keep their last part.
        private static readonly Regex FileExtension = new(@"(?<=\s.*)\.(?=\d*[a-z])[a-z0-9]{1,4}$", RegexOptions.IgnoreCase);
        private static readonly Regex BracketedTag = new(@"\([^)]*\)|\[[^\]]*\]|\{[^}]*\}");

        public DownloadPlatformTracker(string configDirectory)
        {
            _trackingFile = Path.Combine(configDirectory, "download_platform_map.json");
            Load();
        }

        public void Track(string downloadUrl, string? platformFolder, int? gameId = null, string? importSubfolder = null)
        {
            if (string.IsNullOrEmpty(platformFolder) && !gameId.HasValue) return;

            lock (_lock)
            {
                // Remove old entry with same URL if exists
                _entries.RemoveAll(e => e.Url.Equals(downloadUrl, StringComparison.OrdinalIgnoreCase));
                _entries.Add(new TrackedDownload
                {
                    Url = downloadUrl,
                    PlatformFolder = platformFolder ?? string.Empty,
                    GameId = gameId,
                    ImportSubfolder = importSubfolder,
                    AddedAt = DateTime.UtcNow
                });
                Save();
            }
        }

        public string? LookupByName(string downloadName)
        {
            lock (_lock) { return Find(downloadName, _ => true)?.PlatformFolder; }
        }

        public int? LookupGameId(string downloadName)
        {
            lock (_lock) { return Find(downloadName, e => e.GameId.HasValue)?.GameId; }
        }

        public string? LookupImportSubfolder(string downloadName)
        {
            lock (_lock) { return Find(downloadName, e => !string.IsNullOrEmpty(e.ImportSubfolder))?.ImportSubfolder; }
        }

        public void MarkProcessed(string downloadName)
        {
            lock (_lock)
            {
                // Remove entries older than 7 days and the ones the lookups resolved for this download.
                // Mappings for other, similar names stay.
                _entries.RemoveAll(IsExpired);

                var matched = new[]
                {
                    Find(downloadName, _ => true),
                    Find(downloadName, e => e.GameId.HasValue),
                    Find(downloadName, e => !string.IsNullOrEmpty(e.ImportSubfolder)),
                };
                _entries.RemoveAll(e => matched.Contains(e));

                Save();
            }
        }

        public void SetPlatformForDownload(string downloadName, string platformFolder, int? gameId = null, string? importSubfolder = null)
        {
            lock (_lock)
            {
                _entries.RemoveAll(e => e.Url.Equals(downloadName, StringComparison.OrdinalIgnoreCase));
                _entries.Add(new TrackedDownload
                {
                    Url = downloadName,
                    PlatformFolder = platformFolder,
                    GameId = gameId,
                    ImportSubfolder = importSubfolder,
                    AddedAt = DateTime.UtcNow
                });
                Save();
            }
        }

        public List<TrackedDownload> GetAll()
        {
            lock (_lock) { return new List<TrackedDownload>(_entries); }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_trackingFile))
                {
                    var json = File.ReadAllText(_trackingFile);
                    _entries = JsonSerializer.Deserialize<List<TrackedDownload>>(json,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<TrackedDownload>();

                    // Older builds added every NZBGet job as "RetroArr_download", so a mapping stored under that
                    // name would hand one job's platform and game to all of them
                    if (_entries.RemoveAll(e => string.Equals(e.Url, "RetroArr_download", StringComparison.OrdinalIgnoreCase)) > 0)
                    {
                        Save();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[PlatformTracker] Error loading: {ex.Message}");
                _entries = new List<TrackedDownload>();
            }
        }

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_trackingFile, json);
            }
            catch (Exception ex)
            {
                _logger.Error($"[PlatformTracker] Error saving: {ex.Message}");
            }
        }

        private static bool IsExpired(TrackedDownload entry) => entry.AddedAt < DateTime.UtcNow.AddDays(-7);

        // The newest live entry mapped under this exact name, else the newest for the same release name, else the
        // newest that is the same once bracketed tags and a file extension are dropped on both sides. Names are
        // never matched by containment, and generic names (URL endpoints, the old NZBGet 'RetroArr_download')
        // never match by name. No match, or names that point to different platforms or games, leave the
        // download for manual mapping.
        private TrackedDownload? Find(string downloadName, Func<TrackedDownload, bool> filter)
        {
            if (string.IsNullOrEmpty(downloadName)) return null;

            var candidates = _entries.Where(e => !IsExpired(e) && filter(e)).Reverse().ToList();
            var exact = candidates.FirstOrDefault(e => e.Url.Equals(downloadName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var name = CleanName(downloadName);
            if (MeaningfulLength(name) == 0) return null;
            var sameName = candidates.Where(e => CleanName(ExtractName(e.Url)) == name).ToList();
            if (sameName.Count > 0) return OneTarget(sameName);

            // Same title, and one name only adds tags to the other: 'Chrono Trigger (USA) [!].sfc' is
            // 'Chrono Trigger (USA)', but 'Chrono Trigger (Japan)' is a different release
            var baseName = CleanName(StripTags(downloadName));
            if (MeaningfulLength(baseName) == 0) return null;
            var tags = Tags(downloadName);
            return OneTarget(candidates.Where(e =>
            {
                var entryName = ExtractName(e.Url);
                if (CleanName(StripTags(entryName)) != baseName) return false;
                var entryTags = Tags(entryName);
                return entryTags.IsSubsetOf(tags) || tags.IsSubsetOf(entryTags);
            }).ToList());
        }

        private static HashSet<string> Tags(string name) =>
            BracketedTag.Matches(name).Select(m => Regex.Replace(m.Value.ToLowerInvariant(), @"\s+", " ")).ToHashSet();

        private static TrackedDownload? OneTarget(List<TrackedDownload> matches) =>
            matches.Select(e => (e.PlatformFolder, e.GameId)).Distinct().Count() == 1 ? matches[0] : null;

        private static string ExtractName(string input)
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "magnet"))
                return input;

            // Prowlarr and Jackett put the release name in file=, magnet links in dn=. Without dn= a torrent
            // client shows the info hash until it has the metadata.
            var query = HttpUtility.ParseQueryString(uri.Query);
            // Hybrid magnets carry a btmh multihash too; a base32 btih never equals the hex name.
            return query["file"] ?? query["dn"] ??
                   query.GetValues("xt")?.FirstOrDefault(x => x.StartsWith("urn:btih:", StringComparison.OrdinalIgnoreCase))?["urn:btih:".Length..] ??
                   (uri.Segments.Length > 0 ? Uri.UnescapeDataString(uri.Segments[^1]) : string.Empty);
        }

        private static string CleanName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            foreach (var extension in new[] { ".torrent", ".nzb" })
            {
                if (input.EndsWith(extension, StringComparison.OrdinalIgnoreCase)) input = input[..^extension.Length];
            }
            return input.Replace(".", " ").Replace("-", " ").Replace("_", " ").Trim().ToLowerInvariant();
        }

        // 'Chrono Trigger (USA) [!].sfc' -> 'Chrono Trigger'
        private static string StripTags(string name) => BracketedTag.Replace(FileExtension.Replace(name, string.Empty), " ");

        private static int MeaningfulLength(string cleanName) =>
            cleanName.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Where(word => !GenericWords.Contains(word))
                .Sum(word => word.Count(char.IsLetterOrDigit));
    }

    public class TrackedDownload
    {
        public string Url { get; set; } = string.Empty;
        public string PlatformFolder { get; set; } = string.Empty;
        public int? GameId { get; set; }
        public string? ImportSubfolder { get; set; }
        public DateTime AddedAt { get; set; }
    }
}
