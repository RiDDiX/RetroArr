using System.Threading.Tasks;
using RetroArr.Core.Games;

namespace RetroArr.Core.Launcher
{
    public interface ILaunchStrategy
    {
        bool IsSupported(Game game);
        Task LaunchAsync(Game game);
    }
}
