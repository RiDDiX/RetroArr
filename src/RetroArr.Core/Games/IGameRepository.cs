using System.Collections.Generic;
using System.Threading.Tasks;

namespace RetroArr.Core.Games
{
    public interface IGameRepository
    {
        Task<List<Game>> GetAllLightAsync();
        // Only the columns the problems list checks, for every game
        Task<List<GameProblemRow>> GetProblemRowsAsync();
        // Only the paths, for every game
        Task<List<GamePathRow>> GetPathRowsAsync();
        // Number of games per PlatformId, counted by the database
        Task<Dictionary<int, int>> CountByPlatformAsync();
        // Id, Title, PlatformId, SteamId and ProtonDbTier of the games with a Steam AppID or on the Steam or PC platform, by id
        Task<List<GameListDto>> GetSteamRowsAsync();
        // The games waiting for a metadata review, with the columns the review list shows
        Task<List<GameReviewRow>> GetReviewRowsAsync();
        // The columns a metadata rescan picks its games by, of every game or of one platform, by id
        Task<List<GameMetadataRow>> GetMetadataRowsAsync(int? platformId);
        // Id, Title, PlatformId, Region and Path of the games of the platform with this title in any case, by title and id
        Task<List<GameListDto>> FindByTitleAsync(int platformId, string title);
        // Id, Title, PlatformId, Region, Path and IgdbId of the games of the platform with this IGDB id, by id
        Task<List<GameListDto>> FindByIgdbIdAsync(int platformId, int igdbId);
        Task<PagedResult<GameListDto>> GetAllPagedAsync(int page, int pageSize, int? platformId = null, string? search = null, string sortOrder = "asc", bool? missingOnly = null, string? protonDbTier = null);
        Task<Game?> GetByIdAsync(int id);
        Task<Game> AddAsync(Game game);
        Task<Game?> UpdateAsync(int id, Game game);
        // Runs change on the row as it is stored now and saves only the columns it modified, so a field
        // someone else changed since the caller read the game survives. Returns the saved row, or null
        // when the row no longer exists.
        Task<Game?> UpdateFieldsAsync(int id, System.Action<Game> change);
        Task<bool> DeleteAsync(int id);
        // The ids that were there and are gone now
        Task<List<int>> DeleteManyAsync(IReadOnlyCollection<int> gameIds);
        Task<int> DeleteSteamGamesAsync();
        Task<int> DeleteGogGamesAsync();
        Task DeleteAllAsync();
        Task<int?> GetPlatformIdBySlugAsync(string slug);
        Task<HashSet<int>> GetIgdbIdsAsync();
        // Id, Title and PlatformId of these games
        Task<List<GameListDto>> GetListItemsAsync(IReadOnlyCollection<int> gameIds);
        Task<List<GameFile>> GetGameFilesAsync(int gameId);
        Task SyncGameFilesAsync(int gameId, List<GameFile> files);
        // Adds the files not stored yet and updates the others; never removes a row
        Task UpsertGameFilesAsync(int gameId, List<GameFile> files);
        // The update and DLC rows (all but Main) of these games
        Task<List<GameFile>> GetSupplementaryFilesAsync(IReadOnlyCollection<int> gameIds);
        // Removes these rows; returns the games that lost one
        Task<List<int>> RemoveGameFilesAsync(IReadOnlyCollection<int> gameFileIds);
        Task<bool> UpdateGameFilePathAsync(int gameFileId, string newRelativePath);

        // Missing-flag workflow: apply what a content check of checkedPath found
        // to the stored row (Game.ApplyContent), and prune unmonitored entries
        // whose path stayed gone past the retention window. A loss is ignored
        // when the row points somewhere else by now. Returns the row's values,
        // or null when the row no longer exists.
        Task<(bool Changed, GameStatus Status, System.DateTime? MissingSince)?> ApplyContentStateAsync(int gameId, GameContent content, System.DateTime at, string? checkedPath);
        // Id, Status, Monitored and MissingSince of those of these games that have a MissingSince
        Task<List<Game>> GetMissingAsync(IReadOnlyCollection<int> gameIds);
        Task<int> DeleteMissingOlderThanAsync(System.DateTime threshold, IReadOnlyCollection<int> goneIds);
    }
}
