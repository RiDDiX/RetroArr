using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RetroArr.Core.Data
{
    public static class DatabaseServiceExtensions
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);

        // Set when the schema could not be created or upgraded at startup; the app keeps
        // running so the database settings can still be fixed from the UI. Cleared once
        // a background retry of InitializeDatabase gets through.
        public static string? StartupError { get; set; }

        public static IServiceCollection AddRetroArrDatabase(this IServiceCollection services, ConfigurationService configService)
        {
            var dbSettings = configService.LoadDatabaseSettings();
            var configPath = configService.GetConfigDirectory();

            // Backward compat: if retroarr.db doesn't exist but playarr.db does, use the old name
            if (dbSettings.Type == DatabaseType.SQLite && dbSettings.SqlitePath == "retroarr.db")
            {
                var newPath = System.IO.Path.Combine(configPath, "retroarr.db");
                var oldPath = System.IO.Path.Combine(configPath, "playarr.db");
                if (!System.IO.File.Exists(newPath) && System.IO.File.Exists(oldPath))
                {
                    _logger.Info("[Database] Found legacy playarr.db, using it for backward compatibility.");
                    dbSettings.SqlitePath = "playarr.db";
                }
            }

            var connectionString = dbSettings.GetConnectionString(configPath);

            _logger.Info($"[Database] Configuring {dbSettings.Type} database...");

            services.AddDbContextFactory<RetroArrDbContext>(options =>
            {
                switch (dbSettings.Type)
                {
                    case DatabaseType.PostgreSQL:
                        options.UseNpgsql(connectionString, npgsqlOptions =>
                        {
                            npgsqlOptions.CommandTimeout(dbSettings.ConnectionTimeout);
                            npgsqlOptions.EnableRetryOnFailure(3);
                        });
                        _logger.Info($"[Database] PostgreSQL configured: {dbSettings.Host}:{dbSettings.Port}/{dbSettings.Database}");
                        break;

                    case DatabaseType.MariaDB:
                        var serverVersion = DetectServerVersion(connectionString);
                        options.UseMySql(connectionString, serverVersion, mysqlOptions =>
                        {
                            mysqlOptions.CommandTimeout(dbSettings.ConnectionTimeout);
                            mysqlOptions.EnableRetryOnFailure(3);
                        });
                        _logger.Info($"[Database] MariaDB configured: {dbSettings.Host}:{dbSettings.Port}/{dbSettings.Database}");
                        break;

                    case DatabaseType.SQLite:
                    default:
                        options.UseSqlite(connectionString);
                        _logger.Info($"[Database] SQLite configured: {dbSettings.SqlitePath}");
                        break;
                }
            });

            return services;
        }

        // Retried in the background until it gets through, so a server that is still
        // starting or a password fixed later heals without a restart.
        public static void InitializeDatabase(IServiceProvider services, TimeSpan retryInterval, CancellationToken stopping)
        {
            if (TryInitializeDatabase(services)) return;

            _ = Task.Run(async () =>
            {
                try
                {
                    do
                    {
                        await Task.Delay(retryInterval, stopping);
                    }
                    while (!TryInitializeDatabase(services));
                    _logger.Info("[Database] Database initialized after retry, startup error cleared.");
                }
                catch (OperationCanceledException) { }
            });
        }

        private static bool TryInitializeDatabase(IServiceProvider services)
        {
            try
            {
                using var context = services.GetRequiredService<IDbContextFactory<RetroArrDbContext>>().CreateDbContext();
                DatabaseMigrator.EnsureSchema(context);
                _logger.Info("[Database] Schema ensured.");

                // Consolidated schema migrations (columns, tables, indexes). The running
                // provider, not database.json: settings saved since then wait for a restart.
                DatabaseMigrator.ApplyMigrations(context, RunningType(context));

                // Ensure ALL platforms from PlatformDefinitions exist in DB
                bool changesMade = false;
                foreach (var platform in PlatformDefinitions.AllPlatforms)
                {
                    if (!context.Platforms.Any(p => p.Id == platform.Id))
                    {
                        _logger.Info($"[Database] Seeding missing platform: {platform.Name} (ID: {platform.Id})");
                        context.Platforms.Add(platform);
                        changesMade = true;
                    }
                }

                if (changesMade)
                {
                    context.SaveChanges();
                    _logger.Info("[Database] Platforms updated.");
                }

                StartupError = null;
                return true;
            }
            catch (Exception ex)
            {
                // to app.log as well, and to Settings > Database; a retry logs only a new error
                var message = ex.GetBaseException().Message;
                if (message != StartupError)
                    _logger.Error(ex, $"[Database] Error initializing database: {message}");
                StartupError = message;
                return false;
            }
        }

        // AutoDetect connects to the server. Throwing would stop the app (every service
        // with a DbContext builds these options), so a wrong setting could only be fixed
        // in database.json by hand. A server started together with RetroArr gets a few
        // tries, as PostgreSQL gets from EnableRetryOnFailure. After that MySQL 5.7, which
        // both MySQL and MariaDB run: a newer MariaDB version would make Pomelo use INSERT
        // .. RETURNING, which MySQL lacks. Restarting once the server is up detects it.
        private static ServerVersion DetectServerVersion(string connectionString)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    return ServerVersion.AutoDetect(connectionString);
                }
                catch (Exception ex) when (attempt < 4)
                {
                    _logger.Warn($"[Database] MariaDB/MySQL not reachable (attempt {attempt}): {ex.Message}");
                    System.Threading.Thread.Sleep(TimeSpan.FromSeconds(attempt * 2));
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Database] MariaDB/MySQL server version not detected, restart once the server is up: {ex.Message}");
                    return new MySqlServerVersion(new Version(5, 7));
                }
            }
        }

        public static DatabaseType RunningType(RetroArrDbContext context) =>
            context.Database.IsSqlite() ? DatabaseType.SQLite
            : context.Database.IsNpgsql() ? DatabaseType.PostgreSQL
            : DatabaseType.MariaDB;

        // message is also set on success when the database is still to be created
        public static bool TestConnection(DatabaseSettings settings, string configPath, out string message)
        {
            message = string.Empty;
            var connectionString = settings.GetConnectionString(configPath);

            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<RetroArrDbContext>();

                switch (settings.Type)
                {
                    case DatabaseType.PostgreSQL:
                        optionsBuilder.UseNpgsql(connectionString);
                        break;

                    case DatabaseType.MariaDB:
                        // A pooled connection keeps the privileges it logged in with, so a
                        // GRANT made after a failed test would not show on the next one.
                        connectionString = new MySqlConnector.MySqlConnectionStringBuilder(connectionString) { Pooling = false }.ConnectionString;
                        var serverVersion = ServerVersion.AutoDetect(connectionString);
                        optionsBuilder.UseMySql(connectionString, serverVersion);
                        break;

                    case DatabaseType.SQLite:
                    default:
                        optionsBuilder.UseSqlite(connectionString);
                        break;
                }

                using var context = new RetroArrDbContext(optionsBuilder.Options);
                // CanConnect() swallows the error and returns false. Exists() throws it
                // (password, host, SSL); false only means the database is not there yet,
                // and EnsureCreated creates it.
                if (!context.GetService<IRelationalDatabaseCreator>().Exists())
                {
                    if (settings.Type != DatabaseType.SQLite)
                        message = $"Connected. Database '{settings.Database}' does not exist yet; RetroArr creates it, which needs the CREATEDB right (PostgreSQL) or the CREATE privilege (MariaDB/MySQL).";
                    return true;
                }

                message = MissingCreateRight(context, settings) ?? string.Empty;
                return message.Length == 0;
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        // Logging in is not enough: PostgreSQL 15+ lets only the database owner create
        // tables in public, and a MariaDB user may lack CREATE. Without this the test
        // passes and the first CREATE TABLE fails at startup.
        private static string? MissingCreateRight(RetroArrDbContext context, DatabaseSettings settings)
        {
            switch (settings.Type)
            {
                case DatabaseType.PostgreSQL:
                    var canCreate = context.Database
                        .SqlQueryRaw<bool>("SELECT COALESCE(has_schema_privilege(current_schema(), 'CREATE'), false) AS \"Value\"")
                        .AsEnumerable().Single();
                    return canCreate ? null
                        : $"Connected, but user '{settings.Username}' may not create tables in database '{settings.Database}' (PostgreSQL 15+ allows that only to the database owner). As a superuser run: ALTER DATABASE \"{settings.Database}\" OWNER TO \"{settings.Username}\"; or GRANT CREATE ON SCHEMA public TO \"{settings.Username}\";";

                case DatabaseType.MariaDB:
                    try
                    {
                        context.Database.ExecuteSqlRaw("CREATE TABLE IF NOT EXISTS __retroarr_probe (Id INT)");
                        context.Database.ExecuteSqlRaw("DROP TABLE __retroarr_probe");
                        return null;
                    }
                    catch (Exception ex)
                    {
                        return $"Connected, but user '{settings.Username}' may not create tables in database '{settings.Database}': {ex.Message.TrimEnd('.')}. As root run: GRANT ALL PRIVILEGES ON `{settings.Database}`.* TO '{settings.Username}'@'%';";
                    }

                default:
                    return null;
            }
        }
    }
}
