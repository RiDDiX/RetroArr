using System;
using System.ComponentModel.DataAnnotations;

namespace RetroArr.Core.Games
{
    /// <summary>
    /// User review/notes for a game, plus external review scores
    /// </summary>
    public class GameReview
    {
        public int Id { get; set; }
        
        public int GameId { get; set; }
        public Game? Game { get; set; }
        
        /// <summary>
        /// User's personal notes about the game
        /// </summary>
        public string? Notes { get; set; }
        
        /// <summary>
        /// User's personal rating (0-100)
        /// </summary>
        public int? UserRating { get; set; }
        
        /// <summary>
        /// User's completion status
        /// </summary>
        public CompletionStatus CompletionStatus { get; set; } = CompletionStatus.NotPlayed;
        
        /// <summary>
        /// User's playtime in minutes (manual entry or tracked)
        /// </summary>
        public int? PlaytimeMinutes { get; set; }
        
        /// <summary>
        /// Date when the user started playing
        /// </summary>
        public DateTime? StartedAt { get; set; }
        
        /// <summary>
        /// Date when the user completed the game
        /// </summary>
        public DateTime? CompletedAt { get; set; }
        
        /// <summary>
        /// Whether this game is a favorite
        /// </summary>
        public bool IsFavorite { get; set; }
        
        /// <summary>
        /// Whether this game is on the wishlist (for games not yet owned)
        /// </summary>
        public bool IsWishlisted { get; set; }
        
        // External review scores (cached from APIs)
        
        /// <summary>
        /// Metacritic score (0-100)
        /// </summary>
        public int? MetacriticScore { get; set; }
        
        /// <summary>
        /// Metacritic URL
        /// </summary>
        public string? MetacriticUrl { get; set; }
        
        /// <summary>
        /// OpenCritic score (0-100)
        /// </summary>
        public int? OpenCriticScore { get; set; }
        
        /// <summary>
        /// OpenCritic URL
        /// </summary>
        public string? OpenCriticUrl { get; set; }
        
        /// <summary>
        /// HowLongToBeat main story time in hours
        /// </summary>
        public double? HltbMainHours { get; set; }
        
        /// <summary>
        /// HowLongToBeat completionist time in hours
        /// </summary>
        public double? HltbCompletionistHours { get; set; }
        
        /// <summary>
        /// Last time external scores were fetched
        /// </summary>
        public DateTime? ExternalScoresFetchedAt { get; set; }
        
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
        public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
    }
    
    public enum CompletionStatus
    {
        NotPlayed = 0,
        Playing = 1,
        OnHold = 2,
        Dropped = 3,
        Completed = 4,
        Mastered = 5  // 100% completion
    }
}
