using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Games
{
    public class Collection
    {
        public int Id { get; set; }

        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;

        [MaxLength(500)]
        public string? Description { get; set; }

        // icon name or emoji
        [MaxLength(50)]
        public string? Icon { get; set; }

        // hex, e.g. #ff6b35
        [MaxLength(7)]
        public string? Color { get; set; }

        public string? CoverUrl { get; set; }

        public int SortOrder { get; set; }

        // smart collections resolve games via SmartRules instead of CollectionGames
        public bool IsSmartCollection { get; set; }

        // JSON, e.g. genre/platform filters
        public string? SmartRules { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<CollectionGame> CollectionGames { get; set; } = new();
    }

    public class CollectionGame
    {
        public int Id { get; set; }
        public int CollectionId { get; set; }
        public Collection? Collection { get; set; }
        public int GameId { get; set; }
        public Game? Game { get; set; }

        public int SortOrder { get; set; }

        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
