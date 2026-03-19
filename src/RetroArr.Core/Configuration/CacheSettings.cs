namespace RetroArr.Core.Configuration
{
    public class CacheSettings
    {
        public bool Enabled { get; set; } = false;
        public string ConnectionString { get; set; } = "localhost:6379";
        public int LibraryListTtlSeconds { get; set; } = 60;
        public int GameDetailTtlSeconds { get; set; } = 120;
        public int MetadataTtlSeconds { get; set; } = 3600;
        public int DownloadStatusTtlSeconds { get; set; } = 30;
        public int DbStatsTtlSeconds { get; set; } = 300;
    }
}
