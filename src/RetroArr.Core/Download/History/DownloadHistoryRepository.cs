using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;
using RetroArr.Core.Download.TrackedDownloads;

namespace RetroArr.Core.Download.History
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DownloadHistoryRepository
    {
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;

        public DownloadHistoryRepository(IDbContextFactory<RetroArrDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        // NZBGet ids are small numbers every client counts on its own, so a numeric id only matches its
        // own client. Hashes and SABnzbd ids are unique by themselves and still match after a client
        // was removed and added again.
        private static IQueryable<DownloadHistoryEntry> ForDownload(IQueryable<DownloadHistoryEntry> history, string downloadId, int clientId)
        {
            var perClient = TrackedDownloadService.IsNumericId(downloadId);
            return history.Where(h => h.DownloadId == downloadId && (!perClient || h.ClientId == clientId));
        }

        public async Task<DownloadHistoryEntry?> FindByDownloadIdAsync(string downloadId, int clientId, string title)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var entry = await ForDownload(context.DownloadHistory, downloadId, clientId).FirstOrDefaultAsync();
            if (entry != null || !TrackedDownloadService.IsHashId(downloadId))
                return entry;

            // Transmission ids were its per-session torrent numbers before they became the hash. An import
            // recorded under the old number is taken over once, so the torrent isn't imported again.
            var legacy = (await context.DownloadHistory
                    .Where(h => h.ClientId == clientId && h.Title == title && h.State == DownloadHistoryState.Imported)
                    .ToListAsync())
                .FirstOrDefault(h => TrackedDownloadService.IsNumericId(h.DownloadId));
            if (legacy == null) return null;

            legacy.DownloadId = downloadId;
            await context.SaveChangesAsync();
            return legacy;
        }

        // The unique index on DownloadId refuses a second client's row for the same numeric id; that
        // flush fails and the first client's row stays as it was.
        public async Task UpsertAsync(DownloadHistoryEntry entry)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var existing = await ForDownload(context.DownloadHistory, entry.DownloadId, entry.ClientId).FirstOrDefaultAsync();

            if (existing != null)
            {
                existing.State = entry.State;
                existing.Reason = entry.Reason;
                existing.DestinationPath = entry.DestinationPath;
                existing.ImportedAt = entry.ImportedAt;
                existing.GameId = entry.GameId;
                existing.CleanTitle = entry.CleanTitle;
                existing.Platform = entry.Platform;
            }
            else
            {
                context.DownloadHistory.Add(entry);
            }

            await context.SaveChangesAsync();
        }

        public async Task<(List<DownloadHistoryEntry> Items, int TotalCount)> SearchAsync(
            string? query = null,
            string? platform = null,
            string? state = null,
            DateTime? fromDate = null,
            DateTime? toDate = null,
            string sortBy = "importedAt",
            bool sortDescending = true,
            int page = 1,
            int pageSize = 25)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            IQueryable<DownloadHistoryEntry> q = context.DownloadHistory;

            if (!string.IsNullOrWhiteSpace(query))
            {
                var lower = query.ToLower();
                q = q.Where(h =>
                    h.Title.ToLower().Contains(lower) ||
                    (h.CleanTitle != null && h.CleanTitle.ToLower().Contains(lower)));
            }

            if (!string.IsNullOrWhiteSpace(platform))
            {
                q = q.Where(h => h.Platform == platform);
            }

            if (!string.IsNullOrWhiteSpace(state))
            {
                if (Enum.TryParse<DownloadHistoryState>(state, true, out var parsed))
                    q = q.Where(h => h.State == parsed);
            }

            if (fromDate.HasValue)
                q = q.Where(h => h.ImportedAt >= fromDate.Value);

            if (toDate.HasValue)
                q = q.Where(h => h.ImportedAt <= toDate.Value);

            var totalCount = await q.CountAsync();

            q = sortBy.ToLower() switch
            {
                "title" => sortDescending ? q.OrderByDescending(h => h.Title) : q.OrderBy(h => h.Title),
                "platform" => sortDescending ? q.OrderByDescending(h => h.Platform) : q.OrderBy(h => h.Platform),
                "size" => sortDescending ? q.OrderByDescending(h => h.Size) : q.OrderBy(h => h.Size),
                "state" => sortDescending ? q.OrderByDescending(h => h.State) : q.OrderBy(h => h.State),
                _ => sortDescending ? q.OrderByDescending(h => h.ImportedAt) : q.OrderBy(h => h.ImportedAt),
            };

            var items = await q
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();

            return (items, totalCount);
        }

        public async Task<List<DownloadHistoryEntry>> GetFailedAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DownloadHistory
                .Where(h => h.State == DownloadHistoryState.ImportFailed)
                .OrderByDescending(h => h.ImportedAt)
                .ToListAsync();
        }

        public async Task<DownloadHistoryEntry?> GetByIdAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            return await context.DownloadHistory.FindAsync(id);
        }

        public async Task UpdateStateAsync(int id, DownloadHistoryState newState, string? reason = null)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var entry = await context.DownloadHistory.FindAsync(id);
            if (entry != null)
            {
                entry.State = newState;
                if (reason != null) entry.Reason = reason;
                entry.ImportedAt = DateTime.UtcNow;
                await context.SaveChangesAsync();
            }
        }

        public async Task<bool> DeleteAsync(int id)
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var entry = await context.DownloadHistory.FindAsync(id);
            if (entry == null) return false;
            context.DownloadHistory.Remove(entry);
            await context.SaveChangesAsync();
            return true;
        }

        public async Task<Dictionary<string, int>> GetCountsAsync()
        {
            using var context = await _contextFactory.CreateDbContextAsync();
            var counts = await context.DownloadHistory
                .GroupBy(h => h.State)
                .Select(g => new { State = g.Key, Count = g.Count() })
                .ToListAsync();

            var result = new Dictionary<string, int>();
            foreach (var c in counts)
                result[c.State.ToString()] = c.Count;
            return result;
        }
    }
}
