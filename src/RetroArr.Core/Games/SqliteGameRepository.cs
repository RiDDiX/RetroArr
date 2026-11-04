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

        // Untracked: the context is gone on return, and tracking a big library costs seconds
        public async Task<List<Game>> GetAllLightAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking().ToListAsync();
        }

        public async Task<List<GameProblemRow>> GetProblemRowsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Select(g => new GameProblemRow
                {
                    Id = g.Id,
                    Title = g.Title,
                    Path = g.Path,
                    PlatformId = g.PlatformId,
                    Region = g.Region,
                    MissingSince = g.MissingSince,
                    ProblemResolvedAt = g.ProblemResolvedAt,
                    NoMetadata = g.IgdbId == null && (g.Overview == null || g.Overview.Length == 0)
                })
                .ToListAsync();
        }

        public async Task<List<GamePathRow>> GetPathRowsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Select(g => new GamePathRow { Id = g.Id, Path = g.Path, ExecutablePath = g.ExecutablePath })
                .ToListAsync();
        }

        public async Task<Dictionary<int, int>> CountByPlatformAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games
                .GroupBy(g => g.PlatformId)
                .Select(grp => new { PlatformId = grp.Key, Count = grp.Count() })
                .ToDictionaryAsync(x => x.PlatformId, x => x.Count);
        }

        public async Task<List<GameListDto>> GetSteamRowsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Where(g => g.SteamId > 0 || g.PlatformId == PlatformDefinitions.SteamPlatformId || g.PlatformId == PlatformDefinitions.PcPlatformId)
                .OrderBy(g => g.Id)
                .Select(g => new GameListDto
                {
                    Id = g.Id,
                    Title = g.Title,
                    PlatformId = g.PlatformId,
                    SteamId = g.SteamId,
                    ProtonDbTier = g.ProtonDbTier
                })
                .ToListAsync();
        }

        public async Task<List<GameReviewRow>> GetReviewRowsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Where(g => g.NeedsMetadataReview && !g.MetadataConfirmedByUser)
                .Select(g => new GameReviewRow
                {
                    Id = g.Id,
                    Title = g.Title,
                    AlternativeTitle = g.AlternativeTitle,
                    Path = g.Path,
                    PlatformId = g.PlatformId,
                    MatchConfidence = g.MatchConfidence,
                    MetadataReviewReason = g.MetadataReviewReason,
                    IgdbId = g.IgdbId,
                    CoverUrl = g.Images.CoverUrl,
                    Added = g.Added
                })
                .ToListAsync();
        }

        public async Task<List<GameMetadataRow>> GetMetadataRowsAsync(int? platformId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            IQueryable<Game> query = context.Games.AsNoTracking();
            if (platformId.HasValue) query = query.Where(g => g.PlatformId == platformId.Value);
            return await query
                .OrderBy(g => g.Id)
                .Select(g => new GameMetadataRow
                {
                    Id = g.Id,
                    Title = g.Title,
                    PlatformId = g.PlatformId,
                    Incomplete = g.IgdbId == null || g.Overview == null || g.Overview.Length == 0,
                    MetadataSource = g.MetadataSource,
                    MetadataConfirmedByUser = g.MetadataConfirmedByUser
                })
                .ToListAsync();
        }

        public async Task<List<GameListDto>> FindByTitleAsync(int platformId, string title)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            // SQLite's lower() folds ASCII letters only (Ōkami, Élite): the database narrows by length, the title is compared here.
            // length() counts code points, as case folding keeps them.
            var length = title.EnumerateRunes().Count();
            var rows = await context.Games.AsNoTracking()
                .Where(g => g.PlatformId == platformId && g.Title.Length == length)
                .OrderBy(g => g.Title).ThenBy(g => g.Id)
                .Select(g => new GameListDto { Id = g.Id, Title = g.Title, PlatformId = g.PlatformId, Region = g.Region, Path = g.Path })
                .ToListAsync();
            return rows.Where(g => g.Title.Equals(title, StringComparison.OrdinalIgnoreCase)).ToList();
        }

        public async Task<List<GameListDto>> FindByIgdbIdAsync(int platformId, int igdbId)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Where(g => g.PlatformId == platformId && g.IgdbId == igdbId)
                .OrderBy(g => g.Id)
                .Select(g => new GameListDto { Id = g.Id, Title = g.Title, PlatformId = g.PlatformId, Region = g.Region, Path = g.Path, IgdbId = g.IgdbId })
                .ToListAsync();
        }

        public async Task<PagedResult<GameListDto>> GetAllPagedAsync(int page, int pageSize, int? platformId = null, string? search = null, string sortOrder = "asc", bool? missingOnly = null, string? protonDbTier = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            IQueryable<Game> query = context.Games.AsNoTracking();

            if (platformId.HasValue && platformId.Value > 0)
                query = query.Where(g => g.PlatformId == platformId.Value);

            if (!string.IsNullOrWhiteSpace(search))
            {
                // The title as typed too: SQLite's lower() leaves non-ASCII capitals ("Ökoland") as they are
                var typed = search.Trim();
                var term = typed.ToLower();
                query = query.Where(g => g.Title.ToLower().Contains(term) || g.Title.Contains(typed));
            }

            if (missingOnly == true)
                query = query.Where(g => g.MissingSince != null);

            if (!string.IsNullOrWhiteSpace(protonDbTier))
            {
                var tier = protonDbTier.Trim().ToLower();
                query = query.Where(g => g.ProtonDbTier != null && g.ProtonDbTier.ToLower() == tier);
            }

            var totalItems = await query.CountAsync();

            // Tier ranking via inline CASE - SQLite can't sort on an enum-
            // like string directly, so map each tier to a number first.
            query = sortOrder switch
            {
                "desc" => query.OrderByDescending(g => g.Title).ThenBy(g => g.Id),
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
                    .ThenBy(g => g.Title).ThenBy(g => g.Id),
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
                    .ThenBy(g => g.Title).ThenBy(g => g.Id),
                // Id last: PostgreSQL and MariaDB return ties in any order, so equal
                // titles could show on two pages or none
                _ => query.OrderBy(g => g.Title).ThenBy(g => g.Id)
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
                    MissingSince = g.MissingSince,
                    Monitored = g.Monitored
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
            EnsureKnownPlatform(game.PlatformId);
            CanonicalizeRegion(game);
            // the Library add, the scanner and discovery import don't set it
            if (game.Added == default) game.Added = DateTime.UtcNow;
            using var context = await _contextFactory.CreateDbContextAsync();
            context.Games.Add(game);
            await SaveAsync(context, game.Id, game.Title);
            return game;
        }

        public async Task<Game?> UpdateAsync(int id, Game game)
        {
            EnsureKnownPlatform(game.PlatformId);
            CanonicalizeRegion(game);
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.Games.FindAsync(id);
            if (existing == null) return null;

            // Only resolve-problem and a new loss set it, never a copy loaded before the click
            var resolved = existing.ProblemResolvedAt;
            context.Entry(existing).CurrentValues.SetValues(game);
            existing.ProblemResolvedAt = resolved;
            existing.Genres = game.Genres;

            // SetValues copies Game's own properties, not the owned GameImages
            // (OwnsOne, flattened into the Games table), so its values and
            // lists are copied into the tracked instance here.
            if (game.Images != null)
            {
                existing.Images ??= new GameImages();
                context.Entry(existing.Images).CurrentValues.SetValues(game.Images);
                existing.Images.Screenshots = game.Images.Screenshots;
                existing.Images.Artworks = game.Images.Artworks;
            }

            await SaveAsync(context, id, game.Title);
            return existing;
        }

        // Tracked, so the save writes only the columns change modified
        public async Task<Game?> UpdateFieldsAsync(int id, Action<Game> change)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.Games.FindAsync(id);
            if (existing == null) return null;

            change(existing);
            EnsureKnownPlatform(existing.PlatformId);
            CanonicalizeRegion(existing);

            await SaveAsync(context, id, existing.Title);
            return existing;
        }

        internal static async Task SaveAsync(RetroArrDbContext context, int id, string title)
        {
            try
            {
                await context.SaveChangesAsync();
            }
            catch (DbUpdateException ex)
            {
                var inner = ex.GetBaseException();
                _logger.Error($"[Game] Save failed for id={id}: {inner.GetType().Name}: {inner.Message}");

                var msg = inner.Message ?? string.Empty;
                // SQLite names the column ("Games.Path"), PostgreSQL (23505) and MariaDB (1062) the index ("IX_Games_Path")
                if (msg.Contains("UNIQUE constraint failed", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("duplicate key value", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("Duplicate entry", StringComparison.OrdinalIgnoreCase))
                {
                    string? field = null;
                    if (msg.Contains("Games.Title", StringComparison.OrdinalIgnoreCase) || msg.Contains("IX_Games_Title", StringComparison.OrdinalIgnoreCase)) field = "Title+PlatformId";
                    else if (msg.Contains("Games.Path", StringComparison.OrdinalIgnoreCase) || msg.Contains("IX_Games_Path", StringComparison.OrdinalIgnoreCase)) field = "Path";
                    else if (msg.Contains("Games.IgdbId", StringComparison.OrdinalIgnoreCase) || msg.Contains("IX_Games_IgdbId", StringComparison.OrdinalIgnoreCase)) field = "IgdbId+PlatformId";
                    throw new DuplicateGameException(
                        $"Another library entry already has this {field ?? "value"}.",
                        field, title, ex);
                }

                // A value too long for its column (MariaDB 1406 "Data too long", PostgreSQL 22001 "value too long") or for
                // its index (PostgreSQL 54000 "index row size ... exceeds", "index row requires ..."): the caller's value
                if (msg.Contains("too long", StringComparison.OrdinalIgnoreCase)
                    || msg.Contains("index row", StringComparison.OrdinalIgnoreCase))
                    throw new ArgumentException(inner.Message, ex);

                throw new InvalidOperationException(
                    $"Could not save game {id}: {inner.Message}", ex);
            }
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

        public async Task<List<int>> DeleteManyAsync(IReadOnlyCollection<int> gameIds)
        {
            if (gameIds.Count == 0) return new List<int>();
            var ids = gameIds.Distinct().ToList();
            using var context = await _contextFactory.CreateDbContextAsync();
            var rows = context.Games.Where(g => ids.Contains(g.Id));
            var found = await rows.Select(g => g.Id).ToListAsync();
            await rows.ExecuteDeleteAsync();
            return found;
        }

        // Only the store platform: older syncs also linked same-named games on other platforms by title
        public async Task<int> DeleteSteamGamesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games
                .Where(g => g.SteamId > 0 && g.PlatformId == PlatformDefinitions.SteamPlatformId)
                .ExecuteDeleteAsync();
        }

        public async Task<int> DeleteGogGamesAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games
                .Where(g => !string.IsNullOrEmpty(g.GogId) && g.PlatformId == PlatformDefinitions.GogPlatformId)
                .ExecuteDeleteAsync();
        }

        public async Task DeleteAllAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            // One statement instead of a tracked load and a DELETE per row; the database cascades to what hangs on the games
            await context.Games.ExecuteDeleteAsync();
        }

        internal static void EnsureKnownPlatform(int platformId)
        {
            if (platformId <= 0 || !PlatformDefinitions.PlatformDictionary.ContainsKey(platformId))
            {
                throw new InvalidOperationException(
                    $"Refusing to persist Game with unknown PlatformId {platformId}. " +
                    "Route unresolved entries through the review queue instead.");
            }
        }

        // SQLite treats NULL as distinct for unique indexes, so two untagged rows
        // with the same Title + PlatformId slip past (Title, PlatformId, Region).
        // Collapse NULL to empty string so the DB constraint and controller-side
        // duplicate checks agree.
        internal static void CanonicalizeRegion(Game game)
        {
            game.Region ??= string.Empty;
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

        public async Task<List<GameListDto>> GetListItemsAsync(IReadOnlyCollection<int> gameIds)
        {
            if (gameIds.Count == 0) return new List<GameListDto>();
            var ids = gameIds.Distinct().ToList();
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Where(g => ids.Contains(g.Id))
                .Select(g => new GameListDto { Id = g.Id, Title = g.Title, PlatformId = g.PlatformId })
                .ToListAsync();
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

        public async Task<(bool Changed, GameStatus Status, DateTime? MissingSince)?> ApplyContentStateAsync(int gameId, GameContent content, DateTime at, string? checkedPath)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var g = await context.Games.FindAsync(gameId);
            if (g == null) return null;
            // A loss at the path the check saw says nothing once the game was moved (an import, a path edit)
            var changed = (content == GameContent.Present || MediaScannerService.SamePath(g.Path, checkedPath)) && g.ApplyContent(content, at);
            if (changed) await context.SaveChangesAsync();
            return (changed, g.Status, g.MissingSince);
        }

        public async Task<List<Game>> GetMissingAsync(IReadOnlyCollection<int> gameIds)
        {
            if (gameIds.Count == 0) return new List<Game>();
            var ids = gameIds.Distinct().ToList();
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.Games.AsNoTracking()
                .Where(g => ids.Contains(g.Id) && g.MissingSince != null)
                .Select(g => new Game { Id = g.Id, Status = g.Status, Monitored = g.Monitored, MissingSince = g.MissingSince })
                .ToListAsync();
        }

        public async Task<int> DeleteMissingOlderThanAsync(DateTime threshold, IReadOnlyCollection<int> goneIds)
        {
            if (goneIds.Count == 0) return 0;
            var ids = goneIds.ToList();
            using var context = await _contextFactory.CreateDbContextAsync();
            var stale = (await context.Games
                .Where(g => ids.Contains(g.Id) && g.MissingSince != null && g.MissingSince < threshold && !g.Monitored)
                .ToListAsync())
                // The row may point somewhere else by now
                .Where(g => string.IsNullOrEmpty(g.Path) || !(System.IO.File.Exists(g.Path) || System.IO.Directory.Exists(g.Path)))
                .ToList();
            if (stale.Count == 0) return 0;
            context.Games.RemoveRange(stale);
            await context.SaveChangesAsync();
            return stale.Count;
        }

        public Task SyncGameFilesAsync(int gameId, List<GameFile> files) => StoreGameFilesAsync(gameId, files, removeOthers: true);

        public Task UpsertGameFilesAsync(int gameId, List<GameFile> files) => StoreGameFilesAsync(gameId, files, removeOthers: false);

        private async Task StoreGameFilesAsync(int gameId, List<GameFile> files, bool removeOthers)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.GameFiles
                .Where(f => f.GameId == gameId)
                .ToListAsync();

            // The first row of each path, looked up instead of searched for every file (a PC game has thousands)
            var byPath = new Dictionary<string, GameFile>();
            foreach (var f in existing) byPath.TryAdd(f.RelativePath, f);
            var newPaths = new HashSet<string>(files.Select(f => f.RelativePath));

            // Remove files no longer on disk
            var toRemove = existing.Where(f => !newPaths.Contains(f.RelativePath)).ToList();
            if (removeOthers && toRemove.Count > 0)
                context.GameFiles.RemoveRange(toRemove);

            // Add new files or update existing ones
            foreach (var file in files)
            {
                if (!byPath.TryGetValue(file.RelativePath, out var existingFile))
                {
                    file.GameId = gameId;
                    context.GameFiles.Add(file);
                }
                else
                {
                    if (existingFile.Size != file.Size || existingFile.FileType != file.FileType)
                    {
                        existingFile.Size = file.Size;
                        existingFile.FileType = file.FileType;
                        existingFile.DateAdded = file.DateAdded;
                    }
                    // What the new classification found replaces what was stored; what it didn't find stays
                    existingFile.Version = file.Version ?? existingFile.Version;
                    existingFile.ContentName = file.ContentName ?? existingFile.ContentName;
                    existingFile.TitleId = file.TitleId ?? existingFile.TitleId;
                    existingFile.Serial = file.Serial ?? existingFile.Serial;
                }
            }

            await context.SaveChangesAsync();
        }

        public async Task<List<GameFile>> GetSupplementaryFilesAsync(IReadOnlyCollection<int> gameIds)
        {
            var ids = gameIds.ToList();
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.GameFiles.AsNoTracking()
                .Where(f => ids.Contains(f.GameId) && f.FileType != "Main")
                .ToListAsync();
        }

        public async Task<List<int>> RemoveGameFilesAsync(IReadOnlyCollection<int> gameFileIds)
        {
            if (gameFileIds.Count == 0) return new List<int>();
            var ids = gameFileIds.ToList();
            using var context = await _contextFactory.CreateDbContextAsync();
            var rows = context.GameFiles.Where(f => ids.Contains(f.Id));
            var gameIds = await rows.Select(f => f.GameId).Distinct().ToListAsync();
            await rows.ExecuteDeleteAsync();
            return gameIds;
        }
    }
}
