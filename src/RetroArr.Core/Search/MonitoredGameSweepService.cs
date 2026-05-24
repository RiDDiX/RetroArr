using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Search
{
    // Background loop that runs the monitored-game search sweep on the
    // schedule configured in MonitorSettings. Sleeps when disabled, picks
    // up new interval on the next tick (settings are re-read each cycle).
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public sealed class MonitoredGameSweepService : BackgroundService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly ConfigurationService _config;

        // Wait a bit on boot so other services finish wiring up.
        private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(2);

        public MonitoredGameSweepService(IServiceScopeFactory scopeFactory, ConfigurationService config)
        {
            _scopeFactory = scopeFactory;
            _config = config;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            try { await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false); }
            catch (TaskCanceledException) { return; }

            _logger.Info("[Monitor] sweep service starting.");

            while (!stoppingToken.IsCancellationRequested)
            {
                var settings = _config.LoadMonitorSettings();
                if (!settings.Enabled)
                {
                    // Sleep 30 min when disabled - cheap re-check without a tight loop.
                    try { await Task.Delay(TimeSpan.FromMinutes(30), stoppingToken).ConfigureAwait(false); }
                    catch (TaskCanceledException) { break; }
                    continue;
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var svc = scope.ServiceProvider.GetRequiredService<MonitoredGameSearchService>();
                    await svc.RunSweepAsync(stoppingToken).ConfigureAwait(false);
                }
                catch (TaskCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger.Warn($"[Monitor] sweep tick failed: {ex.Message}");
                }

                var hours = Math.Max(1, settings.PollIntervalHours);
                try { await Task.Delay(TimeSpan.FromHours(hours), stoppingToken).ConfigureAwait(false); }
                catch (TaskCanceledException) { break; }
            }
        }
    }
}
