using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;

namespace RetroArr.Api.V3.Settings
{
    [ApiController]
    [Route("api/v3/settings/database")]
    public class DatabaseController : ControllerBase
    {
        private readonly ConfigurationService _configService;
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;
        private readonly DatabaseMigrationService _migrationService;
        private readonly DuplicateGameMergeService _mergeService;
        private readonly ReviewItemService _reviewItems;

        public DatabaseController(
            ConfigurationService configService,
            IDbContextFactory<RetroArrDbContext> contextFactory,
            DatabaseMigrationService migrationService,
            DuplicateGameMergeService mergeService,
            ReviewItemService reviewItems)
        {
            _configService = configService;
            _contextFactory = contextFactory;
            _migrationService = migrationService;
            _mergeService = mergeService;
            _reviewItems = reviewItems;
        }

        [HttpGet]
        public ActionResult<DatabaseSettingsResponse> GetSettings()
        {
            var settings = _configService.LoadDatabaseSettings();
            return Ok(new DatabaseSettingsResponse
            {
                Type = settings.Type.ToString(),
                SqlitePath = settings.SqlitePath,
                Host = settings.Host,
                Port = settings.Port,
                Database = settings.Database,
                Username = settings.Username,
                HasPassword = !string.IsNullOrEmpty(settings.Password),
                UseSsl = settings.UseSsl,
                ConnectionTimeout = settings.ConnectionTimeout,
                IsConfigured = settings.IsConfigured,
                StartupError = DatabaseServiceExtensions.StartupError
            });
        }

        [HttpPut]
        public ActionResult SaveSettings([FromBody] DatabaseSettingsRequest request)
        {
            try
            {
                var settings = ParseSettings(request);
                // Password is write-only on the API. Empty == keep what's stored.
                if (string.IsNullOrEmpty(request.Password))
                {
                    var existing = _configService.LoadDatabaseSettings();
                    settings.Password = existing.Password;
                }
                _configService.SaveDatabaseSettings(settings);
                return Ok(new {
                    message = "Database settings saved. Restart required for changes to take effect.",
                    restartRequired = true
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("test")]
        public ActionResult TestConnection([FromBody] DatabaseSettingsRequest request)
        {
            try
            {
                var settings = ParseSettings(request);
                UseStoredPassword(settings, request);
                var configPath = _configService.GetConfigDirectory();
                var success = DatabaseServiceExtensions.TestConnection(settings, configPath, out var errorMessage);
                return Ok(new { success, message = success && errorMessage.Length == 0 ? "Connection successful!" : errorMessage });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("backup")]
        public ActionResult BackupSqlite()
        {
            try
            {
                var backupPath = _migrationService.BackupSqliteDatabase();
                return Ok(new { success = true, backupPath });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = ex.Message });
            }
        }

        [HttpPost("migrate")]
        public async Task<ActionResult> MigrateData([FromBody] DatabaseSettingsRequest targetRequest)
        {
            try
            {
                var target = ParseSettings(targetRequest);
                UseStoredPassword(target, targetRequest);
                var result = await _migrationService.MigrateAsync(target);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Migration complete."
                            + (result.SkippedRows > 0 ? $" {result.SkippedRows} row(s) of deleted games, tags or collections were left out." : "")
                            + (result.UnknownPlatformGames > 0 ? $" {result.UnknownPlatformGames} game(s) of a missing platform were moved to Unknown and flagged for review." : "")
                            + " Restart required.",
                        backupPath = result.BackupPath,
                        rowCounts = result.RowCounts.ToDictionary(
                            kv => kv.Key,
                            kv => new { source = kv.Value.Source, target = kv.Value.Target }),
                        restartRequired = true
                    });
                }
                else
                {
                    return BadRequest(new { success = false, error = result.Error, backupPath = result.BackupPath });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = $"Migration failed: {ex.Message}" });
            }
        }

        // Challenge tokens for destructive ops. Memory-only, expire after 2 min.
        private static readonly ConcurrentDictionary<string, (string Kind, DateTime Expires)> _challenges = new();

        [HttpPost("health")]
        public async Task<ActionResult<DatabaseHealthReport>> GetHealth()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var report = new DatabaseHealthReport
                {
                    TotalGames = await context.Games.CountAsync()
                };

                var knownPlatformIds = PlatformDefinitions.PlatformDictionary.Keys.ToHashSet();
                var games = await context.Games
                    .Select(g => new { g.Id, g.Title, g.PlatformId, g.Path, g.Region, g.NeedsMetadataReview, g.IgdbId, g.MissingSince })
                    .ToListAsync();

                foreach (var g in games)
                {
                    if (g.PlatformId <= 0 || !knownPlatformIds.Contains(g.PlatformId))
                        report.OrphanPlatformRefs++;
                    if (g.Region == null)
                        report.NullRegions++;
                    if (g.NeedsMetadataReview)
                        report.GamesNeedingReview++;
                    // What the last library scan found, as on the Problems page: a stat of every path takes minutes
                    // on spinning disks, and counts a whole unmounted share as missing
                    if (!string.IsNullOrEmpty(g.Path) && g.MissingSince != null)
                        report.GamesWithMissingPath++;

                    if (!string.IsNullOrEmpty(g.Path))
                    {
                        var suggested = PlatformDefinitions.ResolvePlatformFromPath(g.Path);
                        if (suggested != null && suggested.Id != g.PlatformId)
                        {
                            report.GamesWithMismatchedPath++;
                            if (report.Mismatches.Count < 200)
                            {
                                var current = knownPlatformIds.Contains(g.PlatformId)
                                    ? PlatformDefinitions.PlatformDictionary[g.PlatformId].Name
                                    : $"Unknown ({g.PlatformId})";
                                report.Mismatches.Add(new PlatformMismatch
                                {
                                    GameId = g.Id,
                                    Title = g.Title,
                                    CurrentPlatformId = g.PlatformId,
                                    CurrentPlatform = current,
                                    SuggestedPlatformId = suggested.Id,
                                    SuggestedPlatform = suggested.Name,
                                    Path = g.Path
                                });
                            }
                        }
                    }
                }

                // Dangling GameFiles: GameId doesn't exist in Games.
                var gameIds = games.Select(g => g.Id).ToHashSet();
                var fileGameIds = await context.GameFiles.Select(f => f.GameId).Distinct().ToListAsync();
                report.DanglingGameFiles = fileGameIds.Count(id => !gameIds.Contains(id));

                // Duplicate detection (cue/bin pairs, title collisions, repeat IGDB ids).
                var probes = games.Select(g => new DuplicateProbe
                {
                    Id = g.Id,
                    Title = g.Title,
                    PlatformId = g.PlatformId,
                    Path = g.Path,
                    IgdbId = g.IgdbId,
                    // as the merge reads them: regional releases are not duplicates
                    Region = g.Region
                });
                var clusters = DuplicateGameDetector.Detect(probes);
                report.DuplicateClusterCount = clusters.Count;
                report.DuplicateGames = DuplicateGameDetector.CollectAffectedGameIds(clusters).Count;

                foreach (var cluster in clusters.Take(200))
                {
                    string? platformName = null;
                    if (cluster.PlatformId.HasValue && knownPlatformIds.Contains(cluster.PlatformId.Value))
                        platformName = PlatformDefinitions.PlatformDictionary[cluster.PlatformId.Value].Name;
                    cluster.PlatformName = platformName;
                    report.Duplicates.Add(cluster);
                }

                return Ok(report);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("repair")]
        public async Task<ActionResult<DatabaseRepairResult>> Repair([FromBody] DatabaseRepairRequest request)
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var result = new DatabaseRepairResult();
                var knownPlatformIds = PlatformDefinitions.PlatformDictionary.Keys.ToHashSet();

                // 1. Null regions → '', but not for a title with another untagged row on the platform (the
                // duplicate merge below takes those)
                result.RegionsCanonicalised = await CanonicaliseRegionsAsync(context);

                // A platform change below must not put a second row of a title and region, or of an IGDB id, on a
                // platform (unique indexes; case ignored, as some databases do). Such a row stays where it is, and the
                // duplicate merge takes it as a row of the platform it belongs to.
                var placed = await context.Games.Select(g => new { g.Id, g.Title, g.PlatformId, g.Region, g.IgdbId, g.Path }).ToListAsync();
                var titles = placed.Select(g => TitleKey(g.Title, g.PlatformId, g.Region)).ToHashSet();
                var igdbIds = placed.Where(g => g.IgdbId != null).Select(g => (g.IgdbId!.Value, g.PlatformId)).ToHashSet();
                var waiting = new Dictionary<int, int>();
                bool Move(Game g, int platformId)
                {
                    var title = TitleKey(g.Title, platformId, g.Region);
                    if (titles.Contains(title) || (g.IgdbId != null && igdbIds.Contains((g.IgdbId.Value, platformId))))
                    {
                        waiting[g.Id] = platformId;
                        return false;
                    }
                    g.PlatformId = platformId;
                    titles.Add(title);
                    if (g.IgdbId != null) igdbIds.Add((g.IgdbId.Value, platformId));
                    waiting.Remove(g.Id);
                    return true;
                }

                // 2. Orphan PlatformIds: park on Unknown so folder stays absolute and the row hits review.
                var orphans = await context.Games
                    .Where(g => g.PlatformId <= 0 || !knownPlatformIds.Contains(g.PlatformId))
                    .ToListAsync();
                foreach (var g in orphans)
                {
                    if (Move(g, PlatformDefinitions.UnknownPlatformId)) result.OrphansFixed++;
                    g.NeedsMetadataReview = true;
                    if (string.IsNullOrEmpty(g.MetadataReviewReason))
                        g.MetadataReviewReason = "Original platform could not be resolved. Please reassign.";
                }

                // 3. and 4. work on the columns read above: tracking every game of a big library took most of the
                // repair. Only the rows that change are loaded as entities.
                var pathRows = placed.Where(g => !string.IsNullOrEmpty(g.Path)).ToList();

                // 3. Path-based platform heal (opt-in)
                if (request.HealPlatformFromPath)
                {
                    // an orphan as step 2 left it
                    var orphanById = orphans.ToDictionary(g => g.Id);
                    var healed = new Dictionary<int, int>();
                    foreach (var row in pathRows)
                    {
                        var orphan = orphanById.GetValueOrDefault(row.Id);
                        var suggested = PlatformDefinitions.ResolvePlatformFromPath(row.Path);
                        if (suggested != null && suggested.Id != (orphan?.PlatformId ?? row.PlatformId) && knownPlatformIds.Contains(suggested.Id)
                            && Move(orphan ?? new Game { Id = row.Id, Title = row.Title, Region = row.Region, IgdbId = row.IgdbId }, suggested.Id))
                        {
                            result.PlatformsHealed++;
                            healed[row.Id] = suggested.Id;
                        }
                    }
                    var healedIds = healed.Keys.ToList();
                    foreach (var g in await context.Games.Where(g => healedIds.Contains(g.Id)).ToListAsync())
                        g.PlatformId = healed[g.Id];
                }

                // 4. Container orphans: rows pointing into .psvita / .ps4 etc. Only below a library root: a folder above
                // it that is named like a container is not one, and a row outside every root stays.
                if (request.PruneContainerOrphans)
                {
                    var roots = LibraryPaths.Roots(_configService.LoadMediaSettings());
                    var orphanIds = pathRows
                        .Where(g => TitleCleanerService.IsContainerOrphanPath(LibraryPaths.BelowRoot(g.Path, roots)))
                        .Select(g => g.Id)
                        .ToList();
                    if (orphanIds.Count > 0)
                    {
                        var orphanRows = await context.Games.Where(g => orphanIds.Contains(g.Id)).ToListAsync();
                        context.Games.RemoveRange(orphanRows);
                        result.ContainerOrphansRemoved = orphanRows.Count;
                    }
                }

                // 5. Dangling GameFiles: remove rows whose GameId no longer exists
                var dangling = await context.GameFiles
                    .Where(f => !context.Games.Any(g => g.Id == f.GameId))
                    .ToListAsync();
                context.GameFiles.RemoveRange(dangling);
                result.DanglingGameFilesRemoved = dangling.Count;

                await context.SaveChangesAsync();

                // 5. Optional: merge duplicate game rows. Delegated to the
                // shared service so the heal-after-scan path (MediaScanner)
                // and the manual repair button stay in sync.
                if (request.MergeDuplicates)
                {
                    var mergeResult = await _mergeService.MergeAsync(waiting);
                    result.DuplicatesMerged = mergeResult.RowsMerged;

                    // what is left of untagged duplicates can take '' now
                    using var merged = await _contextFactory.CreateDbContextAsync();
                    result.RegionsCanonicalised += await CanonicaliseRegionsAsync(merged);
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        private static (string, int, string) TitleKey(string title, int platformId, string? region) =>
            (title.ToUpperInvariant(), platformId, (region ?? string.Empty).ToUpperInvariant());

        // '' where two untagged rows of a title on a platform would collide (unique index; case ignored, as some
        // databases do). Written at once, in one UPDATE: a scan leaves a third of a library untagged.
        private static async Task<int> CanonicaliseRegionsAsync(RetroArrDbContext context)
        {
            var untagged = await context.Games.Where(g => g.Region == null || g.Region == "")
                .Select(g => new { g.Id, g.Title, g.PlatformId, g.Region })
                .ToListAsync();
            var twice = untagged.GroupBy(g => (g.Title.ToUpperInvariant(), g.PlatformId))
                .Where(x => x.Count() > 1)
                .Select(x => x.Key)
                .ToHashSet();
            var ids = untagged
                .Where(g => g.Region == null && !twice.Contains((g.Title.ToUpperInvariant(), g.PlatformId)))
                .Select(g => g.Id)
                .ToList();
            if (ids.Count == 0) return 0;
            return await context.Games.Where(g => ids.Contains(g.Id))
                .ExecuteUpdateAsync(s => s.SetProperty(g => g.Region, string.Empty));
        }

        [HttpPost("reset/challenge")]
        public async Task<ActionResult<DatabaseResetChallenge>> ResetChallenge([FromQuery] string kind = "library")
        {
            if (kind != "library" && kind != "download-history")
                return BadRequest(new { error = $"Unknown reset kind '{kind}'." });

            using var context = await _contextFactory.CreateDbContextAsync();
            var challenge = new DatabaseResetChallenge
            {
                Token = Guid.NewGuid().ToString("N"),
                Kind = kind,
                ExpiresInSeconds = 120,
                Confirmation = kind == "library" ? "RESET MY LIBRARY" : "RESET DOWNLOAD HISTORY"
            };

            if (kind == "library")
            {
                challenge.GamesToDelete = await context.Games.CountAsync();
                challenge.GameFilesToDelete = await context.GameFiles.CountAsync();
                challenge.CollectionsToDelete = await context.Collections.CountAsync();
                challenge.ReviewsToDelete = await context.GameReviews.CountAsync();
            }
            else
            {
                challenge.DownloadHistoryToDelete = await context.DownloadHistory.CountAsync();
                challenge.DownloadBlacklistToDelete = await context.DownloadBlacklist.CountAsync();
            }

            PurgeExpiredChallenges();
            _challenges[challenge.Token] = (kind, DateTime.UtcNow.AddSeconds(challenge.ExpiresInSeconds));
            return Ok(challenge);
        }

        [HttpPost("reset")]
        public async Task<ActionResult> ResetLibrary([FromBody] DatabaseResetRequest request)
        {
            if (!TryConsumeChallenge(request.Token, "library"))
                return BadRequest(new { error = "Invalid or expired token. Request a fresh challenge first." });
            if (request.Confirmation != "RESET MY LIBRARY")
                return BadRequest(new { error = "Confirmation text does not match." });

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                // One DELETE per table: RemoveRange loaded and deleted every row one by one
                await WipeAsync(context,
                    () => context.GameFiles.ExecuteDeleteAsync(),
                    () => context.GameTags.ExecuteDeleteAsync(),
                    () => context.CollectionGames.ExecuteDeleteAsync(),
                    () => context.GameReviews.ExecuteDeleteAsync(),
                    () => context.Collections.ExecuteDeleteAsync(),
                    () => context.Games.ExecuteDeleteAsync());

                // review_items.json lives outside the DB, and its service holds the items in memory
                _reviewItems.Clear();

                return Ok(new { message = "Library wiped. Run a Media Scan to rebuild." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        [HttpPost("reset-download-history")]
        public async Task<ActionResult> ResetDownloadHistory([FromBody] DatabaseResetRequest request)
        {
            if (!TryConsumeChallenge(request.Token, "download-history"))
                return BadRequest(new { error = "Invalid or expired token. Request a fresh challenge first." });
            if (request.Confirmation != "RESET DOWNLOAD HISTORY")
                return BadRequest(new { error = "Confirmation text does not match." });

            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                await WipeAsync(context,
                    () => context.DownloadHistory.ExecuteDeleteAsync(),
                    () => context.DownloadBlacklist.ExecuteDeleteAsync());
                return Ok(new { message = "Download history and blacklist cleared." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

        // All or nothing, as the one SaveChanges was. PostgreSQL and MariaDB retry on failure, which takes a
        // transaction only inside the strategy.
        private static Task WipeAsync(RetroArrDbContext context, params Func<Task<int>>[] deletes) =>
            context.Database.CreateExecutionStrategy().ExecuteAsync(async () =>
            {
                await using var transaction = await context.Database.BeginTransactionAsync();
                foreach (var delete in deletes) await delete();
                await transaction.CommitAsync();
            });

        private static bool TryConsumeChallenge(string? token, string expectedKind)
        {
            if (string.IsNullOrEmpty(token)) return false;
            if (!_challenges.TryRemove(token, out var entry)) return false;
            if (entry.Expires < DateTime.UtcNow) return false;
            return entry.Kind == expectedKind;
        }

        private static void PurgeExpiredChallenges()
        {
            var now = DateTime.UtcNow;
            foreach (var kv in _challenges.ToArray())
            {
                if (kv.Value.Expires < now) _challenges.TryRemove(kv.Key, out _);
            }
        }

        [HttpGet("stats")]
        public async Task<ActionResult> GetStats()
        {
            try
            {
                using var context = await _contextFactory.CreateDbContextAsync();
                var gamesCount = await context.Games.CountAsync();
                var collectionsCount = await context.Collections.CountAsync();
                var tagsCount = await context.Tags.CountAsync();
                var reviewsCount = await context.GameReviews.CountAsync();
                var gameFilesCount = await context.GameFiles.CountAsync();
                var downloadHistoryCount = await context.DownloadHistory.CountAsync();

                return Ok(new
                {
                    // the provider in use; database.json may already name the next one
                    databaseType = DatabaseServiceExtensions.RunningType(context).ToString(),
                    gamesCount,
                    gameFilesCount,
                    collectionsCount,
                    tagsCount,
                    reviewsCount,
                    downloadHistoryCount
                });
            }
            catch (Exception ex)
            {
                // the retry strategy wraps the real cause
                return BadRequest(new { error = ex.GetBaseException().Message });
            }
        }

        // Same write-only contract as PUT: empty == the stored one, while the form
        // still points at the same server, database and user.
        private void UseStoredPassword(DatabaseSettings settings, DatabaseSettingsRequest request)
        {
            if (!string.IsNullOrEmpty(request.Password)) return;
            var existing = _configService.LoadDatabaseSettings();
            if (existing.Host == settings.Host
                && existing.Port == settings.Port
                && existing.Database == settings.Database
                && existing.Username == settings.Username)
            {
                settings.Password = existing.Password;
            }
        }

        private static DatabaseSettings ParseSettings(DatabaseSettingsRequest request)
        {
            var type = Enum.Parse<DatabaseType>(request.Type, ignoreCase: true);
            return new DatabaseSettings
            {
                Type = type,
                SqlitePath = request.SqlitePath ?? "retroarr.db",
                Host = request.Host ?? "localhost",
                // a cleared port field arrives as 0
                Port = request.Port is > 0 and <= 65535 ? request.Port.Value : (type == DatabaseType.MariaDB ? 3306 : 5432),
                Database = request.Database ?? "retroarr",
                Username = request.Username ?? string.Empty,
                Password = request.Password ?? string.Empty,
                UseSsl = request.UseSsl ?? false,
                ConnectionTimeout = request.ConnectionTimeout ?? 30
            };
        }
    }

    public class DatabaseSettingsRequest
    {
        public string Type { get; set; } = "SQLite";
        public string? SqlitePath { get; set; }
        public string? Host { get; set; }
        public int? Port { get; set; }
        public string? Database { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public bool? UseSsl { get; set; }
        public int? ConnectionTimeout { get; set; }
    }

    public class DatabaseSettingsResponse
    {
        public string Type { get; set; } = "SQLite";
        public string SqlitePath { get; set; } = "retroarr.db";
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432;
        public string Database { get; set; } = "retroarr";
        public string Username { get; set; } = string.Empty;
        // Read-only flag. The password itself never leaves the server.
        public bool HasPassword { get; set; }
        public bool UseSsl { get; set; }
        public int ConnectionTimeout { get; set; } = 30;
        public bool IsConfigured { get; set; }
        // why the database could not be set up at startup, if it could not
        public string? StartupError { get; set; }
    }

    public class DatabaseHealthReport
    {
        public int TotalGames { get; set; }
        public int OrphanPlatformRefs { get; set; }
        public int NullRegions { get; set; }
        public int DanglingGameFiles { get; set; }
        public int GamesNeedingReview { get; set; }
        public int GamesWithMissingPath { get; set; }
        public int GamesWithMismatchedPath { get; set; }
        public int DuplicateClusterCount { get; set; }
        public int DuplicateGames { get; set; }
        public List<PlatformMismatch> Mismatches { get; set; } = new();
        public List<DuplicateCluster> Duplicates { get; set; } = new();
    }

    public class PlatformMismatch
    {
        public int GameId { get; set; }
        public string Title { get; set; } = string.Empty;
        public int CurrentPlatformId { get; set; }
        public string CurrentPlatform { get; set; } = string.Empty;
        public int SuggestedPlatformId { get; set; }
        public string SuggestedPlatform { get; set; } = string.Empty;
        public string Path { get; set; } = string.Empty;
    }

    public class DatabaseRepairRequest
    {
        public bool HealPlatformFromPath { get; set; } = true;
        public bool MergeDuplicates { get; set; }
        // drop junk rows from inside .psvita / .ps4 folders, files stay
        public bool PruneContainerOrphans { get; set; } = true;
    }

    public class DatabaseRepairResult
    {
        public int RegionsCanonicalised { get; set; }
        public int OrphansFixed { get; set; }
        public int PlatformsHealed { get; set; }
        public int DanglingGameFilesRemoved { get; set; }
        public int DuplicatesMerged { get; set; }
        public int ContainerOrphansRemoved { get; set; }
    }

    public class DatabaseResetChallenge
    {
        public string Token { get; set; } = string.Empty;
        public string Kind { get; set; } = string.Empty;
        public int ExpiresInSeconds { get; set; }
        public string Confirmation { get; set; } = string.Empty;
        public int GamesToDelete { get; set; }
        public int GameFilesToDelete { get; set; }
        public int CollectionsToDelete { get; set; }
        public int ReviewsToDelete { get; set; }
        public int DownloadHistoryToDelete { get; set; }
        public int DownloadBlacklistToDelete { get; set; }
    }

    public class DatabaseResetRequest
    {
        public string Token { get; set; } = string.Empty;
        public string Confirmation { get; set; } = string.Empty;
    }
}
