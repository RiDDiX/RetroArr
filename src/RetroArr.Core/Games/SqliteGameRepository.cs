using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RetroArr.Core.Games
{
    public class SqliteGameRepository : IGameRepository
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;

        public SqliteGameRepository(IDbContextFactory<RetroArrDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<Game>> GetAllAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games
                .Include(g => g.GameFiles)
                .ToListAsync();
        }

        public async Task<List<Game>> GetAllLightAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.ToListAsync();
        }

        public async Task<PagedResult<GameListDto>> GetAllPagedAsync(int page, int pageSize, int? platformId = null, string? search = null, string sortOrder = "asc", bool? missingOnly = null, string? protonDbTier = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            IQueryable<Game> query = context.Games.AsNoTracking();

            if (platformId.HasValue && platformId.Value > 0)
                query = query.Where(g => g.PlatformId == platformId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim().ToLower();
                query = query.Where(g => g.Title.ToLower().Contains(term));
            }

            if (missingOnly == true)
                query = query.Where(g => g.MissingSince != null);

            if (!string.IsNullOrWhiteSpace(protonDbTier))
            {
                var tier = protonDbTier.Trim().ToLower();
                query = query.Where(g => g.ProtonDbTier != null && g.ProtonDbTier.ToLower() == tier);
            }

            var totalItems = await query.CountAsync();

            // Tier ranking via inline CASE — SQLite can't sort on an enum-
            // like string directly, so map each tier to a number first.
            query = sortOrder switch
            {
                "desc" => query.OrderByDescending(g => g.Title),
                "protondb" => query
                    .OrderBy(g => g.ProtonDbTier == null || g.ProtonDbTier == "" ? 99
                        : g.ProtonDbTier.ToLower() == "platinum" ? 1
                        : g.ProtonDbTier.ToLower() == "gold" ? 2
                        : g.ProtonDbTier.ToLower() == "silver" ? 3
                        : g.ProtonDbTier.ToLower() == "bronze" ? 4
                        : g.ProtonDbTier.ToLower() == "native" ? 5
                        : g.ProtonDbTier.ToLower() == "pending" ? 6
                        : g.ProtonDbTier.ToLower() == "borked" ? 7
                        : 98)
                    .ThenBy(g => g.Title),
                "protondb-desc" => query
                    .OrderByDescending(g => g.ProtonDbTier == null || g.ProtonDbTier == "" ? 0
                        : g.ProtonDbTier.ToLower() == "borked" ? 1
                        : g.ProtonDbTier.ToLower() == "pending" ? 2
                        : g.ProtonDbTier.ToLower() == "native" ? 3
                        : g.ProtonDbTier.ToLower() == "bronze" ? 4
                        : g.ProtonDbTier.ToLower() == "silver" ? 5
                        : g.ProtonDbTier.ToLower() == "gold" ? 6
                        : g.ProtonDbTier.ToLower() == "platinum" ? 7
                        : 0)
                    .ThenBy(g => g.Title),
                _ => query.OrderBy(g => g.Title)
            };

            var platformLookup = PlatformDefinitions.PlatformDictionary;

            var items = await query
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(g => new GameListDto
                {
                    Id = g.Id,
                    Title = g.Title,
                    Year = g.Year,
                    CoverUrl = g.Images.CoverUrl,
                    Rating = g.Rating,
                    Genres = g.Genres,
                    PlatformId = g.PlatformId,
                    Status = g.Status,
                    SteamId = g.SteamId,
                    Path = g.Path,
                    Region = g.Region,
                    Languages = g.Languages,
                    Revision = g.Revision,
                    IgdbId = g.IgdbId,
                    ProtonDbTier = g.ProtonDbTier,
                    MissingSince = g.MissingSince
                })
                .ToListAsync();

            foreach (var item in items)
            {
                if (platformLookup.TryGetValue(item.PlatformId, out var plat))
                {
                    item.PlatformName = plat.Name;
                    item.PlatformSlug = plat.Slug;
                }
            }

            return new PagedResult<GameListDto>
            {
                Items = items,
                Page = page,
                PageSize = pageSize,
                TotalItems = totalItems,
                TotalPages = (int)Math.Ceiling((double)totalItems / pageSize)
            };
        }

        public async Task<Game?> GetByIdAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games
                .Include(g => g.GameFiles)
                .FirstOrDefaultAsync(g => g.Id == id);
        }

        public async Task<Game> AddAsync(Game game)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            context.Games.Add(game);
            await context.SaveChangesAsync();
            return game;
        }

        public async Task<Game?> UpdateAsync(int id, Game game)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.Games.FindAsync(id);
            if (existing == null) return null;

            context.Entry(existing).CurrentValues.SetValues(game);
            existing.Genres = game.Genres;

            // GameImages is OwnsOne (flattened into the Games table). Replacing
            // the reference makes EF mark the old entity as Deleted + insert a
            // new one — which blows up because there's no separate table to
            // delete from. Copy the values into the tracked instance instead.
            if (game.Images != null)
            {
                existing.Images ??= new GameImages();
                context.Entry(existing.Images).CurrentValues.SetValues(game.Images);
                existing.Images.Screenshots = game.Images.Screenshots;
                existing.Images.Artworks = game.Images.Artworks;
            }

            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                var inner = ex.GetBaseException();
                _logger.Error($"[Game] Update failed for id={id}: {inner.GetType().Name}: {inner.Message}");

                var msg = inner.Message ?? string.Empty;
                if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase))
                {
                    string? field = null;
                    if (msg.Contains("Games.Title", StringComparison.OrdinalIgnoreCase)) field = "Title+PlatformId";
                    else if (msg.Contains("Games.Path", StringComparison.OrdinalIgnoreCase)) field = "Path";
                    else if (msg.Contains("Games.IgdbId", StringComparison.OrdinalIgnoreCase)) field = "IgdbId+PlatformId";
                    throw new DuplicateGameException(
                        $"Another library entry already has this {field ?? "value"}.",
                        field, game.Title, ex);
                }

                throw new InvalidOperationException(
                    $"Could not save game {id}: {inner.Message}", ex);
            }
            return existing;
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var game = await context.Games.FindAsync(id);
            if (game == null) return false;

            context.Games.Remove(game);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<int> DeleteSteamGamesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var steamGames = await context.Games.Where(g => g.SteamId.HasValue && g.SteamId > 0).ToListAsync();
            context.Games.RemoveRange(steamGames);
            await context.SaveChangesAsync();
            return steamGames.Count;
        }

        public async Task<int> DeleteGogGamesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var gogGames = await context.Games.Where(g => !string.IsNullOrEmpty(g.GogId)).ToListAsync();
            context.Games.RemoveRange(gogGames);
            await context.SaveChangesAsync();
            return gogGames.Count;
        }

        public async Task DeleteAllAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            // RemoveRange for EF Core compatibility:
            var allGames = await context.Games.ToListAsync();
            context.Games.RemoveRange(allGames);
            await context.SaveChangesAsync();
        }

        public async Task<int?> GetPlatformIdBySlugAsync(string slug)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var platform = await context.Platforms
                .FirstOrDefaultAsync(p => p.Slug == slug);
            return platform?.Id;
        }

        public async Task<HashSet<int>> GetIgdbIdsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var ids = await context.Games
                .Where(g => g.IgdbId.HasValue)
                .Select(g => g.IgdbId!.Value)
                .ToListAsync();
            return new HashSet<int>(ids);
        }

        public async Task<List<GameFile>> GetGameFilesAsync(int gameId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.GameFiles
                .Where(f => f.GameId == gameId)
                .ToListAsync();
        }

        public async Task<bool> UpdateGameFilePathAsync(int gameFileId, string newRelativePath)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var gf = await context.GameFiles.FindAsync(gameFileId);
            if (gf == null) return false;
            gf.RelativePath = newRelativePath;
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<int> FlagMissingAsync(IEnumerable<int> gameIds, DateTime at)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var ids = gameIds as List<int> ?? gameIds.ToList();
            if (ids.Count == 0) return 0;

            var targets = await context.Games
                .Where(g => ids.Contains(g.Id) && g.MissingSince == null)
                .ToListAsync();

            foreach (var g in targets)
            {
                g.MissingSince = at;
                g.Status = GameStatus.Missing;
            }
            await context.SaveChangesAsync();
            return targets.Count;
        }

        public async Task<int> ClearMissingAsync(int gameId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var g = await context.Games.FindAsync(gameId);
            if (g == null || g.MissingSince == null) return 0;
            g.MissingSince = null;
            // Flip Missing back to Released; leave anything else alone.
            if (g.Status == GameStatus.Missing) g.Status = GameStatus.Released;
            await context.SaveChangesAsync();
            return 1;
        }

        public async Task<List<Game>> GetMissingAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games
                .AsNoTracking()
                .Where(g => g.MissingSince != null)
                .ToListAsync();
        }

        public async Task<int> DeleteMissingOlderThanAsync(DateTime threshold)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var stale = await context.Games
                .Where(g => g.MissingSince != null && g.MissingSince < threshold)
                .ToListAsync();
            if (stale.Count == 0) return 0;
            context.Games.RemoveRange(stale);
            await context.SaveChangesAsync();
            return stale.Count;
        }

        public async Task SyncGameFilesAsync(int gameId, List<GameFile> files)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.GameFiles
                .Where(f => f.GameId == gameId)
                .ToListAsync();

            var existingPaths = new HashSet<string>(existing.Select(f => f.RelativePath));
            var newPaths = new HashSet<string>(files.Select(f => f.RelativePath));

            // Remove files no longer on disk
            var toRemove = existing.Where(f => !newPaths.Contains(f.RelativePath)).ToList();
            if (toRemove.Count > 0)
                context.GameFiles.RemoveRange(toRemove);

            // Add new files or update existing ones
            foreach (var file in files)
            {
                if (!existingPaths.Contains(file.RelativePath))
                {
                    file.GameId = gameId;
                    context.GameFiles.Add(file);
                }
                else
                {
                    var existingFile = existing.First(f => f.RelativePath == file.RelativePath);
                    if (existingFile.Size != file.Size || existingFile.FileType != file.FileType)
                    {
                        existingFile.Size = file.Size;
                        existingFile.FileType = file.FileType;
                        existingFile.DateAdded = file.DateAdded;
                    }
                }
            }

            await context.SaveChangesAsync();
        }
    }
}
