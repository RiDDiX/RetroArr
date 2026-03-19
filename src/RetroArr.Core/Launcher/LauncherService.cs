using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using RetroArr.Core.Games;

namespace RetroArr.Core.Launcher
{
    public interface ILauncherService
    {
        Task LaunchGameAsync(Game game);
    }

    public class LauncherService : ILauncherService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Launcher);
        private readonly IEnumerable<ILaunchStrategy> _strategies;

        public LauncherService(IEnumerable<ILaunchStrategy> strategies)
        {
            _strategies = strategies;
        }

        public async Task LaunchGameAsync(Game game)
        {
            var strategy = _strategies.FirstOrDefault(s => s.IsSupported(game));

            if (strategy == null)
            {
                _logger.Info($"[LauncherService] No suitable launch strategy found for game: {game.Title}");
                throw new System.Exception("No suitable launch strategy found for this game.");
            }

            _logger.Info($"[LauncherService] Launching game '{game.Title}' using strategy: {strategy.GetType().Name}");
            await strategy.LaunchAsync(game);
        }
    }
}
