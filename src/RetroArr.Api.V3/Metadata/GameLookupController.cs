using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Api.V3.Metadata
{
    [ApiController]
    [Route("api/v3/game/lookup")]
    public class GameLookupController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ScannerMetadata);
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly IGameRepository _gameRepository;

        public GameLookupController(IGameMetadataServiceFactory metadataServiceFactory, IGameRepository gameRepository)
        {
            _metadataServiceFactory = metadataServiceFactory;
            _gameRepository = gameRepository;
        }

        [HttpGet]
        public async Task<ActionResult> Search([FromQuery] string term, [FromQuery] string? platformKey = null, [FromQuery] string? lang = null, [FromQuery] string? source = null)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("Search term is required");
            }

            try
            {
                var metadataService = _metadataServiceFactory.CreateService();
                List<Game> games;
                string sourceLabel = source?.ToLower() ?? "igdb";
                string statusLabel = "ok";
                string? statusMessage = null;

                if (sourceLabel == "screenscraper")
                {
                    var (ssStatus, ssGames) = await metadataService.SearchScreenScraperWithStatusAsync(term, platformKey, lang);
                    games = ssGames;
                    (statusLabel, statusMessage) = MapScreenScraperStatus(ssStatus);
                }
                else if (sourceLabel == "thegamesdb")
                {
                    var (tStatus, tGames) = await metadataService.SearchTheGamesDbWithStatusAsync(term, platformKey, lang);
                    games = tGames;
                    (statusLabel, statusMessage) = MapTheGamesDbStatus(tStatus);
                }
                else if (sourceLabel == "epic" || sourceLabel == "epicstore")
                {
                    var (eStatus, eGames) = await metadataService.SearchEpicWithStatusAsync(term, platformKey, lang);
                    games = eGames;
                    (statusLabel, statusMessage) = MapEpicMetadataStatus(eStatus);
                }
                else
                {
                    games = await metadataService.SearchGamesAsync(term, platformKey, lang);
                    if (games.Count == 0) statusLabel = "empty";
                }

                var ownedIds = await _gameRepository.GetIgdbIdsAsync();
                foreach (var game in games)
                {
                    if (game.IgdbId.HasValue && ownedIds.Contains(game.IgdbId.Value))
                    {
                        game.IsOwned = true;
                    }
                }

                _logger.Info($"[Lookup] Returning {games.Count} game(s) for term='{term}', source={sourceLabel}, status={statusLabel}");
                return Ok(new { games, source = sourceLabel, status = statusLabel, message = statusMessage });
            }
            catch (Exception ex)
            {
                _logger.Error($"[Lookup] Exception: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

        // turn the screenscraper enum into a frontend-friendly string + hint
        private static (string status, string? message) MapScreenScraperStatus(RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus s) => s switch
        {
            RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus.Ok => ("ok", null),
            RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus.Empty => ("empty", null),
            RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus.QuotaExceeded => ("quota_exceeded", "Daily ScreenScraper quota reached. Try again later or sign in with a personal account."),
            RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus.AuthFailed => ("auth_failed", "ScreenScraper login failed. Check username and password in Settings."),
            RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus.Unconfigured => ("unconfigured", "ScreenScraper is not configured in this build."),
            RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperStatus.NetworkError => ("network_error", "Could not reach ScreenScraper, try again."),
            _ => ("unknown", null)
        };

        private static (string status, string? message) MapTheGamesDbStatus(RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus s) => s switch
        {
            RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus.Ok => ("ok", null),
            RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus.Empty => ("empty", null),
            RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus.QuotaExceeded => ("quota_exceeded", "TheGamesDB monthly allowance exceeded."),
            RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus.AuthFailed => ("auth_failed", "TheGamesDB login failed. Check your API key in Settings."),
            RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus.Unconfigured => ("unconfigured", "TheGamesDB is not configured."),
            RetroArr.Core.MetadataSource.TheGamesDb.TheGamesDbStatus.NetworkError => ("network_error", "Could not reach TheGamesDB, try again."),
            _ => ("unknown", null)
        };

        private static (string status, string? message) MapEpicMetadataStatus(RetroArr.Core.MetadataSource.Epic.EpicMetadataStatus s) => s switch
        {
            RetroArr.Core.MetadataSource.Epic.EpicMetadataStatus.Ok => ("ok", null),
            RetroArr.Core.MetadataSource.Epic.EpicMetadataStatus.Empty => ("empty", null),
            RetroArr.Core.MetadataSource.Epic.EpicMetadataStatus.Unconfigured => ("unconfigured", "Epic Store metadata is disabled."),
            RetroArr.Core.MetadataSource.Epic.EpicMetadataStatus.NetworkError => ("network_error", "Could not reach Epic Store."),
            _ => ("unknown", null)
        };

        [HttpGet("igdb/{igdbId}")]
        public async Task<ActionResult<Game>> GetByIgdbId(int igdbId, [FromQuery] string? lang = null)
        {
            try
            {
                var metadataService = _metadataServiceFactory.CreateService();
                var game = await metadataService.GetGameMetadataAsync(igdbId, lang);
                
                if (game == null)
                {
                    return NotFound();
                }

                return Ok(game);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }
    }
}
