using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;

namespace RetroArr.Core.Download.History
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DownloadBlacklistRepository
    {
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;

        public DownloadBlacklistRepository(IDbContextFactory<RetroArrDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task AddAsync(DownloadBlacklistEntry entry)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            context.DownloadBlacklist.Add(entry);
            await context.SaveChangesAsync();
        }

        public async Task<bool> IsBlacklistedAsync(string? downloadId, string? title)
        {
            using var context = await _contextFactory.CreateDbContextAsync();

            if (!string.IsNullOrEmpty(downloadId))
            {
                var byId = await context.DownloadBlacklist
                    .AnyAsync(b => b.DownloadId == downloadId);
                if (byId) return true;
            }

            if (!string.IsNullOrEmpty(title))
            {
                var lowerTitle = title.ToLower();
                var byTitle = await context.DownloadBlacklist
                    .AnyAsync(b => b.Title.ToLower() == lowerTitle);
                if (byTitle) return true;
            }

            return false;
        }

        public async Task<List<DownloadBlacklistEntry>> GetAllAsync(string? query = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            IQueryable<DownloadBlacklistEntry> q = context.DownloadBlacklist
                .OrderByDescending(b => b.BlacklistedAt);

            if (!string.IsNullOrWhiteSpace(query))
            {
                var lower = query.ToLower();
                q = q.Where(b => b.Title.ToLower().Contains(lower));
            }

            return await q.ToListAsync();
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var entry = await context.DownloadBlacklist.FindAsync(id);
            if (entry == null) return false;
            context.DownloadBlacklist.Remove(entry);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<int> GetCountAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DownloadBlacklist.CountAsync();
        }
    }
}
