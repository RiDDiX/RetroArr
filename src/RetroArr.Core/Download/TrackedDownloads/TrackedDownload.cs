using System;
using System.Collections.Generic;

namespace RetroArr.Core.Download.TrackedDownloads
{
    /// <summary>
    /// Tracks a download through its lifecycle.
    /// State machine: Downloading → ImportPending → Importing → Imported | ImportFailed | Ignored
    /// </summary>
    public class TrackedDownload
    {
        public string DownloadId { get; set; } = string.Empty;
        public int DownloadClientId { get; set; }
        public string DownloadClientName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? OutputPath { get; set; }
        public string? Category { get; set; }
        public string? PlatformFolder { get; set; }
        public int? GameId { get; set; }
        public string? ImportSubfolder { get; set; }
        public long Size { get; set; }
        public float Progress { get; set; }

        public TrackedDownloadState State { get; set; } = TrackedDownloadState.Downloading;
        public TrackedDownloadStatus Status { get; set; } = TrackedDownloadStatus.Ok;
        public List<string> StatusMessages { get; set; } = new();
        public DateTime Added { get; set; } = DateTime.UtcNow;
        public DateTime? ImportedAt { get; set; }
        public bool CanBeRemoved { get; set; }
        public bool IsUnmapped { get; set; }

        public void Warn(string message)
        {
            Status = TrackedDownloadStatus.Warning;
            if (!StatusMessages.Contains(message))
                StatusMessages.Add(message);
        }

        public void ClearWarnings()
        {
            Status = TrackedDownloadStatus.Ok;
            StatusMessages.Clear();
        }

        public void MarkImportPending()
        {
            State = TrackedDownloadState.ImportPending;
            ClearWarnings();
        }

        public void MarkImporting()
        {
            State = TrackedDownloadState.Importing;
        }

        public void MarkImported()
        {
            State = TrackedDownloadState.Imported;
            Status = TrackedDownloadStatus.Ok;
            ImportedAt = DateTime.UtcNow;
            CanBeRemoved = true;
        }

        public void MarkFailed(string reason)
        {
            State = TrackedDownloadState.ImportFailed;
            Status = TrackedDownloadStatus.Error;
            StatusMessages.Clear();
            StatusMessages.Add(reason);
        }

        public void MarkIgnored()
        {
            State = TrackedDownloadState.Ignored;
            Status = TrackedDownloadStatus.Ok;
        }
    }

    public enum TrackedDownloadState
    {
        Downloading,
        ImportPending,
        ImportBlocked,
        Importing,
        Imported,
        ImportFailed,
        Ignored
    }

    public enum TrackedDownloadStatus
    {
        Ok,
        Warning,
        Error
    }
}
