using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;

namespace RetroArr.Api.V3.Metadata
{
    [ApiController]
    [Route("api/v3/metadata/review")]
    public class MetadataReviewController : ControllerBase
    {
        private readonly IGameRepository _gameRepository;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;

        public MetadataReviewController(IGameRepository gameRepository, IGameMetadataServiceFactory metadataServiceFactory)
        {
            _gameRepository = gameRepository;
            _metadataServiceFactory = metadataServiceFactory;
        }

        /// <summary>
        /// Get all games that need metadata review.
        /// </summary>
        [HttpGet]
        public async Task<ActionResult<List<MetadataReviewItem>>> GetReviewQueue([FromQuery] string? platformFilter = null)
        {
            var allGames = await _gameRepository.GetAllLightAsync();
            var needsReview = allGames
                .Where(g => g.NeedsMetadataReview && !g.MetadataConfirmedByUser)
                .Select(g =>
                {
                    if (g.PlatformId > 0 && g.Platform == null)
                    {
                        g.Platform = PlatformDefinitions.AllPlatforms
                            .FirstOrDefault(p => p.Id == g.PlatformId);
                    }
                    return new MetadataReviewItem
                    {
                        GameId = g.Id,
                        Title = g.Title,
                        AlternativeTitle = g.AlternativeTitle,
                        PlatformId = g.PlatformId,
                        PlatformName = g.Platform?.Name ?? "Unknown",
                        PlatformSlug = g.Platform?.Slug,
                        MatchConfidence = g.MatchConfidence,
                        ReviewReason = g.MetadataReviewReason ?? "Unknown",
                        CurrentIgdbId = g.IgdbId,
                        CoverUrl = g.Images?.CoverUrl,
                        Added = g.Added
                    };
                })
                .ToList();

            if (!string.IsNullOrEmpty(platformFilter))
            {
                needsReview = needsReview
                    .Where(r => (r.PlatformSlug ?? "").Equals(platformFilter, StringComparison.OrdinalIgnoreCase) ||
                                (r.PlatformName ?? "").Equals(platformFilter, StringComparison.OrdinalIgnoreCase))
                    .ToList();
            }

            return Ok(needsReview);
        }

        /// <summary>
        /// Get match candidates for a specific game in the review queue.
        /// </summary>
        [HttpGet("{gameId}/candidates")]
        public async Task<ActionResult<List<MatchCandidate>>> GetCandidates(int gameId, [FromQuery] string? searchOverride = null)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound();

            var metadataService = _metadataServiceFactory.CreateService();
            var titleCleaner = new TitleCleanerService();

            var searchTitle = searchOverride ?? game.Title;
            var variants = titleCleaner.GenerateSearchVariants(searchTitle);
            if (variants.Count == 0) variants.Add(searchTitle);

            string? platformKey = null;
            if (game.PlatformId > 0)
            {
                var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                platformKey = platform?.FolderName;
            }

            var igdbResults = await metadataService.SearchWithVariantsAsync(variants, platformKey);
            int? expectedPlatformId = null;
            if (!string.IsNullOrEmpty(platformKey))
            {
                var plat = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                    p => p.MatchesFolderName(platformKey));
                expectedPlatformId = plat?.IgdbPlatformId;
            }

            var candidates = igdbResults
                .Select(r => new MatchCandidate
                {
                    IgdbId = r.Id,
                    Title = r.Name,
                    AlternativeNames = r.AlternativeNames.Select(a => a.Name).Where(n => !string.IsNullOrWhiteSpace(n)).ToList(),
                    Platforms = r.Platforms.Select(p => !string.IsNullOrEmpty(p.Abbreviation) ? p.Abbreviation : p.Name).ToList(),
                    Year = r.FirstReleaseDate.HasValue
                        ? DateTimeOffset.FromUnixTimeSeconds(r.FirstReleaseDate.Value).DateTime.Year
                        : (int?)null,
                    CoverUrl = r.Cover != null && !string.IsNullOrEmpty(r.Cover.ImageId)
                        ? IgdbClient.GetImageUrl(r.Cover.ImageId, ImageSize.CoverBig)
                        : null,
                    Score = metadataService.ScoreCandidate(r, variants, expectedPlatformId)
                })
                .OrderByDescending(c => c.Score)
                .Take(10)
                .ToList();

            return Ok(candidates);
        }

        /// <summary>
        /// Confirm a specific IGDB match for a game.
        /// </summary>
        [HttpPost("{gameId}/confirm")]
        public async Task<ActionResult> ConfirmMatch(int gameId, [FromBody] ConfirmMatchRequest request)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound();

            var metadataService = _metadataServiceFactory.CreateService();
            var fullMetadata = await metadataService.GetGameMetadataAsync(request.IgdbId);

            if (fullMetadata != null)
            {
                game.IgdbId = fullMetadata.IgdbId;
                game.Title = fullMetadata.Title;
                game.Overview = fullMetadata.Overview;
                game.Storyline = fullMetadata.Storyline;
                game.Developer = fullMetadata.Developer;
                game.Publisher = fullMetadata.Publisher;
                game.Rating = fullMetadata.Rating;
                game.RatingCount = fullMetadata.RatingCount;
                game.Year = fullMetadata.Year;
                game.ReleaseDate = fullMetadata.ReleaseDate;
                game.Genres = fullMetadata.Genres;
                game.Images = fullMetadata.Images;
            }
            else
            {
                game.IgdbId = request.IgdbId;
            }

            game.MetadataConfirmedByUser = true;
            game.MetadataConfirmedAt = DateTime.UtcNow;
            game.NeedsMetadataReview = false;
            game.MetadataReviewReason = null;
            game.MatchConfidence = request.Score;

            await _gameRepository.UpdateAsync(gameId, game);

            return Ok(new { success = true, title = game.Title });
        }

        /// <summary>
        /// Skip/ignore a game — stops re-prompting for a configurable period.
        /// </summary>
        [HttpPost("{gameId}/skip")]
        public async Task<ActionResult> SkipReview(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound();

            game.MetadataConfirmedByUser = true;
            game.MetadataConfirmedAt = DateTime.UtcNow;
            game.NeedsMetadataReview = false;
            game.MetadataReviewReason = "Skipped by user";

            await _gameRepository.UpdateAsync(gameId, game);

            return Ok(new { success = true });
        }

        /// <summary>
        /// Mark a game as correct without metadata (no IGDB match needed).
        /// </summary>
        [HttpPost("{gameId}/dismiss")]
        public async Task<ActionResult> DismissReview(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound();

            game.MetadataConfirmedByUser = true;
            game.MetadataConfirmedAt = DateTime.UtcNow;
            game.NeedsMetadataReview = false;
            game.MetadataReviewReason = null;

            await _gameRepository.UpdateAsync(gameId, game);

            return Ok(new { success = true });
        }
    }

    // ==================== Request/Response Models ====================

    public class MetadataReviewItem
    {
        public int GameId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? AlternativeTitle { get; set; }
        public int PlatformId { get; set; }
        public string? PlatformName { get; set; }
        public string? PlatformSlug { get; set; }
        public double? MatchConfidence { get; set; }
        public string ReviewReason { get; set; } = string.Empty;
        public int? CurrentIgdbId { get; set; }
        public string? CoverUrl { get; set; }
        public DateTime Added { get; set; }
    }

    public class MatchCandidate
    {
        public int IgdbId { get; set; }
        public string Title { get; set; } = string.Empty;
        public List<string> AlternativeNames { get; set; } = new();
        public List<string> Platforms { get; set; } = new();
        public int? Year { get; set; }
        public string? CoverUrl { get; set; }
        public double Score { get; set; }
    }

    public class ConfirmMatchRequest
    {
        public int IgdbId { get; set; }
        public double? Score { get; set; }
    }
}
