using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Games
{
    /// <summary>
    /// User-defined tags for categorizing games
    /// </summary>
    public class Tag
    {
        public int Id { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string Name { get; set; } = string.Empty;
        
        /// <summary>
        /// Color for the tag badge (hex format)
        /// </summary>
        [MaxLength(7)]
        public string Color { get; set; } = "#6c7086";
        
        /// <summary>
        /// Icon name for the tag (optional)
        /// </summary>
        [MaxLength(50)]
        public string? Icon { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public List<GameTag> GameTags { get; set; } = new();
    }
    
    /// <summary>
    /// Many-to-many relationship between Games and Tags
    /// </summary>
    public class GameTag
    {
        public int Id { get; set; }
        public int GameId { get; set; }
        public Game? Game { get; set; }
        public int TagId { get; set; }
        public Tag? Tag { get; set; }
        public DateTime AddedAt { get; set; } = DateTime.UtcNow;
    }
}
