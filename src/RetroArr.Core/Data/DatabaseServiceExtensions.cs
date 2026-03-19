using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RetroArr.Core.Configuration;
using System;

namespace RetroArr.Core.Data
{
    public static class DatabaseServiceExtensions
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
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
                        var serverVersion = ServerVersion.AutoDetect(connectionString);
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

        public static void EnsureDatabaseCreated(IServiceProvider serviceProvider)
        {
            using var scope = serviceProvider.CreateScope();
            var contextFactory = scope.ServiceProvider.GetRequiredService<IDbContextFactory<RetroArrDbContext>>();
            using var context = contextFactory.CreateDbContext();
            
            try
            {
                context.Database.EnsureCreated();
                _logger.Info("[Database] Database schema ensured.");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Database] Error ensuring database: {ex.Message}");
                throw;
            }
        }

        public static bool TestConnection(DatabaseSettings settings, string configPath, out string errorMessage)
        {
            errorMessage = string.Empty;
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
                        var serverVersion = ServerVersion.AutoDetect(connectionString);
                        optionsBuilder.UseMySql(connectionString, serverVersion);
                        break;

                    case DatabaseType.SQLite:
                    default:
                        optionsBuilder.UseSqlite(connectionString);
                        break;
                }

                using var context = new RetroArrDbContext(optionsBuilder.Options);
                context.Database.CanConnect();
                return true;
            }
            catch (Exception ex)
            {
                errorMessage = ex.Message;
                return false;
            }
        }
    }
}
