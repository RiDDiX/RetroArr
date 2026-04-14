using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using RetroArr.Core.Configuration;
using RetroArr.Core.Games;

namespace RetroArr.Core.Data
{
    public static class DatabaseMigrator
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private static readonly (string Name, string SqliteType, string PgType, string MariaType)[] GamesColumns = new[]
        {
            ("ExecutablePath",          "TEXT",                           "TEXT",                            "TEXT"),
            ("IsExternal",              "INTEGER DEFAULT 0",             "INTEGER DEFAULT 0",               "INT DEFAULT 0"),
            ("Images_CoverUrl",         "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_CoverLargeUrl",    "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_BackgroundUrl",    "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_BannerUrl",        "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_Screenshots",      "TEXT",                          "TEXT",                            "LONGTEXT"),
            ("Images_Artworks",         "TEXT",                          "TEXT",                            "LONGTEXT"),
            ("Genres",                  "TEXT",                          "TEXT",                            "LONGTEXT"),
            ("IsInstallable",           "INTEGER NOT NULL DEFAULT 0",    "INTEGER NOT NULL DEFAULT 0",      "INT NOT NULL DEFAULT 0"),
            ("InstallPath",             "TEXT",                          "TEXT",                            "TEXT"),
            ("IgdbId",                  "INTEGER",                       "INTEGER",                         "INT"),
            ("SteamId",                 "INTEGER",                       "INTEGER",                         "INT"),
            ("GogId",                   "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("PreferredRunner",         "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("MatchConfidence",         "REAL",                          "DOUBLE PRECISION",                "DOUBLE"),
            ("MetadataConfirmedByUser", "INTEGER NOT NULL DEFAULT 0",    "INTEGER NOT NULL DEFAULT 0",      "INT NOT NULL DEFAULT 0"),
            ("MetadataConfirmedAt",     "TEXT",                          "TEXT",                            "TEXT"),
            ("NeedsMetadataReview",     "INTEGER NOT NULL DEFAULT 0",    "INTEGER NOT NULL DEFAULT 0",      "INT NOT NULL DEFAULT 0"),
            ("MetadataReviewReason",    "TEXT",                          "TEXT",                            "TEXT"),
            ("Region",                  "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("Languages",               "TEXT",                          "TEXT",                            "TEXT"),
            ("Revision",                "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("Images_BoxBackUrl",       "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_VideoUrl",         "TEXT",                          "TEXT",                            "TEXT"),
            ("MetadataSource",          "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("ProtonDbTier",            "TEXT",                          "TEXT",                            "VARCHAR(50)")
        };

        private static readonly (string Name, string SqliteType, string PgType, string MariaType)[] GameFilesColumns = new[]
        {
            ("FileType", "TEXT NOT NULL DEFAULT 'Main'", "TEXT NOT NULL DEFAULT 'Main'", "VARCHAR(50) NOT NULL DEFAULT 'Main'"),
            ("Version",     "TEXT", "TEXT", "VARCHAR(255)"),
            ("ContentName", "TEXT", "TEXT", "VARCHAR(500)"),
            ("TitleId",     "TEXT", "TEXT", "VARCHAR(255)"),
            ("Serial",      "TEXT", "TEXT", "VARCHAR(255)")
        };

        public static void ApplyMigrations(RetroArrDbContext context, DatabaseType dbType = DatabaseType.SQLite)
        {
            try
            {
                var connection = context.Database.GetDbConnection();
                if (connection.State != System.Data.ConnectionState.Open) connection.Open();

                EnsureColumns(connection, "Games", GamesColumns, dbType);
                EnsureTablesSafe(connection, dbType);
                EnsureColumns(connection, "GameFiles", GameFilesColumns, dbType);
                EnsureDownloadTablesSafe(connection, dbType);
                EnsureIndexesSafe(connection, dbType);
                EnsurePlatformsSafe(connection, dbType);

                connection.Close();
                _logger.Info($"[Database] Schema migrations applied successfully ({dbType}).");
            }
            catch (Exception ex)
            {
                _logger.Error($"[Database] Migration error: {ex.Message}");
            }
        }

        private static HashSet<string> GetExistingColumns(DbConnection connection, string tableName, DatabaseType dbType)
        {
            var columns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using var cmd = connection.CreateCommand();

            switch (dbType)
            {
                case DatabaseType.SQLite:
                    cmd.CommandText = $"PRAGMA table_info({tableName});";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var colName = reader["name"]?.ToString();
                            if (!string.IsNullOrEmpty(colName)) columns.Add(colName);
                        }
                    }
                    break;

                case DatabaseType.PostgreSQL:
                    cmd.CommandText = $"SELECT column_name FROM information_schema.columns WHERE table_name = '{tableName.ToLower()}';";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var colName = reader[0]?.ToString();
                            if (!string.IsNullOrEmpty(colName)) columns.Add(colName);
                        }
                    }
                    break;

                case DatabaseType.MariaDB:
                    cmd.CommandText = $"SELECT COLUMN_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_NAME = '{tableName}' AND TABLE_SCHEMA = DATABASE();";
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            var colName = reader[0]?.ToString();
                            if (!string.IsNullOrEmpty(colName)) columns.Add(colName);
                        }
                    }
                    break;
            }

            return columns;
        }

        private static void EnsureColumns(DbConnection connection, string tableName,
            (string Name, string SqliteType, string PgType, string MariaType)[] requiredColumns, DatabaseType dbType)
        {
            var existing = GetExistingColumns(connection, tableName, dbType);
            if (existing.Count == 0) return; // Table doesn't exist yet — EnsureCreated will handle it

            foreach (var col in requiredColumns)
            {
                if (existing.Contains(col.Name)) continue;

                var typeDef = dbType switch
                {
                    DatabaseType.PostgreSQL => col.PgType,
                    DatabaseType.MariaDB => col.MariaType,
                    _ => col.SqliteType
                };

                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = $"ALTER TABLE {QuoteTable(tableName, dbType)} ADD COLUMN {QuoteColumn(col.Name, dbType)} {typeDef};";
                    cmd.ExecuteNonQuery();
                    _logger.Info($"[Database] Added column: {tableName}.{col.Name}");
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Database] Column '{tableName}.{col.Name}' skipped: {ex.Message}");
                }
            }
        }

        private static void EnsureTablesSafe(DbConnection connection, DatabaseType dbType)
        {
            if (dbType != DatabaseType.SQLite) return; // Non-SQLite uses EnsureCreated which builds all tables
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS GameFiles (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        GameId INTEGER NOT NULL,
                        RelativePath TEXT,
                        Size INTEGER NOT NULL,
                        DateAdded TEXT NOT NULL DEFAULT '0001-01-01 00:00:00',
                        Quality TEXT, ReleaseGroup TEXT, Edition TEXT,
                        FileType TEXT NOT NULL DEFAULT 'Main',
                        Languages TEXT,
                        CONSTRAINT FK_GameFiles_Games_GameId FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
                    );
                    CREATE INDEX IF NOT EXISTS IX_GameFiles_GameId ON GameFiles (GameId);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] GameFiles table creation skipped: {ex.Message}");
            }
        }

        private static void EnsureDownloadTablesSafe(DbConnection connection, DatabaseType dbType)
        {
            if (dbType != DatabaseType.SQLite) return; // Non-SQLite uses EnsureCreated
            try
            {
                using var cmd1 = connection.CreateCommand();
                cmd1.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DownloadHistory (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DownloadId TEXT NOT NULL, ClientId INTEGER NOT NULL,
                        ClientName TEXT NOT NULL DEFAULT '', Title TEXT NOT NULL,
                        CleanTitle TEXT, Platform TEXT,
                        Size INTEGER NOT NULL DEFAULT 0,
                        State TEXT NOT NULL DEFAULT 'Imported',
                        Reason TEXT, SourcePath TEXT, DestinationPath TEXT,
                        ImportedAt TEXT NOT NULL, AddedAt TEXT NOT NULL, GameId INTEGER
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_DownloadHistory_DownloadId ON DownloadHistory(DownloadId);
                    CREATE INDEX IF NOT EXISTS IX_DownloadHistory_State ON DownloadHistory(State);
                    CREATE INDEX IF NOT EXISTS IX_DownloadHistory_Platform ON DownloadHistory(Platform);
                    CREATE INDEX IF NOT EXISTS IX_DownloadHistory_ImportedAt ON DownloadHistory(ImportedAt);";
                cmd1.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] DownloadHistory table skipped: {ex.Message}");
            }

            try
            {
                using var cmd2 = connection.CreateCommand();
                cmd2.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DownloadBlacklist (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        DownloadId TEXT, Title TEXT NOT NULL,
                        Platform TEXT, Reason TEXT NOT NULL DEFAULT '',
                        BlacklistedAt TEXT NOT NULL, ClientName TEXT
                    );
                    CREATE INDEX IF NOT EXISTS IX_DownloadBlacklist_DownloadId ON DownloadBlacklist(DownloadId);
                    CREATE INDEX IF NOT EXISTS IX_DownloadBlacklist_Title ON DownloadBlacklist(Title);";
                cmd2.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] DownloadBlacklist table skipped: {ex.Message}");
            }
        }

        private static void EnsureIndexesSafe(DbConnection connection, DatabaseType dbType)
        {
            if (dbType != DatabaseType.SQLite) return; // Non-SQLite indexes created by EnsureCreated
            var indexes = new[]
            {
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Games_Title_PlatformId ON Games (Title, PlatformId);",
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Games_Path ON Games (Path) WHERE Path IS NOT NULL;"
            };

            foreach (var sql in indexes)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Database] Index skipped: {ex.Message}");
                }
            }
        }

        private static void EnsurePlatformsSafe(DbConnection connection, DatabaseType dbType)
        {
            foreach (var platform in PlatformDefinitions.AllPlatforms)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = dbType switch
                    {
                        DatabaseType.PostgreSQL =>
                            @"INSERT INTO ""Platforms"" (""Id"", ""Name"", ""Slug"", ""FolderName"", ""Type"", ""Enabled"", ""Category"", ""IgdbPlatformId"", ""ScreenScraperSystemId"", ""ParentPlatformId"")
                              VALUES (@id, @name, @slug, @folder, @type, @enabled, @cat, @igdb, @ss, @parent)
                              ON CONFLICT (""Id"") DO NOTHING",
                        DatabaseType.MariaDB =>
                            @"INSERT IGNORE INTO Platforms (Id, Name, Slug, FolderName, Type, Enabled, Category, IgdbPlatformId, ScreenScraperSystemId, ParentPlatformId)
                              VALUES (@id, @name, @slug, @folder, @type, @enabled, @cat, @igdb, @ss, @parent)",
                        _ =>
                            @"INSERT OR IGNORE INTO Platforms (Id, Name, Slug, FolderName, Type, Enabled, Category, IgdbPlatformId, ScreenScraperSystemId, ParentPlatformId)
                              VALUES ($id, $name, $slug, $folder, $type, $enabled, $cat, $igdb, $ss, $parent)"
                    };

                    var prefix = dbType == DatabaseType.SQLite ? "$" : "@";
                    AddParam(cmd, $"{prefix}id", platform.Id);
                    AddParam(cmd, $"{prefix}name", platform.Name);
                    AddParam(cmd, $"{prefix}slug", platform.Slug);
                    AddParam(cmd, $"{prefix}folder", platform.FolderName);
                    AddParam(cmd, $"{prefix}type", (int)platform.Type);
                    AddParam(cmd, $"{prefix}enabled", platform.Enabled ? 1 : 0);
                    AddParam(cmd, $"{prefix}cat", (object?)platform.Category ?? DBNull.Value);
                    AddParam(cmd, $"{prefix}igdb", (object?)platform.IgdbPlatformId ?? DBNull.Value);
                    AddParam(cmd, $"{prefix}ss", (object?)platform.ScreenScraperSystemId ?? DBNull.Value);
                    AddParam(cmd, $"{prefix}parent", (object?)platform.ParentPlatformId ?? DBNull.Value);

                    cmd.ExecuteNonQuery();
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Database] Platform '{platform.Name}' seed skipped: {ex.Message}");
                }
            }
            _logger.Info($"[Database] Platforms seeded: {PlatformDefinitions.AllPlatforms.Count} definitions.");
        }

        private static void AddParam(DbCommand cmd, string name, object value)
        {
            var p = cmd.CreateParameter();
            p.ParameterName = name;
            p.Value = value;
            cmd.Parameters.Add(p);
        }

        private static string QuoteTable(string name, DatabaseType dbType) =>
            dbType == DatabaseType.PostgreSQL ? $"\"{name}\"" : name;

        private static string QuoteColumn(string name, DatabaseType dbType) =>
            dbType == DatabaseType.PostgreSQL ? $"\"{name}\"" : name;
    }
}
