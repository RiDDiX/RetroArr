using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Games;

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

        public WishlistPriceService(RetroArrDbContext db, SteamPriceClient steamPriceClient)
        {
            _db = db;
            _steamPriceClient = steamPriceClient;
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

                        if (previous.HasValue && quote.Final < previous.Value) summary.Dropped++;
                        if (watch.TargetPrice.HasValue && quote.Final <= watch.TargetPrice.Value) summary.TargetReached++;
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
            return summary;
        }

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
