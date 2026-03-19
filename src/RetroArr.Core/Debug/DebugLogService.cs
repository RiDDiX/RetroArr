using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace RetroArr.Core.Debug
{
    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    public class LogEntry
    {
        public DateTime Timestamp { get; set; }
        public LogLevel Level { get; set; }
        public string Category { get; set; } = string.Empty;
        public string Message { get; set; } = string.Empty;
    }

    public class ScanProgress
    {
        public bool IsScanning { get; set; }
        public string? CurrentDirectory { get; set; }
        public string? CurrentFile { get; set; }
        public string? CurrentPlatform { get; set; }
        public int FilesScanned { get; set; }
        public int GamesFound { get; set; }
        public string? LastGameFound { get; set; }
        public DateTime? ScanStartTime { get; set; }
    }

    public class DebugLogService
    {
        private readonly ConcurrentQueue<LogEntry> _logs = new();
        private const int MaxLogEntries = 1000;
        
        public ScanProgress CurrentScanProgress { get; } = new();
        
        public event Action<LogEntry>? OnLogAdded;

        public void Log(LogLevel level, string category, string message)
        {
            var entry = new LogEntry
            {
                Timestamp = DateTime.Now,
                Level = level,
                Category = category,
                Message = message
            };

            _logs.Enqueue(entry);

            // Keep log size manageable
            while (_logs.Count > MaxLogEntries)
            {
                _logs.TryDequeue(out _);
            }

            OnLogAdded?.Invoke(entry);
        }

        public void Debug(string category, string message) => Log(LogLevel.Debug, category, message);
        public void Info(string category, string message) => Log(LogLevel.Info, category, message);
        public void Warning(string category, string message) => Log(LogLevel.Warning, category, message);
        public void Error(string category, string message) => Log(LogLevel.Error, category, message);

        public void UpdateScanProgress(string? currentDirectory = null, string? currentFile = null, string? currentPlatform = null, int? filesScanned = null, int? gamesFound = null, string? lastGameFound = null)
        {
            if (currentDirectory != null) CurrentScanProgress.CurrentDirectory = currentDirectory;
            if (currentFile != null) CurrentScanProgress.CurrentFile = currentFile;
            if (currentPlatform != null) CurrentScanProgress.CurrentPlatform = currentPlatform;
            if (filesScanned.HasValue) CurrentScanProgress.FilesScanned = filesScanned.Value;
            if (gamesFound.HasValue) CurrentScanProgress.GamesFound = gamesFound.Value;
            if (lastGameFound != null) CurrentScanProgress.LastGameFound = lastGameFound;
        }

        public void StartScan()
        {
            CurrentScanProgress.IsScanning = true;
            CurrentScanProgress.ScanStartTime = DateTime.Now;
            CurrentScanProgress.FilesScanned = 0;
            CurrentScanProgress.GamesFound = 0;
            CurrentScanProgress.CurrentDirectory = null;
            CurrentScanProgress.CurrentFile = null;
            CurrentScanProgress.LastGameFound = null;
        }

        public void EndScan()
        {
            CurrentScanProgress.IsScanning = false;
            CurrentScanProgress.CurrentDirectory = null;
            CurrentScanProgress.CurrentFile = null;
        }

        public List<LogEntry> GetLogs(int count = 100, LogLevel? minLevel = null, string? category = null)
        {
            var query = _logs.AsEnumerable();

            if (minLevel.HasValue)
                query = query.Where(l => l.Level >= minLevel.Value);

            if (!string.IsNullOrEmpty(category))
                query = query.Where(l => l.Category.Equals(category, StringComparison.OrdinalIgnoreCase));

            return query.OrderByDescending(l => l.Timestamp).Take(count).ToList();
        }

        public void ClearLogs()
        {
            while (_logs.TryDequeue(out _)) { }
        }
    }
}
