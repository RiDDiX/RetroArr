using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Diagnostics.CodeAnalysis;
using RetroArr.Core.IO;

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
            // An NZBGet that lost its queue and history counts from 1 again: the number under another name is a new job
            if (IsNumericId(downloadItem.Id) && _cache.TryGetValue((clientId, downloadItem.Id), out var earlier) && earlier.Title != downloadItem.Name)
                _cache.TryRemove((clientId, downloadItem.Id), out _);

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
                if (downloadItem.GameId.HasValue && !existing.GameId.HasValue && !existing.GameSetByHand)
                    existing.GameId = downloadItem.GameId;
                if (!string.IsNullOrEmpty(downloadItem.ImportSubfolder) && string.IsNullOrEmpty(existing.ImportSubfolder))
                    existing.ImportSubfolder = downloadItem.ImportSubfolder;

                // Retried or resumed in the client after it failed: it is downloading again. Deleted or an unknown
                // status is no retry.
                if (existing.State == TrackedDownloadState.DownloadFailed && IsWorking(downloadItem.State))
                {
                    existing.State = TrackedDownloadState.Downloading;
                    existing.ClearWarnings();
                    Save();
                }
                // Still failed, with a newer reason from the client
                else if (existing.State == TrackedDownloadState.DownloadFailed && downloadItem.State == DownloadState.Error
                         && !string.IsNullOrWhiteSpace(existing.ClientMessage) && existing.ClientMessage != existing.StatusMessages.FirstOrDefault())
                {
                    MarkDownloadFailed(existing, downloadItem);
                    Save();
                }

                // If download client reports completed and we're still in Downloading, advance to ImportPending
                if (downloadItem.State == DownloadState.Completed && existing.State == TrackedDownloadState.Downloading)
                {
                    existing.State = TrackedDownloadState.ImportPending;
                }

                // A failed job stays in the client's history (SABnzbd, NZBGet), so it would read Downloading for good
                if (downloadItem.State == DownloadState.Error && existing.State == TrackedDownloadState.Downloading)
                {
                    MarkDownloadFailed(existing, downloadItem);
                    Save();
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
            if (downloadItem.State == DownloadState.Error)
                MarkDownloadFailed(tracked, downloadItem);

            _cache.TryAdd((clientId, downloadItem.Id), tracked);
            Save();
            return tracked;
        }

        // The client works on the download
        private static bool IsWorking(DownloadState state) => state is DownloadState.Downloading
            or DownloadState.Queued or DownloadState.Paused or DownloadState.Checking or DownloadState.Completed;

        // With the client's own reason where it gives one, such as SABnzbd's fail_message
        private static void MarkDownloadFailed(TrackedDownload tracked, DownloadStatus downloadItem)
        {
            var reason = downloadItem.StatusMessages.FirstOrDefault() ?? "The download client reports this download as failed.";
            tracked.MarkDownloadFailed(reason);
            _logger.Info($"[TrackedDownload] '{tracked.Title}' failed in {tracked.DownloadClientName}: {reason}");
        }

        // Tracks a download with what the grabs know of it by its name: platform, game and import subfolder, each where
        // it has none yet. A game mapped by hand, or no game, stays.
        public TrackedDownload TrackDownload(DownloadStatus download, int clientId, string clientName, DownloadPlatformTracker grabs)
        {
            download.PlatformFolder = grabs.LookupByName(download.Name);
            download.GameId = grabs.LookupGameId(download.Name);
            download.ImportSubfolder = grabs.LookupImportSubfolder(download.Name);
            var before = Find(clientId, download.Id);
            var failedBefore = before?.State == TrackedDownloadState.DownloadFailed;
            var tracked = TrackDownload(download, clientId, clientName);
            // The id may now belong to another job
            var trackedBefore = ReferenceEquals(before, tracked);
            failedBefore &= trackedBefore;
            if (!string.IsNullOrEmpty(download.PlatformFolder) && string.IsNullOrEmpty(tracked.PlatformFolder))
                tracked.PlatformFolder = download.PlatformFolder;
            // A failed grab no longer holds back the monitor's next search for the game, and is not sent again by it.
            // A retry the client runs as a new job holds it back again.
            var failedNow = tracked.State == TrackedDownloadState.DownloadFailed;
            if (failedNow)
            {
                // Marked when it fails and checked again only once a grab was stored since, which may be this very job (a
                // torrent sent again, a game mapped by hand). A live job of the same game, or of the same name when the
                // game is unknown (a retry under a new id, a second grab), holds the mark back.
                var stored = grabs.EntriesAdded;
                if (tracked.GrabMarkedAt != stored && !_cache.Values.Any(t => !ReferenceEquals(t, tracked)
                        && (tracked.GameId > 0 ? t.GameId == tracked.GameId : string.Equals(t.Title, tracked.Title, StringComparison.OrdinalIgnoreCase))
                        && t.State is TrackedDownloadState.Downloading or TrackedDownloadState.ImportPending
                            or TrackedDownloadState.ImportBlocked or TrackedDownloadState.Importing))
                {
                    grabs.MarkFailed(download.Name, tracked.GameId);
                    tracked.GrabMarkedAt = stored;
                }
            }
            else
            {
                tracked.GrabMarkedAt = null;
                if (failedBefore || (!trackedBefore && IsWorking(download.State)))
                    grabs.ClearFailed(download.Name, tracked.GameId);
            }
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

                // Downloading or failed but client no longer reports it: gone (deleted or auto-cleaned)
                if (tracked.State == TrackedDownloadState.Downloading || tracked.State == TrackedDownloadState.DownloadFailed)
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
        // earlier build tracked under the number moves to the hash, so the torrent isn't tracked twice. The
        // size tells two torrents with the same name apart.
        private bool TryTakeOverNumericEntry(DownloadStatus downloadItem, int clientId, [NotNullWhen(true)] out TrackedDownload? tracked)
        {
            tracked = IsHashId(downloadItem.Id)
                ? _cache.Values.FirstOrDefault(t => t.DownloadClientId == clientId && t.Title == downloadItem.Name && t.Size == downloadItem.Size && IsNumericId(t.DownloadId))
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
                    AtomicFile.WriteAllText(_persistencePath, json);
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
                    // game from an unrelated grab. Those jobs were never importable, so they are still Downloading;
                    // drop the guess so the job is mapped again or lands in Unmapped. Later states carry a user's mapping.
                    if (string.Equals(entry.Title, "RetroArr_download", StringComparison.OrdinalIgnoreCase)
                        && entry.State == TrackedDownloadState.Downloading)
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
