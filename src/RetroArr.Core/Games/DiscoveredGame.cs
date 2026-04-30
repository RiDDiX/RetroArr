using System;

namespace RetroArr.Core.Games
{
    /// <summary>
    /// A scanner candidate that has not been imported into the library yet.
    /// Persists across scans so the user can pick which ones to actually pull
    /// metadata for. Cleared item-by-item on import or via "discard all".
    /// </summary>
    public class DiscoveredGame
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
        public string? PlatformKey { get; set; }
        public int? PlatformId { get; set; }
        public string? Serial { get; set; }
        public string? ExecutablePath { get; set; }
        public bool IsExternal { get; set; }
        public bool IsInstaller { get; set; }
        public string? Region { get; set; }
        public string? Languages { get; set; }
        public string? Revision { get; set; }
        public DateTime DiscoveredAt { get; set; } = DateTime.UtcNow;
    }
}
