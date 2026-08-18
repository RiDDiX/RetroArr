using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Search;

namespace RetroArr.Api.V3.Monitor
{
    [ApiController]
    [Route("api/v3")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    public class MonitorController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ReleaseSearch);
        private readonly RetroArrDbContext _db;
        private readonly ConfigurationService _config;
        private readonly MonitoredGameSearchService _search;

        public MonitorController(RetroArrDbContext db, ConfigurationService config, MonitoredGameSearchService search)
        {
            _db = db;
            _config = config;
            _search = search;
        }

        public sealed class MonitoredToggleRequest
        {
            public bool Monitored { get; set; }
        }

        [HttpPut("games/{id:int}/monitored")]
        public async Task<IActionResult> SetMonitored(int id, [FromBody] MonitoredToggleRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "body required" });

            var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
            if (game == null) return NotFound();

            game.Monitored = request.Monitored;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Ok(new { id, monitored = game.Monitored });
        }

        public sealed class PlatformMonitoredRequest
        {
            public bool Monitored { get; set; }
            // Apply the flag to every game already on the platform (bulk). Defaults true so
            // the platform toggle behaves like Sonarr/Radarr "monitor all".
            public bool ApplyToExisting { get; set; } = true;
        }

        // Toggle monitoring for a whole platform: persists the per-platform default for
        // newly added games AND (by default) bulk-applies it to existing games. The
        // per-game Game.Monitored flag stays authoritative for the sweep, so a single game
        // can still be monitored even when its platform default is off.
        [HttpPut("platforms/{platformId:int}/monitored")]
        public async Task<IActionResult> SetPlatformMonitored(int platformId, [FromBody] PlatformMonitoredRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "body required" });

            RetroArr.Core.Games.PlatformService.SetMonitorNewItemsDefault(platformId, request.Monitored);

            int updated = 0;
            int total;
            if (request.ApplyToExisting)
            {
                var games = await _db.Games
                    .Where(g => g.PlatformId == platformId)
                    .ToListAsync(ct)
                    .ConfigureAwait(false);
                total = games.Count;
                foreach (var g in games)
                {
                    if (g.Monitored != request.Monitored)
                    {
                        g.Monitored = request.Monitored;
                        updated++;
                    }
                }
                if (updated > 0) await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            }
            else
            {
                total = await _db.Games.CountAsync(g => g.PlatformId == platformId, ct).ConfigureAwait(false);
            }

            return Ok(new { platformId, monitored = request.Monitored, updated, total });
        }

        // Per-platform monitored/total counts for tri-state UI, plus the stored default.
        [HttpGet("platforms/monitored-counts")]
        public async Task<IActionResult> GetPlatformMonitoredCounts(CancellationToken ct)
        {
            var grouped = await _db.Games
                .GroupBy(g => g.PlatformId)
                .Select(grp => new
                {
                    PlatformId = grp.Key,
                    Total = grp.Count(),
                    Monitored = grp.Sum(x => x.Monitored ? 1 : 0)
                })
                .ToListAsync(ct)
                .ConfigureAwait(false);

            var defaults = RetroArr.Core.Games.PlatformService.GetAllMonitorNewItemsDefaults();

            var map = new Dictionary<string, object>();
            foreach (var row in grouped)
            {
                var key = row.PlatformId.ToString(System.Globalization.CultureInfo.InvariantCulture);
                map[key] = new
                {
                    total = row.Total,
                    monitored = row.Monitored,
                    monitorDefault = defaults.TryGetValue(row.PlatformId, out var d) ? (bool?)d : null
                };
            }
            // Platforms with a stored default but no games yet.
            foreach (var kv in defaults)
            {
                var key = kv.Key.ToString(System.Globalization.CultureInfo.InvariantCulture);
                if (!map.ContainsKey(key))
                {
                    map[key] = new { total = 0, monitored = 0, monitorDefault = (bool?)kv.Value };
                }
            }

            return Ok(map);
        }

        public sealed class PreferredGroupRequest
        {
            public string? Group { get; set; }
        }

        [HttpPut("games/{id:int}/preferred-group")]
        public async Task<IActionResult> SetPreferredGroup(int id, [FromBody] PreferredGroupRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "body required" });

            var game = await _db.Games.FirstOrDefaultAsync(g => g.Id == id, ct).ConfigureAwait(false);
            if (game == null) return NotFound();

            // Empty string == clear the preference.
            var raw = (request.Group ?? string.Empty).Trim();
            game.PreferredReleaseGroup = raw.Length == 0 ? null : raw;
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return Ok(new { id, preferredReleaseGroup = game.PreferredReleaseGroup });
        }

        [HttpPost("games/{id:int}/search-now")]
        public async Task<IActionResult> SearchNow(int id, [FromQuery] bool autoDispatch = false, CancellationToken ct = default)
        {
            try
            {
                var result = await _search.SearchAndMaybeDispatchAsync(id, autoDispatch, ct).ConfigureAwait(false);
                return Ok(new
                {
                    gameId = result.GameId,
                    gameTitle = result.GameTitle,
                    autoQueued = result.AutoQueued,
                    autoQueuedRelease = result.AutoQueuedRelease,
                    autoQueuedScore = result.AutoQueuedScore,
                    error = result.Error,
                    scored = result.Scored.Select(s => new
                    {
                        score = s.Score,
                        decision = s.Decision.ToString(),
                        reason = s.Reason,
                        signals = s.Signals,
                        title = s.Release.Title,
                        provider = s.Release.Provider,
                        indexer = s.Release.IndexerName,
                        size = s.Release.Size,
                        formattedSize = s.Release.FormattedSize,
                        seeders = s.Release.EffectiveSeeders,
                        leechers = s.Release.EffectiveLeechers,
                        publishDate = s.Release.PublishDate,
                        detectedPlatform = s.Release.DetectedPlatform,
                        platformFolder = s.Release.PlatformFolder,
                        protocol = s.Release.Protocol,
                        downloadUrl = s.Release.DownloadUrl,
                        magnetUrl = s.Release.MagnetUrl,
                        infoUrl = s.Release.InfoUrl
                    })
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Monitor] search-now failed for game {id}: {ex.Message}");
                return StatusCode(500, new { message = "search failed" });
            }
        }

        [HttpGet("settings/monitor")]
        public IActionResult GetMonitorSettings()
        {
            return Ok(_config.LoadMonitorSettings());
        }

        [HttpPost("settings/monitor")]
        public IActionResult SaveMonitorSettings([FromBody] MonitorSettings settings)
        {
            if (settings == null) return BadRequest(new { message = "body required" });
            // Clamp untrusted input to sane ranges.
            settings.PollIntervalHours = Math.Clamp(settings.PollIntervalHours, 1, 168);
            settings.AutoDownloadThreshold = Math.Clamp(settings.AutoDownloadThreshold, 0, 100);
            settings.ReviewThreshold = Math.Clamp(settings.ReviewThreshold, 0, 100);
            settings.MinSeedersTorrent = Math.Max(0, settings.MinSeedersTorrent);
            settings.MinTitleSimilarityPercent = Math.Clamp(settings.MinTitleSimilarityPercent, 0, 100);
            settings.MaxReleaseAgeDays = Math.Max(0, settings.MaxReleaseAgeDays);
            _config.SaveMonitorSettings(settings);
            return Ok(settings);
        }
    }
}
