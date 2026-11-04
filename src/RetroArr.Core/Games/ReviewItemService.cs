using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;
using RetroArr.Core.IO;

namespace RetroArr.Core.Games
{
    public class ReviewItemService : IDisposable
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly string _storePath;
        private readonly object _lock = new();
        private List<ReviewItem> _items = new();
        // File path -> first open or ignored item holding it, for Add; dropped by every other change
        private Dictionary<string, ReviewItem>? _openPaths;
        private DateTime _lastSave;
        private bool _unsaved;
        // Writes what Add held back; Dispose alone would lose it, the headless host never disposes its services
        private readonly System.Threading.Timer _flush;

        public ReviewItemService(ConfigurationService configService)
        {
            _storePath = Path.Combine(configService.GetConfigDirectory(), "review_items.json");
            Load();
            _flush = new System.Threading.Timer(_ => { lock (_lock) { if (_unsaved) Save(); } });
        }

        public IReadOnlyList<ReviewItem> GetAll()
        {
            lock (_lock) { return _items.ToList(); }
        }

        public IReadOnlyList<ReviewItem> GetPending()
        {
            lock (_lock) { return _items.Where(IsOpen).ToList(); }
        }

        // A mapped item waits on the list to be finalized
        private static bool IsOpen(ReviewItem i) => i.Status is ReviewStatus.Pending or ReviewStatus.Mapped;

        // An ignored file stays ignored: the next scan finds its item instead of adding the file again
        private static bool Holds(ReviewItem i) => IsOpen(i) || i.Status == ReviewStatus.Ignored;

        public ReviewItem? GetById(string id)
        {
            lock (_lock) { return _items.FirstOrDefault(i => i.Id == id); }
        }

        public ReviewItem Add(ReviewItem item)
        {
            lock (_lock)
            {
                // Dedup: if same primary file path already open or ignored, skip
                if (_openPaths == null)
                {
                    _openPaths = new Dictionary<string, ReviewItem>(StringComparer.OrdinalIgnoreCase);
                    foreach (var held in _items.Where(Holds)) Index(held);
                }
                var primaryPath = item.FilePaths.FirstOrDefault();
                if (!string.IsNullOrEmpty(primaryPath) && _openPaths.TryGetValue(primaryPath, out var existing))
                    return existing;

                _items.Add(item);
                if (Holds(item)) Index(item);
                // A scan adds items by the thousand and each write is the whole store: at most one a second, the
                // rest goes a second later or with the next write
                if (DateTime.UtcNow - _lastSave >= TimeSpan.FromSeconds(1)) Save();
                else if (!_unsaved)
                {
                    _unsaved = true;
                    _flush.Change(TimeSpan.FromSeconds(1), System.Threading.Timeout.InfiniteTimeSpan);
                }
                return item;
            }
        }

        public bool UpdateMapping(string id, int? platformId, int? gameId, string? overrideTitle, string? overrideDiskName)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item == null) return false;

                if (platformId.HasValue) item.AssignedPlatformId = platformId;
                if (gameId.HasValue) item.AssignedGameId = gameId;
                if (overrideTitle != null) item.OverrideTitle = overrideTitle;
                if (overrideDiskName != null) item.OverrideDiskName = overrideDiskName;
                item.Status = ReviewStatus.Mapped;
                _openPaths = null;
                Save();
                return true;
            }
        }

        public bool SetStatus(string id, ReviewStatus status)
        {
            lock (_lock)
            {
                var item = _items.FirstOrDefault(i => i.Id == id);
                if (item == null) return false;

                item.Status = status;
                _openPaths = null;
                Save();
                return true;
            }
        }

        public bool Remove(string id)
        {
            lock (_lock)
            {
                var removed = _items.RemoveAll(i => i.Id == id);
                if (removed > 0) { _openPaths = null; Save(); }
                return removed > 0;
            }
        }

        // A library reset empties the store; deleting the file alone left the items here, written back by the next change
        public void Clear()
        {
            lock (_lock)
            {
                _items = new List<ReviewItem>();
                _openPaths = null;
                Save();
            }
        }

        private void Load()
        {
            try
            {
                if (!File.Exists(_storePath)) return;
                var json = File.ReadAllText(_storePath);
                var items = JsonSerializer.Deserialize<List<ReviewItem>>(json);
                if (items != null) _items = items;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[ReviewItemService] Warning: Could not load review items: {ex.Message}");
            }
        }

        private void Index(ReviewItem item)
        {
            foreach (var path in item.FilePaths) _openPaths!.TryAdd(path, item);
        }

        public void Dispose()
        {
            // The timer stays: a scan still adding during shutdown must not throw on a disposed one
            lock (_lock) { if (_unsaved) Save(); }
        }

        private void Save()
        {
            _lastSave = DateTime.UtcNow;
            _unsaved = false;
            try
            {
                var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                AtomicFile.WriteAllText(_storePath, json);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[ReviewItemService] Warning: Could not save review items: {ex.Message}");
            }
        }
    }
}
