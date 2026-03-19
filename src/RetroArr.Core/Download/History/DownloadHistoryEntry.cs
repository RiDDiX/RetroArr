using System;

namespace RetroArr.Core.Download.History
{
    public class DownloadHistoryEntry
    {
        public int Id { get; set; }
        public string DownloadId { get; set; } = string.Empty;
        public int ClientId { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string Title { get; set; } = string.Empty;
        public string? CleanTitle { get; set; }
        public string? Platform { get; set; }
        public long Size { get; set; }
        public DownloadHistoryState State { get; set; } = DownloadHistoryState.Imported;
        public string? Reason { get; set; }
        public string? SourcePath { get; set; }
        public string? DestinationPath { get; set; }
        public DateTime ImportedAt { get; set; } = DateTime.UtcNow;
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
        public int? GameId { get; set; }
    }

    public enum DownloadHistoryState
    {
        Imported,
        ImportFailed,
        Ignored,
        Deleted
    }
}
