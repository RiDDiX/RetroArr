using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Prowlarr;
using RetroArr.Core.Configuration;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.ScreenScraper;
using RetroArr.Core.MetadataSource.Gog;
using RetroArr.Core.Jackett;
using RetroArr.Core.MetadataSource.Steam;
using RetroArr.Core.Games;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Api.V3.Settings
{
    [ApiController]
    [Route("api/v3/settings")]
    [Route("api/v3/metadata/igdb")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    public class SettingsController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.Configuration);
        private readonly ProwlarrSettings _prowlarrSettings;
        private readonly JackettSettings _jackettSettings;
        private readonly ConfigurationService _configService;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly SteamClient _steamClient;
        private readonly IGameRepository _gameRepository;
        private readonly GogDownloadTracker _gogDownloadTracker;

        public SettingsController(
            ProwlarrSettings prowlarrSettings, 
            JackettSettings jackettSettings, 
            ConfigurationService configService, 
            IGameMetadataServiceFactory metadataServiceFactory,
            SteamClient steamClient,
            IGameRepository gameRepository,
            GogDownloadTracker gogDownloadTracker)
        {
            _prowlarrSettings = prowlarrSettings;
            _jackettSettings = jackettSettings;
            _configService = configService;
            _metadataServiceFactory = metadataServiceFactory;
            _steamClient = steamClient;
            _gameRepository = gameRepository;
            _gogDownloadTracker = gogDownloadTracker;
        }

        [HttpPost("prowlarr")]
        public IActionResult SaveProwlarrSettings([FromBody] ProwlarrSettings request)
        {
            // Update the injected singleton so other services see the change immediately
            _prowlarrSettings.Url = request.Url;
            _prowlarrSettings.ApiKey = request.ApiKey;
            _prowlarrSettings.Enabled = request.Enabled;
            
            _logger.Info($"[Settings] Saving Prowlarr Settings. ENABLED = {request.Enabled}");

            // Save to persistent storage
            _configService.SaveProwlarrSettings(request);

            return Ok(new { success = true });
        }

        [HttpGet("prowlarr")]
        public ActionResult<ProwlarrSettings> GetProwlarrSettings()
        {
            // Load directly from disk to ensure persistence is verified
            return Ok(_configService.LoadProwlarrSettings());
        }

        [HttpPost("jackett")]
        public IActionResult SaveJackettSettings([FromBody] JackettSettings request)
        {
            // Update the injected singleton so other services see the change immediately
            _jackettSettings.Url = request.Url;
            _jackettSettings.ApiKey = request.ApiKey;
            _jackettSettings.Enabled = request.Enabled;
            
            _logger.Info($"[Settings] Saving Jackett Settings. ENABLED = {request.Enabled}");

            // Save to persistent storage
            _configService.SaveJackettSettings(request);

            return Ok(new { success = true });
        }

        [HttpGet("jackett")]
        public ActionResult<JackettSettings> GetJackettSettings()
        {
            // Load directly from disk to ensure persistence is verified
            return Ok(_configService.LoadJackettSettings());
        }

        [HttpPost("/api/v3/metadata/igdb")]
        public IActionResult SaveIgdbSettings([FromBody] IgdbSettings request)
        {
            // Save to persistent storage
            _configService.SaveIgdbSettings(request);
            
            // Refresh the IGDB service with new configuration
            _metadataServiceFactory.RefreshConfiguration();

            return Ok(new { success = true, message = "IGDB settings saved and configuration refreshed." });
        }

        [HttpGet("igdb")]
        public ActionResult<IgdbSettings> GetIgdbSettings()
        {
            var settings = _configService.LoadIgdbSettings();
            return Ok(settings);
        }

        [HttpPost("steam")]
        public IActionResult SaveSteamSettings([FromBody] SteamSettings request)
        {
            // Save to persistent storage
            _configService.SaveSteamSettings(request);
            
            return Ok(new { success = true, message = "Steam settings saved." });
        }

        [HttpGet("steam")]
        public ActionResult<SteamSettings> GetSteamSettings()
        {
            var settings = _configService.LoadSteamSettings();
            return Ok(settings);
        }

        [HttpDelete("steam")]
        public async Task<IActionResult> DeleteSteamSettings()
        {
            var emptySettings = new SteamSettings { ApiKey = "", SteamId = "" };
            _configService.SaveSteamSettings(emptySettings);
            
            var deletedCount = await _gameRepository.DeleteSteamGamesAsync();
            return Ok(new { success = true, message = $"Steam settings cleared and {deletedCount} games removed." });
        }

        [HttpDelete("igdb")]
        public IActionResult DeleteIgdbSettings()
        {
            var emptySettings = new IgdbSettings { ClientId = "", ClientSecret = "" };
            _configService.SaveIgdbSettings(emptySettings);
            _metadataServiceFactory.RefreshConfiguration();
            return Ok(new { success = true, message = "IGDB settings cleared." });
        }

        [HttpPost("steam/test")]
        public async Task<IActionResult> TestSteamSettings([FromBody] SteamSettings request)
        {
            _configService.SaveSteamSettings(request);

            try
            {
                // Create a temporary client with the provided credentials to verify them
                var tempClient = new SteamClient(request.ApiKey);
                var profile = await tempClient.GetPlayerProfileAsync(request.SteamId);
                
                if (profile == null)
                {
                    return BadRequest(new { success = false, message = "Connection failed: Invalid API Key or Steam ID. Profile could not be retrieved." });
                }

                var userName = profile.PersonaName;
                return Ok(new { success = true, message = $"Connected as {userName}", userName = userName });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("steam/sync")]
        public async Task<IActionResult> SyncSteamLibrary()
        {
            try
            {
                var settings = _configService.LoadSteamSettings();
                if (!settings.IsConfigured)
                    return BadRequest(new { success = false, message = "Steam not configured" });

                // Use a fresh client with the correct key (avoid DI stale key issues)
                var client = new SteamClient(settings.ApiKey);
                var steamGames = await client.GetOwnedGamesAsync(settings.SteamId);
                var existingGames = await _gameRepository.GetAllAsync();
                
                int addedCount = 0;
                var metadataService = _metadataServiceFactory.CreateService();

                foreach (var steamGame in steamGames)
                {
                    var existingGame = existingGames.FirstOrDefault(g => g.SteamId == steamGame.AppId || 
                                                (g.Title.Equals(steamGame.Name, StringComparison.OrdinalIgnoreCase)));

                    if (existingGame != null)
                    {
                        // Update existing game if it doesn't have the SteamId
                        if (!existingGame.SteamId.HasValue || existingGame.SteamId != steamGame.AppId)
                        {
                            existingGame.SteamId = steamGame.AppId;
                            // Optionally update other fields if needed, but for now just link it
                            await _gameRepository.UpdateAsync(existingGame.Id, existingGame);
                            _logger.Info($"[SteamSync] Linked '{existingGame.Title}' to Steam AppID: {steamGame.AppId}");
                        }
                    }
                    else
                    {
                        var newGame = new Game
                        {
                            Title = steamGame.Name,
                            SteamId = steamGame.AppId,
                            Added = DateTime.UtcNow,
                            Status = GameStatus.Announced, 
                            Monitored = true,
                            PlatformId = 125, // Steam platform
                            IsExternal = true // Mark as external launcher game
                        };

                        // Enrich with IGDB Metadata
                        try 
                        {
                            var searchResults = await metadataService.SearchGamesAsync(steamGame.Name);
                            var match = searchResults.FirstOrDefault();
                            
                            if (match != null)
                            {
                                newGame.IgdbId = match.IgdbId;
                                newGame.Overview = match.Overview;
                                newGame.Images = match.Images;
                                newGame.Genres = match.Genres;
                                newGame.Developer = match.Developer;
                                newGame.Publisher = match.Publisher;
                                newGame.ReleaseDate = match.ReleaseDate;
                                newGame.Year = match.Year;
                                newGame.Rating = match.Rating;
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore enrichment failures
                        }

                        await _gameRepository.AddAsync(newGame);
                        addedCount++;
                    }
                }

                return Ok(new { success = true, message = $"Synced {steamGames.Count} games. Added {addedCount} new games.", count = addedCount });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("screenscraper")]
        public ActionResult<ScreenScraperSettings> GetScreenScraperSettings()
        {
            var settings = _configService.LoadScreenScraperSettings();
            return Ok(settings);
        }

        [HttpPost("screenscraper")]
        public IActionResult SaveScreenScraperSettings([FromBody] ScreenScraperSettings request)
        {
            _configService.SaveScreenScraperSettings(request);
            _logger.Info($"[Settings] Saving ScreenScraper Settings. ENABLED = {request.Enabled}");
            return Ok(new { success = true, message = "ScreenScraper settings saved." });
        }

        [HttpPost("screenscraper/test")]
        public async Task<IActionResult> TestScreenScraperSettings([FromBody] ScreenScraperSettings request)
        {
            try
            {
                var existingSettings = _configService.LoadScreenScraperSettings();
                var devId = !string.IsNullOrWhiteSpace(request.DevId) ? request.DevId : existingSettings.DevId;
                var devPassword = !string.IsNullOrWhiteSpace(request.DevPassword) ? request.DevPassword : existingSettings.DevPassword;

                using var httpClient = new System.Net.Http.HttpClient();
                var client = new RetroArr.Core.MetadataSource.ScreenScraper.ScreenScraperClient(
                    httpClient, request.Username, request.Password, devId, devPassword);
                
                var results = await client.SearchGamesByNameAsync("Super Mario World", 4); // SNES system ID
                
                if (results.Count > 0)
                {
                    return Ok(new { success = true, message = $"Connection successful! Found: {results[0].GetName()}" });
                }
                else
                {
                    return Ok(new { success = true, message = "Connection successful (API responded, no results for test query)" });
                }
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, error = $"Connection failed: {ex.Message}" });
            }
        }

        [HttpDelete("screenscraper")]
        public IActionResult DeleteScreenScraperSettings()
        {
            var emptySettings = new ScreenScraperSettings { Username = "", Password = "", Enabled = false };
            _configService.SaveScreenScraperSettings(emptySettings);
            return Ok(new { success = true, message = "ScreenScraper settings cleared." });
        }

        // ==================== GOG Settings ====================

        [HttpGet("gog")]
        public ActionResult<GogSettings> GetGogSettings()
        {
            var settings = _configService.LoadGogSettings();
            return Ok(settings);
        }

        [HttpPost("gog")]
        public IActionResult SaveGogSettings([FromBody] GogSettings request)
        {
            _configService.SaveGogSettings(request);
            return Ok(new { success = true, message = "GOG settings saved." });
        }

        [HttpDelete("gog")]
        public async Task<IActionResult> DeleteGogSettings()
        {
            var emptySettings = new GogSettings { RefreshToken = null, AccessToken = null };
            _configService.SaveGogSettings(emptySettings);
            
            var deletedCount = await _gameRepository.DeleteGogGamesAsync();
            return Ok(new { success = true, message = $"GOG settings cleared and {deletedCount} games removed." });
        }

        [HttpPost("gog/sync")]
        public async Task<IActionResult> SyncGogLibrary()
        {
            try
            {
                var settings = _configService.LoadGogSettings();
                if (!settings.IsConfigured)
                    return BadRequest(new { success = false, message = "GOG not configured" });

                var client = new GogClient(settings.RefreshToken);
                
                // Set access token if available
                if (!string.IsNullOrEmpty(settings.AccessToken))
                {
                    client.SetAccessToken(settings.AccessToken, 3600);
                }

                var gogGames = await client.GetOwnedGamesAsync();
                var existingGames = await _gameRepository.GetAllAsync();
                
                int addedCount = 0;
                var metadataService = _metadataServiceFactory.CreateService();

                foreach (var gogGame in gogGames.Where(g => g.IsGame))
                {
                    var existingGame = existingGames.FirstOrDefault(g => 
                        g.GogId == gogGame.Id.ToString() || 
                        g.Title.Equals(gogGame.Title, StringComparison.OrdinalIgnoreCase));

                    if (existingGame != null)
                    {
                        // Update existing game if it doesn't have the GogId
                        if (string.IsNullOrEmpty(existingGame.GogId) || existingGame.GogId != gogGame.Id.ToString())
                        {
                            existingGame.GogId = gogGame.Id.ToString();
                            await _gameRepository.UpdateAsync(existingGame.Id, existingGame);
                            _logger.Info($"[GogSync] Linked '{existingGame.Title}' to GOG ID: {gogGame.Id}");
                        }
                    }
                    else
                    {
                        var newGame = new Game
                        {
                            Title = gogGame.Title,
                            GogId = gogGame.Id.ToString(),
                            Added = DateTime.UtcNow,
                            Status = GameStatus.Announced,
                            Monitored = true,
                            PlatformId = 126, // GOG platform
                            IsExternal = true,
                            Images = new GameImages
                            {
                                CoverUrl = gogGame.GetCoverUrl(),
                                BackgroundUrl = gogGame.GetBackgroundUrl()
                            }
                        };

                        // Enrich with IGDB Metadata
                        try 
                        {
                            var searchResults = await metadataService.SearchGamesAsync(gogGame.Title);
                            var match = searchResults.FirstOrDefault();
                            
                            if (match != null)
                            {
                                newGame.IgdbId = match.IgdbId;
                                newGame.Overview = match.Overview;
                                if (match.Images?.CoverUrl != null)
                                    newGame.Images.CoverUrl = match.Images.CoverUrl;
                                if (match.Images?.BackgroundUrl != null)
                                    newGame.Images.BackgroundUrl = match.Images.BackgroundUrl;
                                newGame.Genres = match.Genres;
                                newGame.Developer = match.Developer;
                                newGame.Publisher = match.Publisher;
                                newGame.ReleaseDate = match.ReleaseDate;
                                newGame.Year = match.Year;
                                newGame.Rating = match.Rating;
                            }
                        }
                        catch (Exception)
                        {
                            // Ignore enrichment failures
                        }

                        await _gameRepository.AddAsync(newGame);
                        addedCount++;
                    }
                }

                return Ok(new { success = true, message = $"Synced {gogGames.Count} games. Added {addedCount} new games.", count = addedCount });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpGet("gog/downloads/{gogId}")]
        public async Task<IActionResult> GetGogGameDownloads(string gogId)
        {
            try
            {
                var settings = _configService.LoadGogSettings();
                if (!settings.IsConfigured || string.IsNullOrEmpty(settings.RefreshToken))
                    return BadRequest(new { success = false, message = "GOG not configured. Please authenticate in Settings -> GOG." });

                _logger.Info($"[GOG] Fetching downloads for game ID: {gogId}");
                
                // Create client with refresh token
                var client = new GogClient(settings.RefreshToken);
                
                // Always refresh the access token before API calls (tokens expire quickly)
                // Use the same credentials as GogController
                const string GogClientId = "46899977096215655";
                const string GogClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
                
                _logger.Info("[GOG] Refreshing access token...");
                var refreshed = await client.RefreshTokenAsync(GogClientId, GogClientSecret);
                if (!refreshed)
                {
                    _logger.Error("[GOG] Failed to refresh access token");
                    return BadRequest(new { success = false, message = "Failed to authenticate with GOG. Please re-authenticate in Settings -> GOG." });
                }
                _logger.Info("[GOG] Access token refreshed successfully");

                var downloads = await client.GetGameDownloadsAsync(gogId);
                _logger.Info($"[GOG] Found {downloads.Count} downloads for game ID: {gogId}");
                
                return Ok(new { success = true, downloads });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] Error fetching downloads: {ex.Message}");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        [HttpPost("gog/download-url")]
        public async Task<IActionResult> GetGogDownloadUrl([FromBody] GogDownloadRequest request)
        {
            try
            {
                var settings = _configService.LoadGogSettings();
                if (!settings.IsConfigured)
                    return BadRequest(new { success = false, message = "GOG not configured" });

                var client = new GogClient(settings.RefreshToken);
                if (!string.IsNullOrEmpty(settings.AccessToken))
                {
                    client.SetAccessToken(settings.AccessToken, 3600);
                }

                var downloadUrl = await client.GetDownloadUrlAsync(request.ManualUrl);
                if (string.IsNullOrEmpty(downloadUrl))
                    return BadRequest(new { success = false, message = "Failed to get download URL" });

                return Ok(new { success = true, downloadUrl });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Start downloading a GOG installer file to the configured GOG downloads folder
        /// Creates: {Library}/gog/downloads/{GameTitle}/{FileName}
        /// </summary>
        [HttpPost("gog/download-file")]
        public async Task<IActionResult> DownloadGogFile([FromBody] GogDownloadFileRequest request)
        {
            try
            {
                var gogSettings = _configService.LoadGogSettings();
                if (!gogSettings.IsConfigured)
                    return BadRequest(new { success = false, message = "GOG not configured" });

                var mediaSettings = _configService.LoadMediaSettings();
                if (!mediaSettings.IsConfigured)
                    return BadRequest(new { success = false, message = "Media folder not configured" });

                // Resolve GOG download path: {Library}/gog/downloads/{GameTitle}
                var downloadPath = mediaSettings.ResolveGogDownloadPath(request.GameTitle);
                if (string.IsNullOrEmpty(downloadPath))
                    return BadRequest(new { success = false, message = "Could not resolve download path" });

                // Create directory if it doesn't exist
                if (!Directory.Exists(downloadPath))
                {
                    Directory.CreateDirectory(downloadPath);
                    _logger.Info($"[GOG] Created download directory: {downloadPath}");
                }

                // Get actual download URL — always refresh the token first
                const string GogClientId = "46899977096215655";
                const string GogClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";
                var client = new GogClient(gogSettings.RefreshToken);
                var refreshed = await client.RefreshTokenAsync(GogClientId, GogClientSecret);
                if (!refreshed)
                    return BadRequest(new { success = false, message = "Failed to authenticate with GOG. Please re-authenticate in Settings." });

                var downloadUrl = await client.GetDownloadUrlAsync(request.ManualUrl);
                if (string.IsNullOrEmpty(downloadUrl))
                    return BadRequest(new { success = false, message = "Failed to get download URL" });

                // Resolve filename: use display name from frontend, but ensure it has a file extension
                // GOG CDN URLs contain the real filename with extension (e.g. setup_dungeons_2_1.0.exe)
                var fileName = request.FileName;
                var cdnFileName = "";
                try
                {
                    var uri = new Uri(downloadUrl);
                    cdnFileName = System.IO.Path.GetFileName(uri.LocalPath);
                }
                catch { /* ignore malformed URL */ }

                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = !string.IsNullOrEmpty(cdnFileName) ? cdnFileName : $"{request.GameTitle}_setup.exe";
                }
                else if (!System.IO.Path.HasExtension(fileName) && !string.IsNullOrEmpty(cdnFileName) && System.IO.Path.HasExtension(cdnFileName))
                {
                    // Display name has no extension — append extension from CDN URL
                    var ext = System.IO.Path.GetExtension(cdnFileName);
                    fileName = fileName + ext;
                    _logger.Info($"[GOG] Appended extension from CDN: {ext} -> {fileName}");
                }

                var filePath = System.IO.Path.Combine(downloadPath, fileName);

                _logger.Info($"[GOG] Starting download: {fileName} -> {filePath}");

                var trackId = Guid.NewGuid().ToString("N")[..8];
                var tracker = _gogDownloadTracker;

                // Start tracked download in background with cancellation support
                _ = Task.Run(async () =>
                {
                    System.Threading.CancellationToken ct = default;
                    try
                    {
                        using var httpClient = new System.Net.Http.HttpClient();
                        httpClient.Timeout = TimeSpan.FromHours(2);
                        
                        using var response = await httpClient.GetAsync(downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        // Try to get real filename from Content-Disposition header
                        var cdHeader = response.Content.Headers.ContentDisposition?.FileName?.Trim('"', ' ');
                        if (!string.IsNullOrEmpty(cdHeader) && !System.IO.Path.HasExtension(fileName) && System.IO.Path.HasExtension(cdHeader))
                        {
                            var ext = System.IO.Path.GetExtension(cdHeader);
                            fileName = fileName + ext;
                            filePath = System.IO.Path.Combine(downloadPath, fileName);
                            _logger.Info($"[GOG] Content-Disposition extension: {ext} -> {fileName}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength;
                        ct = tracker.Start(trackId, request.GameTitle, fileName, filePath, totalBytes);

                        using var contentStream = await response.Content.ReadAsStreamAsync();
                        using var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None);
                        var buffer = new byte[81920];
                        long totalRead = 0;
                        int bytesRead;
                        while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                        {
                            await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                            totalRead += bytesRead;
                            tracker.UpdateProgress(trackId, totalRead);
                        }

                        tracker.MarkCompleted(trackId);
                        _logger.Info($"[GOG] Download complete: {filePath} ({totalRead} bytes)");
                    }
                    catch (OperationCanceledException)
                    {
                        tracker.MarkFailed(trackId, "Download cancelled");
                        _logger.Info($"[GOG] Download cancelled: {filePath}");
                        try { if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); } catch { }
                    }
                    catch (Exception ex)
                    {
                        tracker.MarkFailed(trackId, ex.Message);
                        _logger.Error($"[GOG] Download failed: {ex.Message}");
                        try { if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); } catch { }
                    }
                });

                return Ok(new { 
                    success = true, 
                    message = $"Download started: {fileName}",
                    downloadPath = filePath,
                    folder = downloadPath,
                    trackId
                });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Get the configured GOG downloads path
        /// </summary>
        [HttpGet("gog/downloads-path")]
        public IActionResult GetGogDownloadsPath([FromQuery] string? gameTitle = null)
        {
            var mediaSettings = _configService.LoadMediaSettings();
            var path = mediaSettings.ResolveGogDownloadPath(gameTitle);
            return Ok(new { path, configured = !string.IsNullOrEmpty(path) });
        }

        /// <summary>
        /// Get status of all active/completed GOG downloads
        /// </summary>
        [HttpGet("gog/download-status")]
        public IActionResult GetGogDownloadStatus()
        {
            var downloads = _gogDownloadTracker.GetAll();
            return Ok(downloads.Select(d => new
            {
                d.Id,
                d.GameTitle,
                d.FileName,
                d.FilePath,
                d.TotalBytes,
                d.BytesDownloaded,
                d.ProgressPercent,
                State = d.State.ToString(),
                d.ErrorMessage,
                d.StartedAt,
                d.CompletedAt
            }));
        }

        /// <summary>
        /// Remove a completed/failed GOG download from the tracker
        /// </summary>
        [HttpDelete("gog/download-status/{trackId}")]
        public IActionResult RemoveGogDownloadStatus(string trackId)
        {
            _gogDownloadTracker.Remove(trackId);
            return Ok(new { message = "Removed" });
        }
    }

    public class GogDownloadRequest
    {
        public string ManualUrl { get; set; } = string.Empty;
    }

    public class GogDownloadFileRequest
    {
        public string ManualUrl { get; set; } = string.Empty;
        public string GameTitle { get; set; } = string.Empty;
        public string? FileName { get; set; }
    }
}
