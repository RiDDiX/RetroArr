using System;
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
