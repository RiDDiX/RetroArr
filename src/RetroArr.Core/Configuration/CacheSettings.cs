namespace RetroArr.Core.Configuration
{
    public class CacheSettings
    {
        public bool Enabled { get; set; } = false;
        public string ConnectionString { get; set; } = "localhost:6379";
        // Short TTLs by default — every write path already invalidates caches
        // via CachedGameRepository, so these are just a safety net against
        // a missed invalidation. Keeping them small makes the dashboard feel
        // fresh even when a SignalR event was dropped.
        public int LibraryListTtlSeconds { get; set; } = 15;
        public int GameDetailTtlSeconds { get; set; } = 60;
        public int MetadataTtlSeconds { get; set; } = 3600;
        public int DownloadStatusTtlSeconds { get; set; } = 10;
        public int DbStatsTtlSeconds { get; set; } = 20;
    }
}
