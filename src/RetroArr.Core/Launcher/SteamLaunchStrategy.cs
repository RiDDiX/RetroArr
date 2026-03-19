using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using RetroArr.Core.Games;

namespace RetroArr.Core.Launcher
{
    public class SteamLaunchStrategy : ILaunchStrategy
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Launcher);
        public bool IsSupported(Game game)
        {
            return game.SteamId.HasValue && game.SteamId.Value > 0;
        }

        public Task LaunchAsync(Game game)
        {
            if (!game.SteamId.HasValue)
            {
                throw new InvalidOperationException("Game does not have a valid Steam ID.");
            }

            var steamUrl = $"steam://run/{game.SteamId}";
            _logger.Info($"[SteamLaunchStrategy] Launching: {steamUrl}");

            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.UseShellExecute = true; // Required to open URLs/Protocols

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                startInfo.FileName = steamUrl;
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                startInfo.FileName = "open";
                startInfo.Arguments = steamUrl;
                startInfo.UseShellExecute = false; // 'open' is an executable
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                startInfo.FileName = "xdg-open";
                startInfo.Arguments = steamUrl;
                startInfo.UseShellExecute = false;
            }

            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamLaunchStrategy] Key failure: {ex.Message}");
                throw;
            }

            return Task.CompletedTask;
        }
    }
}
