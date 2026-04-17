using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource.Gog;

namespace RetroArr.Api.V3.Gog
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class GogController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.GogDownloads);
        private readonly ConfigurationService _configService;
        private readonly RetroArrDbContext _context;
        private readonly IGameRepository _gameRepository;

        public GogController(ConfigurationService configService, RetroArrDbContext context, IGameRepository gameRepository)
        {
            _configService = configService;
            _context = context;
            _gameRepository = gameRepository;
        }

        // GOG Galaxy OAuth credentials (publicly known from GOG Galaxy client)
        private const string GogClientId = "46899977096215655";
        private const string GogClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
        private const string GogRedirectUri = "https://embed.gog.com/on_login_success?origin=client";

        [HttpGet("settings")]
        public ActionResult GetSettings()
        {
            var settings = _configService.LoadGogSettings();
            return Ok(new
            {
                IsAuthenticated = settings.RefreshToken != null,
                settings.UserId,
                settings.Username
            });
        }

        /// <summary>
        /// Get GOG connection status (for debug console)
        /// </summary>
        [HttpGet("status")]
        public async Task<ActionResult> GetStatus()
        {
            var settings = _configService.LoadGogSettings();
            
            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                return Ok(new
                {
                    isConnected = false,
                    message = "GOG is not configured. Please authenticate in Settings -> GOG.",
                    username = (string?)null,
                    gamesCount = 0
                });
            }

            try
            {
                var client = new GogClient(settings.RefreshToken);
                await client.RefreshTokenAsync(GogClientId, GogClientSecret);
                
                var games = await client.GetOwnedGamesAsync();
                var gameCount = games.Count(g => g.IsGame);

                return Ok(new
                {
                    isConnected = true,
                    message = "Connected to GOG",
                    username = settings.Username ?? settings.UserId,
                    gamesCount = gameCount
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] Status check error: {ex.Message}");
                return Ok(new
                {
                    isConnected = false,
                    message = $"Connection error: {ex.Message}",
                    username = (string?)null,
                    gamesCount = 0
                });
            }
        }

        /// <summary>
        /// Get the GOG OAuth login URL - user opens this in browser to authenticate
        /// </summary>
        [HttpGet("auth/url")]
        public ActionResult GetAuthUrl()
        {
            var loginUrl = $"https://auth.gog.com/auth?client_id={GogClientId}&redirect_uri={Uri.EscapeDataString(GogRedirectUri)}&response_type=code&layout=client2";
            return Ok(new { url = loginUrl });
        }

        /// <summary>
        /// Exchange authorization code for tokens
        /// User copies the code from the redirect URL and submits it here
        /// </summary>
        [HttpPost("auth/code")]
        public async Task<ActionResult> ExchangeCode([FromBody] GogCodeRequest request)
        {
            if (string.IsNullOrEmpty(request.Code))
            {
                return BadRequest(new { success = false, message = "Authorization code is required" });
            }

            // Extract code from full URL if user pasted the whole thing
            var code = request.Code.Trim();
            if (code.Contains("code="))
            {
                // Parse URL to extract code parameter
                try
                {
                    if (code.StartsWith("http"))
                    {
                        var uri = new Uri(code);
                        var query = System.Web.HttpUtility.ParseQueryString(uri.Query);
                        code = query["code"] ?? code;
                    }
                    else
                    {
                        // Just "code=XXX" or "?code=XXX"
                        var match = System.Text.RegularExpressions.Regex.Match(code, @"code=([^&\s]+)");
                        if (match.Success)
                        {
                            code = match.Groups[1].Value;
                        }
                    }
                }
                catch
                {
                    // If parsing fails, use original
                }
            }

            _logger.Info($"[GOG] Attempting code exchange, code length: {code.Length}");

            try
            {
                var client = new GogClient();
                var tokenResponse = await client.ExchangeCodeAsync(code, GogClientId, GogClientSecret, GogRedirectUri);

                if (tokenResponse == null)
                {
                    return BadRequest(new { success = false, message = "Failed to exchange code for tokens. The code may be invalid or expired." });
                }

                // Save tokens
                var settings = new RetroArr.Core.Configuration.GogSettings
                {
                    RefreshToken = tokenResponse.RefreshToken,
                    AccessToken = tokenResponse.AccessToken,
                    UserId = tokenResponse.UserId,
                    Username = null // Will be fetched on first sync
                };
                _configService.SaveGogSettings(settings);

                _logger.Info($"[GOG] OAuth successful - User ID: {tokenResponse.UserId}");

                return Ok(new
                {
                    success = true,
                    message = "GOG authentication successful!",
                    userId = tokenResponse.UserId,
                    expiresIn = tokenResponse.ExpiresIn
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] OAuth error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Authentication failed: {ex.Message}" });
            }
        }

        [HttpPost("settings")]
        public ActionResult SaveSettings([FromBody] GogSettingsRequest request)
        {
            _configService.SaveGogSettings(new RetroArr.Core.Configuration.GogSettings
            {
                RefreshToken = request.RefreshToken,
                AccessToken = request.AccessToken,
                UserId = request.UserId,
                Username = request.Username
            });

            return Ok(new { message = "GOG settings saved" });
        }

        [HttpGet("library")]
        public async Task<ActionResult> GetLibrary()
        {
            var settings = _configService.LoadGogSettings();
            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                return BadRequest("GOG not configured. Please authenticate first.");
            }

            try
            {
                var client = new GogClient(settings.RefreshToken);
                
                // Refresh token using built-in credentials
                await client.RefreshTokenAsync(GogClientId, GogClientSecret);

                var games = await client.GetOwnedGamesAsync();

                return Ok(games.Where(g => g.IsGame).Select(g => new
                {
                    GogId = g.Id.ToString(),
                    g.Title,
                    CoverUrl = g.GetCoverUrl(),
                    BackgroundUrl = g.GetBackgroundUrl(),
                    g.Rating,
                    g.Category
                }));
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] Error fetching library: {ex.Message}");
                return StatusCode(500, $"Failed to fetch GOG library: {ex.Message}");
            }
        }

        [HttpPost("sync")]
        public async Task<ActionResult> SyncLibrary()
        {
            var settings = _configService.LoadGogSettings();
            if (string.IsNullOrEmpty(settings.RefreshToken))
            {
                return BadRequest("GOG not configured. Please authenticate first.");
            }

            try
            {
                var client = new GogClient(settings.RefreshToken);
                
                // Refresh token using built-in credentials
                await client.RefreshTokenAsync(GogClientId, GogClientSecret);

                var gogGames = await client.GetOwnedGamesAsync();
                var addedCount = 0;
                var skippedCount = 0;

                // Get GOG Galaxy platform
                var gogPlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "gog");
                var platformId = gogPlatform?.Id ?? 126;

                foreach (var gogGame in gogGames.Where(g => g.IsGame))
                {
                    // Check if already exists by GOG ID
                    var existing = await _context.Games
                        .FirstOrDefaultAsync(g => g.GogId == gogGame.Id.ToString());

                    if (existing != null)
                    {
                        skippedCount++;
                        continue;
                    }

                    // Check by title on the GOG platform only (avoid cross-platform corruption)
                    existing = await _context.Games
                        .FirstOrDefaultAsync(g => g.Title == gogGame.Title && g.PlatformId == platformId);

                    if (existing != null)
                    {
                        // Update with GOG ID
                        existing.GogId = gogGame.Id.ToString();
                        existing.IsExternal = true;
                        await _gameRepository.UpdateAsync(existing.Id, existing);
                        skippedCount++;
                        continue;
                    }

                    // Add new game
                    var game = new Game
                    {
                        Title = gogGame.Title,
                        GogId = gogGame.Id.ToString(),
                        PlatformId = platformId,
                        Status = GameStatus.Released,
                        IsExternal = true,
                        Added = DateTime.UtcNow,
                        Images = new GameImages
                        {
                            CoverUrl = gogGame.GetCoverUrl(),
                            BackgroundUrl = gogGame.GetBackgroundUrl()
                        }
                    };

                    // Try to get more details
                    try
                    {
                        var details = await client.GetGameDetailsAsync(gogGame.Id.ToString());
                        if (details != null)
                        {
                            game.Overview = details.Description?.Lead ?? details.Description?.Full;
                            game.Developer = details.Developers?.FirstOrDefault()?.Name;
                            game.Publisher = details.Publishers?.FirstOrDefault()?.Name;
                            game.Genres = details.Genres?.Select(g => g.Name).ToList() ?? new List<string>();

                            if (!string.IsNullOrEmpty(details.ReleaseDate) && DateTime.TryParse(details.ReleaseDate, out var releaseDate))
                            {
                                game.ReleaseDate = releaseDate;
                                game.Year = releaseDate.Year;
                            }
                        }
                    }
                    catch { }

                    await _gameRepository.AddAsync(game);
                    addedCount++;
                }

                return Ok(new
                {
                    message = $"GOG sync done",
                    added = addedCount,
                    skipped = skippedCount,
                    total = gogGames.Count(g => g.IsGame)
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] Sync error: {ex.Message}");
                return StatusCode(500, $"GOG sync failed: {ex.Message}");
            }
        }

        [HttpGet("search")]
        public async Task<ActionResult> Search([FromQuery] string query)
        {
            if (string.IsNullOrEmpty(query))
                return BadRequest("Query is required");

            var client = new GogClient();
            var results = await client.SearchGamesAsync(query);

            return Ok(results.Select(r => new
            {
                GogId = r.Id.ToString(),
                r.Title,
                CoverUrl = r.Image != null ? $"https:{r.Image}_product_card_v2_mobile_slider_639.jpg" : null,
                r.Rating,
                r.Category,
                Price = r.Price?.Amount,
                IsFree = r.Price?.IsFree ?? false,
                IsDiscounted = r.Price?.IsDiscounted ?? false,
                DiscountPercentage = r.Price?.DiscountPercentage
            }));
        }

        [HttpGet("game/{gogId}")]
        public async Task<ActionResult> GetGameDetails(string gogId)
        {
            var client = new GogClient();
            var details = await client.GetGameDetailsAsync(gogId);

            if (details == null)
                return NotFound();

            return Ok(new
            {
                GogId = details.Id.ToString(),
                details.Title,
                details.Slug,
                Description = details.Description?.Full,
                ShortDescription = details.Description?.Lead,
                Background = details.Images?.Background,
                Logo = details.Images?.Logo2x ?? details.Images?.Logo,
                details.ReleaseDate,
                Genres = details.Genres?.Select(g => g.Name),
                Developers = details.Developers?.Select(d => d.Name),
                Publishers = details.Publishers?.Select(p => p.Name)
            });
        }
    }

    public class GogSettingsRequest
    {
        public string? RefreshToken { get; set; }
        public string? AccessToken { get; set; }
        public string? UserId { get; set; }
        public string? Username { get; set; }
    }

    public class GogCodeRequest
    {
        public string? Code { get; set; }
    }
}
