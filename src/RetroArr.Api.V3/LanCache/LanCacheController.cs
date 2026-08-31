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
                var ownedIds = new HashSet<uint>(owned.Select(g => (uint)g.AppId));

                var map = new Dictionary<uint, (string name, int playtime, bool shared)>();
                foreach (var g in owned) map[(uint)g.AppId] = (g.Name, g.PlaytimeForever, false);

                // Selected apps that are NOT in the owned library are family-shared /
                // CLI-selected titles. Show them so they can be seen and unticked here.
                // Names come from the public store appdetails endpoint (read-only, no
                // Steam session — we must NEVER touch SteamPrefill's login token, as
                // minting an access token from it invalidates that session).
                var extra = selected.Where(id => !ownedIds.Contains(id)).ToList();
                if (extra.Count > 0)
                {
                    var names = await ResolveAppNamesAsync(extra, ct).ConfigureAwait(false);
                    foreach (var id in extra)
                        if (!map.ContainsKey(id))
                            map[id] = (names.TryGetValue(id, out var nm) ? nm : $"App {id}", 0, true);
                }

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
                    games
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] steam apps load failed: {ex.Message}");
                return StatusCode(502, new { steamConfigured = true, error = "Failed to load Steam library." });
            }
        }

        // Resolve appId -> name via the public store appdetails endpoint. Best-effort
        // and capped so we never hammer the rate-limited endpoint; unresolved ids fall
        // back to "App {id}". No auth, no Steam session — purely read-only.
        private async Task<Dictionary<uint, string>> ResolveAppNamesAsync(List<uint> appIds, CancellationToken ct)
        {
            var names = new Dictionary<uint, string>();
            try
            {
                var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromSeconds(8);
                foreach (var id in appIds.Take(60))
                {
                    if (ct.IsCancellationRequested) break;
                    try
                    {
                        var url = $"https://store.steampowered.com/api/appdetails?appids={id}&filters=basic";
                        using var resp = await http.GetAsync(url, ct).ConfigureAwait(false);
                        if (!resp.IsSuccessStatusCode) continue;
                        using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false));
                        if (doc.RootElement.TryGetProperty(id.ToString(System.Globalization.CultureInfo.InvariantCulture), out var entry)
                            && entry.TryGetProperty("success", out var ok) && ok.ValueKind == JsonValueKind.True
                            && entry.TryGetProperty("data", out var data)
                            && data.TryGetProperty("name", out var n))
                        {
                            var nm = n.GetString();
                            if (!string.IsNullOrEmpty(nm)) names[id] = nm!;
                        }
                    }
                    catch { /* skip this id */ }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] app name resolve failed: {ex.Message}");
            }
            return names;
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

        // Persistent run history per provider (newest first): when it ran, whether it
        // was scheduled or manual, the outcome, which games it processed and where it
        // stopped. Lets the user confirm the cronjobs actually fired.
        [HttpGet("prefill/history")]
        public IActionResult PrefillHistory()
        {
            return Ok(_prefill.GetAllHistory());
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

        // Update one prefill tool to the newest upstream release. The binaries are
        // pinned at image build time, so this bridges the gap between image releases
        // (a container recreate falls back to the bundled version).
        [HttpPost("prefill/{provider}/update")]
        public async Task<IActionResult> PrefillUpdate(string provider, CancellationToken ct)
        {
            if (!_prefill.IsAvailable(provider))
                return StatusCode(503, new { updated = false, message = $"{provider} prefill is not bundled in this image." });

            try
            {
                var http = _httpClientFactory.CreateClient();
                http.Timeout = TimeSpan.FromMinutes(10);
                // api.github.com rejects requests without a User-Agent.
                http.DefaultRequestHeaders.UserAgent.ParseAdd("RetroArr/1.0");

                var result = await _prefill.UpdatePrefillAsync(provider, http, ct).ConfigureAwait(false);
                return Ok(new
                {
                    updated = result.Updated,
                    installedVersion = result.InstalledVersion,
                    latestVersion = result.LatestVersion,
                    message = result.Message
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[LanCache] prefill update ({provider}) failed: {ex.Message}");
                return StatusCode(502, new { updated = false, message = $"Update failed: {ex.Message}" });
            }
        }

        // Stop a running prefill for one provider (kills the tool's process tree).
        [HttpPost("prefill/{provider}/stop")]
        public IActionResult PrefillStop(string provider)
        {
            if (!_prefill.IsRunning(provider))
                return Ok(new { stopped = false, message = "No prefill is running for this provider." });

            var ok = _prefill.StopPrefill(provider);
            return Ok(new
            {
                stopped = ok,
                message = ok ? $"{provider} prefill stopped." : $"Could not stop the {provider} prefill."
            });
        }
    }
}
