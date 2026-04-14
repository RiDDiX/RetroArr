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
        public GameStatus Status { get; set; }
        public int? SteamId { get; set; }
        public string? Path { get; set; }
        public string? Region { get; set; }
        public string? Languages { get; set; }
        public string? Revision { get; set; }
        public int? IgdbId { get; set; }
        public string? ProtonDbTier { get; set; }
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
