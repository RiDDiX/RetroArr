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

        public MediaController(ConfigurationService configService, MediaScannerService scannerService, IGameRepository gameRepository, IGameMetadataServiceFactory metadataServiceFactory)
        {
            _configService = configService;
            _scannerService = scannerService;
            _gameRepository = gameRepository;
            _metadataServiceFactory = metadataServiceFactory;
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
            return Ok(new { message = "Media settings saved successfully" });
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
            return Ok(new { message = "Library cleaned successfully." });
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

        // ==================== Metadata Rescan ====================

        private static volatile bool _isRescanningMetadata;
        public static bool IsRescanningMetadata => _isRescanningMetadata;

        private static int _metadataRescanTotal;
        private static int _metadataRescanProgress;
        private static int _metadataRescanUpdated;
        private static string? _metadataRescanCurrentGame;

        public class MetadataRescanRequest
        {
            [System.Text.Json.Serialization.JsonPropertyName("platformId")]
            public int? PlatformId { get; set; }

            [System.Text.Json.Serialization.JsonPropertyName("missingOnly")]
            public bool MissingOnly { get; set; } = true;
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
                _metadataRescanProgress = 0;
                _metadataRescanUpdated = 0;
                _metadataRescanCurrentGame = null;
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
                        targetGames = targetGames.Where(g => !g.IgdbId.HasValue || string.IsNullOrEmpty(g.Overview));
                    }

                    var gamesList = targetGames.ToList();
                    _metadataRescanTotal = gamesList.Count;
                    _logger.Info($"[MetadataRescan] Starting rescan for {gamesList.Count} games (platformId={request?.PlatformId}, missingOnly={request?.MissingOnly})");

                    var metadataService = _metadataServiceFactory.CreateService();
                    var titleCleaner = new TitleCleanerService();

                    foreach (var game in gamesList)
                    {
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

                            var (cleanTitle, _) = titleCleaner.CleanGameTitle(game.Title);
                            var variants = titleCleaner.GenerateSearchVariants(cleanTitle);
                            if (variants.Count == 0) variants.Add(cleanTitle);

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
                                        await _gameRepository.UpdateAsync(game.Id, game);
                                        _metadataRescanUpdated++;
                                    }
                                }
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
                currentGame = _metadataRescanCurrentGame
            });
        }
    }
}
