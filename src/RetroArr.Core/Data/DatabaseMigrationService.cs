using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Data
{
    public class MigrationProgress
    {
        public string Step { get; set; } = string.Empty;
        public int StepNumber { get; set; }
        public int TotalSteps { get; set; }
        public bool Success { get; set; }
        public string? Error { get; set; }
        public string? BackupPath { get; set; }
        // rows of deleted games, tags or collections left out of the copy
        public int SkippedRows { get; set; }
        // games of a platform the source no longer has, moved to Unknown
        public int UnknownPlatformGames { get; set; }
        public Dictionary<string, (int Source, int Target)> RowCounts { get; set; } = new();
    }

    public class DatabaseMigrationService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly ConfigurationService _configService;
        private readonly IDbContextFactory<RetroArrDbContext> _contextFactory;
        private readonly MediaScannerService _scanner;

        public DatabaseMigrationService(
            ConfigurationService configService,
            IDbContextFactory<RetroArrDbContext> contextFactory,
            MediaScannerService scanner)
        {
            _configService = configService;
            _contextFactory = contextFactory;
            _scanner = scanner;
        }

        // The database in use, which database.json no longer names once the
        // user has pressed Save for another type.
        public string BackupSqliteDatabase()
        {
            using var context = _contextFactory.CreateDbContext();
            if (!context.Database.IsSqlite())
                throw new InvalidOperationException("Backup is only available for SQLite databases.");

            var sourcePath = context.Database.GetDbConnection().DataSource;
            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"SQLite database not found: {sourcePath}");

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(Path.GetDirectoryName(sourcePath) ?? string.Empty, $"{Path.GetFileNameWithoutExtension(sourcePath)}.bak.{timestamp}.db");
            if (File.Exists(backupPath))
                throw new IOException($"Backup already exists: {backupPath}");

            // SQLite's online backup: a copy of the file misses what still sits in retroarr.db-wal
            using (var source = new SqliteConnection(context.Database.GetConnectionString()))
            using (var target = new SqliteConnection($"Data Source={backupPath};Pooling=False"))
            {
                source.Open();
                target.Open();
                source.BackupDatabase(target);
            }

            _logger.Info($"[Migration] SQLite backup created: {backupPath}");
            return backupPath;
        }

        public async Task<MigrationProgress> MigrateAsync(DatabaseSettings targetSettings)
        {
            var progress = new MigrationProgress { TotalSteps = 6 };

            try
            {
                // Step 1: Pre-checks
                progress.Step = "Pre-checks";
                progress.StepNumber = 1;

                if (_scanner.IsScanning)
                    throw new InvalidOperationException("Cannot migrate while a scan is in progress. Stop the scan first.");

                using var sourceContext = await _contextFactory.CreateDbContextAsync();
                if (DatabaseServiceExtensions.RunningType(sourceContext) == targetSettings.Type)
                    throw new InvalidOperationException("Source and target database types are the same.");

                var configPath = _configService.GetConfigDirectory();
                if (!DatabaseServiceExtensions.TestConnection(targetSettings, configPath, out var connError))
                    throw new InvalidOperationException($"Cannot connect to target database: {connError}");

                // Step 2: Backup
                progress.Step = "Backup";
                progress.StepNumber = 2;

                if (sourceContext.Database.IsSqlite())
                {
                    progress.BackupPath = BackupSqliteDatabase();
                }

                // Step 3: Read source data
                progress.Step = "Reading source data";
                progress.StepNumber = 3;

                var platforms = await sourceContext.Platforms.AsNoTracking().ToListAsync();
                var games = await sourceContext.Games.AsNoTracking().ToListAsync();
                var gameFiles = await sourceContext.GameFiles.AsNoTracking().ToListAsync();
                var collections = await sourceContext.Collections.AsNoTracking().ToListAsync();
                var collectionGames = await sourceContext.CollectionGames.AsNoTracking().ToListAsync();
                var tags = await sourceContext.Tags.AsNoTracking().ToListAsync();
                var gameTags = await sourceContext.GameTags.AsNoTracking().ToListAsync();
                var reviews = await sourceContext.GameReviews.AsNoTracking().ToListAsync();
                var webhooks = await sourceContext.Webhooks.AsNoTracking().ToListAsync();
                var downloadHistory = await sourceContext.DownloadHistory.AsNoTracking().ToListAsync();
                var downloadBlacklist = await sourceContext.DownloadBlacklist.AsNoTracking().ToListAsync();
                var priceWatches = await sourceContext.WishlistPriceWatches.AsNoTracking().ToListAsync();
                var discoveredGames = await sourceContext.DiscoveredGames.AsNoTracking().ToListAsync();

                // SQLite files from before the foreign keys can hold rows of deleted games or tags,
                // which the target refuses and so fails the whole copy
                var gameIds = games.Select(g => g.Id).ToHashSet();
                var tagIds = tags.Select(t => t.Id).ToHashSet();
                var collectionIds = collections.Select(c => c.Id).ToHashSet();
                var dropped = gameFiles.RemoveAll(f => !gameIds.Contains(f.GameId))
                    + gameTags.RemoveAll(t => !gameIds.Contains(t.GameId) || !tagIds.Contains(t.TagId))
                    + collectionGames.RemoveAll(c => !gameIds.Contains(c.GameId) || !collectionIds.Contains(c.CollectionId))
                    + reviews.RemoveAll(r => !gameIds.Contains(r.GameId))
                    + priceWatches.RemoveAll(w => !gameIds.Contains(w.GameId));
                progress.SkippedRows = dropped;
                if (dropped > 0)
                    _logger.Warn($"[Migration] Skipping {dropped} row(s) that point to a deleted game, tag or collection.");
                progress.UnknownPlatformGames = MoveOrphansToUnknownPlatform(platforms, games);
                if (progress.UnknownPlatformGames > 0)
                    _logger.Warn($"[Migration] Moving {progress.UnknownPlatformGames} game(s) of a missing platform to Unknown.");

                progress.RowCounts["Platforms"] = (platforms.Count, 0);
                progress.RowCounts["Games"] = (games.Count, 0);
                progress.RowCounts["GameFiles"] = (gameFiles.Count, 0);
                progress.RowCounts["Collections"] = (collections.Count, 0);
                progress.RowCounts["CollectionGames"] = (collectionGames.Count, 0);
                progress.RowCounts["Tags"] = (tags.Count, 0);
                progress.RowCounts["GameTags"] = (gameTags.Count, 0);
                progress.RowCounts["GameReviews"] = (reviews.Count, 0);
                progress.RowCounts["Webhooks"] = (webhooks.Count, 0);
                progress.RowCounts["DownloadHistory"] = (downloadHistory.Count, 0);
                progress.RowCounts["DownloadBlacklist"] = (downloadBlacklist.Count, 0);
                progress.RowCounts["WishlistPriceWatches"] = (priceWatches.Count, 0);
                progress.RowCounts["DiscoveredGames"] = (discoveredGames.Count, 0);

                var targetConnectionString = targetSettings.GetConnectionString(configPath);
                var targetOptionsBuilder = new DbContextOptionsBuilder<RetroArrDbContext>();

                switch (targetSettings.Type)
                {
                    case DatabaseType.PostgreSQL:
                        targetOptionsBuilder.UseNpgsql(targetConnectionString);
                        break;
                    case DatabaseType.MariaDB:
                        var serverVersion = ServerVersion.AutoDetect(targetConnectionString);
                        targetOptionsBuilder.UseMySql(targetConnectionString, serverVersion);
                        break;
                    case DatabaseType.SQLite:
                    default:
                        targetOptionsBuilder.UseSqlite(targetConnectionString);
                        break;
                }

                using var targetContext = new RetroArrDbContext(targetOptionsBuilder.Options);

                // checked before anything is written; SQLite takes any length
                if (targetSettings.Type != DatabaseType.SQLite)
                {
                    var tooLong = FindTooLongValues(targetContext.Model, platforms, games, gameFiles, collections, collectionGames, tags,
                        gameTags, reviews, webhooks, downloadHistory, downloadBlacklist, priceWatches, discoveredGames);
                    if (tooLong.Count > 0)
                        throw new InvalidOperationException($"Some values are longer than the target allows, shorten them and try again: {string.Join("; ", tooLong)}");
                }

                // Step 4: Create target schema
                progress.Step = "Creating target schema";
                progress.StepNumber = 4;

                DatabaseMigrator.EnsureSchema(targetContext);
                DatabaseMigrator.ApplyMigrations(targetContext, targetSettings.Type);

                // Step 5: Copy data with ID preservation
                progress.Step = "Copying data";
                progress.StepNumber = 5;

                await using var transaction = await targetContext.Database.BeginTransactionAsync();
                try
                {
                    // Clear target tables in reverse dependency order
                    await ClearTargetTables(targetContext, targetSettings.Type);

                    // Insert in dependency order. AsNoTracking entities have no tracker
                    // state so Add() treats them as new with their existing IDs.
                    await InsertBatch(targetContext, platforms);
                    await InsertBatch(targetContext, games);
                    await InsertBatch(targetContext, gameFiles);
                    await InsertBatch(targetContext, collections);
                    await InsertBatch(targetContext, collectionGames);
                    await InsertBatch(targetContext, tags);
                    await InsertBatch(targetContext, gameTags);
                    await InsertBatch(targetContext, reviews);
                    await InsertBatch(targetContext, webhooks);
                    await InsertBatch(targetContext, downloadHistory);
                    await InsertBatch(targetContext, downloadBlacklist);
                    await InsertBatch(targetContext, priceWatches);
                    await InsertBatch(targetContext, discoveredGames);

                    // PostgreSQL identity sequences don't move past explicit ids; InnoDB's
                    // AUTO_INCREMENT does, and SQLite takes max(rowid) + 1.
                    if (targetSettings.Type == DatabaseType.PostgreSQL)
                    {
                        await ResetPostgresSequences(targetContext);
                    }

                    await transaction.CommitAsync();
                }
                catch
                {
                    await transaction.RollbackAsync();
                    throw;
                }

                // Step 6: Verify
                progress.Step = "Verifying";
                progress.StepNumber = 6;

                progress.RowCounts["Platforms"] = (platforms.Count, await targetContext.Platforms.CountAsync());
                progress.RowCounts["Games"] = (games.Count, await targetContext.Games.CountAsync());
                progress.RowCounts["GameFiles"] = (gameFiles.Count, await targetContext.GameFiles.CountAsync());
                progress.RowCounts["Collections"] = (collections.Count, await targetContext.Collections.CountAsync());
                progress.RowCounts["CollectionGames"] = (collectionGames.Count, await targetContext.CollectionGames.CountAsync());
                progress.RowCounts["Tags"] = (tags.Count, await targetContext.Tags.CountAsync());
                progress.RowCounts["GameTags"] = (gameTags.Count, await targetContext.GameTags.CountAsync());
                progress.RowCounts["GameReviews"] = (reviews.Count, await targetContext.GameReviews.CountAsync());
                progress.RowCounts["Webhooks"] = (webhooks.Count, await targetContext.Webhooks.CountAsync());
                progress.RowCounts["DownloadHistory"] = (downloadHistory.Count, await targetContext.DownloadHistory.CountAsync());
                progress.RowCounts["DownloadBlacklist"] = (downloadBlacklist.Count, await targetContext.DownloadBlacklist.CountAsync());
                progress.RowCounts["WishlistPriceWatches"] = (priceWatches.Count, await targetContext.WishlistPriceWatches.CountAsync());
                progress.RowCounts["DiscoveredGames"] = (discoveredGames.Count, await targetContext.DiscoveredGames.CountAsync());

                // Check all row counts match
                var mismatches = progress.RowCounts.Where(kv => kv.Value.Source != kv.Value.Target).ToList();
                if (mismatches.Any())
                {
                    var details = string.Join(", ", mismatches.Select(m => $"{m.Key}: {m.Value.Source} vs {m.Value.Target}"));
                    throw new InvalidOperationException($"Row count mismatch after migration: {details}");
                }

                // Save new database settings
                _configService.SaveDatabaseSettings(targetSettings);

                progress.Success = true;
                progress.Step = "Complete";
                _logger.Info("[Migration] database migration done.");
            }
            catch (Exception ex)
            {
                progress.Success = false;
                // EF's own message only says "see the inner exception"
                var table = (ex as DbUpdateException)?.Entries.FirstOrDefault()?.Metadata.GetTableName();
                progress.Error = $"{progress.Step}{(table != null ? $" ({table})" : "")}: {ex.GetBaseException().Message}";
                _logger.Error(ex, $"[Migration] Error at step '{progress.Step}'");

                // the source is never written to, so a failed attempt's backup is only clutter
                if (progress.BackupPath != null)
                {
                    try
                    {
                        File.Delete(progress.BackupPath);
                        progress.BackupPath = null;
                    }
                    catch (Exception deleteEx)
                    {
                        _logger.Warn($"[Migration] Could not delete backup {progress.BackupPath}: {deleteEx.Message}");
                    }
                }
            }

            return progress;
        }

        // The Games FK refuses a platform the copy doesn't carry; same review gate as the startup rescue.
        internal static int MoveOrphansToUnknownPlatform(List<Platform> platforms, List<Game> games)
        {
            var platformIds = platforms.Select(p => p.Id).ToHashSet();
            var orphans = games.Where(g => !platformIds.Contains(g.PlatformId)).ToList();
            foreach (var game in orphans)
            {
                game.PlatformId = PlatformDefinitions.UnknownPlatformId;
                game.NeedsMetadataReview = true;
                game.MetadataReviewReason = "Original platform could not be resolved during migration. Please reassign.";
            }
            return orphans.Count;
        }

        // Old SQLite files never enforced lengths, and the target refuses the first such row
        // and with it the whole copy. Lengths count characters, not UTF-16 units.
        internal static List<string> FindTooLongValues(IModel model, params IEnumerable<object>[] tables)
        {
            var problems = new List<string>();
            foreach (var rows in tables)
            {
                var first = rows.FirstOrDefault();
                var entityType = first == null ? null : model.FindEntityType(first.GetType());
                if (entityType == null) continue;
                var key = entityType.FindPrimaryKey()!.Properties[0].PropertyInfo!;
                foreach (var property in entityType.GetProperties())
                {
                    var max = property.GetMaxLength() ?? property.FindRelationalTypeMapping()?.Size;
                    var info = property.PropertyInfo;
                    if (max == null || info == null || property.ClrType != typeof(string)) continue;
                    var ids = rows.Where(r => info.GetValue(r) is string s && s.Length > max && s.EnumerateRunes().Count() > max)
                        .Select(r => key.GetValue(r)).ToList();
                    if (ids.Count > 0)
                        problems.Add($"{entityType.GetTableName()}.{property.GetColumnName()} over {max} characters (Id {string.Join(", ", ids.Take(5))}{(ids.Count > 5 ? $" and {ids.Count - 5} more" : "")})");
                }
            }
            return problems;
        }

        private static async Task ClearTargetTables(RetroArrDbContext ctx, DatabaseType dbType)
        {
            var tables = new[]
            {
                "DownloadBlacklist", "DownloadHistory", "DiscoveredGames", "WishlistPriceWatches",
                "GameReviews", "GameTags", "CollectionGames",
                "GameFiles", "Webhooks", "Tags", "Collections", "Games", "Platforms"
            };

            // No catch per table: one failed statement aborts a PostgreSQL transaction
            // anyway, and the first error is the one worth showing.
            foreach (var table in tables)
            {
                var quotedTable = dbType == DatabaseType.PostgreSQL ? $"\"{table}\"" : table;
                // Table names can't be parameterised, but they come from the
                // hardcoded `tables` array above - never user input.
                var sql = "DELETE FROM " + quotedTable;
                await ctx.Database.ExecuteSqlRawAsync(sql);
            }
        }

        private static async Task InsertBatch<T>(RetroArrDbContext ctx, List<T> entities) where T : class
        {
            if (entities.Count == 0) return;
            ctx.Set<T>().AddRange(entities);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
        }

        private static readonly string[] SequenceTables = new[]
        {
            "Platforms", "Games", "GameFiles", "Collections", "CollectionGames",
            "Tags", "GameTags", "GameReviews", "Webhooks", "DownloadHistory", "DownloadBlacklist",
            "WishlistPriceWatches", "DiscoveredGames"
        };

        private static async Task ResetPostgresSequences(RetroArrDbContext ctx)
        {
            foreach (var table in SequenceTables)
            {
                // Get max ID from the table, then set sequence to max+1
                var maxCmd = ctx.Database.GetDbConnection().CreateCommand();
                maxCmd.CommandText = $"SELECT COALESCE(MAX(\"Id\"), 0) FROM \"{table}\"";
                if (maxCmd.Connection!.State != System.Data.ConnectionState.Open)
                    await maxCmd.Connection.OpenAsync();
                var maxId = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());
                if (maxId <= 0) continue;

                // table comes from the hardcoded SequenceTables array, maxId is an int.
                var sql = "SELECT setval(pg_get_serial_sequence('\"" + table + "\"', 'Id'), " + (maxId + 1) + ", false)";
                await ctx.Database.ExecuteSqlRawAsync(sql);
            }
        }
    }
}
