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
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.General);
        private readonly IGameRepository _gameRepository;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly LocalMediaExportService _localMediaExport;

        public MetadataReviewController(IGameRepository gameRepository, IGameMetadataServiceFactory metadataServiceFactory, LocalMediaExportService localMediaExport)
        {
            _gameRepository = gameRepository;
            _metadataServiceFactory = metadataServiceFactory;
            _localMediaExport = localMediaExport;
        }

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
                        Path = g.Path,
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
                    Score = metadataService.ScoreCandidate(r, variants, expectedPlatformId),
                    Source = "IGDB"
                })
                .OrderByDescending(c => c.Score)
                .Take(10)
                .ToList();

            // Also search ScreenScraper for additional candidates
            try
            {
                var ssResults = await metadataService.SearchScreenScraperAsync(variants.First(), platformKey);
                foreach (var ssGame in ssResults.Take(5))
                {
                    candidates.Add(new MatchCandidate
                    {
                        IgdbId = 0,
                        Title = ssGame.Title,
                        AlternativeNames = new List<string>(),
                        Platforms = ssGame.Platform != null ? new List<string> { ssGame.Platform.Name } : new List<string>(),
                        Year = ssGame.Year > 0 ? ssGame.Year : null,
                        CoverUrl = ssGame.Images?.CoverUrl,
                        CoverLargeUrl = ssGame.Images?.CoverLargeUrl,
                        BackgroundUrl = ssGame.Images?.BackgroundUrl,
                        BannerUrl = ssGame.Images?.BannerUrl,
                        Overview = ssGame.Overview,
                        Developer = ssGame.Developer,
                        Publisher = ssGame.Publisher,
                        Genres = ssGame.Genres?.Count > 0 ? ssGame.Genres : null,
                        Rating = ssGame.Rating,
                        Score = 0.5,
                        Source = "ScreenScraper"
                    });
                }
            }
            catch (Exception ex)
            {
                // ScreenScraper failure should not block IGDB results
                System.Diagnostics.Debug.WriteLine($"ScreenScraper search in review failed: {ex.Message}");
            }

            return Ok(candidates);
        }

        [HttpPost("{gameId}/confirm")]
        public async Task<ActionResult> ConfirmMatch(int gameId, [FromBody] ConfirmMatchRequest request)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return NotFound();

            if (request.Source == "ScreenScraper" && request.ScreenScraperData != null)
            {
                var ss = request.ScreenScraperData;
                if (!string.IsNullOrEmpty(ss.Title)) game.Title = ss.Title;
                if (!string.IsNullOrEmpty(ss.Overview)) game.Overview = ss.Overview;
                if (ss.Year > 0) game.Year = ss.Year;
                if (!string.IsNullOrEmpty(ss.Developer)) game.Developer = ss.Developer;
                if (!string.IsNullOrEmpty(ss.Publisher)) game.Publisher = ss.Publisher;
                if (ss.Rating.HasValue) game.Rating = ss.Rating;
                if (ss.Genres != null && ss.Genres.Count > 0) game.Genres = ss.Genres;
                if (!string.IsNullOrEmpty(ss.CoverUrl)) game.Images.CoverUrl = ss.CoverUrl;
                if (!string.IsNullOrEmpty(ss.CoverLargeUrl)) game.Images.CoverLargeUrl = ss.CoverLargeUrl;
                if (!string.IsNullOrEmpty(ss.BackgroundUrl)) game.Images.BackgroundUrl = ss.BackgroundUrl;
                if (!string.IsNullOrEmpty(ss.BannerUrl)) game.Images.BannerUrl = ss.BannerUrl;
                if (ss.Screenshots != null && ss.Screenshots.Count > 0) game.Images.Screenshots = ss.Screenshots;
                game.MetadataSource = "ScreenScraper";
            }
            else
            {
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
                game.MetadataSource = "IGDB";
            }

            game.MetadataConfirmedByUser = true;
            game.MetadataConfirmedAt = DateTime.UtcNow;
            game.NeedsMetadataReview = false;
            game.MetadataReviewReason = null;
            game.MatchConfidence = request.Score;

            // Guard: if the title changed, check for Title+PlatformId collision
            var allGames = await _gameRepository.GetAllLightAsync();
            var collision = allGames.FirstOrDefault(g =>
                g.Id != gameId &&
                g.Title.Equals(game.Title, StringComparison.OrdinalIgnoreCase) &&
                g.PlatformId == game.PlatformId);

            if (collision != null)
            {
                _logger.Info($"[MetadataReview] Merging game {gameId} into existing {collision.Id} ('{collision.Title}' on platform {collision.PlatformId})");

                // Transfer path/executable from reviewed game to the existing entry
                if (!string.IsNullOrEmpty(game.Path) && string.IsNullOrEmpty(collision.Path))
                    collision.Path = game.Path;
                if (!string.IsNullOrEmpty(game.ExecutablePath) && string.IsNullOrEmpty(collision.ExecutablePath))
                    collision.ExecutablePath = game.ExecutablePath;

                // Apply confirmed metadata to the surviving entry
                collision.IgdbId = game.IgdbId;
                collision.Overview = game.Overview;
                collision.Storyline = game.Storyline;
                collision.Developer = game.Developer;
                collision.Publisher = game.Publisher;
                collision.Rating = game.Rating;
                collision.RatingCount = game.RatingCount;
                collision.Year = game.Year;
                collision.ReleaseDate = game.ReleaseDate;
                collision.Genres = game.Genres;
                collision.Images = game.Images;
                collision.MetadataSource = game.MetadataSource;
                collision.MetadataConfirmedByUser = true;
                collision.MetadataConfirmedAt = DateTime.UtcNow;
                collision.NeedsMetadataReview = false;
                collision.MetadataReviewReason = null;
                collision.MatchConfidence = request.Score;

                await _gameRepository.UpdateAsync(collision.Id, collision);
                await _gameRepository.DeleteAsync(gameId);

                try { await _localMediaExport.ExportMediaForGameAsync(collision); }
                catch (Exception ex) { _logger.Error($"[MetadataReview] Media export error: {ex.Message}"); }

                return Ok(new { success = true, title = collision.Title, merged = true, survivorId = collision.Id });
            }

            await _gameRepository.UpdateAsync(gameId, game);

            try { await _localMediaExport.ExportMediaForGameAsync(game); }
            catch (Exception ex) { _logger.Error($"[MetadataReview] Media export error: {ex.Message}"); }

            return Ok(new { success = true, title = game.Title });
        }

        // stops re-prompting for a configurable period
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

        // marks as correct without needing an IGDB match
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
        public string? Path { get; set; }
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
        public string Source { get; set; } = "IGDB";
        public string? Overview { get; set; }
        public string? Developer { get; set; }
        public string? Publisher { get; set; }
        public List<string>? Genres { get; set; }
        public double? Rating { get; set; }
        public string? CoverLargeUrl { get; set; }
        public string? BackgroundUrl { get; set; }
        public string? BannerUrl { get; set; }
    }

    public class ConfirmMatchRequest
    {
        public int IgdbId { get; set; }
        public double? Score { get; set; }
        public string Source { get; set; } = "IGDB";
        public ScreenScraperMetadata? ScreenScraperData { get; set; }
    }

    public class ScreenScraperMetadata
    {
        public string Title { get; set; } = string.Empty;
        public string? Overview { get; set; }
        public int Year { get; set; }
        public string? Developer { get; set; }
        public string? Publisher { get; set; }
        public List<string>? Genres { get; set; }
        public double? Rating { get; set; }
        public string? CoverUrl { get; set; }
        public string? CoverLargeUrl { get; set; }
        public string? BackgroundUrl { get; set; }
        public string? BannerUrl { get; set; }
        public List<string>? Screenshots { get; set; }
    }
}
