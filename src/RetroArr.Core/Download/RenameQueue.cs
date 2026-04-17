using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Download
{
    // Auto-renames downloaded files; queues uncertain matches for user confirmation.
    public class RenameQueueService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadsImport);
        private readonly List<PendingRename> _pendingRenames = new();
        private readonly object _lock = new();
        private readonly string _persistPath;
        private const double MatchThreshold = 0.80; // 80% match required for auto-rename

        public RenameQueueService(ConfigurationService configService)
        {
            _persistPath = Path.Combine(configService.GetConfigDirectory(), "rename_queue.json");
            LoadFromDisk();
        }

        public IReadOnlyList<PendingRename> PendingRenames
        {
            get
            {
                lock (_lock)
                {
                    return _pendingRenames.ToList();
                }
            }
        }

        public RenameResult ProcessFile(string filePath, string? expectedGameTitle, string? platform)
        {
            if (!File.Exists(filePath))
                return new RenameResult { Success = false, Message = "File not found" };

            var originalName = Path.GetFileName(filePath);
            var cleanedName = CleanReleaseName(originalName);
            
            // If we have an expected game title, calculate similarity
            if (!string.IsNullOrEmpty(expectedGameTitle))
            {
                var similarity = CalculateSimilarity(cleanedName, expectedGameTitle);
                
                if (similarity >= MatchThreshold)
                {
                    // Auto-rename with high confidence
                    var newFileName = GenerateCleanFileName(expectedGameTitle, Path.GetExtension(filePath), platform);
                    var newPath = Path.Combine(Path.GetDirectoryName(filePath)!, newFileName);
                    
                    try
                    {
                        if (filePath != newPath && !File.Exists(newPath))
                        {
                            File.Move(filePath, newPath);
                            _logger.Info($"[RenameQueue] Auto-renamed: {originalName} -> {newFileName} (Confidence: {similarity:P0})");
                            return new RenameResult 
                            { 
                                Success = true, 
                                AutoRenamed = true,
                                OriginalName = originalName,
                                NewName = newFileName,
                                NewPath = newPath,
                                Confidence = similarity
                            };
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[RenameQueue] Rename failed: {ex.Message}");
                    }
                }
                else
                {
                    // Add to pending queue for user confirmation
                    var suggestedName = GenerateCleanFileName(expectedGameTitle, Path.GetExtension(filePath), platform);
                    var pending = new PendingRename
                    {
                        Id = Guid.NewGuid().ToString(),
                        OriginalPath = filePath,
                        OriginalName = originalName,
                        SuggestedName = suggestedName,
                        ExpectedGameTitle = expectedGameTitle,
                        Platform = platform,
                        Confidence = similarity,
                        DateAdded = DateTime.UtcNow
                    };

                    lock (_lock)
                    {
                        _pendingRenames.Add(pending);
                        SaveToDisk();
                    }

                    _logger.Info($"[RenameQueue] Added to pending: {originalName} (Confidence: {similarity:P0})");
                    return new RenameResult
                    {
                        Success = true,
                        AutoRenamed = false,
                        PendingId = pending.Id,
                        OriginalName = originalName,
                        SuggestedName = suggestedName,
                        Confidence = similarity
                    };
                }
            }

            return new RenameResult { Success = true, AutoRenamed = false, OriginalName = originalName };
        }

        public bool ApproveRename(string pendingId, string? customName = null)
        {
            PendingRename? pending;
            lock (_lock)
            {
                pending = _pendingRenames.FirstOrDefault(p => p.Id == pendingId);
                if (pending == null) return false;
            }

            var newName = !string.IsNullOrEmpty(customName) ? customName : pending.SuggestedName;
            var newPath = Path.Combine(Path.GetDirectoryName(pending.OriginalPath)!, newName);

            try
            {
                if (File.Exists(pending.OriginalPath) && !File.Exists(newPath))
                {
                    File.Move(pending.OriginalPath, newPath);
                    _logger.Info($"[RenameQueue] User approved rename: {pending.OriginalName} -> {newName}");
                    
                    lock (_lock)
                    {
                        _pendingRenames.Remove(pending);
                        SaveToDisk();
                    }
                    return true;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[RenameQueue] Approve rename failed: {ex.Message}");
            }

            return false;
        }

        // keeps the original name
        public bool RejectRename(string pendingId)
        {
            lock (_lock)
            {
                var pending = _pendingRenames.FirstOrDefault(p => p.Id == pendingId);
                if (pending != null)
                {
                    _pendingRenames.Remove(pending);
                    SaveToDisk();
                    _logger.Info($"[RenameQueue] User rejected rename: {pending.OriginalName}");
                    return true;
                }
            }
            return false;
        }

        // strips scene tags, release groups, etc.
        private static string CleanReleaseName(string name)
        {
            // Remove extension
            name = Path.GetFileNameWithoutExtension(name);
            
            // Common scene/release group patterns to remove
            var patterns = new[]
            {
                @"\[.*?\]", // [Group]
                @"\(.*?\)", // (info)
                @"-[A-Z0-9]+$", // -GROUPNAME
                @"\.v?\d+\.\d+.*", // .v1.0.0
                @"(?i)\.(repack|multi|gog|plaza|codex|skidrow|goty|deluxe|edition|setup|install).*",
                @"(?i)(repack|multi|gog|plaza|codex|skidrow|fitgirl|dodi).*",
                @"_", // Underscores to spaces
                @"\.", // Dots to spaces (before final cleanup)
            };

            foreach (var pattern in patterns)
            {
                name = Regex.Replace(name, pattern, " ", RegexOptions.IgnoreCase);
            }

            // Clean up whitespace
            name = Regex.Replace(name, @"\s+", " ").Trim();
            
            return name;
        }

        private static string GenerateCleanFileName(string gameTitle, string extension, string? platform)
        {
            // Sanitize title
            var invalidChars = Path.GetInvalidFileNameChars();
            var cleanTitle = new string(gameTitle.Where(c => !invalidChars.Contains(c)).ToArray());
            
            // Add platform suffix if provided
            if (!string.IsNullOrEmpty(platform))
            {
                return $"{cleanTitle} [{platform}]{extension}";
            }
            
            return $"{cleanTitle}{extension}";
        }

        // Levenshtein-based
        private static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0;

            source = source.ToLowerInvariant();
            target = target.ToLowerInvariant();

            if (source == target) return 1.0;

            int sourceLength = source.Length;
            int targetLength = target.Length;

            if (sourceLength == 0) return 0;
            if (targetLength == 0) return 0;

            var distance = new int[sourceLength + 1, targetLength + 1];

            for (int i = 0; i <= sourceLength; i++)
                distance[i, 0] = i;

            for (int j = 0; j <= targetLength; j++)
                distance[0, j] = j;

            for (int i = 1; i <= sourceLength; i++)
            {
                for (int j = 1; j <= targetLength; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            int levenshteinDistance = distance[sourceLength, targetLength];
            int maxLength = Math.Max(sourceLength, targetLength);
            
            return 1.0 - (double)levenshteinDistance / maxLength;
        }

        private void LoadFromDisk()
        {
            try
            {
                if (File.Exists(_persistPath))
                {
                    var json = File.ReadAllText(_persistPath);
                    var items = JsonSerializer.Deserialize<List<PendingRename>>(json);
                    if (items != null)
                    {
                        lock (_lock)
                        {
                            _pendingRenames.Clear();
                            _pendingRenames.AddRange(items);
                        }
                        _logger.Info($"[RenameQueue] Loaded {items.Count} pending renames from disk.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[RenameQueue] Warning: Could not load persisted queue: {ex.Message}");
            }
        }

        private void SaveToDisk()
        {
            try
            {
                var json = JsonSerializer.Serialize(_pendingRenames, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_persistPath, json);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[RenameQueue] Warning: Could not persist queue: {ex.Message}");
            }
        }
    }

    public class PendingRename
    {
        public string Id { get; set; } = string.Empty;
        public string OriginalPath { get; set; } = string.Empty;
        public string OriginalName { get; set; } = string.Empty;
        public string SuggestedName { get; set; } = string.Empty;
        public string? ExpectedGameTitle { get; set; }
        public string? Platform { get; set; }
        public double Confidence { get; set; }
        public DateTime DateAdded { get; set; }
    }

    public class RenameResult
    {
        public bool Success { get; set; }
        public bool AutoRenamed { get; set; }
        public string? PendingId { get; set; }
        public string? OriginalName { get; set; }
        public string? NewName { get; set; }
        public string? SuggestedName { get; set; }
        public string? NewPath { get; set; }
        public double Confidence { get; set; }
        public string? Message { get; set; }
    }
}
