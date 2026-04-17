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
        public async Task<ActionResult<List<Game>>> Search([FromQuery] string term, [FromQuery] string? platformKey = null, [FromQuery] string? lang = null, [FromQuery] string? source = null)
        {
            if (string.IsNullOrWhiteSpace(term))
            {
                return BadRequest("Search term is required");
            }

            try
            {
                var metadataService = _metadataServiceFactory.CreateService();
                List<Game> games;
                
                // Allow explicit source selection
                if (!string.IsNullOrEmpty(source) && source.ToLower() == "screenscraper")
                {
                    games = await metadataService.SearchScreenScraperAsync(term, platformKey, lang);
                }
                else
                {
                    games = await metadataService.SearchGamesAsync(term, platformKey, lang);
                }

                // Check which of these are already in the library
                var ownedIds = await _gameRepository.GetIgdbIdsAsync();
                foreach (var game in games)
                {
                    if (game.IgdbId.HasValue && ownedIds.Contains(game.IgdbId.Value))
                    {
                        game.IsOwned = true;
                    }
                }

                _logger.Info($"[Lookup] Returning {games.Count} game(s) for term='{term}', source={source ?? "igdb"}");
                return Ok(games);
            }
            catch (Exception ex)
            {
                _logger.Error($"[Lookup] Exception: {ex.Message}");
                return StatusCode(500, new { error = ex.Message });
            }
        }

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
