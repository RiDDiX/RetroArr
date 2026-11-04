using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

namespace RetroArr.Api.V3.Reviews
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class ReviewController : ControllerBase
    {
        private readonly RetroArrDbContext _context;

        public ReviewController(RetroArrDbContext context)
        {
            _context = context;
        }

        [HttpGet("bulk")]
        public async Task<ActionResult> GetBulk([FromQuery] string gameIds)
        {
            if (string.IsNullOrWhiteSpace(gameIds))
                return Ok(new Dictionary<string, object>());

            var ids = gameIds.Split(',')
                .Select(s => int.TryParse(s.Trim(), out var id) ? id : (int?)null)
                .Where(id => id.HasValue)
                .Select(id => id!.Value)
                .Distinct()
                .ToList();

            if (ids.Count == 0)
                return Ok(new Dictionary<string, object>());

            var reviews = await _context.GameReviews
                .AsNoTracking()
                .Where(r => ids.Contains(r.GameId))
                .Select(r => new
                {
                    r.GameId,
                    r.UserRating,
                    r.MetacriticScore,
                    r.OpenCriticScore
                })
                .ToListAsync();

            var result = reviews.ToDictionary(
                r => r.GameId,
                r => new { r.UserRating, r.MetacriticScore, r.OpenCriticScore });

            return Ok(result);
        }

        [HttpGet("game/{gameId}")]
        public async Task<ActionResult<object>> GetByGameId(int gameId)
        {
            var review = await _context.GameReviews
                .Include(r => r.Game)
                .FirstOrDefaultAsync(r => r.GameId == gameId);

            if (review == null)
            {
                // Return empty review template
                return Ok(new
                {
                    GameId = gameId,
                    Notes = (string?)null,
                    UserRating = (int?)null,
                    CompletionStatus = CompletionStatus.NotPlayed,
                    PlaytimeMinutes = (int?)null,
                    IsFavorite = false,
                    IsWishlisted = false,
                    MetacriticScore = (int?)null,
                    OpenCriticScore = (int?)null,
                    HltbMainHours = (double?)null,
                    HltbCompletionistHours = (double?)null
                });
            }

            return Ok(new
            {
                review.Id,
                review.GameId,
                review.Notes,
                review.UserRating,
                review.CompletionStatus,
                CompletionStatusName = review.CompletionStatus.ToString(),
                review.PlaytimeMinutes,
                PlaytimeFormatted = FormatPlaytime(review.PlaytimeMinutes),
                review.StartedAt,
                review.CompletedAt,
                review.IsFavorite,
                review.IsWishlisted,
                review.MetacriticScore,
                review.MetacriticUrl,
                review.OpenCriticScore,
                review.OpenCriticUrl,
                review.HltbMainHours,
                review.HltbCompletionistHours,
                review.ExternalScoresFetchedAt,
                review.CreatedAt,
                review.UpdatedAt
            });
        }

        [HttpPost("game/{gameId}")]
        public async Task<ActionResult<GameReview>> CreateOrUpdate(int gameId, [FromBody] UpdateReviewRequest request)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
                return NotFound("Game not found");

            var review = await _context.GameReviews.FirstOrDefaultAsync(r => r.GameId == gameId);
            
            if (review == null)
            {
                review = new GameReview { GameId = gameId };
                _context.GameReviews.Add(review);
            }

            if (request.Notes != null) review.Notes = request.Notes;
            if (request.UserRating.HasValue) review.UserRating = request.UserRating;
            if (request.CompletionStatus.HasValue) review.CompletionStatus = request.CompletionStatus.Value;
            if (request.PlaytimeMinutes.HasValue) review.PlaytimeMinutes = request.PlaytimeMinutes;
            if (request.StartedAt.HasValue) review.StartedAt = request.StartedAt;
            if (request.CompletedAt.HasValue) review.CompletedAt = request.CompletedAt;
            if (request.IsFavorite.HasValue) review.IsFavorite = request.IsFavorite.Value;
            if (request.IsWishlisted.HasValue) review.IsWishlisted = request.IsWishlisted.Value;

            review.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(review);
        }

        [HttpPost("game/{gameId}/favorite")]
        public async Task<ActionResult> ToggleFavorite(int gameId)
        {
            var review = await GetOrCreateReview(gameId);
            if (review == null)
                return NotFound("Game not found");

            review.IsFavorite = !review.IsFavorite;
            review.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { isFavorite = review.IsFavorite });
        }

        [HttpPost("game/{gameId}/wishlist")]
        public async Task<ActionResult> ToggleWishlist(int gameId)
        {
            var review = await GetOrCreateReview(gameId);
            if (review == null)
                return NotFound("Game not found");

            review.IsWishlisted = !review.IsWishlisted;
            review.UpdatedAt = DateTime.UtcNow;
            await _context.SaveChangesAsync();

            return Ok(new { isWishlisted = review.IsWishlisted });
        }

        [HttpGet("favorites")]
        public async Task<ActionResult> GetFavorites()
        {
            var favorites = await _context.GameReviews
                .Where(r => r.IsFavorite)
                .Include(r => r.Game)
                .Select(r => new
                {
                    r.Game!.Id,
                    r.Game.Title,
                    CoverUrl = r.Game.Images.CoverUrl,
                    r.Game.Year,
                    r.Game.Rating,
                    r.UserRating,
                    r.CompletionStatus
                })
                .ToListAsync();

            return Ok(favorites);
        }

        [HttpGet("wishlist")]
        public async Task<ActionResult> GetWishlist()
        {
            var wishlist = await _context.GameReviews
                .Where(r => r.IsWishlisted)
                .Include(r => r.Game)
                .Select(r => new
                {
                    r.Game!.Id,
                    r.Game.Title,
                    CoverUrl = r.Game.Images.CoverUrl,
                    r.Game.Year,
                    r.Game.Rating
                })
                .ToListAsync();

            return Ok(wishlist);
        }

        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            // Counted in SQL, one row per completion status: loading every review to count them grew with the library
            var byStatus = await _context.GameReviews
                .GroupBy(r => r.CompletionStatus)
                .Select(grp => new
                {
                    Status = grp.Key,
                    Count = grp.Count(),
                    Favorites = grp.Sum(r => r.IsFavorite ? 1 : 0),
                    Wishlisted = grp.Sum(r => r.IsWishlisted ? 1 : 0),
                    Playtime = grp.Sum(r => r.PlaytimeMinutes ?? 0),
                    Rated = grp.Sum(r => r.UserRating != null ? 1 : 0),
                    Rating = grp.Sum(r => r.UserRating ?? 0)
                })
                .ToListAsync();
            var totalGames = await _context.Games.CountAsync();
            int Of(CompletionStatus status) => byStatus.Where(s => s.Status == status).Sum(s => s.Count);
            var rated = byStatus.Sum(s => s.Rated);

            var stats = new
            {
                TotalGames = totalGames,
                Favorites = byStatus.Sum(s => s.Favorites),
                Wishlisted = byStatus.Sum(s => s.Wishlisted),
                NotPlayed = Of(CompletionStatus.NotPlayed),
                Playing = Of(CompletionStatus.Playing),
                Completed = Of(CompletionStatus.Completed),
                Mastered = Of(CompletionStatus.Mastered),
                Dropped = Of(CompletionStatus.Dropped),
                TotalPlaytimeHours = byStatus.Sum(s => s.Playtime) / 60.0,
                AverageUserRating = rated == 0 ? (double?)null : (double)byStatus.Sum(s => s.Rating) / rated
            };

            return Ok(stats);
        }

        private async Task<GameReview?> GetOrCreateReview(int gameId)
        {
            var game = await _context.Games.FindAsync(gameId);
            if (game == null)
                return null;

            var review = await _context.GameReviews.FirstOrDefaultAsync(r => r.GameId == gameId);
            if (review == null)
            {
                review = new GameReview { GameId = gameId };
                _context.GameReviews.Add(review);
                await _context.SaveChangesAsync();
            }

            return review;
        }

        private static string? FormatPlaytime(int? minutes)
        {
            if (!minutes.HasValue || minutes.Value == 0)
                return null;

            var hours = minutes.Value / 60;
            var mins = minutes.Value % 60;

            if (hours > 0)
                return $"{hours}h {mins}m";
            return $"{mins}m";
        }
    }

    public class UpdateReviewRequest
    {
        public string? Notes { get; set; }
        public int? UserRating { get; set; }
        public CompletionStatus? CompletionStatus { get; set; }
        public int? PlaytimeMinutes { get; set; }
        public DateTime? StartedAt { get; set; }
        public DateTime? CompletedAt { get; set; }
        public bool? IsFavorite { get; set; }
        public bool? IsWishlisted { get; set; }
    }
}
