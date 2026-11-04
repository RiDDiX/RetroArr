using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource.Steam;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Api.V3.Steam
{
    [ApiController]
    [Route("api/v3/protondb")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class ProtonDbController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.General);
        private readonly IGameRepository _gameRepository;

        // Background refresh state
        private static readonly object _refreshLock = new();
        private static volatile bool _isRefreshing;
        private static volatile bool _refreshCancellationRequested;
        private static int _refreshTotal;
        private static int _refreshProgress;
        private static int _refreshUpdated;
        private static int _refreshSkipped;
        private static string? _refreshCurrentGame;
        private static string? _refreshError;

        public ProtonDbController(IGameRepository gameRepository)
        {
            _gameRepository = gameRepository;
        }

        [HttpGet("game/{gameId}")]
        public async Task<IActionResult> GetTierForGame(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null)
                return NotFound(new { message = "Game not found" });

            if (!game.SteamId.HasValue)
                return Ok(new { tier = (string?)null, message = "No Steam ID linked" });

            if (!string.IsNullOrEmpty(game.ProtonDbTier))
                return Ok(new { tier = game.ProtonDbTier, steamId = game.SteamId, cached = true });

            var client = new ProtonDbClient();
            var tier = await client.GetTierAsync(game.SteamId.Value);

            // Only the tier: a whole-row write of the copy read before the ProtonDB call would undo changes made since
            if (!string.IsNullOrEmpty(tier))
                await _gameRepository.UpdateFieldsAsync(game.Id, g => g.ProtonDbTier = tier);

            return Ok(new { tier, steamId = game.SteamId, cached = false });
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> RefreshAll()
        {
            // Claimed before the first await, so a second click during the load can't start a second run
            lock (_refreshLock)
            {
                if (_isRefreshing)
                    return Conflict(new { success = false, message = "A ProtonDB refresh is already in progress." });
                _isRefreshing = true;
            }

            List<GameListDto> steamGames;
            try
            {
                steamGames = (await _gameRepository.GetSteamRowsAsync()).Where(g => g.SteamId > 0).ToList();
            }
            catch
            {
                _isRefreshing = false;
                throw;
            }

            if (steamGames.Count == 0)
            {
                _isRefreshing = false;
                return Ok(new { success = true, message = "No games with Steam IDs found.", count = 0 });
            }

            _refreshCancellationRequested = false;
            _refreshTotal = steamGames.Count;
            _refreshProgress = 0;
            _refreshUpdated = 0;
            _refreshSkipped = 0;
            _refreshCurrentGame = null;
            _refreshError = null;

            var gameRepository = _gameRepository;

            _ = Task.Run(async () =>
            {
                try
                {
                    var client = new ProtonDbClient();

                    foreach (var game in steamGames)
                    {
                        if (_refreshCancellationRequested)
                        {
                            _logger.Info("[ProtonDB] Refresh cancelled by user.");
                            break;
                        }

                        _refreshProgress++;
                        _refreshCurrentGame = game.Title;

                        try
                        {
                            var tier = await client.GetTierAsync(game.SteamId!.Value);

                            if (!string.IsNullOrEmpty(tier))
                            {
                                if (game.ProtonDbTier != tier)
                                {
                                    // only the tier: the row was read before this run started
                                    await gameRepository.UpdateFieldsAsync(game.Id, g => g.ProtonDbTier = tier);
                                    _refreshUpdated++;
                                }
                                else
                                {
                                    _refreshSkipped++;
                                }
                            }
                            else
                            {
                                _refreshSkipped++;
                            }

                            await Task.Delay(100);
                        }
                        catch (Exception ex)
                        {
                            _refreshSkipped++;
                            _logger.Debug($"[ProtonDB] Error for '{game.Title}': {ex.Message}");
                        }
                    }

                    _logger.Info($"[ProtonDB] Refresh complete. Updated: {_refreshUpdated}, Skipped: {_refreshSkipped}");
                }
                catch (Exception ex)
                {
                    _refreshError = ex.Message;
                    _logger.Error($"[ProtonDB] Fatal refresh error: {ex}");
                }
                finally
                {
                    _isRefreshing = false;
                }
            });

            return Ok(new { success = true, message = $"ProtonDB refresh started for {steamGames.Count} games." });
        }

        [HttpGet("refresh/status")]
        public IActionResult GetRefreshStatus()
        {
            return Ok(new
            {
                isRefreshing = _isRefreshing,
                total = _refreshTotal,
                progress = _refreshProgress,
                updated = _refreshUpdated,
                skipped = _refreshSkipped,
                currentGame = _refreshCurrentGame,
                error = _refreshError
            });
        }

        [HttpPost("refresh/cancel")]
        public IActionResult CancelRefresh()
        {
            if (!_isRefreshing)
                return Ok(new { message = "No ProtonDB refresh is running." });

            _refreshCancellationRequested = true;
            _logger.Info("[ProtonDB] Cancellation requested.");
            return Ok(new { message = "Cancellation requested." });
        }
    }
}
