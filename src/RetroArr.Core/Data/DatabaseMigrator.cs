using System;
using System.Collections.Generic;
using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
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
            ("IsExternal",              "INTEGER DEFAULT 0",             "BOOLEAN NOT NULL DEFAULT FALSE",  "INT DEFAULT 0"),
            ("Images_CoverUrl",         "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_CoverLargeUrl",    "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_BackgroundUrl",    "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_BannerUrl",        "TEXT",                          "TEXT",                            "TEXT"),
            // JSON lists EF reads as NOT NULL; MySQL takes a TEXT default only as an expression
            ("Images_Screenshots",      "TEXT NOT NULL DEFAULT '[]'",    "TEXT NOT NULL DEFAULT '[]'",      "LONGTEXT NOT NULL DEFAULT ('[]')"),
            ("Images_Artworks",         "TEXT NOT NULL DEFAULT '[]'",    "TEXT NOT NULL DEFAULT '[]'",      "LONGTEXT NOT NULL DEFAULT ('[]')"),
            ("Genres",                  "TEXT NOT NULL DEFAULT '[]'",    "TEXT NOT NULL DEFAULT '[]'",      "LONGTEXT NOT NULL DEFAULT ('[]')"),
            ("IsInstallable",           "INTEGER NOT NULL DEFAULT 0",    "BOOLEAN NOT NULL DEFAULT FALSE",  "INT NOT NULL DEFAULT 0"),
            ("InstallPath",             "TEXT",                          "TEXT",                            "TEXT"),
            ("IgdbId",                  "INTEGER",                       "INTEGER",                         "INT"),
            ("SteamId",                 "INTEGER",                       "INTEGER",                         "INT"),
            ("GogId",                   "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("EpicId",                  "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("PreferredRunner",         "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("MatchConfidence",         "REAL",                          "DOUBLE PRECISION",                "DOUBLE"),
            ("MetadataConfirmedByUser", "INTEGER NOT NULL DEFAULT 0",    "BOOLEAN NOT NULL DEFAULT FALSE",  "INT NOT NULL DEFAULT 0"),
            ("MetadataConfirmedAt",     "TEXT",                          "TIMESTAMPTZ",                     "TEXT"),
            ("NeedsMetadataReview",     "INTEGER NOT NULL DEFAULT 0",    "BOOLEAN NOT NULL DEFAULT FALSE",  "INT NOT NULL DEFAULT 0"),
            ("MetadataReviewReason",    "TEXT",                          "TEXT",                            "TEXT"),
            ("Region",                  "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("Languages",               "TEXT",                          "TEXT",                            "TEXT"),
            ("Revision",                "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("Images_BoxBackUrl",       "TEXT",                          "TEXT",                            "TEXT"),
            ("Images_VideoUrl",         "TEXT",                          "TEXT",                            "TEXT"),
            ("MetadataSource",          "TEXT",                          "TEXT",                            "VARCHAR(255)"),
            ("ProtonDbTier",            "TEXT",                          "TEXT",                            "VARCHAR(50)"),
            ("MissingSince",            "TEXT",                          "TIMESTAMPTZ",                     "DATETIME(6)"),
            ("PreferredReleaseGroup",   "TEXT",                          "TEXT",                            "VARCHAR(100)")
        };

        private static readonly (string Name, string SqliteType, string PgType, string MariaType)[] GameFilesColumns = new[]
        {
            ("FileType", "TEXT NOT NULL DEFAULT 'Main'", "TEXT NOT NULL DEFAULT 'Main'", "VARCHAR(50) NOT NULL DEFAULT 'Main'"),
            ("Version",     "TEXT", "TEXT", "VARCHAR(255)"),
            ("ContentName", "TEXT", "TEXT", "VARCHAR(500)"),
            ("TitleId",     "TEXT", "TEXT", "VARCHAR(255)"),
            ("Serial",      "TEXT", "TEXT", "VARCHAR(255)"),
            ("LastAutoRenameAt", "TEXT", "TIMESTAMPTZ", "DATETIME(6)")
        };

        // EnsureCreated does nothing once the database holds any table, also another
        // app's, so a shared database would be left without RetroArr's tables.
        public static void EnsureSchema(RetroArrDbContext context)
        {
            if (context.Database.EnsureCreated()) return;

            context.Database.OpenConnection();
            try
            {
                var dbType = DatabaseServiceExtensions.RunningType(context);
                if (GetExistingColumns(context.Database.GetDbConnection(), "Games", dbType).Count > 0) return;
            }
            finally
            {
                context.Database.CloseConnection();
            }
            context.GetService<IRelationalDatabaseCreator>().CreateTables();
        }

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
                EnsureDownloadHistoryIndexSafe(connection, dbType);
                EnsureDiscoveryTablesSafe(connection, dbType);
                EnsureWishlistTablesSafe(connection, dbType);
                EnsureIndexesSafe(connection, dbType);
                EnsureColumnShapesSafe(connection, dbType);
                EnsurePlatformsSafe(connection, dbType);
                RescueOrphanPlatformRefs(connection, dbType);
                CanonicalizeNullRegions(connection, dbType);
                BackfillGamesSafe(connection, dbType);

                connection.Close();
                _logger.Info($"[Database] schema migrations applied ({dbType}).");
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
                    // EF creates quoted, case-kept names ("Games")
                    cmd.CommandText = $"SELECT column_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = '{tableName}';";
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
            if (existing.Count == 0) return; // Table doesn't exist yet - EnsureCreated will handle it

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

        // NZBGet ids are numbers every client counts on its own, so a download id is unique per client.
        // Earlier builds made DownloadId unique by itself, which refused a second client's row.
        private static void EnsureDownloadHistoryIndexSafe(DbConnection connection, DatabaseType dbType)
        {
            string? create = "CREATE UNIQUE INDEX IF NOT EXISTS IX_DownloadHistory_DownloadId_ClientId ON DownloadHistory (DownloadId, ClientId);";
            string? drop = "DROP INDEX IF EXISTS IX_DownloadHistory_DownloadId;";
            try
            {
                if (dbType == DatabaseType.PostgreSQL)
                {
                    create = @"CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DownloadHistory_DownloadId_ClientId"" ON ""DownloadHistory"" (""DownloadId"", ""ClientId"");";
                    drop = @"DROP INDEX IF EXISTS ""IX_DownloadHistory_DownloadId"";";
                }
                else if (dbType == DatabaseType.MariaDB)
                {
                    // MySQL has neither CREATE INDEX IF NOT EXISTS nor DROP INDEX IF EXISTS
                    var indexes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = "SELECT INDEX_NAME FROM INFORMATION_SCHEMA.STATISTICS WHERE TABLE_NAME = 'DownloadHistory' AND TABLE_SCHEMA = DATABASE();";
                        using var reader = cmd.ExecuteReader();
                        while (reader.Read()) indexes.Add(reader[0]?.ToString() ?? string.Empty);
                    }
                    if (indexes.Count == 0) return; // no table
                    create = indexes.Contains("IX_DownloadHistory_DownloadId_ClientId") ? null
                        : "CREATE UNIQUE INDEX IX_DownloadHistory_DownloadId_ClientId ON DownloadHistory (DownloadId, ClientId);";
                    drop = indexes.Contains("IX_DownloadHistory_DownloadId") ? "DROP INDEX IX_DownloadHistory_DownloadId ON DownloadHistory;" : null;
                }

                // the new index first, so a failure leaves the old one in place
                foreach (var sql in new[] { create, drop })
                {
                    if (sql == null) continue;
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] DownloadHistory index skipped: {ex.Message}");
            }
        }

        private static void EnsureDiscoveryTablesSafe(DbConnection connection, DatabaseType dbType)
        {
            if (dbType != DatabaseType.SQLite) return;
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = @"
                    CREATE TABLE IF NOT EXISTS DiscoveredGames (
                        Id INTEGER PRIMARY KEY AUTOINCREMENT,
                        Title TEXT NOT NULL,
                        Path TEXT NOT NULL,
                        PlatformKey TEXT,
                        PlatformId INTEGER,
                        Serial TEXT,
                        ExecutablePath TEXT,
                        IsExternal INTEGER NOT NULL DEFAULT 0,
                        IsInstaller INTEGER NOT NULL DEFAULT 0,
                        Region TEXT,
                        Languages TEXT,
                        Revision TEXT,
                        DiscoveredAt TEXT NOT NULL DEFAULT '0001-01-01 00:00:00'
                    );
                    CREATE UNIQUE INDEX IF NOT EXISTS IX_DiscoveredGames_Path ON DiscoveredGames(Path);
                    CREATE INDEX IF NOT EXISTS IX_DiscoveredGames_PlatformKey ON DiscoveredGames(PlatformKey);";
                cmd.ExecuteNonQuery();
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] DiscoveredGames table skipped: {ex.Message}");
            }
        }

        private static void EnsureWishlistTablesSafe(DbConnection connection, DatabaseType dbType)
        {
            try
            {
                using var cmd = connection.CreateCommand();
                cmd.CommandText = dbType switch
                {
                    DatabaseType.PostgreSQL => @"
                        CREATE TABLE IF NOT EXISTS ""WishlistPriceWatches"" (
                            ""Id"" INTEGER GENERATED BY DEFAULT AS IDENTITY,
                            ""GameId"" INTEGER NOT NULL,
                            ""Provider"" VARCHAR(20) NOT NULL DEFAULT 'steam',
                            ""ExternalId"" VARCHAR(64) NOT NULL,
                            ""Currency"" VARCHAR(8),
                            ""CurrentPrice"" NUMERIC(10,2),
                            ""PreviousPrice"" NUMERIC(10,2),
                            ""TargetPrice"" NUMERIC(10,2),
                            ""NotifyOnAnyDrop"" BOOLEAN NOT NULL DEFAULT TRUE,
                            ""IsOnSale"" BOOLEAN NOT NULL DEFAULT FALSE,
                            ""DiscountPercent"" INTEGER,
                            ""LastCheckedAt"" TIMESTAMPTZ,
                            ""LastChangedAt"" TIMESTAMPTZ,
                            ""CreatedAt"" TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                            CONSTRAINT ""PK_WishlistPriceWatches"" PRIMARY KEY (""Id""),
                            CONSTRAINT ""FK_WishlistPriceWatches_Games_GameId"" FOREIGN KEY (""GameId"") REFERENCES ""Games"" (""Id"") ON DELETE CASCADE
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_WishlistPriceWatches_GameId_Provider"" ON ""WishlistPriceWatches"" (""GameId"", ""Provider"");
                        CREATE INDEX IF NOT EXISTS ""IX_WishlistPriceWatches_Provider"" ON ""WishlistPriceWatches"" (""Provider"");",
                    DatabaseType.MariaDB => @"
                        CREATE TABLE IF NOT EXISTS WishlistPriceWatches (
                            Id INT AUTO_INCREMENT PRIMARY KEY,
                            GameId INT NOT NULL,
                            Provider VARCHAR(20) NOT NULL DEFAULT 'steam',
                            ExternalId VARCHAR(64) NOT NULL,
                            Currency VARCHAR(8),
                            CurrentPrice DECIMAL(10,2),
                            PreviousPrice DECIMAL(10,2),
                            TargetPrice DECIMAL(10,2),
                            NotifyOnAnyDrop TINYINT(1) NOT NULL DEFAULT 1,
                            IsOnSale TINYINT(1) NOT NULL DEFAULT 0,
                            DiscountPercent INT,
                            LastCheckedAt DATETIME(6),
                            LastChangedAt DATETIME(6),
                            CreatedAt DATETIME(6) NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
                            UNIQUE KEY IX_WishlistPriceWatches_GameId_Provider (GameId, Provider),
                            INDEX IX_WishlistPriceWatches_Provider (Provider),
                            CONSTRAINT FK_WishlistPriceWatches_Games_GameId FOREIGN KEY (GameId) REFERENCES Games(Id) ON DELETE CASCADE
                        );",
                    _ => @"
                        CREATE TABLE IF NOT EXISTS WishlistPriceWatches (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            GameId INTEGER NOT NULL,
                            Provider TEXT NOT NULL DEFAULT 'steam',
                            ExternalId TEXT NOT NULL,
                            Currency TEXT,
                            CurrentPrice DECIMAL(10,2),
                            PreviousPrice DECIMAL(10,2),
                            TargetPrice DECIMAL(10,2),
                            NotifyOnAnyDrop INTEGER NOT NULL DEFAULT 1,
                            IsOnSale INTEGER NOT NULL DEFAULT 0,
                            DiscountPercent INTEGER,
                            LastCheckedAt TEXT,
                            LastChangedAt TEXT,
                            CreatedAt TEXT NOT NULL DEFAULT (datetime('now')),
                            CONSTRAINT FK_WishlistPriceWatches_Games_GameId FOREIGN KEY (GameId) REFERENCES Games (Id) ON DELETE CASCADE
                        );
                        CREATE UNIQUE INDEX IF NOT EXISTS IX_WishlistPriceWatches_GameId_Provider ON WishlistPriceWatches (GameId, Provider);
                        CREATE INDEX IF NOT EXISTS IX_WishlistPriceWatches_Provider ON WishlistPriceWatches (Provider);"
                };
                cmd.ExecuteNonQuery();
                _logger.Info($"[Database] WishlistPriceWatches table ensured ({dbType}).");
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] WishlistPriceWatches table skipped: {ex.Message}");
            }
        }

        private static void EnsureIndexesSafe(DbConnection connection, DatabaseType dbType)
        {
            if (dbType != DatabaseType.SQLite) return; // Non-SQLite indexes created by EnsureCreated
            var indexes = new[]
            {
                // Drop the old Title+PlatformId unique index - region-variants kept colliding.
                "DROP INDEX IF EXISTS IX_Games_Title_PlatformId;",
                "CREATE UNIQUE INDEX IF NOT EXISTS IX_Games_Title_PlatformId_Region ON Games (Title, PlatformId, Region);",
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

        // Column changes EnsureCreated never makes on an existing table: Collections.Color
        // was varchar(7) but holds 'var(--ctp-mauve)', and on MariaDB see the IsMySql block in
        // RetroArrDbContext. SQLite enforces neither length nor collation.
        private static void EnsureColumnShapesSafe(DbConnection connection, DatabaseType dbType)
        {
            if (dbType == DatabaseType.SQLite) return;
            var pg = dbType == DatabaseType.PostgreSQL;
            var changes = pg
                ? new (string Table, string Column, int Length, string? Collation, string Sql)[]
                {
                    ("Collections", "Color", 32, null, @"ALTER TABLE ""Collections"" ALTER COLUMN ""Color"" TYPE character varying(32)")
                }
                : new (string Table, string Column, int Length, string? Collation, string Sql)[]
                {
                    ("Collections", "Color", 32, null, "ALTER TABLE Collections MODIFY Color varchar(32) CHARACTER SET utf8mb4 NULL"),
                    ("Games", "Path", 768, "utf8mb4_bin", "ALTER TABLE Games MODIFY Path varchar(768) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL"),
                    ("Games", "Title", 255, "utf8mb4_bin", "ALTER TABLE Games MODIFY Title varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL"),
                    ("Games", "Region", 255, "utf8mb4_bin", "ALTER TABLE Games MODIFY Region varchar(255) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NULL"),
                    ("DiscoveredGames", "Path", 768, "utf8mb4_bin", "ALTER TABLE DiscoveredGames MODIFY Path varchar(768) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL"),
                    ("Tags", "Name", 50, "utf8mb4_bin", "ALTER TABLE Tags MODIFY Name varchar(50) CHARACTER SET utf8mb4 COLLATE utf8mb4_bin NOT NULL")
                };

            foreach (var change in changes)
            {
                try
                {
                    using (var cmd = connection.CreateCommand())
                    {
                        cmd.CommandText = pg
                            ? "SELECT character_maximum_length, collation_name FROM information_schema.columns WHERE table_schema = current_schema() AND table_name = @t AND column_name = @c"
                            : "SELECT CHARACTER_MAXIMUM_LENGTH, COLLATION_NAME FROM INFORMATION_SCHEMA.COLUMNS WHERE TABLE_SCHEMA = DATABASE() AND TABLE_NAME = @t AND COLUMN_NAME = @c";
                        AddParam(cmd, "@t", change.Table);
                        AddParam(cmd, "@c", change.Column);
                        using var reader = cmd.ExecuteReader();
                        if (!reader.Read()) continue;
                        var length = reader.IsDBNull(0) ? (long?)null : Convert.ToInt64(reader[0]);
                        var collation = reader.IsDBNull(1) ? null : reader.GetString(1);
                        // never narrows a column; done when length and collation are there
                        if (length == null || length > change.Length) continue;
                        if (length == change.Length && (change.Collation == null || collation == change.Collation)) continue;
                    }

                    using (var alter = connection.CreateCommand())
                    {
                        alter.CommandText = change.Sql;
                        alter.ExecuteNonQuery();
                    }
                    _logger.Info($"[Database] Changed column: {change.Table}.{change.Column}");
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Database] Column change '{change.Table}.{change.Column}' skipped: {ex.Message}");
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
                    // a bool: PostgreSQL refuses an integer for a boolean column
                    AddParam(cmd, $"{prefix}enabled", platform.Enabled);
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

        // Any Games row whose PlatformId doesn't exist in Platforms is stuck
        // below a brand-new FK. Rather than delete, route them to the review
        // gate: pin to PC (Id=1), mark NeedsMetadataReview=1, leave a reason
        // so the user sees "this needs attention" in the UI.
        private static void RescueOrphanPlatformRefs(DbConnection connection, DatabaseType dbType)
        {
            try
            {
                string selectSql = dbType == DatabaseType.PostgreSQL
                    ? @"SELECT ""Id"", ""PlatformId"" FROM ""Games"" WHERE ""PlatformId"" <= 0 OR ""PlatformId"" NOT IN (SELECT ""Id"" FROM ""Platforms"")"
                    : "SELECT Id, PlatformId FROM Games WHERE PlatformId <= 0 OR PlatformId NOT IN (SELECT Id FROM Platforms)";

                var orphans = new List<(int Id, int PlatformId)>();
                using (var cmd = connection.CreateCommand())
                {
                    cmd.CommandText = selectSql;
                    using var reader = cmd.ExecuteReader();
                    while (reader.Read())
                    {
                        orphans.Add((Convert.ToInt32(reader[0]), Convert.ToInt32(reader[1])));
                    }
                }

                if (orphans.Count == 0)
                {
                    return;
                }

                _logger.Warn($"[Database] Rescuing {orphans.Count} game row(s) with unknown PlatformId - pinning to PC and flagging for review.");

                // TRUE and @id work on all three providers
                string updateSql = dbType == DatabaseType.PostgreSQL
                    ? @"UPDATE ""Games"" SET ""PlatformId"" = 1, ""NeedsMetadataReview"" = TRUE, ""MetadataReviewReason"" = 'Original platform could not be resolved during upgrade. Please reassign.' WHERE ""Id"" = @id"
                    : "UPDATE Games SET PlatformId = 1, NeedsMetadataReview = TRUE, MetadataReviewReason = 'Original platform could not be resolved during upgrade. Please reassign.' WHERE Id = @id";

                foreach (var (id, _) in orphans)
                {
                    try
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = updateSql;
                        AddParam(cmd, "@id", id);
                        cmd.ExecuteNonQuery();
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[Database] Orphan rescue skipped game {id}: {ex.Message}");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Database] Orphan rescue failed: {ex.Message}");
            }
        }

        // SQLite treats NULL as distinct in unique indexes, so untagged rows
        // slip past (Title, PlatformId, Region). Collapse NULL to '' once so
        // the DB constraint and the controller-side duplicate check agree.
        private static void CanonicalizeNullRegions(DbConnection connection, DatabaseType dbType)
        {
            // Row by row: two untagged rows of one title would collide on the
            // unique index and fail a single UPDATE for every row.
            try
            {
                var pg = dbType == DatabaseType.PostgreSQL;
                var ids = new List<int>();
                using (var select = connection.CreateCommand())
                {
                    select.CommandText = pg
                        ? @"SELECT ""Id"" FROM ""Games"" WHERE ""Region"" IS NULL"
                        : "SELECT Id FROM Games WHERE Region IS NULL";
                    using var reader = select.ExecuteReader();
                    while (reader.Read()) ids.Add(Convert.ToInt32(reader[0]));
                }

                var affected = 0;
                foreach (var id in ids)
                {
                    try
                    {
                        using var cmd = connection.CreateCommand();
                        cmd.CommandText = pg
                            ? @"UPDATE ""Games"" SET ""Region"" = '' WHERE ""Id"" = @id"
                            : "UPDATE Games SET Region = '' WHERE Id = @id";
                        AddParam(cmd, "@id", id);
                        affected += cmd.ExecuteNonQuery();
                    }
                    catch
                    {
                        // left for Settings > Database > Repair (merge duplicates)
                    }
                }
                if (affected > 0)
                {
                    _logger.Info($"[Database] Canonicalised {affected} Region column(s) from NULL to ''.");
                }
                if (affected < ids.Count)
                {
                    _logger.Info($"[Database] {ids.Count - affected} untagged duplicate(s) left with NULL Region; run Repair with 'merge duplicates'.");
                }
            }
            catch (Exception ex)
            {
                _logger.Info($"[Database] Region canonicalisation skipped: {ex.Message}");
            }
        }

        // Earlier builds added the JSON list columns nullable, leaving NULL in every old row, and
        // games of old builds have Added = DateTime.MinValue ('-infinity' on PostgreSQL); their
        // first file is the closest date there is. Both match nothing once done.
        private static void BackfillGamesSafe(DbConnection connection, DatabaseType dbType)
        {
            var statements = dbType == DatabaseType.PostgreSQL
                ? new[]
                {
                    @"UPDATE ""Games"" SET ""Genres"" = COALESCE(""Genres"", '[]'), ""Images_Screenshots"" = COALESCE(""Images_Screenshots"", '[]'), ""Images_Artworks"" = COALESCE(""Images_Artworks"", '[]')
                      WHERE ""Genres"" IS NULL OR ""Images_Screenshots"" IS NULL OR ""Images_Artworks"" IS NULL",
                    @"UPDATE ""Games"" SET ""Added"" = (SELECT MIN(f.""DateAdded"") FROM ""GameFiles"" f WHERE f.""GameId"" = ""Games"".""Id"" AND f.""DateAdded"" >= '0001-01-02')
                      WHERE ""Added"" < '0001-01-02' AND EXISTS (SELECT 1 FROM ""GameFiles"" f WHERE f.""GameId"" = ""Games"".""Id"" AND f.""DateAdded"" >= '0001-01-02')"
                }
                : new[]
                {
                    @"UPDATE Games SET Genres = COALESCE(Genres, '[]'), Images_Screenshots = COALESCE(Images_Screenshots, '[]'), Images_Artworks = COALESCE(Images_Artworks, '[]')
                      WHERE Genres IS NULL OR Images_Screenshots IS NULL OR Images_Artworks IS NULL",
                    @"UPDATE Games SET Added = (SELECT MIN(f.DateAdded) FROM GameFiles f WHERE f.GameId = Games.Id AND f.DateAdded >= '0001-01-02')
                      WHERE Added < '0001-01-02' AND EXISTS (SELECT 1 FROM GameFiles f WHERE f.GameId = Games.Id AND f.DateAdded >= '0001-01-02')"
                };

            foreach (var sql in statements)
            {
                try
                {
                    using var cmd = connection.CreateCommand();
                    cmd.CommandText = sql;
                    var rows = cmd.ExecuteNonQuery();
                    if (rows > 0) _logger.Info($"[Database] Backfilled {rows} game row(s).");
                }
                catch (Exception ex)
                {
                    _logger.Info($"[Database] Games backfill skipped: {ex.Message}");
                }
            }
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
