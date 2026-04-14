using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;

namespace RetroArr.Core.Games
{
    public class TrashPurgeService : BackgroundService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly TrashService _trash;

        // Daily tick; purge happens only when entries have aged past the
        // retention-days setting (which the service itself reads each run).
        private static readonly TimeSpan Interval = TimeSpan.FromHours(24);

        public TrashPurgeService(TrashService trash)
        {
            _trash = trash;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // First run on boot, then on interval.
            try { _trash.PurgeExpired(); }
            catch (Exception ex) { _logger.Warn($"[Trash] Initial purge failed: {ex.Message}"); }

            while (!stoppingToken.IsCancellationRequested)
            {
                try { await Task.Delay(Interval, stoppingToken); }
                catch (TaskCanceledException) { break; }

                try { _trash.PurgeExpired(); }
                catch (Exception ex) { _logger.Warn($"[Trash] Scheduled purge failed: {ex.Message}"); }
            }
        }
    }
}
