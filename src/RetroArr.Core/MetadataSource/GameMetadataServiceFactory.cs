using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.ScreenScraper;
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
            
            _logger.Info($"[MetadataFactory] Refreshing Configuration. IGDB: {igdbSettings.IsConfigured}, ScreenScraper: {ssSettings.IsConfigured}");

            // Create ScreenScraper client if configured
            ScreenScraperClient? ssClient = null;
            if (ssSettings.Enabled)
            {
                ssClient = new ScreenScraperClient(_httpClient, ssSettings.Username, ssSettings.Password, ssSettings.DevId, ssSettings.DevPassword);
            }

            // ALWAYS recreate the service to ensure fresh credentials are used
            if (igdbSettings.IsConfigured)
            {
                var igdbClient = new IgdbClient(igdbSettings.ClientId, igdbSettings.ClientSecret);
                var steamClient = new SteamClient(steamSettings.ApiKey);
                _currentService = new GameMetadataService(igdbClient, steamClient, ssClient);
            }
            else
            {
                // Create a dummy client for when IGDB is not configured
                var dummyClient = new IgdbClient(string.Empty, string.Empty);
                var steamClient = new SteamClient(steamSettings.ApiKey);
                _currentService = new GameMetadataService(dummyClient, steamClient, ssClient);
            }
        }
    }
}
