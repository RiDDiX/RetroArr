using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Data;

namespace RetroArr.Core.Games
{
    public class DuplicateMergeResult
    {
        public int ClustersFound { get; set; }
        public int RowsMerged { get; set; }
    }

    // Collapses duplicate Game rows. Shared by the repair endpoint and the
    // post-scan heal pass.
    public class DuplicateGameMergeService
    {
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;

        public DuplicateGameMergeService(IDbContextFactory<RetroArrDbContext> contextFactory)
        {
            _contextFactory = contextFactory;
        }

        public async Task<DuplicateMergeResult> MergeAsync(CancellationToken ct = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            return await MergeAsync(context, ct);
        }

        public static async Task<DuplicateMergeResult> MergeAsync(RetroArrDbContext context, CancellationToken ct = default)
        {
            var result = new DuplicateMergeResult();

            var games = await context.Games
                .Select(g => new { g.Id, g.Title, g.PlatformId, g.Path, g.IgdbId, g.Region })
                .ToListAsync(ct);

            var probes = games.Select(g => new DuplicateProbe
            {
                Id = g.Id,
                Title = g.Title,
                PlatformId = g.PlatformId,
                Path = g.Path,
                IgdbId = g.IgdbId,
                Region = g.Region
            });

            var clusters = DuplicateGameDetector.Detect(probes);
            result.ClustersFound = clusters.Count;
            if (clusters.Count == 0) return result;

            var members = games.ToDictionary(g => g.Id, g => new DuplicateMember { GameId = g.Id, Title = g.Title, Path = g.Path, IgdbId = g.IgdbId });
            // A row merged away stands for the row that took it over
            var mergedInto = new Dictionary<int, int>();
            int Survivor(int id)
            {
                while (mergedInto.TryGetValue(id, out var next)) id = next;
                return id;
            }

            foreach (var cluster in clusters)
            {
                ct.ThrowIfCancellationRequested();

                var alive = cluster.Games.Select(m => Survivor(m.GameId)).Distinct().Select(id => members[id]).ToList();
                if (alive.Count < 2) continue;

                var winner = DuplicateGameDetector.PickWinner(new DuplicateCluster { Games = alive });
                var losers = alive.Where(m => m.GameId != winner.GameId).Select(m => m.GameId).ToList();

                await ReattachReferencesAsync(context, losers, winner.GameId, ct);

                var loserRows = await context.Games.Where(g => losers.Contains(g.Id)).ToListAsync(ct);
                context.Games.RemoveRange(loserRows);
                result.RowsMerged += loserRows.Count;
                foreach (var id in losers) mergedInto[id] = winner.GameId;

                // Stored before the next cluster reads: a row that took others over and loses later passes all of it on
                await context.SaveChangesAsync(ct);
            }

            return result;
        }

        // Move FK rows from losers to the winner, respecting unique indexes.
        private static async Task ReattachReferencesAsync(RetroArrDbContext context, IReadOnlyCollection<int> loserIds, int winnerId, CancellationToken ct)
        {
            // GameFiles: no unique constraint.
            var loserFiles = await context.GameFiles.Where(f => loserIds.Contains(f.GameId)).ToListAsync(ct);
            foreach (var f in loserFiles) f.GameId = winnerId;

            // CollectionGames: one row per (GameId, CollectionId).
            var winnerCollectionIds = await context.CollectionGames
                .Where(cg => cg.GameId == winnerId)
                .Select(cg => cg.CollectionId)
                .ToListAsync(ct);
            var winnerCollectionSet = winnerCollectionIds.ToHashSet();
            var loserCollectionRows = await context.CollectionGames
                .Where(cg => loserIds.Contains(cg.GameId))
                .ToListAsync(ct);
            foreach (var cg in loserCollectionRows)
            {
                if (winnerCollectionSet.Contains(cg.CollectionId))
                    context.CollectionGames.Remove(cg);
                else
                {
                    cg.GameId = winnerId;
                    winnerCollectionSet.Add(cg.CollectionId);
                }
            }

            // GameTags: unique (GameId, TagId). Drop conflicting losers, reattach the rest.
            var winnerTagIds = await context.GameTags
                .Where(gt => gt.GameId == winnerId)
                .Select(gt => gt.TagId)
                .ToListAsync(ct);
            var winnerTagSet = winnerTagIds.ToHashSet();
            var loserTagRows = await context.GameTags
                .Where(gt => loserIds.Contains(gt.GameId))
                .ToListAsync(ct);
            foreach (var gt in loserTagRows)
            {
                if (winnerTagSet.Contains(gt.TagId))
                    context.GameTags.Remove(gt);
                else
                {
                    gt.GameId = winnerId;
                    winnerTagSet.Add(gt.TagId);
                }
            }

            // GameReviews: unique on GameId. Keep winner's; otherwise promote the first loser review.
            var winnerHasReview = await context.GameReviews.AnyAsync(r => r.GameId == winnerId, ct);
            var loserReviews = await context.GameReviews.Where(r => loserIds.Contains(r.GameId)).ToListAsync(ct);
            if (winnerHasReview)
            {
                context.GameReviews.RemoveRange(loserReviews);
            }
            else
            {
                bool promoted = false;
                foreach (var r in loserReviews)
                {
                    if (!promoted) { r.GameId = winnerId; promoted = true; }
                    else context.GameReviews.Remove(r);
                }
            }
        }
    }
}
