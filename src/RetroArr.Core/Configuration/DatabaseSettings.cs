namespace RetroArr.Core.Configuration
{
    public enum DatabaseType
    {
        SQLite,
        PostgreSQL,
        MariaDB
    }

    public class DatabaseSettings
    {
        public DatabaseType Type { get; set; } = DatabaseType.SQLite;
        
        // SQLite (default)
        public string SqlitePath { get; set; } = "retroarr.db";
        
        // PostgreSQL / MariaDB
        public string Host { get; set; } = "localhost";
        public int Port { get; set; } = 5432; // Default PostgreSQL port, MariaDB uses 3306
        public string Database { get; set; } = "retroarr";
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        
        // Connection options
        public bool UseSsl { get; set; } = false;
        public int ConnectionTimeout { get; set; } = 30;

        public bool IsConfigured => Type == DatabaseType.SQLite || 
                                    (!string.IsNullOrEmpty(Host) && 
                                     !string.IsNullOrEmpty(Database) && 
                                     !string.IsNullOrEmpty(Username));

        public string GetConnectionString(string configPath)
        {
            // Foreign Keys=True triggers PRAGMA foreign_keys=ON on each connection.
            return Type switch
            {
                DatabaseType.SQLite => $"Data Source={System.IO.Path.Combine(configPath, SqlitePath)};Foreign Keys=True",
                DatabaseType.PostgreSQL => BuildPostgreSqlConnectionString(),
                DatabaseType.MariaDB => BuildMariaDbConnectionString(),
                _ => $"Data Source={System.IO.Path.Combine(configPath, SqlitePath)};Foreign Keys=True"
            };
        }

        private string BuildPostgreSqlConnectionString()
        {
            var builder = new Npgsql.NpgsqlConnectionStringBuilder
            {
                Host = Host,
                Port = Port > 0 ? Port : GetDefaultPort(),
                Database = Database,
                Username = Username,
                Password = Password,
                SslMode = UseSsl ? Npgsql.SslMode.Require : Npgsql.SslMode.Prefer,
                Timeout = ConnectionTimeout,
                // Shows up in pg_stat_activity / pgAdmin.
                ApplicationName = "RetroArr"
            };
            return builder.ConnectionString;
        }

        private string BuildMariaDbConnectionString()
        {
            // The builder quotes a ';' in a password. Preferred, like PostgreSQL's Prefer:
            // None breaks MySQL 8 caching_sha2_password logins on a cold auth cache.
            return new MySqlConnector.MySqlConnectionStringBuilder
            {
                Server = Host,
                Port = (uint)(Port > 0 ? Port : GetDefaultPort()),
                Database = Database,
                UserID = Username,
                Password = Password,
                SslMode = UseSsl ? MySqlConnector.MySqlSslMode.Required : MySqlConnector.MySqlSslMode.Preferred,
                ConnectionTimeout = (uint)ConnectionTimeout
            }.ConnectionString;
        }

        public int GetDefaultPort()
        {
            return Type switch
            {
                DatabaseType.PostgreSQL => 5432,
                DatabaseType.MariaDB => 3306,
                _ => 0
            };
        }
    }
}
