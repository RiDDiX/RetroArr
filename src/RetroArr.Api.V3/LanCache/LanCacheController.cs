using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Configuration;
using RetroArr.Core.LanCache;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Api.V3.LanCache
{
    [ApiController]
    [Route("api/v3/lancache")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    public class LanCacheController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.General);
        private readonly ConfigurationService _configService;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly SteamPrefillService _prefill;

        public LanCacheController(ConfigurationService configService, IHttpClientFactory httpClientFactory, SteamPrefillService prefill)
        {
            _configService = configService;
            _httpClientFactory = httpClientFactory;
            _prefill = prefill;
        }

        [HttpGet("settings")]
        public IActionResult GetSettings()
        {
            return Ok(_configService.LoadLanCacheSettings());
        }

        [HttpPost("settings")]
        public IActionResult SaveSettings([FromBody] LanCacheSettings settings)
        {
            if (settings == null) return BadRequest(new { message = "body required" });

            settings.Host = (settings.Host ?? string.Empty).Trim();
            // Strip an accidental scheme so "http://host" and "host" both work.
            if (settings.Host.StartsWith("http://", StringComparison.OrdinalIgnoreCase))
                settings.Host = settings.Host.Substring("http://".Length);
            else if (settings.Host.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                settings.Host = settings.Host.Substring("https://".Length);
            settings.Host = settings.Host.TrimEnd('/');
            settings.Port = settings.Port <= 0 || settings.Port > 65535 ? 80 : settings.Port;

            _configService.SaveLanCacheSettings(settings);
            return Ok(settings);
        }

        // Reachability check against the LanCache heartbeat endpoint. A monolithic
        // LanCache answers /lancache-heartbeat with 204 + an X-LanCache-Processed-By
        // header; we treat any HTTP response as "reachable" and a connect
        // failure/timeout as "not reachable".
        [HttpGet("status")]
        public async Task<IActionResult> GetStatus(CancellationToken ct)
        {
            var settings = _configService.LoadLanCacheSettings();
            if (!settings.IsConfigured)
            {
                return Ok(new { configured = false, reachable = false, message = "No LanCache host configured." });
            }

            var baseUrl = $"http://{settings.Host}:{settings.Port}";
            var client = _httpClientFactory.CreateClient();
            client.Timeout = TimeSpan.FromSeconds(4);

            try
            {
                using var resp = await client.GetAsync($"{baseUrl}/lancache-heartbeat", ct).ConfigureAwait(false);
                var processedBy = resp.Headers.TryGetValues("X-LanCache-Processed-By", out var vals)
                    ? string.Join(", ", vals)
                    : null;
                return Ok(new
                {
                    configured = true,
                    reachable = true,
                    statusCode = (int)resp.StatusCode,
                    isLanCache = processedBy != null,
                    processedBy,
                    host = settings.Host,
                    port = settings.Port
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] status check failed for {baseUrl}: {ex.Message}");
                return Ok(new
                {
                    configured = true,
                    reachable = false,
                    host = settings.Host,
                    port = settings.Port,
                    error = ex.Message
                });
            }
        }

        // Phase 1 reconcile: list the Steam library that could be prefilled. Whether
        // each title is already cached is not queryable from LanCache directly; the
        // per-title prefill state arrives with the SteamPrefill integration (phase 2).
        [HttpGet("reconcile")]
        public async Task<IActionResult> Reconcile(CancellationToken ct)
        {
            var steam = _configService.LoadSteamSettings();
            if (!steam.IsConfigured)
            {
                return Ok(new { steamConfigured = false, ownedCount = 0, games = Array.Empty<object>() });
            }

            try
            {
                var client = new SteamClient(steam.ApiKey);
                var owned = await client.GetOwnedGamesAsync(steam.SteamId).ConfigureAwait(false);
                var prefilled = _prefill.GetPrefilledAppIds();
                var games = owned
                    .OrderByDescending(g => g.PlaytimeForever)
                    .Select(g => new { appId = g.AppId, name = g.Name, playtimeMinutes = g.PlaytimeForever, prefilled = prefilled.Contains(g.AppId) })
                    .ToList();
                return Ok(new
                {
                    steamConfigured = true,
                    ownedCount = games.Count,
                    prefilledCount = games.Count(g => g.prefilled),
                    games
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] reconcile failed: {ex.Message}");
                return StatusCode(502, new { steamConfigured = true, error = "Failed to load Steam library." });
            }
        }

        // ---- SteamPrefill orchestration (phase 2) ----

        [HttpGet("prefill/status")]
        public IActionResult PrefillStatus()
        {
            return Ok(_prefill.GetStatus());
        }

        // Kick off a prefill in the background. Requires the bundled SteamPrefill
        // binary and a one-time interactive Steam login (see the returned message).
        // Progress is polled via prefill/status.
        [HttpPost("prefill/run")]
        public IActionResult PrefillRun()
        {
            if (!_prefill.IsAvailable())
                return StatusCode(503, new { started = false, message = "SteamPrefill is not bundled in this image." });
            if (!_prefill.IsLoggedIn())
                return StatusCode(409, new { started = false, message = "Not logged in to Steam. One-time login: docker exec -it retroarr /opt/steamprefill/SteamPrefill select-apps" });
            if (_prefill.GetStatus().Running)
                return Ok(new { started = false, message = "A prefill run is already in progress." });

            var settings = _configService.LoadLanCacheSettings();
            _ = Task.Run(async () =>
            {
                try { await _prefill.RunPrefillAsync(settings).ConfigureAwait(false); }
                catch (Exception ex) { _logger.Error($"[LanCache] background prefill failed: {ex.Message}"); }
            });
            return Ok(new { started = true, message = "Prefill started. Watch progress in the LanCache tab." });
        }
    }
}
