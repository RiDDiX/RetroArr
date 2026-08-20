using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
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
        private readonly LanCachePrefillService _prefill;

        public LanCacheController(ConfigurationService configService, IHttpClientFactory httpClientFactory, LanCachePrefillService prefill)
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
                var prefilled = _prefill.GetPrefilledAppIds("steam");
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

        // ---- Steam app picker (Web-GUI selection written to selectedAppsToPrefill.json) ----

        // Owned Steam games plus which ones are currently selected for prefill.
        [HttpGet("prefill/steam/apps")]
        public async Task<IActionResult> GetSteamApps(CancellationToken ct)
        {
            var steam = _configService.LoadSteamSettings();
            if (!steam.IsConfigured)
                return Ok(new { steamConfigured = false, ownedCount = 0, familyCount = 0, selectedCount = 0, familyAvailable = false, games = Array.Empty<object>() });

            try
            {
                var client = new SteamClient(steam.ApiKey);
                var owned = await client.GetOwnedGamesAsync(steam.SteamId).ConfigureAwait(false);
                var selected = new HashSet<uint>(_prefill.GetSelectedAppIds("steam"));

                // Family-shared games via SteamPrefill's saved session (best-effort).
                var family = await GetFamilyAppsAsync(steam.SteamId, ct).ConfigureAwait(false);

                var map = new Dictionary<uint, (string name, int playtime, bool shared)>();
                foreach (var g in owned) map[(uint)g.AppId] = (g.Name, g.PlaytimeForever, false);
                foreach (var f in family) if (!map.ContainsKey(f.appId)) map[f.appId] = (f.name, 0, true);

                var games = map
                    .OrderByDescending(kv => selected.Contains(kv.Key))
                    .ThenBy(kv => kv.Value.name, StringComparer.OrdinalIgnoreCase)
                    .Select(kv => new { appId = kv.Key, name = kv.Value.name, playtimeMinutes = kv.Value.playtime, shared = kv.Value.shared, selected = selected.Contains(kv.Key) })
                    .ToList();

                return Ok(new
                {
                    steamConfigured = true,
                    ownedCount = owned.Count,
                    familyCount = games.Count(g => g.shared),
                    selectedCount = games.Count(g => g.selected),
                    familyAvailable = family.Count > 0,
                    games
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] steam apps load failed: {ex.Message}");
                return StatusCode(502, new { steamConfigured = true, error = "Failed to load Steam library." });
            }
        }

        // Family-shared apps by reusing SteamPrefill's saved refresh token. Returns
        // empty on ANY failure (not logged in, token expired, API change), so the
        // picker degrades to owned-only rather than erroring.
        private async Task<List<(uint appId, string name)>> GetFamilyAppsAsync(string steamId, CancellationToken ct)
        {
            var result = new List<(uint, string)>();
            try
            {
                var refresh = _prefill.GetSteamRefreshToken();
                if (string.IsNullOrEmpty(refresh)) return result;

                var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(15);

                var accessToken = await GenerateAccessTokenAsync(http, refresh!, steamId, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(accessToken)) return result;

                var familyGroupId = await GetFamilyGroupIdAsync(http, accessToken!, steamId, ct).ConfigureAwait(false);
                if (string.IsNullOrEmpty(familyGroupId)) return result;

                var url = $"https://api.steampowered.com/IFamilyGroupsService/GetSharedLibraryApps/v1/?access_token={Uri.EscapeDataString(accessToken!)}&family_groupid={Uri.EscapeDataString(familyGroupId!)}&steamid={Uri.EscapeDataString(steamId)}&include_own=false&include_free=false&include_non_games=false&max_apps=5000";
                using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                if (!resp.IsSuccessStatusCode) return result;

                using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                if (doc.RootElement.TryGetProperty("response", out var r)
                    && r.TryGetProperty("apps", out var apps) && apps.ValueKind == JsonValueKind.Array)
                {
                    foreach (var a in apps.EnumerateArray())
                    {
                        if (!a.TryGetProperty("appid", out var idEl)) continue;
                        uint id = idEl.ValueKind == JsonValueKind.Number
                            ? (uint)idEl.GetInt64()
                            : (uint.TryParse(idEl.GetString(), out var pid) ? pid : 0);
                        if (id == 0) continue;
                        var name = a.TryGetProperty("name", out var n) ? (n.GetString() ?? $"App {id}") : $"App {id}";
                        result.Add((id, name));
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] family library fetch failed: {ex.Message}");
            }
            return result;
        }

        private static async Task<string?> GenerateAccessTokenAsync(HttpClient http, string refreshToken, string steamId, CancellationToken ct)
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("refresh_token", refreshToken),
                new KeyValuePair<string, string>("steamid", steamId)
            });
            using var resp = await http.PostAsync("https://api.steampowered.com/IAuthenticationService/GenerateAccessTokenForApp/v1/", body, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            return doc.RootElement.TryGetProperty("response", out var r) && r.TryGetProperty("access_token", out var at)
                ? at.GetString() : null;
        }

        private static async Task<string?> GetFamilyGroupIdAsync(HttpClient http, string accessToken, string steamId, CancellationToken ct)
        {
            var url = $"https://api.steampowered.com/IFamilyGroupsService/GetFamilyGroupForUser/v1/?access_token={Uri.EscapeDataString(accessToken)}&steamid={Uri.EscapeDataString(steamId)}";
            using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) return null;
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
            if (!doc.RootElement.TryGetProperty("response", out var r)) return null;
            if (r.TryGetProperty("family_groupid", out var g))
                return g.ValueKind == JsonValueKind.Number
                    ? g.GetInt64().ToString(System.Globalization.CultureInfo.InvariantCulture)
                    : g.GetString();
            return null;
        }

        public sealed class SteamAppsSelectionRequest
        {
            [SuppressMessage("Microsoft.Design", "CA2227:CollectionPropertiesShouldBeReadOnly")]
            [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
            public List<uint> AppIds { get; set; } = new();
        }

        // Persist the Web-GUI selection to the same file `select-apps` uses.
        [HttpPost("prefill/steam/apps")]
        public async Task<IActionResult> SetSteamApps([FromBody] SteamAppsSelectionRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "body required" });
            var picked = new HashSet<uint>(request.AppIds ?? new List<uint>());

            // Merge: preserve any already-selected appIds that are NOT in the owned
            // library the Web-GUI can see — i.e. family-shared / CLI-selected titles.
            // The GUI only manages owned games; it must not wipe those other picks.
            var preserved = new List<uint>();
            try
            {
                var steam = _configService.LoadSteamSettings();
                if (steam.IsConfigured)
                {
                    var client = new SteamClient(steam.ApiKey);
                    var owned = await client.GetOwnedGamesAsync(steam.SteamId).ConfigureAwait(false);
                    var ownedIds = new HashSet<uint>(owned.Select(g => (uint)g.AppId));
                    foreach (var id in _prefill.GetSelectedAppIds("steam"))
                        if (!ownedIds.Contains(id)) preserved.Add(id);
                }
                else
                {
                    // No owned list to compare against — keep existing picks to be safe.
                    preserved.AddRange(_prefill.GetSelectedAppIds("steam"));
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] steam save merge fell back (keeping existing picks): {ex.Message}");
                preserved.AddRange(_prefill.GetSelectedAppIds("steam"));
            }

            var final = new HashSet<uint>(picked);
            foreach (var id in preserved) final.Add(id);

            var ok = _prefill.SetSelectedAppIds("steam", final);
            if (!ok) return StatusCode(500, new { saved = false, message = "Could not write the selection." });
            return Ok(new { saved = true, selectedCount = final.Count, ownedSelected = picked.Count, preservedNonOwned = preserved.Count });
        }

        // ---- Prefill orchestration: Steam / Battle.net / Epic ----

        [HttpGet("prefill/status")]
        public IActionResult PrefillStatus()
        {
            return Ok(_prefill.GetAllStatus());
        }

        // Kick off a prefill for one provider in the background. Requires the bundled
        // binary and (where applicable) a one-time interactive login. Progress is
        // polled via prefill/status.
        [HttpPost("prefill/{provider}/run")]
        public IActionResult PrefillRun(string provider)
        {
            if (!_prefill.IsAvailable(provider))
                return StatusCode(503, new { started = false, message = $"{provider} prefill is not bundled in this image." });
            if (!_prefill.IsLoggedIn(provider))
                return StatusCode(409, new { started = false, message = $"Not logged in for {provider}. Run the one-time login first (see the tab)." });
            if (_prefill.IsRunning(provider))
                return Ok(new { started = false, message = "A prefill run is already in progress for this provider." });

            var settings = _configService.LoadLanCacheSettings();
            _ = Task.Run(async () =>
            {
                try { await _prefill.RunPrefillAsync(provider, settings).ConfigureAwait(false); }
                catch (Exception ex) { _logger.Error($"[LanCache] background prefill ({provider}) failed: {ex.Message}"); }
            });
            return Ok(new { started = true, message = $"{provider} prefill started. Watch progress in the LanCache tab." });
        }
    }
}
