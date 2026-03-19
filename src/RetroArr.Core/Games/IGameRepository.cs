using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetroArr.Core.Games
{
    public interface IGameRepository
    {
        Task<List<Game>> GetAllAsync();
        Task<List<Game>> GetAllLightAsync();
        Task<Game?> GetByIdAsync(int id);
        Task<Game> AddAsync(Game game);
        Task<Game?> UpdateAsync(int id, Game game);
        Task<bool> DeleteAsync(int id);
        Task<int> DeleteSteamGamesAsync();
        Task<int> DeleteGogGamesAsync();
        Task DeleteAllAsync();
        Task<int?> GetPlatformIdBySlugAsync(string slug);
        Task<HashSet<int>> GetIgdbIdsAsync();
        Task<List<GameFile>> GetGameFilesAsync(int gameId);
        Task SyncGameFilesAsync(int gameId, List<GameFile> files);
        Task<bool> UpdateGameFilePathAsync(int gameFileId, string newRelativePath);
    }
}
