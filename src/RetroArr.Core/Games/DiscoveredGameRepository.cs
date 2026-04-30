using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;

namespace RetroArr.Core.Games
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DiscoveredGameRepository
    {
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;

        public DiscoveredGameRepository(IDbContextFactory<RetroArrDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<List<DiscoveredGame>> GetAllAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DiscoveredGames
                .OrderBy(d => d.PlatformKey)
                .ThenBy(d => d.Title)
                .ToListAsync();
        }

        public async Task<DiscoveredGame?> GetByIdAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DiscoveredGames.FindAsync(id);
        }

        public async Task<int> CountAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DiscoveredGames.CountAsync();
        }

        public async Task<bool> ExistsByPathAsync(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DiscoveredGames.AnyAsync(d => d.Path == path);
        }

        public async Task AddAsync(DiscoveredGame entry)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            context.DiscoveredGames.Add(entry);
            await context.SaveChangesAsync();
        }

        public async Task UpsertAsync(DiscoveredGame entry)
        {
            if (string.IsNullOrEmpty(entry.Path)) return;
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await context.DiscoveredGames.FirstOrDefaultAsync(d => d.Path == entry.Path);
            if (existing != null)
            {
                existing.Title = entry.Title;
                existing.PlatformKey = entry.PlatformKey;
                existing.PlatformId = entry.PlatformId;
                existing.Serial = entry.Serial;
                existing.ExecutablePath = entry.ExecutablePath;
                existing.IsExternal = entry.IsExternal;
                existing.IsInstaller = entry.IsInstaller;
                existing.Region = entry.Region;
                existing.Languages = entry.Languages;
                existing.Revision = entry.Revision;
            }
            else
            {
                context.DiscoveredGames.Add(entry);
            }
            await context.SaveChangesAsync();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var entry = await context.DiscoveredGames.FindAsync(id);
            if (entry == null) return false;
            context.DiscoveredGames.Remove(entry);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<int> DeleteManyAsync(IEnumerable<int> ids)
        {
            var idList = ids?.ToList();
            if (idList == null || idList.Count == 0) return 0;
            using var context = await _contextFactory.CreateDbContextAsync();
            var entries = await context.DiscoveredGames.Where(d => idList.Contains(d.Id)).ToListAsync();
            if (entries.Count == 0) return 0;
            context.DiscoveredGames.RemoveRange(entries);
            await context.SaveChangesAsync();
            return entries.Count;
        }

        public async Task<int> ClearAllAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var all = await context.DiscoveredGames.ToListAsync();
            if (all.Count == 0) return 0;
            context.DiscoveredGames.RemoveRange(all);
            await context.SaveChangesAsync();
            return all.Count;
        }
    }
}
