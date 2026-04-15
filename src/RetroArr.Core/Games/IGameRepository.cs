using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetroArr.Core.Games
{
    public interface IGameRepository
    {
        Task<List<Game>> GetAllAsync();
        Task<List<Game>> GetAllLightAsync();
        Task<PagedResult<GameListDto>> GetAllPagedAsync(int page, int pageSize, int? platformId = null, string? search = null, string sortOrder = "asc", bool? missingOnly = null, string? protonDbTier = null);
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

        // Missing-flag workflow: mark when files first disappeared, clear when
        // the file is rediscovered, and prune entries that stayed missing past
        // the retention window.
        Task<int> FlagMissingAsync(System.Collections.Generic.IEnumerable<int> gameIds, System.DateTime at);
        Task<int> ClearMissingAsync(int gameId);
        Task<List<Game>> GetMissingAsync();
        Task<int> DeleteMissingOlderThanAsync(System.DateTime threshold);
    }
}
