using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using RetroArr.Core.Games;

namespace RetroArr.Core.Launcher
{
    public class GogLaunchStrategy : ILaunchStrategy
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Launcher);
        public bool IsSupported(Game game)
        {
            return !string.IsNullOrEmpty(game.GogId);
        }

        public Task LaunchAsync(Game game)
        {
            if (string.IsNullOrEmpty(game.GogId))
            {
                throw new InvalidOperationException("Game does not have a valid GOG ID.");
            }

            // GOG Galaxy URL protocol: goggalaxy://runGame/{productId}
            var gogUrl = $"goggalaxy://runGame/{game.GogId}";
            _logger.Info($"[GogLaunchStrategy] Launching: {gogUrl}");

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.UseShellExecute = true;

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = gogUrl;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo.FileName = "open";
                startInfo.Arguments = gogUrl;
                startInfo.UseShellExecute = false;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                // GOG Galaxy is not natively available on Linux
                // Try xdg-open anyway in case user has a handler
                startInfo.FileName = "xdg-open";
                startInfo.Arguments = gogUrl;
                startInfo.UseShellExecute = false;
            }

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                _logger.Error($"[GogLaunchStrategy] Launch failure: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }
    }
}
