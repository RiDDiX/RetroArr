using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Games
{
    public class Game
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public int Year { get; set; }
        public string? Overview { get; set; }
        public string? Storyline { get; set; }
        public int PlatformId { get; set; }
        public Platform? Platform { get; set; }
        public DateTime Added { get; set; }
        
        // Visual Assets
        public GameImages Images { get; set; } = new();
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<string> Genres { get; set; } = new();
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<string> AvailablePlatforms { get; set; } = new();
        public string? Developer { get; set; }
        public string? Publisher { get; set; }
        public DateTime? ReleaseDate { get; set; }
        public double? Rating { get; set; } // 0-100 from IGDB
        public int? RatingCount { get; set; }
        
        public GameStatus Status { get; set; }
        public bool Monitored { get; set; }
        public string? Path { get; set; }
        public long? SizeOnDisk { get; set; }

        // When the scanner first noticed the files were gone. Null = present.
        // Two-stage cleanup: flag now, purge after retention window expires.
        public DateTime? MissingSince { get; set; }
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<GameFile> GameFiles { get; set; } = new();
        
        // Metadata IDs
        public int? IgdbId { get; set; }
        public int? SteamId { get; set; }
        public string? GogId { get; set; }
        public string? EpicId { get; set; }

        // Metadata match confidence and review state
        public double? MatchConfidence { get; set; }
        public bool MetadataConfirmedByUser { get; set; }
        public DateTime? MetadataConfirmedAt { get; set; }
        public bool NeedsMetadataReview { get; set; }
        public string? MetadataReviewReason { get; set; }
        public string? InstallPath { get; set; }
        public bool IsInstallable { get; set; }
        
        // Launcher V2
        public string? ExecutablePath { get; set; }
        public bool IsExternal { get; set; }
        
        // Region/country code detected from filename or metadata (e.g. "USA", "Europe", "Japan")
        public string? Region { get; set; }
        
        // Comma-separated language codes detected from filename (e.g. "En, Fr, De, Es, It")
        public string? Languages { get; set; }
        
        // Revision/variant detected from filename (e.g. "Rev A", "Beta", "Disc 1", "v2.00")
        public string? Revision { get; set; }
        
        // Linux runner preference (wine, proton, native). Null = auto-detect.
        public string? PreferredRunner { get; set; }
        
        // ProtonDB compatibility tier (platinum, gold, silver, bronze, borked, native, pending)
        public string? ProtonDbTier { get; set; }

        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public bool IsOwned { get; set; }
        
        public string? MetadataSource { get; set; } // "IGDB" or "ScreenScraper"

        // Installer tracking (GOG downloads, etc.)
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public string? InstallerPath { get; set; }
        
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public InstallerStatus InstallerStatus { get; set; } = InstallerStatus.NotFound;

        // Update files tracking (Switch updates, DLCs, etc.)
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        [System.ComponentModel.DataAnnotations.Schema.NotMapped]
        public List<GameUpdateFile> UpdateFiles { get; set; } = new();
    }

    public enum InstallerStatus
    {
        NotFound,
        Found,
        Multiple // Multiple installers found
    }

    public class GameUpdateFile
    {
        public string FileName { get; set; } = string.Empty;
        public string FilePath { get; set; } = string.Empty;
        public string? Version { get; set; }
        public long Size { get; set; }
        public DateTime? DateAdded { get; set; }
        public UpdateFileType Type { get; set; } = UpdateFileType.Update;
    }

    public enum UpdateFileType
    {
        Update,
        DLC,
        Patch
    }
    
    public class GameImages
    {
        public string? CoverUrl { get; set; }
        public string? CoverLargeUrl { get; set; }
        public string? BackgroundUrl { get; set; }
        [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
        public string? BannerUrl { get; set; }         // Banner horizontal (wheel/marquee)
        public string? BoxBackUrl { get; set; }        // Box back cover
        public string? VideoUrl { get; set; }          // Gameplay video
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<string> Screenshots { get; set; } = new();
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<string> Artworks { get; set; } = new();
    }

    public enum GameStatus
    {
        TBA,
        Announced,
        Released,
        Downloading,
        Downloaded,
        Missing,
        InstallerDetected // Found setup.exe but not game.exe
    }
}
