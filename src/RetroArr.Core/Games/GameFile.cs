using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Games
{
    public class GameFile
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public Game? Game { get; set; }
        public string RelativePath { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime DateAdded { get; set; }
        public string? Quality { get; set; }
        public string? ReleaseGroup { get; set; }
        public string? Edition { get; set; }
        public string FileType { get; set; } = "Main"; // "Main", "Patch", or "DLC"
        public string? Version { get; set; }
        public string? ContentName { get; set; }
        public string? TitleId { get; set; }
        public string? Serial { get; set; }
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<string> Languages { get; set; } = new();
    }
}
