using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Data;
using System;
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

        public DatabaseController(
            ConfigurationService configService,
            IDbContextFactory<RetroArrDbContext> contextFactory,
            DatabaseMigrationService migrationService)
        {
            _configService = configService;
            _contextFactory = contextFactory;
            _migrationService = migrationService;
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
        public bool UseSsl { get; set; }
        public int ConnectionTimeout { get; set; } = 30;
        public bool IsConfigured { get; set; }
    }
}
