using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Games
{
    public class ReviewItemService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly string _storePath;
        private readonly object _lock = new();
        private List<ReviewItem> _items = new();

        public ReviewItemService(ConfigurationService configService)
        {
            _storePath = Path.Combine(configService.GetConfigDirectory(), "review_items.json");
            Load();
        }

        public IReadOnlyList<ReviewItem> GetAll()
        {
            lock (_lock) { return _items.ToList(); }
        }

        public IReadOnlyList<ReviewItem> GetPending()
        {
            lock (_lock) { return _items.Where(i => i.Status == ReviewStatus.Pending).ToList(); }
        }

        public ReviewItem? GetById(string id)
        {
            lock (_lock) { return _items.FirstOrDefault(i => i.Id == id); }
        }

        public ReviewItem Add(ReviewItem item)
        {
            lock (_lock)
            {
                // Dedup: if same primary file path already pending, skip
                var primaryPath = item.FilePaths.FirstOrDefault();
                if (!string.IsNullOrEmpty(primaryPath))
                {
                    var existing = _items.FirstOrDefault(i =>
                        i.Status == ReviewStatus.Pending &&
                        i.FilePaths.Any(f => f.Equals(primaryPath, StringComparison.OrdinalIgnoreCase)));
                    if (existing != null) return existing;
                }

                _items.Add(item);
                Save();
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
                Save();
                return true;
            }
        }

        public bool Remove(string id)
        {
            lock (_lock)
            {
                var removed = _items.RemoveAll(i => i.Id == id);
                if (removed > 0) Save();
                return removed > 0;
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

        private void Save()
        {
            try
            {
                var json = JsonSerializer.Serialize(_items, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_storePath, json);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[ReviewItemService] Warning: Could not save review items: {ex.Message}");
            }
        }
    }
}
