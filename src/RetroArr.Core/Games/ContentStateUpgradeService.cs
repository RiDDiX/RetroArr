using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Games
{
    // Once after the upgrade: games that older versions stored as Released although their files were there are promoted
    // (MediaScannerService.PromotePresentGamesAsync). Waits for a running scan; the marker in the config folder says it ran.
    public class ContentStateUpgradeService : BackgroundService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        internal const string MarkerName = "content-state-1.done";

        private readonly MediaScannerService _scanner;
        private readonly ConfigurationService _config;
        private readonly TimeSpan _retry;

        public ContentStateUpgradeService(MediaScannerService scanner, ConfigurationService config) : this(scanner, config, TimeSpan.FromMinutes(1)) { }

        internal ContentStateUpgradeService(MediaScannerService scanner, ConfigurationService config, TimeSpan retry)
        {
            _scanner = scanner;
            _config = config;
            _retry = retry;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            // Off the startup path
            await Task.Yield();
            var marker = Path.Combine(_config.GetConfigDirectory(), MarkerName);
            if (File.Exists(marker)) return;
            try
            {
                int? promoted;
                while ((promoted = await _scanner.PromotePresentGamesAsync()) == null)
                    await Task.Delay(_retry, stoppingToken);
                File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
                _logger.Info($"[Library] Content pass after the upgrade: {promoted} game(s) with their files now count as downloaded");
            }
            catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
            {
                _logger.Info("[Library] Content pass after the upgrade was stopped; it runs again on the next start");
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // Tried again on the next start
                _logger.Warn($"[Library] Content pass after the upgrade failed: {ex.Message}");
            }
        }
    }
}
