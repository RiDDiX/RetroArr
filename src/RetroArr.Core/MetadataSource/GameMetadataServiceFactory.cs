using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.ScreenScraper;
using RetroArr.Core.MetadataSource.TheGamesDb;
using RetroArr.Core.MetadataSource.SteamGridDb;
using RetroArr.Core.MetadataSource.Epic;
using RetroArr.Core.Configuration;
using RetroArr.Core.MetadataSource.Steam;
using System.Net.Http;

namespace RetroArr.Core.MetadataSource
{
    public interface IGameMetadataServiceFactory
    {
        GameMetadataService CreateService();
        void RefreshConfiguration();
    }

    public class GameMetadataServiceFactory : IGameMetadataServiceFactory
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly ConfigurationService _configService;
        private readonly HttpClient _httpClient;
        private GameMetadataService? _currentService;

        public GameMetadataServiceFactory(ConfigurationService configService)
        {
            _configService = configService;
            _httpClient = new HttpClient();
        }

        public GameMetadataService CreateService()
        {
            if (_currentService == null)
            {
                RefreshConfiguration();
            }
            return _currentService!;
        }

        public void RefreshConfiguration()
        {
            var igdbSettings = _configService.LoadIgdbSettings();
            var steamSettings = _configService.LoadSteamSettings();
            var ssSettings = _configService.LoadScreenScraperSettings();
            var tgdbSettings = _configService.LoadTheGamesDbSettings();
            var sgdbSettings = _configService.LoadSteamGridDbSettings();
            var epicMetaSettings = _configService.LoadEpicMetadataSettings();

            _logger.Info($"[MetadataFactory] Refreshing Configuration. IGDB: {igdbSettings.IsConfigured}, ScreenScraper: {ssSettings.IsConfigured}, TheGamesDB: {tgdbSettings.IsConfigured}, SteamGridDB: {sgdbSettings.IsConfigured}, EpicStore: {epicMetaSettings.Enabled}");

            ScreenScraperClient? ssClient = null;
            if (ssSettings.Enabled)
            {
                ssClient = new ScreenScraperClient(_httpClient, ssSettings.Username, ssSettings.Password, ssSettings.DevId, ssSettings.DevPassword);
            }

            TheGamesDbClient? tgdbClient = null;
            if (tgdbSettings.Enabled && tgdbSettings.IsConfigured)
            {
                tgdbClient = new TheGamesDbClient(_httpClient, tgdbSettings.ApiKey);
            }

            SteamGridDbClient? sgdbClient = null;
            if (sgdbSettings.Enabled && sgdbSettings.IsConfigured)
            {
                sgdbClient = new SteamGridDbClient(_httpClient, sgdbSettings.ApiKey);
            }

            EpicMetadataClient? epicMetaClient = null;
            if (epicMetaSettings.Enabled)
            {
                epicMetaClient = new EpicMetadataClient(_httpClient);
            }

            // Recreate on every call so credential edits take effect right away
            if (igdbSettings.IsConfigured)
            {
                var igdbClient = new IgdbClient(igdbSettings.ClientId, igdbSettings.ClientSecret);
                var steamClient = new SteamClient(steamSettings.ApiKey);
                _currentService = new GameMetadataService(igdbClient, steamClient, ssClient, tgdbClient, sgdbClient, epicMetaClient);
            }
            else
            {
                var dummyClient = new IgdbClient(string.Empty, string.Empty);
                var steamClient = new SteamClient(steamSettings.ApiKey);
                _currentService = new GameMetadataService(dummyClient, steamClient, ssClient, tgdbClient, sgdbClient, epicMetaClient);
            }
        }
    }
}
