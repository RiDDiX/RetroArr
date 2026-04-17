using System;
using System.ComponentModel.DataAnnotations;

namespace RetroArr.Core.Games
{
    public class GameReview
    {
        public int Id { get; set; }

        public int GameId { get; set; }
        public Game? Game { get; set; }

        public string? Notes { get; set; }

        // 0-100
        public int? UserRating { get; set; }

        public CompletionStatus CompletionStatus { get; set; } = CompletionStatus.NotPlayed;

        public int? PlaytimeMinutes { get; set; }

        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }

        public bool IsFavorite { get; set; }

        // games not yet owned
        public bool IsWishlisted { get; set; }

        // External scores (cached from APIs)
        public int? MetacriticScore { get; set; }
        public string? MetacriticUrl { get; set; }
        public int? OpenCriticScore { get; set; }
        public string? OpenCriticUrl { get; set; }
        public double? HltbMainHours { get; set; }
        public double? HltbCompletionistHours { get; set; }
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
