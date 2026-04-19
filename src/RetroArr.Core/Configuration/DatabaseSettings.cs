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
            // Microsoft.Data.Sqlite respects "Foreign Keys=True" and runs
            // PRAGMA foreign_keys=ON on every opened connection. Without
            // this, the FK declared on Games.PlatformId is never enforced.
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
                Port = Port,
                Database = Database,
                Username = Username,
                Password = Password,
                SslMode = UseSsl ? Npgsql.SslMode.Require : Npgsql.SslMode.Prefer,
                Timeout = ConnectionTimeout
            };
            return builder.ConnectionString;
        }

        private string BuildMariaDbConnectionString()
        {
            var sslMode = UseSsl ? "Required" : "None";
            return $"Server={Host};Port={Port};Database={Database};User={Username};Password={Password};SslMode={sslMode};ConnectionTimeout={ConnectionTimeout}";
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
