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
        private readonly string _lancacheConfigFile;
        private readonly string _postDownloadConfigFile;
        private readonly string _hydraConfigFile;
        private readonly string _screenScraperConfigFile;
        private readonly string _theGamesDbConfigFile;
        private readonly string _steamGridDbConfigFile;
        private readonly string _gogConfigFile;
        private readonly string _epicConfigFile;
        private readonly string _epicMetadataConfigFile;
        private readonly string _databaseConfigFile;
        private readonly string _loggingConfigFile;
        private readonly string _cacheConfigFile;
        private readonly string _proxyConfigFile;
        private readonly string _monitorConfigFile;
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
            _lancacheConfigFile = Path.Combine(_configDirectory, "lancache.json");
            _postDownloadConfigFile = Path.Combine(_configDirectory, "postdownload.json");
            _hydraConfigFile = Path.Combine(_configDirectory, "hydra.json");
            _screenScraperConfigFile = Path.Combine(_configDirectory, "screenscraper.json");
            _theGamesDbConfigFile = Path.Combine(_configDirectory, "thegamesdb.json");
            _steamGridDbConfigFile = Path.Combine(_configDirectory, "steamgriddb.json");
            _gogConfigFile = Path.Combine(_configDirectory, "gog.json");
            _epicConfigFile = Path.Combine(_configDirectory, "epic.json");
            _epicMetadataConfigFile = Path.Combine(_configDirectory, "epic_metadata.json");
            _databaseConfigFile = Path.Combine(_configDirectory, "database.json");
            _loggingConfigFile = Path.Combine(_configDirectory, "logging.json");
            _cacheConfigFile = Path.Combine(_configDirectory, "cache.json");
            _proxyConfigFile = Path.Combine(_configDirectory, "proxy.json");
            _monitorConfigFile = Path.Combine(_configDirectory, "monitor.json");

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

        private TheGamesDbSettings UnprotectTheGamesDb(TheGamesDbSettings s)
        {
            s.ApiKey = Unprotect(s.ApiKey);
            return s;
        }

        private SteamGridDbSettings UnprotectSteamGridDb(SteamGridDbSettings s)
        {
            s.ApiKey = Unprotect(s.ApiKey);
            return s;
        }

        private GogSettings UnprotectGog(GogSettings s)
        {
            s.RefreshToken = Unprotect(s.RefreshToken);
            s.AccessToken = Unprotect(s.AccessToken);
            return s;
        }

        private EpicSettings UnprotectEpic(EpicSettings s)
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

        private ProxySettings UnprotectProxy(ProxySettings s)
        {
            s.Password = Unprotect(s.Password);
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

        public LanCacheSettings LoadLanCacheSettings()
        {
            if (File.Exists(_lancacheConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_lancacheConfigFile);
                    return JsonSerializer.Deserialize<LanCacheSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new LanCacheSettings();
                }
                catch (Exception ex) { _logger.Error($"Error loading LanCache settings: {ex.Message}"); }
            }
            return new LanCacheSettings();
        }

        public void SaveLanCacheSettings(LanCacheSettings settings)
        {
            try
            {
                var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_lancacheConfigFile, json);
            }
            catch (Exception ex) { _logger.Error($"Error saving LanCache settings: {ex.Message}"); }
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

        public TheGamesDbSettings LoadTheGamesDbSettings()
        {
            TheGamesDbSettings settings;
            if (File.Exists(_theGamesDbConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_theGamesDbConfigFile);
                    settings = JsonSerializer.Deserialize<TheGamesDbSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new TheGamesDbSettings();
                    UnprotectTheGamesDb(settings);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error loading TheGamesDB settings: {ex.Message}");
                    settings = new TheGamesDbSettings();
                }
            }
            else
            {
                settings = new TheGamesDbSettings();
            }

            var envKey = Environment.GetEnvironmentVariable("THEGAMESDB_APIKEY");
            if (!string.IsNullOrEmpty(envKey)) settings.ApiKey = envKey;

            return settings;
        }

        public void SaveTheGamesDbSettings(TheGamesDbSettings settings)
        {
            try { WriteEncryptedJson(_theGamesDbConfigFile, settings, s => s.ApiKey = Protect(s.ApiKey)); }
            catch (Exception ex) { _logger.Error($"Error saving TheGamesDB settings: {ex.Message}"); }
        }

        public SteamGridDbSettings LoadSteamGridDbSettings()
        {
            SteamGridDbSettings settings;
            if (File.Exists(_steamGridDbConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_steamGridDbConfigFile);
                    settings = JsonSerializer.Deserialize<SteamGridDbSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new SteamGridDbSettings();
                    UnprotectSteamGridDb(settings);
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error loading SteamGridDB settings: {ex.Message}");
                    settings = new SteamGridDbSettings();
                }
            }
            else
            {
                settings = new SteamGridDbSettings();
            }

            var envKey = Environment.GetEnvironmentVariable("STEAMGRIDDB_APIKEY");
            if (!string.IsNullOrEmpty(envKey)) settings.ApiKey = envKey;

            return settings;
        }

        public void SaveSteamGridDbSettings(SteamGridDbSettings settings)
        {
            try { WriteEncryptedJson(_steamGridDbConfigFile, settings, s => s.ApiKey = Protect(s.ApiKey)); }
            catch (Exception ex) { _logger.Error($"Error saving SteamGridDB settings: {ex.Message}"); }
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

        public EpicSettings LoadEpicSettings()
        {
            if (File.Exists(_epicConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_epicConfigFile);
                    var loaded = JsonSerializer.Deserialize<EpicSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new EpicSettings();
                    return UnprotectEpic(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading Epic settings: {ex.Message}"); }
            }
            return new EpicSettings();
        }

        public void SaveEpicSettings(EpicSettings settings)
        {
            try
            {
                WriteEncryptedJson(_epicConfigFile, settings, s =>
                {
                    s.RefreshToken = Protect(s.RefreshToken);
                    s.AccessToken = Protect(s.AccessToken);
                });
            }
            catch (Exception ex) { _logger.Error($"Error saving Epic settings: {ex.Message}"); }
        }

        public EpicMetadataSettings LoadEpicMetadataSettings()
        {
            if (File.Exists(_epicMetadataConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_epicMetadataConfigFile);
                    return JsonSerializer.Deserialize<EpicMetadataSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new EpicMetadataSettings();
                }
                catch (Exception ex) { _logger.Error($"Error loading Epic metadata settings: {ex.Message}"); }
            }
            return new EpicMetadataSettings();
        }

        public void SaveEpicMetadataSettings(EpicMetadataSettings settings)
        {
            try { File.WriteAllText(_epicMetadataConfigFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true })); }
            catch (Exception ex) { _logger.Error($"Error saving Epic metadata settings: {ex.Message}"); }
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

        public ProxySettings LoadProxySettings()
        {
            if (File.Exists(_proxyConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_proxyConfigFile);
                    var loaded = JsonSerializer.Deserialize<ProxySettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new ProxySettings();
                    return UnprotectProxy(loaded);
                }
                catch (Exception ex) { _logger.Error($"Error loading proxy settings: {ex.Message}"); }
            }
            return new ProxySettings();
        }

        public void SaveProxySettings(ProxySettings settings)
        {
            try
            {
                WriteEncryptedJson(_proxyConfigFile, settings, s => s.Password = Protect(s.Password));
                _logger.Info($"[Configuration] Proxy settings saved. Enabled: {settings.Enabled}, Type: {settings.Type}");
            }
            catch (Exception ex) { _logger.Error($"Error saving proxy settings: {ex.Message}"); }
        }

        public MonitorSettings LoadMonitorSettings()
        {
            if (File.Exists(_monitorConfigFile))
            {
                try
                {
                    var json = File.ReadAllText(_monitorConfigFile);
                    var loaded = JsonSerializer.Deserialize<MonitorSettings>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? MonitorSettings.CreateDefault();
                    // Older config files written before the append-bug fix may
                    // hold the same preset twice. Squash duplicates on read so
                    // affected installs heal on next boot without manual edit.
                    loaded.VerifiedSources = DedupeCaseInsensitive(loaded.VerifiedSources);
                    loaded.TrustedReleaseGroups = DedupeCaseInsensitive(loaded.TrustedReleaseGroups);
                    loaded.HackPatchTokens = DedupeCaseInsensitive(loaded.HackPatchTokens);
                    return loaded;
                }
                catch (Exception ex) { _logger.Error($"Error loading monitor settings: {ex.Message}"); }
            }
            return MonitorSettings.CreateDefault();
        }

        private static List<string> DedupeCaseInsensitive(List<string> values)
        {
            if (values == null || values.Count == 0) return new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var result = new List<string>(values.Count);
            foreach (var v in values)
            {
                if (string.IsNullOrWhiteSpace(v)) continue;
                var trimmed = v.Trim();
                if (seen.Add(trimmed)) result.Add(trimmed);
            }
            return result;
        }

        public void SaveMonitorSettings(MonitorSettings settings)
        {
            try
            {
                // Belt-and-braces: also dedupe on write so a UI that submits
                // duplicates can't poison the file.
                settings.VerifiedSources = DedupeCaseInsensitive(settings.VerifiedSources);
                settings.TrustedReleaseGroups = DedupeCaseInsensitive(settings.TrustedReleaseGroups);
                settings.HackPatchTokens = DedupeCaseInsensitive(settings.HackPatchTokens);
                File.WriteAllText(_monitorConfigFile, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
                _logger.Info($"[Configuration] Monitor settings saved. Enabled={settings.Enabled} autoThreshold={settings.AutoDownloadThreshold}");
            }
            catch (Exception ex) { _logger.Error($"Error saving monitor settings: {ex.Message}"); }
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

    // LanCache (https://lancache.net) integration. Host is the cache server's
    // IP/DNS; the SPA lets the user point RetroArr at it so status can be checked
    // and, in phase 2, a SteamPrefill run can warm the cache.
    public class LanCacheSettings
    {
        public bool Enabled { get; set; }
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 80;
        // Phase 2 (SteamPrefill orchestration) toggles.
        // Default OFF: prefill only the apps the user selected (via the Web-GUI
        // picker or `select-apps`). Turning this on passes --all and prefills the
        // entire owned library, ignoring that selection.
        public bool PrefillAllOwned { get; set; }

        // Per-provider schedules (steam / battlenet / epic). A provider runs when
        // Enabled and the local time-of-day HH:mm is reached on a matching day.
        // Days: empty/null = every day; otherwise 0=Sunday .. 6=Saturday.
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        public Dictionary<string, PrefillSchedule> Schedules { get; set; } = new();
        public bool PrefillRecent { get; set; }

        // Pass --force on a manual run: re-download every selected app instead of only
        // what is new or missing. A deliberate reseed/benchmark knob, off by default -
        // the tools skip up-to-date apps entirely without it.
        public bool PrefillForceManual { get; set; }

        // After a run that skipped apps (a dropped Steam session usually takes the
        // whole tail of a long run with it), retry those once. The retry is a plain
        // incremental pass, so it only touches what is still missing.
        public bool PrefillRetryFailed { get; set; } = true;
        public string PrefillOs { get; set; } = "windows"; // windows/linux/macos, comma-separated
        public bool IsConfigured => !string.IsNullOrWhiteSpace(Host);
    }

    // One provider's prefill schedule. Time is local server time, 24h "HH:mm".
    public class PrefillSchedule
    {
        public bool Enabled { get; set; }
        // Time window (local server time, 24h "HH:mm"). The prefill starts at
        // StartTime; if EndTime is set and it is still running when that time is
        // reached it is stopped, so providers can be staggered (Steam 00:00-04:00,
        // Epic 04:00-06:00, …). EndTime before StartTime means the window wraps
        // past midnight. Empty EndTime = no forced stop.
        public string StartTime { get; set; } = "04:00";
        public string? EndTime { get; set; }
        [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
        [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
        public List<int> Days { get; set; } = new();  // empty = every day

        // Back-compat: earlier builds stored a single "Time". Deserializing it
        // still fills StartTime.
        public string Time { set { if (!string.IsNullOrWhiteSpace(value)) StartTime = value; } }
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

    public class TheGamesDbSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }

    public class SteamGridDbSettings
    {
        public string ApiKey { get; set; } = string.Empty;
        public bool Enabled { get; set; } = true;
        public bool IsConfigured => !string.IsNullOrWhiteSpace(ApiKey);
    }

    public class GogSettings
    {
        public string? RefreshToken { get; set; }
        public string? AccessToken { get; set; }
        public string? UserId { get; set; }
        public string? Username { get; set; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(RefreshToken) || !string.IsNullOrWhiteSpace(AccessToken);
    }

    public class EpicSettings
    {
        public string? RefreshToken { get; set; }
        public string? AccessToken { get; set; }
        public string? AccountId { get; set; }
        public string? DisplayName { get; set; }
        public bool IsConfigured => !string.IsNullOrWhiteSpace(RefreshToken);
    }

    public class EpicMetadataSettings
    {
        public bool Enabled { get; set; } = true;
        public string Locale { get; set; } = "en-US";
        public string Country { get; set; } = "US";
        public bool IsConfigured => Enabled;
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

    public class ProxySettings
    {
        public bool Enabled { get; set; }
        public string Type { get; set; } = "http";
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 8080;
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public bool BypassLocal { get; set; } = true;
        public List<string> BypassList { get; set; } = new();
        public bool IsConfigured => Enabled && !string.IsNullOrWhiteSpace(Host);
    }
}
