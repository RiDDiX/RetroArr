using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace RetroArr.Core.Games
{
    public class SqliteGameRepository : IGameRepository
    {
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
            
            // Handle lists and owned types separately
            existing.Genres = game.Genres;
            existing.Images = game.Images;
            
            await context.SaveChangesAsync();
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
