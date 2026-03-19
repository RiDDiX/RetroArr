using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
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
            if (string.IsNullOrEmpty(downloadName)) return null;

            lock (_lock)
            {
                // Try exact URL match first (some clients preserve the URL as name)
                var entry = _entries.FirstOrDefault(e =>
                    e.Url.Contains(downloadName, StringComparison.OrdinalIgnoreCase) ||
                    downloadName.Contains(Path.GetFileNameWithoutExtension(e.Url), StringComparison.OrdinalIgnoreCase));

                if (entry != null) return entry.PlatformFolder;

                // Try fuzzy: compare cleaned names
                var cleanDownload = CleanName(downloadName);
                entry = _entries
                    .Where(e => !string.IsNullOrEmpty(e.Url))
                    .FirstOrDefault(e =>
                    {
                        var cleanEntry = CleanName(ExtractName(e.Url));
                        return cleanEntry.Contains(cleanDownload, StringComparison.OrdinalIgnoreCase) ||
                               cleanDownload.Contains(cleanEntry, StringComparison.OrdinalIgnoreCase);
                    });

                return entry?.PlatformFolder;
            }
        }

        public int? LookupGameId(string downloadName)
        {
            if (string.IsNullOrEmpty(downloadName)) return null;

            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e =>
                    e.GameId.HasValue && (
                        e.Url.Contains(downloadName, StringComparison.OrdinalIgnoreCase) ||
                        downloadName.Contains(Path.GetFileNameWithoutExtension(e.Url), StringComparison.OrdinalIgnoreCase)));

                if (entry != null) return entry.GameId;

                var cleanDownload = CleanName(downloadName);
                entry = _entries
                    .Where(e => e.GameId.HasValue && !string.IsNullOrEmpty(e.Url))
                    .FirstOrDefault(e =>
                    {
                        var cleanEntry = CleanName(ExtractName(e.Url));
                        return cleanEntry.Contains(cleanDownload, StringComparison.OrdinalIgnoreCase) ||
                               cleanDownload.Contains(cleanEntry, StringComparison.OrdinalIgnoreCase);
                    });

                return entry?.GameId;
            }
        }

        public string? LookupImportSubfolder(string downloadName)
        {
            if (string.IsNullOrEmpty(downloadName)) return null;

            lock (_lock)
            {
                var entry = _entries.FirstOrDefault(e =>
                    !string.IsNullOrEmpty(e.ImportSubfolder) && (
                        e.Url.Contains(downloadName, StringComparison.OrdinalIgnoreCase) ||
                        downloadName.Contains(Path.GetFileNameWithoutExtension(e.Url), StringComparison.OrdinalIgnoreCase)));

                if (entry != null) return entry.ImportSubfolder;

                var cleanDownload = CleanName(downloadName);
                entry = _entries
                    .Where(e => !string.IsNullOrEmpty(e.ImportSubfolder) && !string.IsNullOrEmpty(e.Url))
                    .FirstOrDefault(e =>
                    {
                        var cleanEntry = CleanName(ExtractName(e.Url));
                        return cleanEntry.Contains(cleanDownload, StringComparison.OrdinalIgnoreCase) ||
                               cleanDownload.Contains(cleanEntry, StringComparison.OrdinalIgnoreCase);
                    });

                return entry?.ImportSubfolder;
            }
        }

        public void MarkProcessed(string downloadName)
        {
            lock (_lock)
            {
                // Remove entries older than 7 days or matching this download
                _entries.RemoveAll(e => e.AddedAt < DateTime.UtcNow.AddDays(-7));

                var cleanDownload = CleanName(downloadName);
                _entries.RemoveAll(e =>
                {
                    var cleanEntry = CleanName(ExtractName(e.Url));
                    return cleanEntry.Contains(cleanDownload, StringComparison.OrdinalIgnoreCase) ||
                           cleanDownload.Contains(cleanEntry, StringComparison.OrdinalIgnoreCase);
                });

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

        private static string ExtractName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            // If it's an absolute URL, extract the filename from the path
            if (Uri.TryCreate(input, UriKind.Absolute, out var uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
            {
                return Path.GetFileNameWithoutExtension(uri.AbsolutePath);
            }
            // Otherwise treat it as a plain name
            return Path.GetFileNameWithoutExtension(input);
        }

        private static string CleanName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            return input.Replace(".", " ").Replace("-", " ").Replace("_", " ").Trim().ToLowerInvariant();
        }
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
