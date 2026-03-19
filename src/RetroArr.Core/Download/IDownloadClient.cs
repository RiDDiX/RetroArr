using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetroArr.Core.Download
{
    public interface IDownloadClient
    {
        Task<bool> TestConnectionAsync();
        Task<string> GetVersionAsync();
        Task<bool> AddTorrentAsync(string url, string? category = null);
        Task<bool> AddNzbAsync(string url, string? category = null);
        Task<bool> RemoveDownloadAsync(string id);
        Task<bool> PauseDownloadAsync(string id);
        Task<bool> ResumeDownloadAsync(string id);
        Task<List<DownloadStatus>> GetDownloadsAsync();
    }

    public class DownloadStatus
    {
        public int ClientId { get; set; }
        public string Id { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public float Progress { get; set; } // 0.0 to 1.0 (or 100.0 depending on client, we should normalize)
        public DownloadState State { get; set; }
        public string? Category { get; set; }
        public string? DownloadPath { get; set; }
        public string ClientName { get; set; } = string.Empty;
        public string? PlatformFolder { get; set; }
        public int? GameId { get; set; }
        public string? GameTitle { get; set; }
        public string? ImportSubfolder { get; set; }
        public string? TrackedState { get; set; }
        public List<string> StatusMessages { get; set; } = new();
    }

    public enum DownloadState
    {
        Downloading,
        Paused,
        Completed,
        Error,
        Queued,
        Checking = 5,
        Deleted = 6,
        Importing = 7,
        Unknown
    }
}
