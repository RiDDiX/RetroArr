using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download.TrackedDownloads
{
    /// <summary>
    /// In-memory cache of tracked downloads with JSON persistence.
    /// Replaces the simple HashSet&lt;string&gt; approach with a proper tracking service
    /// inspired by Sonarr's TrackedDownloadService.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class TrackedDownloadService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadsMonitor);
        // NZBGet ids are small numbers every client counts on its own, so an id only means something per client
        private readonly ConcurrentDictionary<(int ClientId, string DownloadId), TrackedDownload> _cache = new();
        private readonly string _persistencePath;
        private readonly object _saveLock = new();

        public TrackedDownloadService(string configDirectory)
        {
            _persistencePath = Path.Combine(configDirectory, "tracked_downloads.json");
            Load();
        }

        public TrackedDownload? Find(int clientId, string downloadId)
        {
            _cache.TryGetValue((clientId, downloadId), out var tracked);
            return tracked;
        }

        public TrackedDownload TrackDownload(DownloadStatus downloadItem, int clientId, string clientName)
        {
            if (_cache.TryGetValue((clientId, downloadItem.Id), out var existing) || TryTakeOverNumericEntry(downloadItem, clientId, out existing))
            {
                // Update mutable fields but preserve state
                existing.Title = downloadItem.Name;
                existing.OutputPath = downloadItem.DownloadPath;
                existing.Size = downloadItem.Size;
                existing.Progress = downloadItem.Progress;
                existing.Category = downloadItem.Category;
                existing.DownloadClientName = clientName;
                existing.ClientMessage = downloadItem.StatusMessages.FirstOrDefault();
                if (downloadItem.GameId.HasValue && !existing.GameId.HasValue)
                    existing.GameId = downloadItem.GameId;
                if (!string.IsNullOrEmpty(downloadItem.ImportSubfolder) && string.IsNullOrEmpty(existing.ImportSubfolder))
                    existing.ImportSubfolder = downloadItem.ImportSubfolder;

                // If download client reports completed and we're still in Downloading, advance to ImportPending
                if (downloadItem.State == DownloadState.Completed && existing.State == TrackedDownloadState.Downloading)
                {
                    existing.State = TrackedDownloadState.ImportPending;
                }

                return existing;
            }

            var tracked = new TrackedDownload
            {
                DownloadId = downloadItem.Id,
                DownloadClientId = clientId,
                DownloadClientName = clientName,
                Title = downloadItem.Name,
                OutputPath = downloadItem.DownloadPath,
                Category = downloadItem.Category,
                PlatformFolder = downloadItem.PlatformFolder,
                GameId = downloadItem.GameId,
                ImportSubfolder = downloadItem.ImportSubfolder,
                Size = downloadItem.Size,
                Progress = downloadItem.Progress,
                ClientMessage = downloadItem.StatusMessages.FirstOrDefault(),
                State = downloadItem.State == DownloadState.Completed
                    ? TrackedDownloadState.ImportPending
                    : TrackedDownloadState.Downloading,
                Added = DateTime.UtcNow
            };

            _cache.TryAdd((clientId, downloadItem.Id), tracked);
            Save();
            return tracked;
        }

        public List<TrackedDownload> GetTrackedDownloads()
        {
            return _cache.Values.ToList();
        }

        public void StopTracking(int clientId, string downloadId)
        {
            _cache.TryRemove((clientId, downloadId), out _);
            Save();
        }

        /// <summary>
        /// Reconcile tracked downloads against the set of IDs currently reported by all clients.
        /// Removes stale entries that the download client no longer knows about.
        /// </summary>
        public int ReconcileWithClientIds(HashSet<(int ClientId, string DownloadId)> activeClientIds)
        {
            int removed = 0;
            var allTracked = _cache.Values.ToList();

            foreach (var tracked in allTracked)
            {
                // If the client still reports this download, skip
                if (activeClientIds.Contains(Key(tracked)))
                    continue;

                // Terminal states: remove from cache immediately
                if (tracked.State == TrackedDownloadState.Imported ||
                    tracked.State == TrackedDownloadState.Ignored)
                {
                    _cache.TryRemove(Key(tracked), out _);
                    removed++;
                    continue;
                }

                // Downloading but client no longer reports it: gone (deleted or auto-cleaned)
                if (tracked.State == TrackedDownloadState.Downloading)
                {
                    _logger.Info($"[TrackedDownload] Removing stale entry '{tracked.Title}' - no longer in client queue.");
                    _cache.TryRemove(Key(tracked), out _);
                    removed++;
                    continue;
                }

                // ImportPending/ImportBlocked/Importing - client removed it but files may still exist.
                // Allow a grace period (1 hour) before cleaning up.
                if (tracked.State == TrackedDownloadState.ImportPending ||
                    tracked.State == TrackedDownloadState.ImportBlocked ||
                    tracked.State == TrackedDownloadState.Importing)
                {
                    var age = DateTime.UtcNow - tracked.Added;
                    if (age.TotalHours > 1)
                    {
                        _logger.Info($"[TrackedDownload] Removing stale import entry '{tracked.Title}' - not in client queue for over 1h.");
                        _cache.TryRemove(Key(tracked), out _);
                        removed++;
                    }
                    continue;
                }

                // ImportFailed: keep for user visibility, but clean up after 24h
                if (tracked.State == TrackedDownloadState.ImportFailed)
                {
                    var age = DateTime.UtcNow - tracked.Added;
                    if (age.TotalHours > 24)
                    {
                        _cache.TryRemove(Key(tracked), out _);
                        removed++;
                    }
                }
            }

            if (removed > 0)
            {
                _logger.Info($"[TrackedDownload] Reconciliation removed {removed} stale entries.");
                Save();
            }

            return removed;
        }

        private static (int, string) Key(TrackedDownload tracked) => (tracked.DownloadClientId, tracked.DownloadId);

        internal static bool IsNumericId(string id) => id.Length > 0 && id.All(char.IsAsciiDigit);

        internal static bool IsHashId(string id) => id.Length == 40 && id.All(char.IsAsciiHexDigit);

        // Transmission ids were its per-session torrent numbers before they became the hash. An entry an
        // earlier build tracked under the number moves to the hash, so the torrent isn't tracked twice.
        private bool TryTakeOverNumericEntry(DownloadStatus downloadItem, int clientId, [NotNullWhen(true)] out TrackedDownload? tracked)
        {
            tracked = IsHashId(downloadItem.Id)
                ? _cache.Values.FirstOrDefault(t => t.DownloadClientId == clientId && t.Title == downloadItem.Name && IsNumericId(t.DownloadId))
                : null;
            if (tracked == null || !_cache.TryRemove(Key(tracked), out _)) return false;

            tracked.DownloadId = downloadItem.Id;
            _cache[Key(tracked)] = tracked;
            Save();
            return true;
        }

        public void Save()
        {
            try
            {
                lock (_saveLock)
                {
                    var entries = _cache.Values.ToList();
                    var json = JsonSerializer.Serialize(entries, new JsonSerializerOptions { WriteIndented = true });
                    File.WriteAllText(_persistencePath, json);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[TrackedDownloadService] Error saving: {ex.Message}");
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_persistencePath)) return;

                var json = File.ReadAllText(_persistencePath);
                var entries = JsonSerializer.Deserialize<List<TrackedDownload>>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (entries == null) return;

                foreach (var entry in entries)
                {
                    // Don't restore transient states - reset Importing back to ImportPending
                    if (entry.State == TrackedDownloadState.Importing)
                        entry.State = TrackedDownloadState.ImportPending;

                    // Prune old imported/ignored entries (> 7 days)
                    if ((entry.State == TrackedDownloadState.Imported || entry.State == TrackedDownloadState.Ignored)
                        && entry.ImportedAt.HasValue && entry.ImportedAt.Value < DateTime.UtcNow.AddDays(-7))
                    {
                        continue;
                    }

                    // Older builds added every NZBGet job as "RetroArr_download" and guessed its platform and
                    // game from an unrelated grab. Drop that guess so the job is mapped again or lands in Unmapped.
                    if (entry.Title.Equals("RetroArr_download", StringComparison.OrdinalIgnoreCase)
                        && entry.State != TrackedDownloadState.Imported && entry.State != TrackedDownloadState.Ignored)
                    {
                        entry.PlatformFolder = null;
                        entry.GameId = null;
                        entry.ImportSubfolder = null;
                    }

                    _cache.TryAdd(Key(entry), entry);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[TrackedDownloadService] Error loading: {ex.Message}");
            }
        }
    }
}
