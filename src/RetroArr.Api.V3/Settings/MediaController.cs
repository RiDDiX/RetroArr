using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Api.V3.Settings
{
    [ApiController]
    [Route("api/v3/[controller]")]
    [SuppressMessage("Microsoft.Design", "CA1034:NestedTypesShouldNotBeVisible")]
    public class MediaController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ScannerMedia);
        private readonly ConfigurationService _configService;
        private readonly MediaScannerService _scannerService;
        private readonly IGameRepository _gameRepository;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly LocalMediaExportService _localMediaExport;

        public MediaController(ConfigurationService configService, MediaScannerService scannerService, IGameRepository gameRepository, IGameMetadataServiceFactory metadataServiceFactory, LocalMediaExportService localMediaExport)
        {
            _configService = configService;
            _scannerService = scannerService;
            _gameRepository = gameRepository;
            _metadataServiceFactory = metadataServiceFactory;
            _localMediaExport = localMediaExport;
        }

        [HttpGet]
        public IActionResult GetSettings()
        {
            return Ok(_configService.LoadMediaSettings());
        }

        [HttpPost]
        public IActionResult SaveSettings([FromBody] MediaSettings settings)
        {
            _configService.SaveMediaSettings(settings);
            return Ok(new { message = "Media settings saved" });
        }

        public class ScanRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("folderPath")]
            public string? FolderPath { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("platform")]
            public string? Platform { get; set; }
        }

        [HttpPost("scan")]
        public IActionResult TriggerScan([FromBody] ScanRequest? request = null)
        {
            // Validate IGDB credentials before starting
            var igdbSettings = _configService.LoadIgdbSettings();
            if (!igdbSettings.IsConfigured)
            {
                return BadRequest(new { 
                    success = false, 
                    errorCode = "IGDB_NOT_CONFIGURED",
                    message = "IGDB credentials are required for scanning. Please configure them in the Metadata section." 
                });
            }

            _logger.Info($"TriggerScan received. FolderPath: '{request?.FolderPath}', Platform: '{request?.Platform}'");
            // Run scan in background to avoid timeouts
            Task.Run(async () => 
            {
                try
                {
                    await _scannerService.ScanAsync(request?.FolderPath, request?.Platform);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Background scan error: {ex}");
                }
            });

            return Ok(new { message = "Scan started in background. Check library in a few minutes." });
        }

        [HttpPost("scan/stop")]
        public IActionResult StopScan()
        {
            _scannerService.StopScan();
            return Ok(new { message = "Scan stopping. Check status bar." });
        }

        [HttpDelete("clean")]
        public async Task<IActionResult> CleanLibrary()
        {
            await _scannerService.CleanLibraryAsync();
            return Ok(new { message = "Library cleaned." });
        }

        [HttpGet("scan/status")]
        public IActionResult GetScanStatus()
        {
            return Ok(new
            {
                isScanning = _scannerService.IsScanning,
                lastGameFound = _scannerService.LastGameFound,
                gamesAddedCount = _scannerService.GamesAddedCount
            });
        }

        // Ad-hoc platform reconciliation: walks every DB row and fixes entries
        // whose stored PlatformId disagrees with the folder the file lives in.
        // Drops duplicates when a correct row already occupies the target slot.
        [HttpPost("heal-platforms")]
        public async Task<IActionResult> HealPlatforms()
        {
            if (_scannerService.IsScanning)
                return Conflict(new { message = "A scan is already running — heal will run at the end of it." });
            var (healed, dupesDropped) = await _scannerService.HealWrongPlatformsAsync();
            return Ok(new { healed, dupesDropped });
        }

        // ==================== Metadata Rescan ====================

        private static volatile bool _isRescanningMetadata;
        private static volatile bool _rescanCancellationRequested;
        public static bool IsRescanningMetadata => _isRescanningMetadata;

        private static int _metadataRescanTotal;
        private static int _metadataRescanProgress;
        private static int _metadataRescanUpdated;
        private static string? _metadataRescanCurrentGame;
        private static DateTime? _rescanStartedAt;

        public class MetadataRescanRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("platformId")]
            public int? PlatformId { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("missingOnly")]
            public bool MissingOnly { get; set; } = true;

            [System.Text.Json.Serialization.JsonPropertyName("preferredSource")]
            public string PreferredSource { get; set; } = "igdb";
        }

        [HttpPost("metadata/rescan")]
        public IActionResult TriggerMetadataRescan([FromBody] MetadataRescanRequest? request = null)
        {
            if (_isRescanningMetadata)
            {
                return Conflict(new { message = "A metadata rescan is already in progress." });
            }

            var igdbSettings = _configService.LoadIgdbSettings();
            if (!igdbSettings.IsConfigured)
            {
                return BadRequest(new
                {
                    success = false,
                    errorCode = "IGDB_NOT_CONFIGURED",
                    message = "IGDB credentials are required for metadata rescan."
                });
            }

            Task.Run(async () =>
            {
                _isRescanningMetadata = true;
                _rescanCancellationRequested = false;
                _metadataRescanProgress = 0;
                _metadataRescanUpdated = 0;
                _metadataRescanCurrentGame = null;
                _rescanStartedAt = DateTime.UtcNow;
                try
                {
                    var allGames = await _gameRepository.GetAllAsync();
                    IEnumerable<Game> targetGames = allGames;

                    if (request?.PlatformId.HasValue == true)
                    {
                        targetGames = targetGames.Where(g => g.PlatformId == request.PlatformId.Value);
                    }

                    if (request?.MissingOnly == true)
                    {
                        var preferredSourceOverride = request?.PreferredSource;
                        targetGames = targetGames.Where(g =>
                        {
                            if (!g.IgdbId.HasValue || string.IsNullOrEmpty(g.Overview))
                                return true;

                            var effectiveSrc = !string.IsNullOrEmpty(preferredSourceOverride)
                                ? preferredSourceOverride
                                : (g.PlatformId > 0 ? PlatformService.GetMetadataSource(g.PlatformId) : PlatformService.MetadataSourceIgdb);
                            var currentSrc = g.MetadataSource ?? "IGDB";
                            return !currentSrc.Equals(effectiveSrc, StringComparison.OrdinalIgnoreCase);
                        });
                    }

                    var gamesList = targetGames.ToList();
                    _metadataRescanTotal = gamesList.Count;
                    _logger.Info($"[MetadataRescan] Starting rescan for {gamesList.Count} games (platformId={request?.PlatformId}, missingOnly={request?.MissingOnly})");

                    var metadataService = _metadataServiceFactory.CreateService();
                    var titleCleaner = new TitleCleanerService();
                    var globalPreferredSource = request?.PreferredSource;

                    foreach (var game in gamesList)
                    {
                        if (_rescanCancellationRequested)
                        {
                            _logger.Info("[MetadataRescan] Cancelled by user.");
                            break;
                        }
                        _metadataRescanProgress++;
                        _metadataRescanCurrentGame = game.Title;
                        try
                        {
                            string? platformKey = null;
                            if (game.PlatformId > 0)
                            {
                                var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                                platformKey = platform?.FolderName;
                            }

                            var effectiveSource = !string.IsNullOrEmpty(globalPreferredSource)
                                ? globalPreferredSource
                                : (game.PlatformId > 0 ? PlatformService.GetMetadataSource(game.PlatformId) : PlatformService.MetadataSourceIgdb);
                            var useScreenScraperFirst = effectiveSource.Equals("screenscraper", StringComparison.OrdinalIgnoreCase);

                            var (cleanTitle, _) = titleCleaner.CleanGameTitle(game.Title);
                            var variants = titleCleaner.GenerateSearchVariants(cleanTitle);
                            if (variants.Count == 0) variants.Add(cleanTitle);

                            // Fix stored title if the cleaner improved it (e.g. stripped PS serial prefix)
                            bool titleCleaned = false;
                            if (!cleanTitle.Equals(game.Title, StringComparison.Ordinal) && !string.IsNullOrEmpty(cleanTitle))
                            {
                                _logger.Info($"[MetadataRescan] Title cleaned: '{game.Title}' → '{cleanTitle}'");
                                game.Title = cleanTitle;
                                titleCleaned = true;
                            }

                            bool updated = false;

                            if (useScreenScraperFirst)
                            {
                                // ScreenScraper primary, IGDB fallback
                                var ssResults = await metadataService.SearchScreenScraperAsync(variants.First(), platformKey);
                                if (ssResults.Count > 0)
                                {
                                    var best = ssResults.First();
                                    ApplyMetadataToGame(game, best, "ScreenScraper");
                                    await _gameRepository.UpdateAsync(game.Id, game);
                                    try { await _localMediaExport.ExportMediaForGameAsync(game); } catch (Exception mex) { _logger.Error($"[MetadataRescan] Media export error: {mex.Message}"); }
                                    _metadataRescanUpdated++;
                                    updated = true;
                                }

                                if (!updated)
                                {
                                    updated = await TryIgdbRescan(game, metadataService, variants, platformKey);
                                }
                            }
                            else
                            {
                                // IGDB primary, ScreenScraper fallback
                                updated = await TryIgdbRescan(game, metadataService, variants, platformKey);

                                if (!updated)
                                {
                                    var ssResults = await metadataService.SearchScreenScraperAsync(variants.First(), platformKey);
                                    if (ssResults.Count > 0)
                                    {
                                        var best = ssResults.First();
                                        ApplyMetadataToGame(game, best, "ScreenScraper");
                                        await _gameRepository.UpdateAsync(game.Id, game);
                                        try { await _localMediaExport.ExportMediaForGameAsync(game); } catch (Exception mex) { _logger.Error($"[MetadataRescan] Media export error: {mex.Message}"); }
                                        _metadataRescanUpdated++;
                                    }
                                }
                            }

                            // Persist cleaned title even when no metadata was found
                            if (!updated && titleCleaned)
                            {
                                await _gameRepository.UpdateAsync(game.Id, game);
                            }

                            await Task.Delay(300);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error($"[MetadataRescan] Error for '{game.Title}': {ex.Message}");
                        }
                    }

                    _logger.Info($"[MetadataRescan] Complete. Updated {_metadataRescanUpdated}/{gamesList.Count} games.");
                }
                catch (Exception ex)
                {
                    _logger.Error($"[MetadataRescan] Fatal error: {ex}");
                }
                finally
                {
                    _isRescanningMetadata = false;
                }
            });

            return Ok(new { message = "Metadata rescan started in background." });
        }

        [HttpGet("metadata/rescan/status")]
        public IActionResult GetMetadataRescanStatus()
        {
            return Ok(new
            {
                isRescanning = _isRescanningMetadata,
                total = _metadataRescanTotal,
                progress = _metadataRescanProgress,
                updated = _metadataRescanUpdated,
                currentGame = _metadataRescanCurrentGame,
                startedAt = _rescanStartedAt?.ToString("o")
            });
        }

        [HttpPost("metadata/rescan/cancel")]
        public IActionResult CancelMetadataRescan()
        {
            if (!_isRescanningMetadata)
                return Ok(new { message = "No rescan is running." });

            _rescanCancellationRequested = true;
            _logger.Info("[MetadataRescan] Cancellation requested.");
            return Ok(new { message = "Cancellation requested." });
        }

        private async Task<bool> TryIgdbRescan(Game game, GameMetadataService metadataService, List<string> variants, string? platformKey)
        {
            var searchResults = await metadataService.SearchGamesAsync(variants.First(), platformKey);
            if (searchResults.Count > 0)
            {
                var best = searchResults.First();
                if (best.IgdbId > 0)
                {
                    var fullMeta = await metadataService.GetGameMetadataAsync(best.IgdbId.Value);
                    if (fullMeta != null)
                    {
                        game.IgdbId = fullMeta.IgdbId;
                        game.Title = fullMeta.Title;
                        game.Overview = fullMeta.Overview;
                        game.Storyline = fullMeta.Storyline;
                        game.Developer = fullMeta.Developer;
                        game.Publisher = fullMeta.Publisher;
                        game.Rating = fullMeta.Rating;
                        game.RatingCount = fullMeta.RatingCount;
                        game.Year = fullMeta.Year;
                        game.ReleaseDate = fullMeta.ReleaseDate;
                        game.Genres = fullMeta.Genres;
                        game.Images = fullMeta.Images;
                        game.NeedsMetadataReview = false;
                        game.MetadataSource = "IGDB";
                        await _gameRepository.UpdateAsync(game.Id, game);
                        try { await _localMediaExport.ExportMediaForGameAsync(game); } catch (Exception mex) { _logger.Error($"[MetadataRescan] Media export error: {mex.Message}"); }
                        _metadataRescanUpdated++;
                        return true;
                    }
                }
            }
            return false;
        }

        private static void ApplyMetadataToGame(Game game, Game source, string metadataSource)
        {
            if (!string.IsNullOrEmpty(source.Title)) game.Title = source.Title;
            if (!string.IsNullOrEmpty(source.Overview)) game.Overview = source.Overview;
            if (source.Year > 0) game.Year = source.Year;
            if (!string.IsNullOrEmpty(source.Developer)) game.Developer = source.Developer;
            if (!string.IsNullOrEmpty(source.Publisher)) game.Publisher = source.Publisher;
            if (source.Rating.HasValue) game.Rating = source.Rating;
            if (source.Genres != null && source.Genres.Count > 0) game.Genres = source.Genres;
            if (source.Images != null)
            {
                if (!string.IsNullOrEmpty(source.Images.CoverUrl)) game.Images.CoverUrl = source.Images.CoverUrl;
                if (!string.IsNullOrEmpty(source.Images.CoverLargeUrl)) game.Images.CoverLargeUrl = source.Images.CoverLargeUrl;
                if (!string.IsNullOrEmpty(source.Images.BackgroundUrl)) game.Images.BackgroundUrl = source.Images.BackgroundUrl;
                if (!string.IsNullOrEmpty(source.Images.BannerUrl)) game.Images.BannerUrl = source.Images.BannerUrl;
                if (!string.IsNullOrEmpty(source.Images.BoxBackUrl)) game.Images.BoxBackUrl = source.Images.BoxBackUrl;
                if (!string.IsNullOrEmpty(source.Images.VideoUrl)) game.Images.VideoUrl = source.Images.VideoUrl;
                if (source.Images.Screenshots != null && source.Images.Screenshots.Count > 0)
                    game.Images.Screenshots = source.Images.Screenshots;
            }
            game.NeedsMetadataReview = false;
            game.MetadataSource = metadataSource;
        }
    }
}
