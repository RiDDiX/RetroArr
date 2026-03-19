using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download
{
    public class DownloadClient
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Implementation { get; set; } = string.Empty; // qBittorrent, Transmission, etc
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? Category { get; set; }
        
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        public string? UrlBase { get; set; }
        
        public string? ApiKey { get; set; }
        public bool Enable { get; set; } = true;
        public bool UseSsl { get; set; }
        public int Priority { get; set; }
        
        // Remote Path Mapping
        public string? RemotePathMapping { get; set; }
        public string? LocalPathMapping { get; set; }
    }
}
