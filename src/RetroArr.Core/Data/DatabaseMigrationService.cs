using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
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

        public string BackupSqliteDatabase()
        {
            var settings = _configService.LoadDatabaseSettings();
            if (settings.Type != DatabaseType.SQLite)
                throw new InvalidOperationException("Backup is only available for SQLite databases.");

            var configPath = _configService.GetConfigDirectory();
            var sourcePath = Path.Combine(configPath, settings.SqlitePath);

            if (!File.Exists(sourcePath))
                throw new FileNotFoundException($"SQLite database not found: {sourcePath}");

            var timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
            var backupPath = Path.Combine(configPath, $"{Path.GetFileNameWithoutExtension(settings.SqlitePath)}.bak.{timestamp}.db");
            File.Copy(sourcePath, backupPath, overwrite: false);

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

                var sourceSettings = _configService.LoadDatabaseSettings();
                if (sourceSettings.Type == targetSettings.Type)
                    throw new InvalidOperationException("Source and target database types are the same.");

                var configPath = _configService.GetConfigDirectory();
                if (!DatabaseServiceExtensions.TestConnection(targetSettings, configPath, out var connError))
                    throw new InvalidOperationException($"Cannot connect to target database: {connError}");

                // Step 2: Backup
                progress.Step = "Backup";
                progress.StepNumber = 2;

                if (sourceSettings.Type == DatabaseType.SQLite)
                {
                    progress.BackupPath = BackupSqliteDatabase();
                }

                // Step 3: Read source data
                progress.Step = "Reading source data";
                progress.StepNumber = 3;

                using var sourceContext = await _contextFactory.CreateDbContextAsync();
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

                // Step 4: Create target schema
                progress.Step = "Creating target schema";
                progress.StepNumber = 4;

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
                await targetContext.Database.EnsureCreatedAsync();
                DatabaseMigrator.ApplyMigrations(targetContext, targetSettings.Type);

                // Step 5: Copy data with ID preservation
                progress.Step = "Copying data";
                progress.StepNumber = 5;

                await using var transaction = await targetContext.Database.BeginTransactionAsync();
                try
                {
                    // Clear target tables in reverse dependency order
                    await ClearTargetTables(targetContext, targetSettings.Type);

                    // Enable identity insert for providers that need it
                    if (targetSettings.Type == DatabaseType.PostgreSQL)
                    {
                        await SetPostgresIdentityInsert(targetContext, true);
                    }

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

                    // Reset sequences/auto-increment so future inserts don't conflict
                    if (targetSettings.Type == DatabaseType.PostgreSQL)
                    {
                        await ResetPostgresSequences(targetContext);
                    }
                    else if (targetSettings.Type == DatabaseType.MariaDB)
                    {
                        await ResetMariaDbAutoIncrement(targetContext);
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
                _logger.Info("[Migration] Database migration completed successfully.");
            }
            catch (Exception ex)
            {
                progress.Success = false;
                progress.Error = ex.Message;
                _logger.Error($"[Migration] Error: {ex.Message}");
            }

            return progress;
        }

        private static async Task ClearTargetTables(RetroArrDbContext ctx, DatabaseType dbType)
        {
            var tables = new[]
            {
                "DownloadBlacklist", "DownloadHistory",
                "GameReviews", "GameTags", "CollectionGames",
                "GameFiles", "Webhooks", "Tags", "Collections", "Games", "Platforms"
            };

            foreach (var table in tables)
            {
                try
                {
                    var quotedTable = dbType == DatabaseType.PostgreSQL ? $"\"{table}\"" : table;
                    await ctx.Database.ExecuteSqlRawAsync($"DELETE FROM {quotedTable}");
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Migration] Clear {table} skipped: {ex.Message}");
                }
            }
        }

        private static async Task InsertBatch<T>(RetroArrDbContext ctx, List<T> entities) where T : class
        {
            if (entities.Count == 0) return;
            ctx.Set<T>().AddRange(entities);
            await ctx.SaveChangesAsync();
            ctx.ChangeTracker.Clear();
        }

        private static async Task SetPostgresIdentityInsert(RetroArrDbContext ctx, bool enable)
        {
            // PostgreSQL with GENERATED BY DEFAULT AS IDENTITY allows explicit IDs.
            // No special toggle needed.
            await Task.CompletedTask;
        }

        private static readonly string[] SequenceTables = new[]
        {
            "Platforms", "Games", "GameFiles", "Collections", "CollectionGames",
            "Tags", "GameTags", "GameReviews", "Webhooks", "DownloadHistory", "DownloadBlacklist"
        };

        private static async Task ResetPostgresSequences(RetroArrDbContext ctx)
        {
            foreach (var table in SequenceTables)
            {
                try
                {
                    // Get max ID from the table, then set sequence to max+1
                    var maxCmd = ctx.Database.GetDbConnection().CreateCommand();
                    maxCmd.CommandText = $"SELECT COALESCE(MAX(\"Id\"), 0) FROM \"{table}\"";
                    if (maxCmd.Connection!.State != System.Data.ConnectionState.Open)
                        await maxCmd.Connection.OpenAsync();
                    var maxId = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());
                    if (maxId <= 0) continue;

                    await ctx.Database.ExecuteSqlRawAsync(
                        $"SELECT setval(pg_get_serial_sequence('\"{table}\"', 'Id'), {maxId + 1}, false)");
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Migration] Sequence reset for {table} skipped: {ex.Message}");
                }
            }
        }

        private static async Task ResetMariaDbAutoIncrement(RetroArrDbContext ctx)
        {
            foreach (var table in SequenceTables)
            {
                try
                {
                    var maxCmd = ctx.Database.GetDbConnection().CreateCommand();
                    maxCmd.CommandText = $"SELECT COALESCE(MAX(Id), 0) FROM {table}";
                    if (maxCmd.Connection!.State != System.Data.ConnectionState.Open)
                        await maxCmd.Connection.OpenAsync();
                    var maxId = Convert.ToInt32(await maxCmd.ExecuteScalarAsync());
                    if (maxId <= 0) continue;

                    await ctx.Database.ExecuteSqlRawAsync($"ALTER TABLE {table} AUTO_INCREMENT = {maxId + 1}");
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Migration] AUTO_INCREMENT reset for {table} skipped: {ex.Message}");
                }
            }
        }
    }
}
