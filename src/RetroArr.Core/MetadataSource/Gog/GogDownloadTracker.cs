using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;

namespace RetroArr.Core.MetadataSource.Gog
{
    public class GogDownloadTracker
    {
        private readonly ConcurrentDictionary<string, GogDownloadStatus> _downloads = new();
        private readonly ConcurrentDictionary<string, CancellationTokenSource> _cancellationTokens = new();

        public CancellationToken Start(string id, string gameTitle, string fileName, string filePath, long? totalBytes)
        {
            var cts = new CancellationTokenSource();
            _cancellationTokens[id] = cts;
            _downloads[id] = new GogDownloadStatus
            {
                Id = id,
                GameTitle = gameTitle,
                FileName = fileName,
                FilePath = filePath,
                TotalBytes = totalBytes ?? 0,
                BytesDownloaded = 0,
                State = GogDownloadState.Downloading,
                StartedAt = DateTime.UtcNow
            };
            return cts.Token;
        }

        public void Cancel(string id)
        {
            if (_cancellationTokens.TryGetValue(id, out var cts))
            {
                cts.Cancel();
            }
        }

        public void UpdateProgress(string id, long bytesDownloaded)
        {
            if (_downloads.TryGetValue(id, out var status))
            {
                status.BytesDownloaded = bytesDownloaded;
            }
        }

        public void MarkCompleted(string id)
        {
            if (_downloads.TryGetValue(id, out var status))
            {
                status.State = GogDownloadState.Completed;
                status.CompletedAt = DateTime.UtcNow;
            }
        }

        public void MarkFailed(string id, string reason)
        {
            if (_downloads.TryGetValue(id, out var status))
            {
                status.State = GogDownloadState.Failed;
                status.ErrorMessage = reason;
                status.CompletedAt = DateTime.UtcNow;
            }
        }

        public void Remove(string id)
        {
            _downloads.TryRemove(id, out _);
            if (_cancellationTokens.TryRemove(id, out var cts))
            {
                cts.Dispose();
            }
        }

        public List<GogDownloadStatus> GetAll()
        {
            return _downloads.Values.ToList();
        }

        public GogDownloadStatus? Get(string id)
        {
            return _downloads.TryGetValue(id, out var status) ? status : null;
        }
    }

    public class GogDownloadStatus
    {
        public string Id { get; set; } = string.Empty;
        public string GameTitle { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public long TotalBytes { get; set; }
        public long BytesDownloaded { get; set; }
        public GogDownloadState State { get; set; }
        public string? ErrorMessage { get; set; }
        public DateTime StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public double ProgressPercent => TotalBytes > 0 ? (double)BytesDownloaded / TotalBytes * 100 : 0;
    }

    public enum GogDownloadState
    {
        Downloading,
        Completed,
        Failed
    }
}
