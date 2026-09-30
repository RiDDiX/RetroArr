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
        // Until the source is gone, and for good when the move stops halfway: the entry may hold the only copy of part
        // of the payload, so it is listed but never purged with the rest, only put back or deleted by hand
        public bool Incomplete { get; set; }
    }

    // Part of a payload is in this entry, the rest is still where it was
    public class TrashPartialMoveException : IOException
    {
        public TrashEntry Entry { get; }

        public TrashPartialMoveException(string message, TrashEntry entry, Exception inner) : base(message, inner) => Entry = entry;
    }

    public class TrashService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly ConfigurationService _config;

        // internal for unit tests: a rename that fails as it does across volumes, a delete that stops halfway, a full volume
        internal Action<string, string> Rename = Directory.Move;
        internal Action<string> Delete = path =>
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
            else File.Delete(path);
        };
        internal Func<string, long> FreeSpace = path => new DriveInfo(path).AvailableFreeSpace;

        public TrashService(ConfigurationService config)
        {
            _config = config;
        }

        // Entries are folders named like 20260929-183000_1a2b3c4d, nothing else in the trash folder is RetroArr's
        private static readonly System.Text.RegularExpressions.Regex _entryName = new(@"^\d{8}-\d{6}_[0-9a-f]{8}$");

        private static bool IsEntry(string dir) => _entryName.IsMatch(Path.GetFileName(dir));

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
            // the sidecar would overwrite it
            if (payloadName.Equals("meta.json", StringComparison.OrdinalIgnoreCase)) payloadName += ".payload";
            var destPath = Path.Combine(entryDir, payloadName);
            var entry = new TrashEntry
            {
                Id = entryId,
                GameId = gameId,
                GameTitle = gameTitle,
                OriginalPath = sourcePath,
                TrashPath = destPath,
                DeletedAt = DateTime.UtcNow,
                IsDirectory = isDir,
                SizeBytes = TryMeasure(sourcePath),
                Incomplete = true,
            };

            // The sidecar comes first: a payload the trash can't list could never be put back
            try
            {
                await WriteSidecarAsync(entryDir, entry);
            }
            catch (Exception ex)
            {
                try { Directory.Delete(entryDir, true); } catch { }
                throw new IOException($"Could not write the trash entry for {sourcePath}, nothing was moved: {ex.Message}", ex);
            }

            try
            {
                Move(sourcePath, destPath);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Trash] Move failed {sourcePath} -> {destPath}: {ex.Message}");
                // Nothing reached the trash, the source is as it was
                if (!File.Exists(destPath) && !Directory.Exists(destPath))
                {
                    try { Directory.Delete(entryDir, true); } catch { }
                    throw;
                }
                // What got to the trash stays listed there, so it can be found and put back
                throw new TrashPartialMoveException($"{sourcePath} could not be moved to the trash whole, what got there is kept in trash entry {entryId}: {ex.Message}", entry, ex);
            }

            // The source is gone, the entry holds all of it
            entry.Incomplete = false;
            try { await WriteSidecarAsync(entryDir, entry); }
            catch (Exception ex) { _logger.Warn($"[Trash] Could not mark trash entry {entryId} complete, it stays until deleted by hand: {ex.Message}"); }

            _logger.Info($"[Trash] {(isDir ? "Directory" : "File")} moved to trash: {sourcePath} (entry {entryId})");
            return entry;
        }

        private static Task WriteSidecarAsync(string entryDir, TrashEntry entry) =>
            File.WriteAllTextAsync(Path.Combine(entryDir, "meta.json"), JsonSerializer.Serialize(entry, new JsonSerializerOptions { WriteIndented = true }));

        public List<TrashEntry> List()
        {
            var root = GetTrashRoot();
            var entries = new List<TrashEntry>();
            if (!Directory.Exists(root)) return entries;

            foreach (var dir in Directory.EnumerateDirectories(root).Where(IsEntry))
            {
                var sidecar = Path.Combine(dir, "meta.json");
                if (!File.Exists(sidecar)) continue;
                try
                {
                    var entry = JsonSerializer.Deserialize<TrashEntry>(File.ReadAllText(sidecar));
                    var payload = Path.GetFileName(entry?.TrashPath);
                    if (entry == null || string.IsNullOrEmpty(payload)) continue;
                    // The payload lies next to its sidecar, so a restore or purge never reaches outside the entry
                    entry.TrashPath = Path.Combine(dir, payload);
                    entries.Add(entry);
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

                Move(entry.TrashPath, target);

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
            return entry != null && Purge(entry);
        }

        private static bool Purge(TrashEntry entry)
        {
            var entryDir = Path.GetDirectoryName(entry.TrashPath);
            if (string.IsNullOrEmpty(entryDir)) return false;
            try
            {
                if (Directory.Exists(entryDir)) Directory.Delete(entryDir, true);
                _logger.Info($"[Trash] Purged {entry.Id}");
                return true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Trash] Purge failed for {entry.Id}: {ex.Message}");
                return false;
            }
        }

        // What the trash lists as moved whole. An incomplete entry stays, only the user puts it back or deletes it.
        public int PurgeAll()
        {
            var count = 0;
            var kept = 0;
            foreach (var entry in List())
            {
                if (entry.Incomplete) kept++;
                else if (Purge(entry)) count++;
            }
            _logger.Info($"[Trash] Emptied: {count} entries{(kept > 0 ? $", kept {kept} whose move did not complete" : "")}");
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
                if (entry.DeletedAt > cutoff || entry.Incomplete) continue;
                if (Purge(entry)) count++;
            }
            if (count > 0) _logger.Info($"[Trash] Auto-purged {count} entries older than {days}d");
            return count;
        }

        // Across volumes a move is a copy and a delete. The copy only starts when it fits, and when the delete stops
        // halfway the source gets back what it lost before the copy goes, so the payload is whole on one side at all times.
        private void Move(string src, string dst)
        {
            try
            {
                // Renames files too and, unlike File.Move, never copies to another volume by itself
                Rename(src, dst);
                return;
            }
            catch (IOException)
            {
                // another volume
            }

            var need = TryMeasure(src);
            var dir = Path.GetDirectoryName(Path.GetFullPath(dst))!;
            long free;
            try { free = FreeSpace(dir); }
            catch { free = long.MaxValue; }
            if (need > free)
                throw new IOException($"Not enough free space in {dir}: {need / 1048576.0:0.#} MB needed, {free / 1048576.0:0.#} MB free.");

            try
            {
                Copy(src, dst, strict: true);
            }
            catch
            {
                try { Delete(dst); } catch { }
                throw;
            }

            Exception? lastDeleteError = null;
            for (int attempt = 1; attempt <= 3; attempt++)
            {
                try
                {
                    Delete(src);
                    return;
                }
                catch (Exception ex)
                {
                    lastDeleteError = ex;
                    System.Threading.Thread.Sleep(250 * attempt);
                }
            }

            Copy(dst, src, strict: false);
            Delete(dst);
            throw new IOException($"Could not remove {src} after copying it, it stays where it was: {lastDeleteError?.Message}", lastDeleteError);
        }

        // Copies src to dst. A link is copied as the link and never followed, so a restore brings the link back and a
        // link to a folder above can't loop. The copy of a payload is strict: a name that is there already fails it, as
        // a volume that ignores case folds Data and data into one and the source would then go with one of them lost.
        // The copy back into a source only fills in what it lacks.
        private static void Copy(string src, string dst, bool strict)
        {
            var taken = File.Exists(dst) || Directory.Exists(dst);
            if (taken && strict)
                throw new IOException($"{dst} is there already: that volume doesn't tell apart names that differ only in case or form");
            var link = LinkTarget(src);
            if (link != null)
            {
                if (!taken)
                {
                    if (Directory.Exists(src)) Directory.CreateSymbolicLink(dst, link);
                    else File.CreateSymbolicLink(dst, link);
                }
                return;
            }
            if (File.Exists(src))
            {
                if (!taken) File.Copy(src, dst);
                return;
            }
            Directory.CreateDirectory(dst);
            foreach (var entry in Directory.GetFileSystemEntries(src))
                Copy(entry, Path.Combine(dst, Path.GetFileName(entry)), strict);
        }

        private static string? LinkTarget(string path) =>
            (Directory.Exists(path) ? (FileSystemInfo)new DirectoryInfo(path) : new FileInfo(path)).LinkTarget;

        // Links take no room, they are copied as links
        private static long TryMeasure(string path)
        {
            try
            {
                if (LinkTarget(path) != null) return 0;
                if (File.Exists(path)) return new FileInfo(path).Length;
                if (Directory.Exists(path))
                    return new DirectoryInfo(path).EnumerateFiles("*", new EnumerationOptions { RecurseSubdirectories = true, AttributesToSkip = FileAttributes.ReparsePoint })
                        .Sum(f => f.Length);
            }
            catch { }
            return 0;
        }
    }
}
