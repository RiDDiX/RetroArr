using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
                _entries.RemoveAll(e => e.AddedAt < DateTime.UtcNow.AddDays(-7));

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

        // The entry mapped under this exact name, else one for the same release name, else one whose name
        // contains the other. Generic names (URL endpoints, the old NZBGet 'RetroArr_download') never match
        // by name, and a name needs 4 meaningful characters to match inside a longer one.
        private TrackedDownload? Find(string downloadName, Func<TrackedDownload, bool> filter)
        {
            if (string.IsNullOrEmpty(downloadName)) return null;

            var candidates = _entries.Where(filter).ToList();
            var exact = candidates.FirstOrDefault(e => e.Url.Equals(downloadName, StringComparison.OrdinalIgnoreCase));
            if (exact != null) return exact;

            var name = CleanName(downloadName);
            var nameLength = MeaningfulLength(name);
            if (nameLength == 0) return null;

            TrackedDownload? partial = null;
            foreach (var entry in candidates)
            {
                var entryName = CleanName(ExtractName(entry.Url));
                var entryLength = MeaningfulLength(entryName);
                if (entryLength == 0) continue;
                if (entryName == name) return entry;
                if (partial == null && ((entryLength >= 4 && name.Contains(entryName, StringComparison.Ordinal)) ||
                                        (nameLength >= 4 && entryName.Contains(name, StringComparison.Ordinal))))
                {
                    partial = entry;
                }
            }
            return partial;
        }

        private static string ExtractName(string input)
        {
            if (!Uri.TryCreate(input, UriKind.Absolute, out var uri) || (uri.Scheme != "http" && uri.Scheme != "https" && uri.Scheme != "magnet"))
                return input;

            // Prowlarr and Jackett put the release name in file=, magnet links in dn=. Without dn= a torrent
            // client shows the info hash until it has the metadata.
            var query = HttpUtility.ParseQueryString(uri.Query);
            return query["file"] ?? query["dn"] ?? query["xt"]?.Split(':')[^1] ??
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
