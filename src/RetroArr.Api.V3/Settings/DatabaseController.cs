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

        public DatabaseController(
            ConfigurationService configService,
            IDbContextFactory<RetroArrDbContext> contextFactory,
            DatabaseMigrationService migrationService,
            DuplicateGameMergeService mergeService)
        {
            _configService = configService;
            _contextFactory = contextFactory;
            _migrationService = migrationService;
            _mergeService = mergeService;
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
                IsConfigured = settings.IsConfigured
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
                // Same write-only contract as PUT: empty == use the stored one.
                if (string.IsNullOrEmpty(request.Password))
                {
                    var existing = _configService.LoadDatabaseSettings();
                    if (existing.Host == settings.Host
                        && existing.Port == settings.Port
                        && existing.Database == settings.Database
                        && existing.Username == settings.Username)
                    {
                        settings.Password = existing.Password;
                    }
                }
                var configPath = _configService.GetConfigDirectory();
                var success = DatabaseServiceExtensions.TestConnection(settings, configPath, out var errorMessage);
                return Ok(new { success, message = success ? "Connection successful!" : errorMessage });
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
                var result = await _migrationService.MigrateAsync(target);

                if (result.Success)
                {
                    return Ok(new
                    {
                        success = true,
                        message = "Migration complete. Restart required.",
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
                    .Select(g => new { g.Id, g.Title, g.PlatformId, g.Path, g.Region, g.NeedsMetadataReview, g.IgdbId })
                    .ToListAsync();

                foreach (var g in games)
                {
                    if (g.PlatformId <= 0 || !knownPlatformIds.Contains(g.PlatformId))
                        report.OrphanPlatformRefs++;
                    if (g.Region == null)
                        report.NullRegions++;
                    if (g.NeedsMetadataReview)
                        report.GamesNeedingReview++;
                    if (!string.IsNullOrEmpty(g.Path) && !Directory.Exists(g.Path) && !System.IO.File.Exists(g.Path))
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
                    IgdbId = g.IgdbId
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

                // 1. Null regions → ''
                var nullRegionRows = await context.Games.Where(g => g.Region == null).ToListAsync();
                foreach (var g in nullRegionRows) g.Region = string.Empty;
                result.RegionsCanonicalised = nullRegionRows.Count;

                // 2. Orphan PlatformIds → pin to PC + flag review
                var orphans = await context.Games
                    .Where(g => g.PlatformId <= 0 || !knownPlatformIds.Contains(g.PlatformId))
                    .ToListAsync();
                foreach (var g in orphans)
                {
                    g.PlatformId = 1;
                    g.NeedsMetadataReview = true;
                    if (string.IsNullOrEmpty(g.MetadataReviewReason))
                        g.MetadataReviewReason = "Original platform could not be resolved. Please reassign.";
                }
                result.OrphansFixed = orphans.Count;

                // 3. Path-based platform heal (opt-in)
                if (request.HealPlatformFromPath)
                {
                    var candidates = await context.Games
                        .Where(g => g.Path != null && g.Path != "")
                        .ToListAsync();

                    foreach (var g in candidates)
                    {
                        var suggested = PlatformDefinitions.ResolvePlatformFromPath(g.Path);
                        if (suggested != null && suggested.Id != g.PlatformId && knownPlatformIds.Contains(suggested.Id))
                        {
                            g.PlatformId = suggested.Id;
                            result.PlatformsHealed++;
                        }
                    }
                }

                // 4. Container orphans: rows pointing into .psvita / .ps4 etc.
                if (request.PruneContainerOrphans)
                {
                    var pathRows = await context.Games
                        .Where(g => g.Path != null && g.Path != "")
                        .ToListAsync();
                    var orphanRows = pathRows
                        .Where(g => TitleCleanerService.IsContainerOrphanPath(g.Path))
                        .ToList();
                    if (orphanRows.Count > 0)
                    {
                        context.Games.RemoveRange(orphanRows);
                        result.ContainerOrphansRemoved = orphanRows.Count;
                    }
                }

                // 5. Dangling GameFiles: remove rows whose GameId no longer exists
                var gameIds = await context.Games.Select(g => g.Id).ToListAsync();
                var gameIdSet = gameIds.ToHashSet();
                var dangling = await context.GameFiles
                    .Where(f => !gameIdSet.Contains(f.GameId))
                    .ToListAsync();
                context.GameFiles.RemoveRange(dangling);
                result.DanglingGameFilesRemoved = dangling.Count;

                await context.SaveChangesAsync();

                // 5. Optional: merge duplicate game rows. Delegated to the
                // shared service so the heal-after-scan path (MediaScanner)
                // and the manual repair button stay in sync.
                if (request.MergeDuplicates)
                {
                    var mergeResult = await DuplicateGameMergeService.MergeAsync(context);
                    result.DuplicatesMerged = mergeResult.RowsMerged;
                }

                return Ok(result);
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
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
                context.GameFiles.RemoveRange(context.GameFiles);
                context.GameTags.RemoveRange(context.GameTags);
                context.CollectionGames.RemoveRange(context.CollectionGames);
                context.GameReviews.RemoveRange(context.GameReviews);
                context.Collections.RemoveRange(context.Collections);
                context.Games.RemoveRange(context.Games);
                await context.SaveChangesAsync();

                // review_items.json lives outside the DB
                var reviewItemsPath = Path.Combine(_configService.GetConfigDirectory(), "review_items.json");
                if (System.IO.File.Exists(reviewItemsPath))
                {
                    try { System.IO.File.Delete(reviewItemsPath); }
                    catch { /* best effort */ }
                }

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
                context.DownloadHistory.RemoveRange(context.DownloadHistory);
                context.DownloadBlacklist.RemoveRange(context.DownloadBlacklist);
                await context.SaveChangesAsync();
                return Ok(new { message = "Download history and blacklist cleared." });
            }
            catch (Exception ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }

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

                var settings = _configService.LoadDatabaseSettings();

                return Ok(new
                {
                    databaseType = settings.Type.ToString(),
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
                return BadRequest(new { error = ex.Message });
            }
        }

        private static DatabaseSettings ParseSettings(DatabaseSettingsRequest request)
        {
            return new DatabaseSettings
            {
                Type = Enum.Parse<DatabaseType>(request.Type, ignoreCase: true),
                SqlitePath = request.SqlitePath ?? "retroarr.db",
                Host = request.Host ?? "localhost",
                Port = request.Port ?? 5432,
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
