using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Cache
{
    public class CachedGameRepository : IGameRepository
    {
        private readonly IGameRepository _inner;
        private readonly ICacheService _cache;
        private readonly CacheSettings _settings;

        public CachedGameRepository(IGameRepository inner, ICacheService cache, CacheSettings settings)
        {
            _inner = inner;
            _cache = cache;
            _settings = settings;
        }

        public async Task<List<Game>> GetAllAsync()
        {
            if (!_cache.IsEnabled) return await _inner.GetAllAsync();
            return await _cache.GetOrSetAsync(
                CacheKeys.GamesAll,
                () => _inner.GetAllAsync(),
                TimeSpan.FromSeconds(_settings.LibraryListTtlSeconds));
        }

        public async Task<List<Game>> GetAllLightAsync()
        {
            if (!_cache.IsEnabled) return await _inner.GetAllLightAsync();
            return await _cache.GetOrSetAsync(
                CacheKeys.GamesAll + ":light",
                () => _inner.GetAllLightAsync(),
                TimeSpan.FromSeconds(_settings.LibraryListTtlSeconds));
        }

        public Task<PagedResult<GameListDto>> GetAllPagedAsync(int page, int pageSize, int? platformId = null, string? search = null, string sortOrder = "asc")
            => _inner.GetAllPagedAsync(page, pageSize, platformId, search, sortOrder);

        public async Task<Game?> GetByIdAsync(int id)
        {
            if (!_cache.IsEnabled) return await _inner.GetByIdAsync(id);
            var cached = await _cache.GetAsync<Game>(CacheKeys.GameDetail(id));
            if (cached != null) return cached;
            var game = await _inner.GetByIdAsync(id);
            if (game != null)
                await _cache.SetAsync(CacheKeys.GameDetail(id), game, TimeSpan.FromSeconds(_settings.GameDetailTtlSeconds));
            return game;
        }

        public async Task<Game> AddAsync(Game game)
        {
            var result = await _inner.AddAsync(game);
            await InvalidateListCaches();
            return result;
        }

        public async Task<Game?> UpdateAsync(int id, Game game)
        {
            var result = await _inner.UpdateAsync(id, game);
            await _cache.RemoveAsync(CacheKeys.GameDetail(id));
            await InvalidateListCaches();
            return result;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            var result = await _inner.DeleteAsync(id);
            if (result)
            {
                await _cache.RemoveAsync(CacheKeys.GameDetail(id));
                await _cache.RemoveAsync(CacheKeys.GameFiles(id));
                await InvalidateListCaches();
            }
            return result;
        }

        public async Task<int> DeleteSteamGamesAsync()
        {
            var count = await _inner.DeleteSteamGamesAsync();
            if (count > 0) await InvalidateAllGameCaches();
            return count;
        }

        public async Task<int> DeleteGogGamesAsync()
        {
            var count = await _inner.DeleteGogGamesAsync();
            if (count > 0) await InvalidateAllGameCaches();
            return count;
        }

        public async Task DeleteAllAsync()
        {
            await _inner.DeleteAllAsync();
            await InvalidateAllGameCaches();
        }

        public Task<int?> GetPlatformIdBySlugAsync(string slug)
            => _inner.GetPlatformIdBySlugAsync(slug);

        public Task<HashSet<int>> GetIgdbIdsAsync()
            => _inner.GetIgdbIdsAsync();

        public async Task<List<GameFile>> GetGameFilesAsync(int gameId)
        {
            if (!_cache.IsEnabled) return await _inner.GetGameFilesAsync(gameId);
            return await _cache.GetOrSetAsync(
                CacheKeys.GameFiles(gameId),
                () => _inner.GetGameFilesAsync(gameId),
                TimeSpan.FromSeconds(_settings.GameDetailTtlSeconds));
        }

        public async Task SyncGameFilesAsync(int gameId, List<GameFile> files)
        {
            await _inner.SyncGameFilesAsync(gameId, files);
            await _cache.RemoveAsync(CacheKeys.GameFiles(gameId));
            await _cache.RemoveAsync(CacheKeys.GameDetail(gameId));
        }

        public async Task<bool> UpdateGameFilePathAsync(int gameFileId, string newRelativePath)
        {
            var result = await _inner.UpdateGameFilePathAsync(gameFileId, newRelativePath);
            if (result) await InvalidateAllGameCaches();
            return result;
        }

        private async Task InvalidateListCaches()
        {
            await _cache.RemoveAsync(CacheKeys.GamesAll);
            await _cache.RemoveAsync(CacheKeys.GamesAll + ":light");
            await _cache.RemoveAsync(CacheKeys.GamesProblems);
            await _cache.RemoveAsync(CacheKeys.DbStats);
        }

        private async Task InvalidateAllGameCaches()
        {
            await _cache.RemoveByPrefixAsync(CacheKeys.GamePrefix);
            await _cache.RemoveByPrefixAsync(CacheKeys.GamesPrefix);
            await _cache.RemoveAsync(CacheKeys.DbStats);
        }
    }
}
