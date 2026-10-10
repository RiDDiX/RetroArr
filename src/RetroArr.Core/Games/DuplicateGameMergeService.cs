using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
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
        private readonly ConfigurationService _config;

        public DuplicateGameMergeService(IDbContextFactory<RetroArrDbContext> contextFactory, ConfigurationService config)
        {
            _contextFactory = contextFactory;
            _config = config;
        }

        public Task<DuplicateMergeResult> MergeAsync(CancellationToken ct = default) => MergeAsync(null, ct);

        // moves: rows that belong on another platform but could not go there yet, as a row of their title or IGDB id
        // is there (the repair's). They are merged as rows of that platform, and a survivor among them goes there.
        public async Task<DuplicateMergeResult> MergeAsync(IReadOnlyDictionary<int, int>? moves, CancellationToken ct = default)
        {
            using var context = await _contextFactory.CreateDbContextAsync(ct);
            return await MergeAsync(context, ct, Roots(), moves);
        }

        public static async Task<DuplicateMergeResult> MergeAsync(RetroArrDbContext context, CancellationToken ct = default, IReadOnlyList<string>? roots = null, IReadOnlyDictionary<int, int>? moves = null)
        {
            var result = new DuplicateMergeResult();

            var games = await context.Games
                .Select(g => new { g.Id, g.Title, g.PlatformId, g.Path, g.IgdbId, g.Region, g.ExecutablePath, g.Status })
                .ToListAsync(ct);
            int PlatformOf(int id, int platformId) => moves != null && moves.TryGetValue(id, out var moved) ? moved : platformId;
            var library = games.Select(g => new Game { Id = g.Id, PlatformId = PlatformOf(g.Id, g.PlatformId), Path = g.Path, ExecutablePath = g.ExecutablePath, Status = g.Status }).ToList();

            var probes = games.Select(g => new DuplicateProbe
            {
                Id = g.Id,
                Title = g.Title,
                PlatformId = PlatformOf(g.Id, g.PlatformId),
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

                var survivor = await context.Games.FirstAsync(g => g.Id == winner.GameId, ct);
                var loserRows = await context.Games.Where(g => losers.Contains(g.Id)).OrderBy(g => g.Id).ToListAsync(ct);
                foreach (var loser in loserRows) TakeOver(survivor, loser, roots ?? Array.Empty<string>(), library);
                // unless the IGDB id is another release's there (another region)
                if (moves != null && moves.TryGetValue(survivor.Id, out var platform)
                    && !games.Any(g => g.PlatformId == platform && g.IgdbId != null && g.IgdbId == survivor.IgdbId && !alive.Any(m => m.GameId == g.Id)))
                    survivor.PlatformId = platform;
                // a status taken over with the path may be stale
                if (MediaScannerService.CheckContent(survivor, roots ?? Array.Empty<string>()) == GameContent.Present)
                    survivor.ApplyContent(GameContent.Present, DateTime.UtcNow);

                await ReattachReferencesAsync(context, losers, winner.GameId, survivor.Path, ct);

                context.Games.RemoveRange(loserRows);
                result.RowsMerged += loserRows.Count;
                foreach (var id in losers) mergedInto[id] = winner.GameId;

                // Stored before the next cluster reads: a row that took others over and loses later passes all of it on
                await context.SaveChangesAsync(ct);
            }

            return result;
        }

        // A merge through the repository (metadata review, merge-into): what hangs off the source moves to the target
        // and the source row goes, so the target can take its path (unique). TakeOver and change are then applied to
        // the stored target. library: the games in the library, for the breaker. Null when the target is gone.
        public async Task<Game?> MergeIntoAsync(IGameRepository repository, Game source, int targetId, IEnumerable<Game> library, Action<Game>? change = null)
        {
            using (var context = await _contextFactory.CreateDbContextAsync())
            {
                await ReattachReferencesAsync(context, new[] { source.Id }, targetId, null, default);
                await context.SaveChangesAsync();
            }
            await repository.DeleteAsync(source.Id);
            var roots = Roots();
            var saved = await repository.UpdateFieldsAsync(targetId, t =>
            {
                TakeOver(t, source, roots, library);
                change?.Invoke(t);
                if (MediaScannerService.CheckContent(t, roots) == GameContent.Present)
                    t.ApplyContent(GameContent.Present, DateTime.UtcNow);
            });
            if (saved == null) return null;

            // Its files by the path it has now
            using (var context = await _contextFactory.CreateDbContextAsync())
            {
                await MoveFilesAsync(context, Array.Empty<int>(), targetId, saved.Path, default);
                await context.SaveChangesAsync();
            }
            return saved;
        }

        // What a check of the game's path finds, by the scanner's rules
        public GameContent CheckContent(Game game) => MediaScannerService.CheckContent(game, Roots());

        // Its content gone or emptied and not held back by the breaker. library: the games in the library.
        public bool Lost(Game game, IEnumerable<Game> library) => Lost(game, Roots(), library);

        private List<string> Roots() => MediaScannerService.LibraryRoots(_config.LoadMediaSettings());

        // The survivor is monitored when either was. Like the scanner's TakePath, it keeps a path whose content may
        // still be there (present, or unknown while its share may be offline) and takes the other entry's path with
        // its state when it has none, or when its content is lost and the other's is there. True when it took it.
        private static bool TakeOver(Game survivor, Game other, IReadOnlyList<string> roots, IEnumerable<Game> library)
        {
            survivor.Monitored |= other.Monitored;
            if (string.IsNullOrEmpty(other.Path)) return false;
            if (!string.IsNullOrEmpty(survivor.Path)
                && !(MediaScannerService.CheckContent(other, roots) == GameContent.Present && Lost(survivor, roots, library)))
                return false;

            survivor.Path = other.Path;
            survivor.ExecutablePath = other.ExecutablePath;
            survivor.IsExternal = other.IsExternal;
            survivor.Status = other.Status;
            survivor.MissingSince = other.MissingSince;
            return true;
        }

        // Gone or emptied, and not held back by the breaker: a mass loss in the library is more likely an offline share
        private static bool Lost(Game game, IReadOnlyList<string> roots, IEnumerable<Game> library) =>
            MediaScannerService.CheckContent(game, roots) is GameContent.Gone or GameContent.Empty
            && MediaScannerService.CheckAll(library.Where(g => g.Id != game.Id && !string.IsNullOrEmpty(g.Path)).Append(game).ToList(), roots, new List<string>())[^1].Content
                is GameContent.Gone or GameContent.Empty;

        // Move FK rows from losers to the winner, respecting unique indexes. winnerPath: where the winner ends up, null
        // while that is not settled.
        private static async Task ReattachReferencesAsync(RetroArrDbContext context, IReadOnlyCollection<int> loserIds, int winnerId, string? winnerPath, CancellationToken ct)
        {
            await MoveFilesAsync(context, loserIds, winnerId, winnerPath, ct);

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

            // WishlistPriceWatches: unique (GameId, Provider). The winner's watch of a store stays.
            var winnerProviders = (await context.WishlistPriceWatches
                .Where(w => w.GameId == winnerId)
                .Select(w => w.Provider)
                .ToListAsync(ct)).ToHashSet();
            var loserWatches = await context.WishlistPriceWatches.Where(w => loserIds.Contains(w.GameId)).ToListAsync(ct);
            foreach (var w in loserWatches)
            {
                if (winnerProviders.Add(w.Provider)) w.GameId = winnerId;
                else context.WishlistPriceWatches.Remove(w);
            }

            // DownloadHistory: no FK, but a game's grabs are found by its id (delete plan, re-import of a lost game).
            var loserHistory = await context.DownloadHistory.Where(h => h.GameId.HasValue && loserIds.Contains(h.GameId.Value)).ToListAsync(ct);
            foreach (var h in loserHistory) h.GameId = winnerId;
        }

        // GameFiles: no unique constraint. The winner's rows and the losers' are resolved by their own game's stored
        // path and stored for the winner's path (GameFilePaths), with their full path while that is not settled. A
        // file both had a row of keeps one, the winner's.
        private static async Task MoveFilesAsync(RetroArrDbContext context, IReadOnlyCollection<int> loserIds, int winnerId, string? winnerPath, CancellationToken ct)
        {
            var paths = await context.Games.Where(g => g.Id == winnerId || loserIds.Contains(g.Id))
                .Select(g => new { g.Id, g.Path })
                .ToDictionaryAsync(g => g.Id, g => g.Path, ct);
            var folder = GameFilePaths.FilesFolder(winnerPath);
            var rows = await context.GameFiles.Where(f => f.GameId == winnerId || loserIds.Contains(f.GameId)).ToListAsync(ct);
            foreach (var f in rows)
            {
                if (GameFilePaths.Resolve(paths.GetValueOrDefault(f.GameId), f.RelativePath) is { } full)
                    f.RelativePath = GameFilePaths.StoredPathIn(folder, full);
            }
            foreach (var file in rows.GroupBy(f => f.RelativePath))
            {
                var keep = file.OrderByDescending(f => f.GameId == winnerId).First();
                keep.GameId = winnerId;
                context.GameFiles.RemoveRange(file.Where(f => f != keep));
            }
        }
    }
}
