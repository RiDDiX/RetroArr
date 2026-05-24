using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Wishlist;

namespace RetroArr.Api.V3.Wishlist
{
    [ApiController]
    [Route("api/v3/wishlist")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    public class WishlistController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.General);
        private readonly WishlistPriceService _service;

        public WishlistController(WishlistPriceService service)
        {
            _service = service;
        }

        public sealed class WatchRequest
        {
            public string Provider { get; set; } = "steam";
            public string ExternalId { get; set; } = string.Empty;
            public decimal? TargetPrice { get; set; }
            public bool NotifyOnAnyDrop { get; set; } = true;
        }

        public sealed class RefreshRequest
        {
            public string CountryCode { get; set; } = "US";
        }

        [HttpGet]
        public async Task<IActionResult> GetWishlist(CancellationToken ct)
        {
            var games = await _service.ListWishlistedGamesAsync(ct).ConfigureAwait(false);
            var watches = await _service.ListAsync(ct).ConfigureAwait(false);
            var watchByGame = watches.ToDictionary(w => w.GameId, w => w);

            var entries = games.Select(g => new
            {
                game = new
                {
                    g.Id,
                    g.Title,
                    g.Year,
                    Platform = g.Platform?.Name,
                    Cover = g.Images?.CoverUrl,
                    g.SteamId,
                    g.GogId
                },
                watch = watchByGame.TryGetValue(g.Id, out var w) ? Project(w) : null
            }).ToList();

            return Ok(new { entries });
        }

        [HttpGet("{gameId:int}")]
        public async Task<IActionResult> GetWatch(int gameId, [FromQuery] string provider = "steam", CancellationToken ct = default)
        {
            var watch = await _service.GetForGameAsync(gameId, provider, ct).ConfigureAwait(false);
            return watch == null ? NotFound() : Ok(Project(watch));
        }

        [HttpPost("{gameId:int}/watch")]
        public async Task<IActionResult> Upsert(int gameId, [FromBody] WatchRequest request, CancellationToken ct)
        {
            if (request == null) return BadRequest(new { message = "request body required" });
            if (string.IsNullOrWhiteSpace(request.ExternalId))
            {
                return BadRequest(new { message = "externalId required" });
            }
            if (string.IsNullOrWhiteSpace(request.Provider)) request.Provider = "steam";
            if (request.Provider != "steam")
            {
                return BadRequest(new { message = "only 'steam' provider is supported in this release" });
            }

            try
            {
                var watch = await _service.UpsertAsync(gameId, request.Provider, request.ExternalId.Trim(),
                    request.TargetPrice, request.NotifyOnAnyDrop, ct).ConfigureAwait(false);
                return Ok(Project(watch));
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Wishlist] upsert failed: {ex.Message}");
                return StatusCode(500, new { message = "could not save the watch" });
            }
        }

        [HttpDelete("{gameId:int}/watch")]
        public async Task<IActionResult> Remove(int gameId, [FromQuery] string provider = "steam", CancellationToken ct = default)
        {
            var removed = await _service.RemoveAsync(gameId, provider, ct).ConfigureAwait(false);
            return removed ? NoContent() : NotFound();
        }

        [HttpPost("refresh")]
        public async Task<IActionResult> Refresh([FromBody] RefreshRequest? request, CancellationToken ct)
        {
            var cc = string.IsNullOrWhiteSpace(request?.CountryCode) ? "US" : request!.CountryCode.Trim().ToUpperInvariant();
            try
            {
                var summary = await _service.RefreshAllAsync(cc, ct).ConfigureAwait(false);
                return Ok(new
                {
                    checkedCount = summary.Checked,
                    updated = summary.Updated,
                    dropped = summary.Dropped,
                    targetReached = summary.TargetReached,
                    failed = summary.Failed
                });
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Wishlist] refresh failed: {ex.Message}");
                return StatusCode(500, new { message = "refresh failed" });
            }
        }

        private static object Project(WishlistPriceWatch w) => new
        {
            id = w.Id,
            gameId = w.GameId,
            provider = w.Provider,
            externalId = w.ExternalId,
            currency = w.Currency,
            currentPrice = w.CurrentPrice,
            previousPrice = w.PreviousPrice,
            targetPrice = w.TargetPrice,
            notifyOnAnyDrop = w.NotifyOnAnyDrop,
            isOnSale = w.IsOnSale,
            discountPercent = w.DiscountPercent,
            lastCheckedAt = w.LastCheckedAt,
            lastChangedAt = w.LastChangedAt,
            createdAt = w.CreatedAt
        };
    }
}
