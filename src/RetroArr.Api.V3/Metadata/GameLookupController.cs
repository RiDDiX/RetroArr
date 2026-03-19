using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Api.V3.Metadata
{
    /// <summary>
    /// Game metadata lookup controller
    /// Search games on IGDB and retrieve visual metadata
    /// </summary>
    [ApiController]
    [Route("api/v3/game/lookup")]
    public class GameLookupController : ControllerBase
    {
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly IGameRepository _gameRepository;

        public GameLookupController(IGameMetadataServiceFactory metadataServiceFactory, IGameRepository gameRepository)
        {
            _metadataServiceFactory = metadataServiceFactory;
            _gameRepository = gameRepository;
        }

        /// <summary>
        /// Search games by title
        /// </summary>
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

                return Ok(games);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { error = ex.Message });
            }
        }

        /// <summary>
        /// Get full game info by IGDB ID
        /// </summary>
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
