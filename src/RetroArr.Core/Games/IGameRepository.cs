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

        // Missing-flag workflow: apply what a content check of checkedPath found
        // to the stored row (Game.ApplyContent), and prune unmonitored entries
        // whose path stayed gone past the retention window. A loss is ignored
        // when the row points somewhere else by now. Returns the row's values,
        // or null when the row no longer exists.
        Task<(bool Changed, GameStatus Status, System.DateTime? MissingSince)?> ApplyContentStateAsync(int gameId, GameContent content, System.DateTime at, string? checkedPath);
        Task<List<Game>> GetMissingAsync();
        Task<int> DeleteMissingOlderThanAsync(System.DateTime threshold, IReadOnlyCollection<int> goneIds);
    }
}
