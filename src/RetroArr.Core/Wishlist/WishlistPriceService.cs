using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.Notifications;

namespace RetroArr.Core.Wishlist
{
    public sealed class WishlistRefreshSummary
    {
        public int Checked { get; set; }
        public int Updated { get; set; }
        public int Dropped { get; set; }
        public int TargetReached { get; set; }
        public int Failed { get; set; }
    }

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class WishlistPriceService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly RetroArrDbContext _db;
        private readonly SteamPriceClient _steamPriceClient;
        private readonly IWebhookService _webhooks;

        public WishlistPriceService(RetroArrDbContext db, SteamPriceClient steamPriceClient, IWebhookService webhooks)
        {
            _db = db;
            _steamPriceClient = steamPriceClient;
            _webhooks = webhooks;
        }

        public async Task<IReadOnlyList<WishlistPriceWatch>> ListAsync(CancellationToken ct = default)
        {
            return await _db.WishlistPriceWatches
                .Include(w => w.Game)
                .OrderByDescending(w => w.LastChangedAt ?? w.CreatedAt)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }

        public async Task<WishlistPriceWatch?> GetForGameAsync(int gameId, string provider = "steam", CancellationToken ct = default)
        {
            return await _db.WishlistPriceWatches
                .Include(w => w.Game)
                .FirstOrDefaultAsync(w => w.GameId == gameId && w.Provider == provider, ct)
                .ConfigureAwait(false);
        }

        public async Task<WishlistPriceWatch> UpsertAsync(
            int gameId, string provider, string externalId, decimal? targetPrice, bool notifyOnAnyDrop,
            CancellationToken ct = default)
        {
            var existing = await _db.WishlistPriceWatches
                .FirstOrDefaultAsync(w => w.GameId == gameId && w.Provider == provider, ct)
                .ConfigureAwait(false);

            if (existing == null)
            {
                existing = new WishlistPriceWatch
                {
                    GameId = gameId,
                    Provider = provider,
                    ExternalId = externalId,
                    TargetPrice = targetPrice,
                    NotifyOnAnyDrop = notifyOnAnyDrop,
                    CreatedAt = DateTime.UtcNow
                };
                _db.WishlistPriceWatches.Add(existing);
            }
            else
            {
                existing.ExternalId = externalId;
                existing.TargetPrice = targetPrice;
                existing.NotifyOnAnyDrop = notifyOnAnyDrop;
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return existing;
        }

        public async Task<bool> RemoveAsync(int gameId, string provider = "steam", CancellationToken ct = default)
        {
            var existing = await _db.WishlistPriceWatches
                .FirstOrDefaultAsync(w => w.GameId == gameId && w.Provider == provider, ct)
                .ConfigureAwait(false);
            if (existing == null) return false;

            _db.WishlistPriceWatches.Remove(existing);
            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            return true;
        }

        public async Task<WishlistRefreshSummary> RefreshAllAsync(string countryCode = "US", CancellationToken ct = default)
        {
            var summary = new WishlistRefreshSummary();
            var watches = await _db.WishlistPriceWatches
                .Include(w => w.Game)
                    .ThenInclude(g => g!.Platform)
                .Where(w => w.Provider == "steam")
                .ToListAsync(ct)
                .ConfigureAwait(false);

            if (watches.Count == 0)
            {
                _logger.Info("[Wishlist] no steam watches to refresh.");
                return summary;
            }

            summary.Checked = watches.Count;
            var appIds = watches.Select(w => w.ExternalId).Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
            var quotes = await _steamPriceClient.GetPricesAsync(appIds, countryCode, ct).ConfigureAwait(false);
            var now = DateTime.UtcNow;

            // Collect events to fire AFTER SaveChanges, so the webhook payload
            // reflects the persisted state and a DB failure doesn't lead to
            // a "wishlist alert for a price that wasn't actually saved".
            var dropped = new List<WishlistPriceWatch>();
            var targetHit = new List<WishlistPriceWatch>();

            foreach (var watch in watches)
            {
                try
                {
                    watch.LastCheckedAt = now;

                    if (!quotes.TryGetValue(watch.ExternalId, out var quote) || quote == null)
                    {
                        summary.Failed++;
                        continue;
                    }

                    if (quote.IsFree)
                    {
                        // No price to track; mark currency empty and price 0.
                        if (watch.CurrentPrice != 0m)
                        {
                            watch.PreviousPrice = watch.CurrentPrice;
                            watch.CurrentPrice = 0m;
                            watch.Currency = quote.Currency;
                            watch.IsOnSale = false;
                            watch.DiscountPercent = 0;
                            watch.LastChangedAt = now;
                            summary.Updated++;
                        }
                        continue;
                    }

                    var previous = watch.CurrentPrice;
                    var changed = previous != quote.Final
                                  || watch.Currency != quote.Currency
                                  || watch.DiscountPercent != quote.DiscountPercent;

                    if (changed)
                    {
                        watch.PreviousPrice = previous;
                        watch.CurrentPrice = quote.Final;
                        watch.Currency = quote.Currency;
                        watch.IsOnSale = quote.DiscountPercent > 0;
                        watch.DiscountPercent = quote.DiscountPercent;
                        watch.LastChangedAt = now;
                        summary.Updated++;

                        if (previous.HasValue && quote.Final < previous.Value)
                        {
                            summary.Dropped++;
                            if (watch.NotifyOnAnyDrop) dropped.Add(watch);
                        }
                        if (watch.TargetPrice.HasValue && quote.Final <= watch.TargetPrice.Value)
                        {
                            summary.TargetReached++;
                            targetHit.Add(watch);
                        }
                    }
                }
                catch (Exception ex)
                {
                    summary.Failed++;
                    _logger.Warn($"[Wishlist] refresh failed for watch {watch.Id}: {ex.Message}");
                }
            }

            await _db.SaveChangesAsync(ct).ConfigureAwait(false);
            _logger.Info($"[Wishlist] refresh complete. checked={summary.Checked} updated={summary.Updated} dropped={summary.Dropped} target_reached={summary.TargetReached} failed={summary.Failed}");

            // Fire webhooks. TriggerAsync is fire-and-forget internally, so we
            // don't await per-event - just kick them and move on.
            foreach (var watch in dropped)
            {
                try { await _webhooks.TriggerAsync(WebhookEvents.OnWishlistPriceDropped, BuildPayload(watch)).ConfigureAwait(false); }
                catch (Exception ex) { _logger.Warn($"[Wishlist] webhook trigger (drop) failed for watch {watch.Id}: {ex.Message}"); }
            }
            foreach (var watch in targetHit)
            {
                try { await _webhooks.TriggerAsync(WebhookEvents.OnWishlistTargetReached, BuildPayload(watch)).ConfigureAwait(false); }
                catch (Exception ex) { _logger.Warn($"[Wishlist] webhook trigger (target) failed for watch {watch.Id}: {ex.Message}"); }
            }

            return summary;
        }

        private static object BuildPayload(WishlistPriceWatch watch) => new
        {
            gameId = watch.GameId,
            gameTitle = watch.Game?.Title,
            platform = watch.Game?.Platform?.Name,
            provider = watch.Provider,
            externalId = watch.ExternalId,
            currency = watch.Currency,
            currentPrice = watch.CurrentPrice,
            previousPrice = watch.PreviousPrice,
            targetPrice = watch.TargetPrice,
            discountPercent = watch.DiscountPercent,
            isOnSale = watch.IsOnSale,
            lastChangedAt = watch.LastChangedAt
        };

        public async Task<IReadOnlyList<Game>> ListWishlistedGamesAsync(CancellationToken ct = default)
        {
            // Pull games whose GameReview has IsWishlisted=true so the UI can
            // offer a "track price" button next to each one. Joined here so the
            // controller doesn't need to know about the review table.
            var query = from g in _db.Games
                        join r in _db.GameReviews on g.Id equals r.GameId
                        where r.IsWishlisted
                        select g;

            return await query
                .Include(g => g.Platform)
                .OrderBy(g => g.Title)
                .ToListAsync(ct)
                .ConfigureAwait(false);
        }
    }
}
