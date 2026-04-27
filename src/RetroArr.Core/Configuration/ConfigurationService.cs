using System;
using System.Text.Json;
using System.IO;
using RetroArr.Core.Prowlarr;
using RetroArr.Core.Jackett;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.Download;
using System.Diagnostics.CodeAnalysis;
using System.Collections.Generic;

namespace RetroArr.Core.Configuration
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    public class ConfigurationService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.Configuration);
        private readonly string _configDirectory;
        private readonly string _prowlarrConfigFile;
        private readonly string _jackettConfigFile;
        private readonly string _igdbConfigFile;
        private readonly string _downloadClientsConfigFile;
        private readonly string _mediaConfigFile;
        private readonly string _steamConfigFile;
        private readonly string _postDownloadConfigFile;
        private readonly string _hydraConfigFile;
        private readonly string _screenScraperConfigFile;
        private readonly string _gogConfigFile;
        private readonly string _databaseConfigFile;
        private readonly string _loggingConfigFile;
        private readonly string _cacheConfigFile;
        private readonly SecretProtector? _secretProtector;

        public ConfigurationService(string contentRoot) : this(contentRoot, (SecretProtector?)null) { }

        public ConfigurationService(string contentRoot, SecretProtector? secretProtector)
        {
            _secretProtector = secretProtector;
            var localConfig = Path.Combine(contentRoot, "config");
            
            if (Directory.Exists(localConfig))
            {
                _configDirectory = localConfig;
            }
            else
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                if (!string.IsNullOrEmpty(appData))
                {
                    _configDirectory = Path.Combine(appData, "RetroArr", "config");
                }
                else
                {
                    _configDirectory = localConfig;
                }
            }

            _prowlarrConfigFile = Path.Combine(_configDirectory, "prowlarr.json");
            _jackettConfigFile = Path.Combine(_configDirectory, "jackett.json");
            _igdbConfigFile = Path.Combine(_configDirectory, "igdb.json");
            _downloadClientsConfigFile = Path.Combine(_configDirectory, "downloadclients.json");
            _mediaConfigFile = Path.Combine(_configDirectory, "media.json");
            _steamConfigFile = Path.Combine(_configDirectory, "steam.json");
            _postDownloadConfigFile = Path.Combine(_configDirectory, "postdownload.json");
            _hydraConfigFile = Path.Combine(_configDirectory, "hydra.json");
            _screenScraperConfigFile = Path.Combine(_configDirectory, "screenscraper.json");
            _gogConfigFile = Path.Combine(_configDirectory, "gog.json");
            _databaseConfigFile = Path.Combine(_configDirectory, "database.json");
            _loggingConfigFile = Path.Combine(_configDirectory, "logging.json");
            _cacheConfigFile = Path.Combine(_configDirectory, "cache.json");
            
            try 
            {
                Directory.CreateDirectory(_configDirectory);
                _logger.Info($"[Configuration] Service initialized. Using Config Directory: {_configDirectory}");
            }
            catch (Exception ex)
            {
                _logger.Error($"Critical Error: Could not create config directory at {_configDirectory}. Details: {ex.Message}");
            }
        }

        public string GetConfigDirectory() => _configDirectory;

        private string Protect(string? value) => _secretProtector?.Protect(value) ?? value ?? string.Empty;
        private string Unprotect(string? value) => _secretProtector?.Unprotect(value) ?? value ?? string.Empty;

        private ProwlarrSettings UnprotectProwlarr(ProwlarrSettings s)
        {
            s.ApiKey = Unprotect(s.ApiKey);
            return s;
        }

        private JackettSettings UnprotectJackett(JackettSettings s)
        {
            s.ApiKey = Unprotect(s.ApiKey);
            return s;
        }

        private IgdbSettings UnprotectIgdb(IgdbSettings s)
        {
            s.ClientSecret = Unprotect(s.ClientSecret);
            return s;
        }

        private SteamSettings UnprotectSteam(SteamSettings s)
        {
            s.ApiKey = Unprotect(s.ApiKey);
            return s;
        }

        private ScreenScraperSettings UnprotectScreenScraper(ScreenScraperSettings s)
        {
            s.Password = Unprotect(s.Password);
            s.DevPassword = Unprotect(s.DevPassword);
            return s;
        }

        private GogSettings UnprotectGog(GogSettings s)
        {
            s.RefreshToken = Unprotect(s.RefreshToken);
            s.AccessToken = Unprotect(s.AccessToken);
            return s;
        }

        private GogOAuthSettings UnprotectGogOAuth(GogOAuthSettings s)
        {
            s.ClientSecret = Unprotect(s.ClientSecret);
            return s;
        }

        private void UnprotectDownloadClients(IList<RetroArr.Core.Download.DownloadClient> clients)
        {
            foreach (var c in clients)
            {
                if (!string.IsNullOrEmpty(c.Password)) c.Password = Unprotect(c.Password);
                if (!string.IsNullOrEmpty(c.ApiKey)) c.ApiKey = Unprotect(c.ApiKey);
            }
        }

        private void WriteEncryptedJson<T>(string path, T obj, Action<T> protectSecrets)
        {
            if (_secretProtector != null)
            {
                // Clone via round-trip serialization so we don't mutate the caller's instance.
                var raw = JsonSerializer.Serialize(obj);
                var clone = JsonSerializer.Deserialize<T>(raw)!;
                protectSecrets(clone);
                File.WriteAllText(path, JsonSerializer.Serialize(clone, new JsonSerializerOptions { WriteIndented = true }));
            }
            else
            {
                File.WriteAllText(path, JsonSerializer.Serialize(obj, new JsonSerializerOptions { WriteIndented = true }));
            }
        }

        public ProwlarrSettings LoadProwlarrSettings()
        {
            if (File.Exists(_prowlarrConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_prowlarrConfigFile);
                    var loaded = JsonSerializer.Deserialize<ProwlarrSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ProwlarrSettings { Url = string.Empty };
                    return UnprotectProwlarr(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading Prowlarr settings: {ex.Message}"); }
            }
            return new ProwlarrSettings { Url = string.Empty };
        }

        public void SaveProwlarrSettings(ProwlarrSettings settings)
        {
            try { WriteEncryptedJson(_prowlarrConfigFile, settings, s => s.ApiKey = Protect(s.ApiKey)); }
            catch (Exception ex) { _logger.Error($"Error saving Prowlarr settings: {ex.Message}"); }
        }

        public JackettSettings LoadJackettSettings()
        {
            if (File.Exists(_jackettConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_jackettConfigFile);
                    var loaded = JsonSerializer.Deserialize<JackettSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new JackettSettings { Url = string.Empty };
                    return UnprotectJackett(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading Jackett settings: {ex.Message}"); }
            }
            return new JackettSettings { Url = string.Empty };
        }

        public void SaveJackettSettings(JackettSettings settings)
        {
            try { WriteEncryptedJson(_jackettConfigFile, settings, s => s.ApiKey = Protect(s.ApiKey)); }
            catch (Exception ex) { _logger.Error($"Error saving Jackett settings: {ex.Message}"); }
        }

        public IgdbSettings LoadIgdbSettings()
        {
            if (File.Exists(_igdbConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_igdbConfigFile);
                    var loaded = JsonSerializer.Deserialize<IgdbSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new IgdbSettings();
                    return UnprotectIgdb(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading IGDB settings: {ex.Message}"); }
            }
            return new IgdbSettings { ClientId = Environment.GetEnvironmentVariable("IGDB_CLIENT_ID") ?? "", ClientSecret = Environment.GetEnvironmentVariable("IGDB_CLIENT_SECRET") ?? "" };
        }

        public void SaveIgdbSettings(IgdbSettings settings)
        {
            try { WriteEncryptedJson(_igdbConfigFile, settings, s => s.ClientSecret = Protect(s.ClientSecret)); }
            catch (Exception ex) { _logger.Error($"Error saving IGDB settings: {ex.Message}"); }
        }

        public List<RetroArr.Core.Download.DownloadClient> LoadDownloadClients()
        {
            if (File.Exists(_downloadClientsConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_downloadClientsConfigFile);
                    var loaded = JsonSerializer.Deserialize<List<RetroArr.Core.Download.DownloadClient>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<RetroArr.Core.Download.DownloadClient>();
                    UnprotectDownloadClients(loaded);
                    return loaded;
                }
                catch (Exception ex) { _logger.Error($"Error loading download clients: {ex.Message}"); }
            }
            return new List<RetroArr.Core.Download.DownloadClient>();
        }

        public void SaveDownloadClients(List<RetroArr.Core.Download.DownloadClient> clients)
        {
            try
            {
                WriteEncryptedJson(_downloadClientsConfigFile, clients, list =>
                {
                    foreach (var c in list)
                    {
                        if (!string.IsNullOrEmpty(c.Password)) c.Password = Protect(c.Password);
                        if (!string.IsNullOrEmpty(c.ApiKey)) c.ApiKey = Protect(c.ApiKey);
                    }
                });
            }
            catch (Exception ex) { _logger.Error($"Error saving download clients: {ex.Message}"); }
        }

        public MediaSettings LoadMediaSettings()
        {
            MediaSettings settings = new MediaSettings();
            if (File.Exists(_mediaConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_mediaConfigFile);
                    settings = JsonSerializer.Deserialize<MediaSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new MediaSettings();
                }
                catch (Exception ex) { _logger.Error($"Error loading media settings: {ex.Message}"); }
            }

            // Apply Defaults if paths are empty
            var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            var documents = Path.Combine(userProfile, "Documents");
            var downloads = Path.Combine(userProfile, "Downloads");

            // Ensure base processing folder exists in Downloads
            var defaultDownloadPath = Path.Combine(downloads, "RetroArr");
            
            // Ensure Library folder exists in Documents
            var defaultLibraryPath = Path.Combine(documents, "RetroArr", "Library");
            var defaultGamesPath = Path.Combine(documents, "RetroArr", "Games");

            if (string.IsNullOrWhiteSpace(settings.DownloadPath)) settings.DownloadPath = defaultDownloadPath;
            if (string.IsNullOrWhiteSpace(settings.DestinationPath)) settings.DestinationPath = defaultLibraryPath;
            if (string.IsNullOrWhiteSpace(settings.FolderPath)) settings.FolderPath = defaultGamesPath;
            if (string.IsNullOrWhiteSpace(settings.BiosPath)) settings.BiosPath = Path.Combine(_configDirectory, "bios");
            if (string.IsNullOrWhiteSpace(settings.TrashPath)) settings.TrashPath = Path.Combine(_configDirectory, "trash");


            // Create directories if they don't exist (UX convenience)
            try 
            {
                if (!Directory.Exists(settings.DownloadPath)) Directory.CreateDirectory(settings.DownloadPath);
                if (!Directory.Exists(settings.DestinationPath)) Directory.CreateDirectory(settings.DestinationPath);
                if (!Directory.Exists(settings.FolderPath)) Directory.CreateDirectory(settings.FolderPath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Config] Warning: Could not create default directories: {ex.Message}");
            }

            return settings;
        }

        public void SaveMediaSettings(MediaSettings settings)
        {
            try { File.WriteAllText(_mediaConfigFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { _logger.Error($"Error saving media settings: {ex.Message}"); }
        }

        public SteamSettings LoadSteamSettings()
        {
            if (File.Exists(_steamConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_steamConfigFile);
                    var loaded = JsonSerializer.Deserialize<SteamSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SteamSettings();
                    return UnprotectSteam(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading Steam settings: {ex.Message}"); }
            }
            return new SteamSettings();
        }

        public void SaveSteamSettings(SteamSettings settings)
        {
            try { WriteEncryptedJson(_steamConfigFile, settings, s => s.ApiKey = Protect(s.ApiKey)); }
            catch (Exception ex) { _logger.Error($"Error saving Steam settings: {ex.Message}"); }
        }

        public PostDownloadSettings LoadPostDownloadSettings()
        {
            if (File.Exists(_postDownloadConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_postDownloadConfigFile);
                    return JsonSerializer.Deserialize<PostDownloadSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new PostDownloadSettings();
                }
                catch { }
            }
            return new PostDownloadSettings();
        }

        public void SavePostDownloadSettings(PostDownloadSettings settings)
        {
            try { File.WriteAllText(_postDownloadConfigFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); }
            catch { }
        }

        public List<RetroArr.Core.Indexers.HydraConfiguration> LoadHydraIndexers()
        {
            if (File.Exists(_hydraConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_hydraConfigFile);
                    return JsonSerializer.Deserialize<List<RetroArr.Core.Indexers.HydraConfiguration>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new List<RetroArr.Core.Indexers.HydraConfiguration>();
                }
                catch (Exception ex) { _logger.Error($"Error loading Hydra indexers: {ex.Message}"); }
            }
            return new List<RetroArr.Core.Indexers.HydraConfiguration>();
        }

        public void SaveHydraIndexers(List<RetroArr.Core.Indexers.HydraConfiguration> indexers)
        {
            try { File.WriteAllText(_hydraConfigFile, JsonSerializer.Serialize(indexers, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { _logger.Error($"Error saving Hydra indexers: {ex.Message}"); }
        }

        public ScreenScraperSettings LoadScreenScraperSettings()
        {
            ScreenScraperSettings settings;
            if (File.Exists(_screenScraperConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_screenScraperConfigFile);
                    settings = JsonSerializer.Deserialize<ScreenScraperSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ScreenScraperSettings();
                    UnprotectScreenScraper(settings);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error loading ScreenScraper settings: {ex.Message}");
                    settings = new ScreenScraperSettings();
                }
            }
            else
            {
                settings = new ScreenScraperSettings();
            }

            // Environment variables override config file values (user credentials)
            var envUser = Environment.GetEnvironmentVariable("SCREENSCRAPER_USER");
            var envPass = Environment.GetEnvironmentVariable("SCREENSCRAPER_PASSWORD");
            if (!string.IsNullOrEmpty(envUser)) settings.Username = envUser;
            if (!string.IsNullOrEmpty(envPass)) settings.Password = envPass;

            // Dev credentials from env vars (app-level, not exposed in UI)
            var envDevId = Environment.GetEnvironmentVariable("SCREENSCRAPER_DEVID");
            var envDevPass = Environment.GetEnvironmentVariable("SCREENSCRAPER_DEVPASSWORD");
            if (!string.IsNullOrEmpty(envDevId)) settings.DevId = envDevId;
            if (!string.IsNullOrEmpty(envDevPass)) settings.DevPassword = envDevPass;

            return settings;
        }

        public void SaveScreenScraperSettings(ScreenScraperSettings settings)
        {
            try
            {
                WriteEncryptedJson(_screenScraperConfigFile, settings, s =>
                {
                    s.Password = Protect(s.Password);
                    s.DevPassword = Protect(s.DevPassword);
                });
            }
            catch (Exception ex) { _logger.Error($"Error saving ScreenScraper settings: {ex.Message}"); }
        }

        public GogSettings LoadGogSettings()
        {
            if (File.Exists(_gogConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_gogConfigFile);
                    var loaded = JsonSerializer.Deserialize<GogSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GogSettings();
                    return UnprotectGog(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading GOG settings: {ex.Message}"); }
            }
            return new GogSettings();
        }

        public void SaveGogSettings(GogSettings settings)
        {
            try
            {
                WriteEncryptedJson(_gogConfigFile, settings, s =>
                {
                    s.RefreshToken = Protect(s.RefreshToken);
                    s.AccessToken = Protect(s.AccessToken);
                });
            }
            catch (Exception ex) { _logger.Error($"Error saving GOG settings: {ex.Message}"); }
        }

        public GogOAuthSettings LoadGogOAuthSettings()
        {
            // GOG OAuth settings (client ID/secret) - for advanced users
            var oauthFile = Path.Combine(_configDirectory, "gog_oauth.json");
            if (File.Exists(oauthFile))
            {
                try
                {
                    var json = File.ReadAllText(oauthFile);
                    return JsonSerializer.Deserialize<GogOAuthSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new GogOAuthSettings();
                }
                catch { }
            }
            return new GogOAuthSettings();
        }

        public DatabaseSettings LoadDatabaseSettings()
        {
            if (File.Exists(_databaseConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_databaseConfigFile);
                    var loaded = JsonSerializer.Deserialize<DatabaseSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new DatabaseSettings();
                    loaded.Password = Unprotect(loaded.Password);
                    return loaded;
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error loading database settings: {ex.Message}");
                }
            }
            return new DatabaseSettings();
        }

        public void SaveDatabaseSettings(DatabaseSettings settings)
        {
            try
            {
                WriteEncryptedJson(_databaseConfigFile, settings, s => s.Password = Protect(s.Password));
                _logger.Info($"[Configuration] Database settings saved. Type: {settings.Type}");
            }
            catch (Exception ex) { _logger.Error($"Error saving database settings: {ex.Message}"); }
        }

        public CacheSettings LoadCacheSettings()
        {
            if (File.Exists(_cacheConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_cacheConfigFile);
                    return JsonSerializer.Deserialize<CacheSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new CacheSettings();
                }
                catch (Exception ex) { _logger.Error($"Error loading cache settings: {ex.Message}"); }
            }
            return new CacheSettings();
        }

        public void SaveCacheSettings(CacheSettings settings)
        {
            try
            {
                File.WriteAllText(_cacheConfigFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                _logger.Info($"[Configuration] Cache settings saved. Enabled: {settings.Enabled}");
            }
            catch (Exception ex) { _logger.Error($"Error saving cache settings: {ex.Message}"); }
        }

        private static readonly List<string> DefaultRedactHeaders = new() { "Authorization", "Cookie", "X-Api-Key" };

        public LoggingSettings LoadLoggingSettings()
        {
            if (File.Exists(_loggingConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_loggingConfigFile);
                    return JsonSerializer.Deserialize<LoggingSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? CreateDefaultLoggingSettings();
                }
                catch (Exception ex) { _logger.Error($"Error loading logging settings: {ex.Message}"); }
            }
            return CreateDefaultLoggingSettings();
        }

        private static LoggingSettings CreateDefaultLoggingSettings()
        {
            return new LoggingSettings { RedactHeaders = new List<string>(DefaultRedactHeaders) };
        }

        public void SaveLoggingSettings(LoggingSettings settings)
        {
            try
            {
                File.WriteAllText(_loggingConfigFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch (Exception ex) { _logger.Error($"Error saving logging settings: {ex.Message}"); }
        }

        public string GetDefaultLogDirectory()
        {
            var configLogs = Path.Combine(_configDirectory, "logs");
            if (Directory.Exists(_configDirectory))
                return configLogs;

            if (OperatingSystem.IsWindows())
            {
                var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                return Path.Combine(appData, "RetroArr", "logs");
            }
            if (OperatingSystem.IsMacOS())
            {
                var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
                return Path.Combine(home, "Library", "Application Support", "RetroArr", "logs");
            }
            // Linux / fallback
            var homeDir = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            return Path.Combine(homeDir, ".local", "share", "RetroArr", "logs");
        }

        public string GetEffectiveLogDirectory()
        {
            var settings = LoadLoggingSettings();
            return string.IsNullOrWhiteSpace(settings.LogDirectory) ? GetDefaultLogDirectory() : settings.LogDirectory;
        }
    }

    public class IgdbSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId) && !string.IsNullOrWhiteSpace(ClientSecret);
    }

    public class SteamSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public string SteamId { get; set; } = string.Empty;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey) && !string.IsNullOrWhiteSpace(SteamId);
    }

    public class PostDownloadSettings
    {
        public bool EnableAutoMove { get; set; } = true;
        public bool EnableAutoExtract { get; set; } = true;
        public bool EnableDeepClean { get; set; } = true;
        public int MonitorIntervalSeconds { get; set; } = 60;
        public List<string> UnwantedExtensions { get; set; } = new List<string> { ".txt", ".nfo", ".url" };
    }

    public class ScreenScraperSettings
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string DevId { get; set; } = string.Empty;
        public string DevPassword { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Username) && !string.IsNullOrWhiteSpace(Password);
    }

    public class GogSettings
    {
        public string? RefreshToken { get; set; }
        public string? AccessToken { get; set; }
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(RefreshToken) || !string.IsNullOrWhiteSpace(AccessToken);
    }

    public class GogOAuthSettings
    {
        public string ClientId { get; set; } = string.Empty;
        public string ClientSecret { get; set; } = string.Empty;
    }

    public class LoggingSettings
    {
        public bool Enabled { get; set; } = true;
        public string LogDirectory { get; set; } = string.Empty;
        public string LogLevel { get; set; } = "Info";
        public bool PerFeatureFiles { get; set; } = true;
        public int MaxDays { get; set; } = 14;
        public int MaxTotalSizeMb { get; set; } = 500;
        public int RotateSizeMb { get; set; } = 50;
        public bool RedactTokens { get; set; } = true;
        public List<string> RedactHeaders { get; set; } = new();
    }
}
