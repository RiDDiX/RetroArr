using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Games
{
    public class TrashEntry
    {
        public string Id { get; set; } = string.Empty;
        public int? GameId { get; set; }
        public string? GameTitle { get; set; }
        public string OriginalPath { get; set; } = string.Empty;
        public string TrashPath { get; set; } = string.Empty;
        public DateTime DeletedAt { get; set; }
        public long SizeBytes { get; set; }
        public bool IsDirectory { get; set; }
    }

    public class TrashService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly ConfigurationService _config;

        public TrashService(ConfigurationService config)
        {
            _config = config;
        }

        private string GetTrashRoot()
        {
            var settings = _config.LoadMediaSettings();
            var path = string.IsNullOrWhiteSpace(settings.TrashPath)
                ? Path.Combine(_config.GetConfigDirectory(), "trash")
                : settings.TrashPath;
            Directory.CreateDirectory(path);
            return path;
        }

        public async Task<TrashEntry?> MoveAsync(string sourcePath, int? gameId = null, string? gameTitle = null)
        {
            if (string.IsNullOrEmpty(sourcePath)) return null;
            if (!File.Exists(sourcePath) && !Directory.Exists(sourcePath))
            {
                _logger.Warn($"[Trash] Source path missing, nothing to move: {sourcePath}");
                return null;
            }

            var root = GetTrashRoot();
            var isDir = Directory.Exists(sourcePath);
            var entryId = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "_" + Guid.NewGuid().ToString("N").Substring(0, 8);
            var entryDir = Path.Combine(root, entryId);
            Directory.CreateDirectory(entryDir);

            var payloadName = isDir ? new DirectoryInfo(sourcePath).Name : Path.GetFileName(sourcePath);
            var destPath = Path.Combine(entryDir, payloadName);

            try
            {
                if (isDir)
                {
                    MoveDirectoryCrossVolume(sourcePath, destPath);
                }
                else
                {
                    try { File.Move(sourcePath, destPath); }
                    catch (IOException)
                    {
                        File.Copy(sourcePath, destPath, overwrite: true);
                        File.Delete(sourcePath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Trash] Move failed {sourcePath} -> {destPath}: {ex.Message}");
                return null;
            }

            var entry = new TrashEntry
            {
                Id = entryId,
                GameId = gameId,
                GameTitle = gameTitle,
                OriginalPath = sourcePath,
                TrashPath = destPath,
                DeletedAt = DateTime.UtcNow,
                IsDirectory = isDir,
                SizeBytes = TryMeasure(destPath),
            };

            try
            {
                var sidecar = Path.Combine(entryDir, "meta.json");
                await File.WriteAllTextAsync(sidecar, JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Trash] Could not write sidecar for {entryId}: {ex.Message}");
            }

            _logger.Info($"[Trash] {(isDir ? "Directory" : "File")} moved to trash: {sourcePath} (entry {entryId})");
            return entry;
        }

        public List<TrashEntry> List()
        {
            var root = GetTrashRoot();
            var entries = new List<TrashEntry>();
            if (!Directory.Exists(root)) return entries;

            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                var sidecar = Path.Combine(dir, "meta.json");
                if (!File.Exists(sidecar)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<TrashEntry>(File.ReadAllText(sidecar));
                    if (entry != null) entries.Add(entry);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[Trash] Could not read sidecar {sidecar}: {ex.Message}");
                }
            }

            return entries.OrderByDescending(e => e.DeletedAt).ToList();
        }

        public TrashEntry? Get(string id) =>
            List().FirstOrDefault(e => e.Id == id);

        public bool Restore(string id)
        {
            var entry = Get(id);
            if (entry == null) return false;
            if (!File.Exists(entry.TrashPath) && !Directory.Exists(entry.TrashPath))
            {
                _logger.Warn($"[Trash] Restore: payload missing for {id}");
                return false;
            }

            var target = entry.OriginalPath;
            try
            {
                var parent = Path.GetDirectoryName(target);
                if (!string.IsNullOrEmpty(parent)) Directory.CreateDirectory(parent);

                if (File.Exists(target) || Directory.Exists(target))
                {
                    _logger.Error($"[Trash] Restore refused: original path already occupied ({target}).");
                    return false;
                }

                if (entry.IsDirectory)
                    MoveDirectoryCrossVolume(entry.TrashPath, target);
                else
                    File.Move(entry.TrashPath, target);

                // Drop the sidecar folder (meta.json + now-empty payload name).
                var entryDir = Path.GetDirectoryName(entry.TrashPath);
                if (!string.IsNullOrEmpty(entryDir) && Directory.Exists(entryDir))
                {
                    try { Directory.Delete(entryDir, true); } catch { }
                }
                _logger.Info($"[Trash] Restored {id} to {target}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Trash] Restore failed for {id}: {ex.Message}");
                return false;
            }
        }

        public bool PurgeOne(string id)
        {
            var entry = Get(id);
            if (entry == null) return false;
            var entryDir = Path.GetDirectoryName(entry.TrashPath);
            if (string.IsNullOrEmpty(entryDir)) return false;
            try
            {
                if (Directory.Exists(entryDir)) Directory.Delete(entryDir, true);
                _logger.Info($"[Trash] Purged {id}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Trash] Purge failed for {id}: {ex.Message}");
                return false;
            }
        }

        public int PurgeAll()
        {
            var root = GetTrashRoot();
            var count = 0;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try { Directory.Delete(dir, true); count++; }
                catch (Exception ex) { _logger.Warn($"[Trash] Could not delete {dir}: {ex.Message}"); }
            }
            _logger.Info($"[Trash] Emptied: {count} entries");
            return count;
        }

        public int PurgeExpired()
        {
            var settings = _config.LoadMediaSettings();
            var days = settings.TrashRetentionDays;
            if (days <= 0) return 0;

            var cutoff = DateTime.UtcNow.AddDays(-days);
            var count = 0;
            foreach (var entry in List())
            {
                if (entry.DeletedAt > cutoff) continue;
                if (PurgeOne(entry.Id)) count++;
            }
            if (count > 0) _logger.Info($"[Trash] Auto-purged {count} entries older than {days}d");
            return count;
        }

        private static void MoveDirectoryCrossVolume(string src, string dst)
        {
            try
            {
                Directory.Move(src, dst);
                return;
            }
            catch (IOException)
            {
                // fall through to copy+delete
            }

            CopyDirRecursive(src, dst);
            Directory.Delete(src, recursive: true);
        }

        private static void CopyDirRecursive(string src, string dst)
        {
            Directory.CreateDirectory(dst);
            foreach (var file in Directory.GetFiles(src))
                File.Copy(file, Path.Combine(dst, Path.GetFileName(file)), overwrite: true);
            foreach (var sub in Directory.GetDirectories(src))
                CopyDirRecursive(sub, Path.Combine(dst, Path.GetFileName(sub)));
        }

        private static long TryMeasure(string path)
        {
            try
            {
                if (File.Exists(path)) return new FileInfo(path).Length;
                if (Directory.Exists(path))
                    return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(f => f.Length);
            }
            catch { }
            return 0;
        }
    }
}
