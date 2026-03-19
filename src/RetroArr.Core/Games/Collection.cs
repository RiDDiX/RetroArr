using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Games
{
    /// <summary>
    /// User-defined collection/playlist for organizing games
    /// </summary>
    public class Collection
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(100)]
        public string Name { get; set; } = string.Empty;
        
        [MaxLength(500)]
        public string? Description { get; set; }
        
        /// <summary>
        /// Icon name or emoji for the collection
        /// </summary>
        [MaxLength(50)]
        public string? Icon { get; set; }
        
        /// <summary>
        /// Custom color for the collection (hex format)
        /// </summary>
        [MaxLength(7)]
        public string? Color { get; set; }
        
        /// <summary>
        /// Cover image URL (can be from a game in the collection)
        /// </summary>
        public string? CoverUrl { get; set; }
        
        /// <summary>
        /// Sort order for display
        /// </summary>
        public int SortOrder { get; set; }
        
        /// <summary>
        /// Whether this is a smart/auto collection based on rules
        /// </summary>
        public bool IsSmartCollection { get; set; }
        
        /// <summary>
        /// JSON rules for smart collections (e.g., genre filters, platform filters)
        /// </summary>
        public string? SmartRules { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<CollectionGame> CollectionGames { get; set; } = new();
    }
    
    /// <summary>
    /// Many-to-many relationship between Collections and Games
    /// </summary>
    public class CollectionGame
    {
        public int Id { get; set; }
        public int CollectionId { get; set; }
        public Collection? Collection { get; set; }
        public int GameId { get; set; }
        public Game? Game { get; set; }
        
        /// <summary>
        /// Sort order within the collection
        /// </summary>
        public int SortOrder { get; set; }
        
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
