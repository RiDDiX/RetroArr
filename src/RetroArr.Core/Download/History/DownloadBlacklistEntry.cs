using System;

namespace RetroArr.Core.Download.History
{
    public class DownloadBlacklistEntry
    {
        public int Id { get; set; }
        public string? DownloadId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Platform { get; set; }
        public string Reason { get; set; } = string.Empty;
        public DateTime BlacklistedAt { get; set; } = DateTime.UtcNow;
        public string? ClientName { get; set; }
    }
}
