using System;
using System.Collections.Generic;

namespace RetroArr.Core.Games
{
    public class GameListDto
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int Year { get; set; }
        public string? CoverUrl { get; set; }
        public double? Rating { get; set; }
        public List<string> Genres { get; set; } = new();
        public int PlatformId { get; set; }
        public string? PlatformName { get; set; }
        public string? PlatformSlug { get; set; }
        // The library list compares names ("Downloaded"), the full Game keeps sending numbers
        [Newtonsoft.Json.JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public GameStatus Status { get; set; }
        public int? SteamId { get; set; }
        public string? Path { get; set; }
        public string? Region { get; set; }
        public string? Languages { get; set; }
        public string? Revision { get; set; }
        public int? IgdbId { get; set; }
        public string? ProtonDbTier { get; set; }
        public DateTime? MissingSince { get; set; }
        public bool Monitored { get; set; }
    }

    public class GameProblemRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Path { get; set; }
        public int PlatformId { get; set; }
        public string? Region { get; set; }
        public DateTime? MissingSince { get; set; }
        public DateTime? ProblemResolvedAt { get; set; }
        // No IGDB id and no description
        public bool NoMetadata { get; set; }
    }

    public class GamePathRow
    {
        public int Id { get; set; }
        public string? Path { get; set; }
        public string? ExecutablePath { get; set; }
    }

    public class GameReviewRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public string? Path { get; set; }
        public int PlatformId { get; set; }
        public double? MatchConfidence { get; set; }
        public string? MetadataReviewReason { get; set; }
        public int? IgdbId { get; set; }
        public string? CoverUrl { get; set; }
        public DateTime Added { get; set; }
    }

    public class GameMetadataRow
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int PlatformId { get; set; }
        // No IGDB id or no description
        public bool Incomplete { get; set; }
        public string? MetadataSource { get; set; }
        public bool MetadataConfirmedByUser { get; set; }
    }

    public class PagedResult<T>
    {
        public List<T> Items { get; set; } = new();
        public int Page { get; set; }
        public int PageSize { get; set; }
        public int TotalItems { get; set; }
        public int TotalPages { get; set; }
    }
}
