using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource.Epic;

namespace RetroArr.Api.V3.Epic
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class EpicController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ScannerMetadata);
        private readonly ConfigurationService _configService;
        private readonly RetroArrDbContext _context;
        private readonly IGameRepository _gameRepository;

        public EpicController(ConfigurationService configService, RetroArrDbContext context, IGameRepository gameRepository)
        {
            _configService = configService;
            _context = context;
            _gameRepository = gameRepository;
        }

        [HttpGet("settings")]
        public ActionResult GetSettings()
        {
            var s = _configService.LoadEpicSettings();
            return Ok(new
            {
                isAuthenticated = !string.IsNullOrEmpty(s.RefreshToken),
                accountId = s.AccountId,
                displayName = s.DisplayName
            });
        }

        [HttpGet("auth/url")]
        public ActionResult GetAuthUrl()
        {
            return Ok(new { url = EpicClient.LoginUrl });
        }

        public class EpicCodeRequest
        {
            public string Code { get; set; } = string.Empty;
        }

        // accepts a raw authorizationCode, the JSON blob from the redirect page,
        // or the full ?code=XXX URL pasted back from the browser
        [HttpPost("auth/code")]
        public async Task<ActionResult> ExchangeCode([FromBody] EpicCodeRequest request)
        {
            if (request == null || string.IsNullOrWhiteSpace(request.Code))
                return BadRequest(new { success = false, message = "Authorization code is required" });

            var code = ExtractCode(request.Code.Trim());
            if (string.IsNullOrEmpty(code))
                return BadRequest(new { success = false, message = "Could not parse an authorization code from the input." });

            try
            {
                var client = new EpicClient();
                var token = await client.ExchangeCodeAsync(code);
                if (token == null)
                    return BadRequest(new { success = false, message = "Code exchange failed. Code may be expired, request a new one." });

                _configService.SaveEpicSettings(new EpicSettings
                {
                    RefreshToken = token.RefreshToken,
                    AccessToken = token.AccessToken,
                    AccountId = token.AccountId,
                    DisplayName = token.DisplayName
                });

                _logger.Info($"[Epic] OAuth ok, account={token.DisplayName ?? token.AccountId}");
                return Ok(new
                {
                    success = true,
                    message = "Epic authentication successful.",
                    accountId = token.AccountId,
                    displayName = token.DisplayName
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[Epic] OAuth error: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"Authentication failed: {ex.Message}" });
            }
        }

        [HttpPost("settings")]
        public ActionResult SaveSettings([FromBody] EpicSettings request)
        {
            // explicit clear: empty body wipes the saved tokens
            _configService.SaveEpicSettings(request ?? new EpicSettings());
            return Ok(new { success = true });
        }

        [HttpPost("sync")]
        public async Task<ActionResult> Sync()
        {
            var settings = _configService.LoadEpicSettings();
            if (string.IsNullOrEmpty(settings.RefreshToken))
                return BadRequest(new { success = false, message = "Epic not configured. Authenticate first." });

            try
            {
                var client = new EpicClient();
                var refreshed = await client.RefreshAsync(settings.RefreshToken);
                if (refreshed == null)
                    return BadRequest(new { success = false, message = "Epic token refresh failed. Please reconnect your account." });

                // persist fresh tokens before doing any heavy lifting
                _configService.SaveEpicSettings(new EpicSettings
                {
                    RefreshToken = refreshed.RefreshToken,
                    AccessToken = refreshed.AccessToken,
                    AccountId = refreshed.AccountId ?? settings.AccountId,
                    DisplayName = refreshed.DisplayName ?? settings.DisplayName
                });

                var assets = await client.GetOwnedAssetsAsync();
                var epicPlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "epic");
                var platformId = epicPlatform?.Id ?? 190;
                _logger.Info($"[Epic] Sync start: {assets.Count} owned asset(s) reported by Epic");

                int added = 0, skipped = 0, failed = 0;
                var seenCatalog = new HashSet<string>();
                var dedupedAssets = new List<EpicAsset>();
                foreach (var asset in assets)
                {
                    if (string.IsNullOrEmpty(asset.CatalogItemId) || string.IsNullOrEmpty(asset.Namespace)) continue;
                    if (!seenCatalog.Add(asset.CatalogItemId)) continue;
                    dedupedAssets.Add(asset);
                }
                _logger.Info($"[Epic] Sync deduped to {dedupedAssets.Count} unique catalog item(s)");

                // small concurrency window so catalog lookups don't take minutes on big libraries
                using var gate = new System.Threading.SemaphoreSlim(4);
                var lookups = dedupedAssets.Select(async asset =>
                {
                    await gate.WaitAsync();
                    try
                    {
                        var item = await client.GetCatalogItemAsync(asset.Namespace!, asset.CatalogItemId!);
                        return (asset, item);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[Epic] Catalog lookup {asset.CatalogItemId} threw {ex.GetType().Name}: {ex.Message}");
                        return (asset, (EpicCatalogItem?)null);
                    }
                    finally { gate.Release(); }
                }).ToList();
                var pairs = await Task.WhenAll(lookups);

                foreach (var (asset, item) in pairs)
                {
                    var existing = await _context.Games.FirstOrDefaultAsync(g => g.EpicId == asset.CatalogItemId);
                    if (existing != null) { skipped++; continue; }

                    if (item == null)
                    {
                        _logger.Info($"[Epic] No catalog item for {asset.CatalogItemId}, skipping");
                        skipped++;
                        continue;
                    }

                    if (!item.LooksLikeGame() || string.IsNullOrEmpty(item.Title))
                    {
                        _logger.Info($"[Epic] Filtered '{item.Title ?? asset.CatalogItemId}' (not a base game)");
                        skipped++;
                        continue;
                    }

                    var byTitle = await _context.Games.FirstOrDefaultAsync(g => g.Title == item.Title && g.PlatformId == platformId);
                    if (byTitle != null)
                    {
                        byTitle.EpicId = asset.CatalogItemId;
                        byTitle.IsExternal = true;
                        await _gameRepository.UpdateAsync(byTitle.Id, byTitle);
                        skipped++;
                        continue;
                    }

                    var game = new Game
                    {
                        Title = item.Title!,
                        EpicId = asset.CatalogItemId,
                        PlatformId = platformId,
                        Status = GameStatus.Released,
                        IsExternal = true,
                        Added = DateTime.UtcNow,
                        Overview = item.Description,
                        Storyline = item.LongDescription,
                        Developer = item.Developer,
                        Year = item.GetReleaseYear() ?? 0,
                        MetadataSource = "Epic",
                        Images = new GameImages
                        {
                            CoverUrl = item.PickImage("DieselStoreFrontTall", "OfferImageTall", "Thumbnail"),
                            CoverLargeUrl = item.PickImage("DieselStoreFrontTall", "OfferImageTall"),
                            BackgroundUrl = item.PickImage("DieselStoreFrontWide", "OfferImageWide", "VaultClosed"),
                            BannerUrl = item.PickImage("DieselGameBoxLogo", "ProductLogo")
                        }
                    };

                    if (DateTime.TryParse(item.ReleaseInfo?.FirstOrDefault()?.DateAdded ?? item.CreationDate, out var dt))
                        game.ReleaseDate = dt;

                    try
                    {
                        await _gameRepository.AddAsync(game);
                        added++;
                        _logger.Info($"[Epic] Added '{item.Title}'");
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[Epic] Add game failed for '{item.Title}': {ex.Message}");
                        failed++;
                    }
                }

                _logger.Info($"[Epic] Sync done: added={added}, skipped={skipped}, failed={failed}, total={assets.Count}");
                return Ok(new
                {
                    success = true,
                    message = "Epic sync done",
                    added,
                    skipped,
                    failed,
                    total = assets.Count
                });
            }
            catch (Exception ex)
            {
                _logger.Error($"[Epic] Sync error: {ex.GetType().Name}: {ex.Message}");
                return StatusCode(500, new { success = false, message = $"{ex.GetType().Name}: {ex.Message}" });
            }
        }

        [HttpDelete("settings")]
        public ActionResult Disconnect()
        {
            _configService.SaveEpicSettings(new EpicSettings());
            return Ok(new { success = true, message = "Epic account disconnected." });
        }

        private static string ExtractCode(string raw)
        {
            // 1. raw 32-char hex token
            if (Regex.IsMatch(raw, @"^[A-Fa-f0-9]{32,}$")) return raw;

            // 2. JSON blob from the redirect page contains "authorizationCode"
            var jsonMatch = Regex.Match(raw, @"""authorizationCode""\s*:\s*""([A-Fa-f0-9]+)""");
            if (jsonMatch.Success) return jsonMatch.Groups[1].Value;

            // 3. URL with ?code=...
            var urlMatch = Regex.Match(raw, @"[?&]code=([A-Fa-f0-9]+)");
            if (urlMatch.Success) return urlMatch.Groups[1].Value;

            // 4. plain "code=..."
            var bareMatch = Regex.Match(raw, @"code=([A-Fa-f0-9]+)");
            if (bareMatch.Success) return bareMatch.Groups[1].Value;

            return raw;
        }
    }
}
