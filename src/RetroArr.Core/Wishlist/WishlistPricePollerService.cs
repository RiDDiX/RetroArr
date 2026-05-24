using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace RetroArr.Core.Wishlist
{
    // Cycles the wishlist price refresh on its own so users don't have to
    // hit the manual refresh. Country and interval are set via env vars to
    // avoid a settings-UI dependency in this slice; a Settings tab can
    // override later by writing to a JSON in ConfigurationService.
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class WishlistPricePollerService : BackgroundService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly IServiceScopeFactory _scopeFactory;

        // First refresh fires ~30s after boot so the app finishes settling
        // before hammering an external API.
        private static readonly TimeSpan StartupDelay = TimeSpan.FromSeconds(30);

        public WishlistPricePollerService(IServiceScopeFactory scopeFactory)
        {
            _scopeFactory = scopeFactory;
        }

        private static TimeSpan ReadInterval()
        {
            var raw = Environment.GetEnvironmentVariable("RETROARR_WISHLIST_POLL_HOURS");
            if (int.TryParse(raw, out var hours) && hours >= 1 && hours <= 168)
            {
                return TimeSpan.FromHours(hours);
            }
            return TimeSpan.FromHours(6);
        }

        private static string ReadCountry()
        {
            var raw = Environment.GetEnvironmentVariable("RETROARR_WISHLIST_COUNTRY");
            if (!string.IsNullOrWhiteSpace(raw) && raw.Length <= 4)
            {
                return raw.Trim().ToUpperInvariant();
            }
            return "US";
        }

        private static bool IsEnabled()
        {
            var raw = Environment.GetEnvironmentVariable("RETROARR_WISHLIST_POLL_DISABLE");
            return !string.Equals(raw, "1", StringComparison.Ordinal)
                && !string.Equals(raw, "true", StringComparison.OrdinalIgnoreCase);
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            if (!IsEnabled())
            {
                _logger.Info("[Wishlist] poller disabled via RETROARR_WISHLIST_POLL_DISABLE.");
                return;
            }

            try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }

            var interval = ReadInterval();
            var country = ReadCountry();
            _logger.Info($"[Wishlist] poller starting. interval={interval.TotalHours}h country={country}");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<WishlistPriceService>();
                    var summary = await service.RefreshAllAsync(country, stoppingToken).ConfigureAwait(false);
                    _logger.Info($"[Wishlist] auto-refresh: checked={summary.Checked} updated={summary.Updated} dropped={summary.Dropped} target_reached={summary.TargetReached} failed={summary.Failed}");
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Warn($"[Wishlist] auto-refresh tick failed: {ex.Message}");
                }

                try { await Task.Delay(interval, stoppingToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}
