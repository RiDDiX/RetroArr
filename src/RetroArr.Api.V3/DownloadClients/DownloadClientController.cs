using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Download;
using RetroArr.Core.Download.TrackedDownloads;
using RetroArr.Core.Configuration;
using RetroArr.Core.Download.History;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Api.V3.DownloadClients
{
    [ApiController]
    [Route("api/v3/downloadclient")]
    [SuppressMessage("Microsoft.Performance", "CA1860:AvoidUsingAnyWhenUseCount")]
    [SuppressMessage("Microsoft.Maintainability", "CA1508:AvoidDeadConditionalCode")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class DownloadClientController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.DownloadClient);
        private readonly List<DownloadClient> _clients;
        private readonly ConfigurationService _configService;
        private readonly ImportStatusService _importStatus;
        private readonly DownloadPlatformTracker _platformTracker;
        private readonly TrackedDownloadService _trackedDownloadService;
        private readonly CompletedDownloadService _completedDownloadService;
        private readonly DownloadHistoryRepository _historyRepo;
        private readonly DownloadBlacklistRepository _blacklistRepo;
        private readonly RetroArr.Core.Games.IGameRepository _gameRepository;

        public DownloadClientController(
            ConfigurationService configService,
            ImportStatusService importStatus,
            DownloadPlatformTracker platformTracker,
            TrackedDownloadService trackedDownloadService,
            CompletedDownloadService completedDownloadService,
            DownloadHistoryRepository historyRepo,
            DownloadBlacklistRepository blacklistRepo,
            RetroArr.Core.Games.IGameRepository gameRepository)
        {
            _configService = configService;
            _importStatus = importStatus;
            _platformTracker = platformTracker;
            _trackedDownloadService = trackedDownloadService;
            _completedDownloadService = completedDownloadService;
            _historyRepo = historyRepo;
            _blacklistRepo = blacklistRepo;
            _gameRepository = gameRepository;
            _clients = _configService.LoadDownloadClients();
        }

        [HttpGet]
        public ActionResult<List<DownloadClient>> GetAll()
        {
            return Ok(_clients);
        }

        [HttpGet("{id}")]
        public ActionResult<DownloadClient> GetById(int id)
        {
            var client = _clients.FirstOrDefault(c => c.Id == id);
            if (client == null)
            {
                return NotFound();
            }
            return Ok(client);
        }

        [HttpPost]
        public ActionResult<DownloadClient> Create([FromBody] DownloadClient client)
        {
            client.Id = _clients.Any() ? _clients.Max(c => c.Id) + 1 : 1;
            _clients.Add(client);
            _configService.SaveDownloadClients(_clients);
            return CreatedAtAction(nameof(GetById), new { id = client.Id }, client);
        }

        [HttpPut("{id}")]
        public ActionResult<DownloadClient> Update(int id, [FromBody] DownloadClient client)
        {
            var existingClient = _clients.FirstOrDefault(c => c.Id == id);
            if (existingClient == null)
            {
                return NotFound();
            }

            existingClient.Name = client.Name;
            existingClient.Implementation = client.Implementation;
            existingClient.Host = client.Host;
            existingClient.Port = client.Port;
            existingClient.Username = client.Username;
            existingClient.Password = client.Password;
            existingClient.Category = client.Category;
            existingClient.UrlBase = client.UrlBase;
            existingClient.ApiKey = client.ApiKey;
            existingClient.Enable = client.Enable;
            existingClient.Priority = client.Priority;
            existingClient.RemotePathMapping = client.RemotePathMapping;
            existingClient.LocalPathMapping = client.LocalPathMapping;

            _configService.SaveDownloadClients(_clients);

            return Ok(existingClient);
        }

        [HttpDelete("{id}")]
        public ActionResult Delete(int id)
        {
            var client = _clients.FirstOrDefault(c => c.Id == id);
            if (client == null)
            {
                return NotFound();
            }

            _clients.Remove(client);
            _configService.SaveDownloadClients(_clients);
            return NoContent();
        }

        [HttpGet("queue")]
        public async Task<ActionResult<List<DownloadStatus>>> GetQueue()
        {
            var allDownloads = new List<DownloadStatus>();
            // Pre-load games for GameId→Title resolution
            List<RetroArr.Core.Games.Game>? allGames = null;

            foreach (var config in _clients.Where(c => c.Enable))
            {
                try
                {
                    IDownloadClient? client = null;
                    if (config.Implementation.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
                    {
                        client = new QBittorrentClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
                    }
                    else if (config.Implementation.Equals("Transmission", StringComparison.OrdinalIgnoreCase))
                    {
                        client = new TransmissionClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "");
                    }
                    else if (config.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
                    {
                        client = new SabnzbdClient(config.Host, config.Port, config.ApiKey ?? "", config.UrlBase);
                    }
                    else if (config.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                    {
                        client = new NzbgetClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
                    }
                    else if (config.Implementation.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
                    {
                        client = new DelugeClient(config.Host, config.Port, config.Password ?? "", config.UseSsl);
                    }

                    if (client != null)
                    {
                        var downloads = await client.GetDownloadsAsync();
                        foreach (var d in downloads) 
                        {
                            d.ClientId = config.Id;
                            d.ClientName = config.Name;
                            d.PlatformFolder = _platformTracker.LookupByName(d.Name);
                            d.GameId = _platformTracker.LookupGameId(d.Name);
                            if (_importStatus.IsImporting(d.Id))
                            {
                                d.State = DownloadState.Importing;
                            }

                            // Enrich with tracked download state
                            var tracked = _trackedDownloadService.Find(d.Id);
                            if (tracked != null)
                            {
                                d.TrackedState = tracked.State.ToString();
                                d.StatusMessages = tracked.StatusMessages;
                                if (tracked.GameId.HasValue && !d.GameId.HasValue)
                                    d.GameId = tracked.GameId;
                            }

                            // Resolve game title from DB if GameId is set
                            if (d.GameId.HasValue)
                            {
                                allGames ??= await _gameRepository.GetAllAsync();
                                var game = allGames.FirstOrDefault(g => g.Id == d.GameId.Value);
                                if (game != null)
                                {
                                    d.GameTitle = game.Title;
                                    // Also set platform from game if not already detected
                                    if (string.IsNullOrEmpty(d.PlatformFolder) && game.PlatformId > 0)
                                    {
                                        var platform = RetroArr.Core.Games.PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                                        if (platform != null) d.PlatformFolder = platform.FolderName;
                                    }
                                }
                            }
                        }

                        // Filter by configured category so only RetroArr-managed downloads are shown
                        if (!string.IsNullOrEmpty(config.Category))
                        {
                            downloads = downloads.Where(d =>
                                !string.IsNullOrEmpty(d.Category) &&
                                d.Category.Equals(config.Category, StringComparison.OrdinalIgnoreCase)
                            ).ToList();
                        }

                        allDownloads.AddRange(downloads);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"Error fetching downloads for client {config.Name}: {ex.Message}");
                }
            }

            return Ok(allDownloads);
        }

        [HttpDelete("queue/{clientId}/{downloadId}")]
        public async Task<ActionResult> DeleteDownload(int clientId, string downloadId)
        {
            var config = _clients.FirstOrDefault(c => c.Id == clientId);
            if (config == null) return NotFound("Client not found");

            IDownloadClient? client = null;
            if (config.Implementation.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
            {
                client = new QBittorrentClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
            }
            else if (config.Implementation.Equals("Transmission", StringComparison.OrdinalIgnoreCase))
            {
                client = new TransmissionClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "");
            }
            else if (config.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
            {
                client = new SabnzbdClient(config.Host, config.Port, config.ApiKey ?? "", config.UrlBase);
            }
            else if (config.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
            {
                client = new NzbgetClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
            }
            else if (config.Implementation.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
                client = new DelugeClient(config.Host, config.Port, config.Password ?? "", config.UseSsl);

            if (client == null) return BadRequest("Unsupported client implementation");

            try 
            {
                // Decode URL encoded ID (especially for SABnzbd/Transmission which might have funky chars, although unlikely for IDs)
                var decodedId = Uri.UnescapeDataString(downloadId);
                var result = await client.RemoveDownloadAsync(decodedId);
                if (result) return Ok();
                return BadRequest("Failed to delete download from client.");
            }
            catch (Exception ex)
            {
                return StatusCode(500, $"Error deleting download: {ex.Message}");
            }
        }

        [HttpPost("queue/{clientId}/{downloadId}/pause")]
        public async Task<ActionResult> PauseDownload(int clientId, string downloadId)
        {
            var result = await HandleDownloadAction(clientId, downloadId, (client, id) => client.PauseDownloadAsync(id));
            if (result) return Ok();
            return BadRequest("Failed to pause download.");
        }

        [HttpPost("queue/{clientId}/{downloadId}/resume")]
        public async Task<ActionResult> ResumeDownload(int clientId, string downloadId)
        {
            var result = await HandleDownloadAction(clientId, downloadId, (client, id) => client.ResumeDownloadAsync(id));
            if (result) return Ok();
            return BadRequest("Failed to resume download.");
        }

        [HttpPost("queue/map-platform")]
        public ActionResult MapPlatform([FromBody] MapPlatformRequest request)
        {
            if (string.IsNullOrEmpty(request.DownloadName) || string.IsNullOrEmpty(request.PlatformFolder))
                return BadRequest("DownloadName and PlatformFolder are required.");

            _platformTracker.SetPlatformForDownload(request.DownloadName, request.PlatformFolder, request.GameId, request.ImportSubfolder);
            _logger.Info($"[DownloadClient] Manual platform mapping: '{request.DownloadName}' -> '{request.PlatformFolder}' (gameId={request.GameId}, subfolder={request.ImportSubfolder})");
            return Ok(new { message = $"Platform mapped to {request.PlatformFolder}" });
        }

        [HttpPost("queue/{clientId}/{downloadId}/import")]
        public async Task<ActionResult> ManualImport(int clientId, string downloadId)
        {
            var config = _clients.FirstOrDefault(c => c.Id == clientId);
            if (config == null) return NotFound("Client not found");

            IDownloadClient? client = CreateClient(config);
            if (client == null) return BadRequest("Unsupported client implementation");

            try
            {
                var downloads = await client.GetDownloadsAsync();
                var decodedId = Uri.UnescapeDataString(downloadId);
                var download = downloads.FirstOrDefault(d => d.Id == decodedId);
                if (download == null) return NotFound($"Download '{decodedId}' not found in client");

                // Resolve platform, gameId, importSubfolder from tracker
                download.PlatformFolder = _platformTracker.LookupByName(download.Name);
                var trackedGameId = _platformTracker.LookupGameId(download.Name);
                var trackedSubfolder = _platformTracker.LookupImportSubfolder(download.Name);

                // Track via TrackedDownloadService (creates or updates)
                var tracked = _trackedDownloadService.TrackDownload(download, clientId, config.Name ?? config.Implementation);

                // Propagate platform, gameId, importSubfolder
                if (!string.IsNullOrEmpty(download.PlatformFolder))
                    tracked.PlatformFolder = download.PlatformFolder;
                if (trackedGameId.HasValue)
                    tracked.GameId = trackedGameId;
                if (!string.IsNullOrEmpty(trackedSubfolder))
                    tracked.ImportSubfolder = trackedSubfolder;

                // Force state to ImportPending for manual import
                tracked.ClearWarnings();
                tracked.State = TrackedDownloadState.ImportPending;

                // Phase 1: Check (validates path, applies remote mapping, blacklist, unmapped)
                await _completedDownloadService.CheckAsync(tracked, config);

                // If Check blocked import, return the reason
                if (tracked.State == TrackedDownloadState.ImportBlocked)
                {
                    var reason = tracked.StatusMessages.Count > 0 ? string.Join("; ", tracked.StatusMessages) : "Import blocked (unknown reason)";
                    return BadRequest(new { message = reason });
                }

                // Phase 2: Import
                _importStatus.MarkImporting(tracked.DownloadId);
                try
                {
                    await _completedDownloadService.ImportAsync(tracked);
                }
                finally
                {
                    _importStatus.MarkFinished(tracked.DownloadId);
                }

                if (tracked.State == TrackedDownloadState.Imported)
                {
                    return Ok(new { message = $"Imported: {tracked.Title}" });
                }
                else
                {
                    var reason = tracked.StatusMessages.Count > 0 ? string.Join("; ", tracked.StatusMessages) : "Import did not finish";
                    return BadRequest(new { message = reason });
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[DownloadClient] Manual import error: {ex.Message}");
                return StatusCode(500, new { message = $"Import failed: {ex.Message}" });
            }
        }

        private IDownloadClient? CreateClient(DownloadClient config)
        {
            if (config.Implementation.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
                return new QBittorrentClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
            if (config.Implementation.Equals("Transmission", StringComparison.OrdinalIgnoreCase))
                return new TransmissionClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "");
            if (config.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
                return new SabnzbdClient(config.Host, config.Port, config.ApiKey ?? "", config.UrlBase);
            if (config.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                return new NzbgetClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
            if (config.Implementation.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
                return new DelugeClient(config.Host, config.Port, config.Password ?? "", config.UseSsl);
            return null;
        }

        private async Task<bool> HandleDownloadAction(int clientId, string downloadId, Func<IDownloadClient, string, Task<bool>> action)
        {
            var config = _clients.FirstOrDefault(c => c.Id == clientId);
            if (config == null) return false;

            IDownloadClient? client = null;
            if (config.Implementation.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
                client = new QBittorrentClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
            else if (config.Implementation.Equals("Transmission", StringComparison.OrdinalIgnoreCase))
                client = new TransmissionClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "");
            else if (config.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
                client = new SabnzbdClient(config.Host, config.Port, config.ApiKey ?? "", config.UrlBase);
            else if (config.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                client = new NzbgetClient(config.Host, config.Port, config.Username ?? "", config.Password ?? "", config.UrlBase);
            else if (config.Implementation.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
                client = new DelugeClient(config.Host, config.Port, config.Password ?? "", config.UseSsl);

            if (client == null) return false;

            try 
            {
                var decodedId = Uri.UnescapeDataString(downloadId);
                return await action(client, decodedId);
            }
            catch { return false; }
        }

        [HttpPost("test")]
        public async Task<ActionResult> TestConnection([FromBody] TestDownloadClientRequest request)
        {
            try
            {
                bool isConnected = false;
                string version = string.Empty;

                _logger.Info($"[DownloadClient] Testing {request.Implementation} at {request.Host}:{request.Port}");

                if (request.Implementation.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
                {
                    var qbClient = new QBittorrentClient(
                        request.Host,
                        request.Port,
                        request.Username ?? string.Empty,
                        request.Password ?? string.Empty,
                        request.UrlBase
                    );

                    isConnected = await qbClient.TestConnectionAsync();
                    if (isConnected)
                    {
                        version = await qbClient.GetVersionAsync();
                    }
                }
                else if (request.Implementation.Equals("Transmission", StringComparison.OrdinalIgnoreCase))
                {
                    var transmissionClient = new TransmissionClient(
                        request.Host,
                        request.Port,
                        request.Username ?? string.Empty,
                        request.Password ?? string.Empty
                    );

                    isConnected = await transmissionClient.TestConnectionAsync();
                    if (isConnected)
                    {
                        version = await transmissionClient.GetVersionAsync();
                    }
                }
                else if (request.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
                {
                    var sabClient = new SabnzbdClient(
                        request.Host,
                        request.Port,
                        request.ApiKey ?? string.Empty,
                        request.UrlBase
                    );

                    isConnected = await sabClient.TestConnectionAsync();
                    if (isConnected)
                    {
                        version = await sabClient.GetVersionAsync();
                    }
                }
                else if (request.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                {
                    var nzbClient = new NzbgetClient(
                        request.Host,
                        request.Port,
                        request.Username ?? string.Empty,
                        request.Password ?? string.Empty,
                        request.UrlBase
                    );

                    if (isConnected)
                    {
                        version = await nzbClient.GetVersionAsync();
                    }
                }
                else if (request.Implementation.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
                {
                    var delugeClient = new DelugeClient(
                        request.Host,
                        request.Port,
                        request.Password ?? string.Empty,
                        request.UseSsl
                    );

                    isConnected = await delugeClient.TestConnectionAsync();
                    if (isConnected)
                    {
                        version = await delugeClient.GetVersionAsync();
                    }
                }
                else
                {
                    return BadRequest(new { message = $"Unsupported download client: {request.Implementation}" });
                }

                return Ok(new
                {
                    connected = isConnected,
                    version = version,
                    message = isConnected ? "Connection successful" : "Connection failed"
                });
            }
            catch (Exception ex)
            {
                return Ok(new
                {
                    connected = false,
                    message = $"Connection failed: {ex.Message}"
                });
            }
        }

        [HttpPost("add")]
        public async Task<ActionResult> AddTorrent([FromBody] AddTorrentRequest request)
        {
            try
            {
                _logger.Info($"[DownloadClient] Attempting to add torrent: {request.Url} (Platform: {request.PlatformFolder ?? "unset"}, GameId: {request.GameId?.ToString() ?? "none"})");
                
                // Track platform folder, game ID and patch flag for post-download processing
                if (!string.IsNullOrEmpty(request.PlatformFolder) || request.GameId.HasValue)
                {
                    _platformTracker.Track(request.Url, request.PlatformFolder, request.GameId, request.ImportSubfolder);
                }
                
                DownloadClient? client = null;
                
                // Smart Selection based on Protocol (Passed from Frontend) or URL extension
                bool isNzb = false;
                
                if (!string.IsNullOrEmpty(request.Protocol))
                {
                    isNzb = request.Protocol.Equals("nzb", StringComparison.OrdinalIgnoreCase)
                         || request.Protocol.Equals("usenet", StringComparison.OrdinalIgnoreCase);
                }
                else
                {
                    // Fallback to URL check
                    isNzb = request.Url.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase);
                }
                
                _logger.Info($"[DownloadClient] Request Protocol: '{request.Protocol}', IsNZB: {isNzb}");
                
                if (isNzb)
                {
                    // Prioritize Usenet clients
                     client = _clients
                        .Where(c => c.Enable && (c.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase) || c.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase)))
                        .OrderBy(c => c.Priority).ThenBy(c => c.Id)
                        .FirstOrDefault();
                }
                else
                {
                    // Prioritize Torrent clients (default)
                     client = _clients
                        .Where(c => c.Enable && !c.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase) && !c.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                        .OrderBy(c => c.Priority).ThenBy(c => c.Id)
                        .FirstOrDefault();
                }

                if (client == null)
                {
                    _logger.Info($"[DownloadClient] No enabled download client found for {(isNzb ? "NZB" : "Torrent")}");
                    return BadRequest(new { message = $"No enabled {(isNzb ? "Usenet" : "Torrent")} download client found." });
                }
                
                if (client.Implementation.Equals("qBittorrent", StringComparison.OrdinalIgnoreCase))
                {
                    var qbClient = new QBittorrentClient(
                        client.Host,
                        client.Port,
                        client.Username ?? string.Empty,
                        client.Password ?? string.Empty,
                        client.UrlBase
                    );

                    bool success = await qbClient.AddTorrentAsync(request.Url, client.Category ?? string.Empty);
                    if (success)
                    {
                        _logger.Info("[DownloadClient] torrent added to qBittorrent");
                        return Ok(new { message = "Torrent added to qBittorrent" });
                    }
                    else
                    {
                        _logger.Error("[DownloadClient] Failed to add torrent to qBittorrent. It might be an NZB. Attempting failover...");
                        
                        // Failover: Try adding to Usenet client
                        var usenetClient = _clients
                            .Where(c => c.Enable && (c.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase) || c.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase)))
                            .OrderBy(c => c.Priority).ThenBy(c => c.Id)
                            .FirstOrDefault();
                            
                        if (usenetClient != null)
                        {
                            _logger.Error($"[DownloadClient] Failover: Found Usenet client {usenetClient.Implementation}. Trying...");
                            if (usenetClient.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
                            {
                                var sabClient = new SabnzbdClient(usenetClient.Host, usenetClient.Port, usenetClient.ApiKey ?? string.Empty, usenetClient.UrlBase);
                                if (await sabClient.AddNzbAsync(request.Url, usenetClient.Category ?? string.Empty))
                                    return Ok(new { message = "Added to SABnzbd (Failover from Torrent)" });
                            }
                            else if (usenetClient.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                            {
                                var nzbClient = new NzbgetClient(usenetClient.Host, usenetClient.Port, usenetClient.Username ?? string.Empty, usenetClient.Password ?? string.Empty, usenetClient.UrlBase);
                                if (await nzbClient.AddNzbAsync(request.Url, usenetClient.Category ?? string.Empty))
                                    return Ok(new { message = "Added to NZBGet (Failover from Torrent)" });
                            }
                        }
                        
                        return StatusCode(500, new { message = "Failed to add torrent to qBittorrent and Failover failed." });
                    }
                }
                else if (client.Implementation.Equals("Transmission", StringComparison.OrdinalIgnoreCase))
                {
                    var transmissionClient = new TransmissionClient(
                        client.Host,
                        client.Port,
                        client.Username ?? string.Empty,
                        client.Password ?? string.Empty
                    );

                    bool success = await transmissionClient.AddTorrentAsync(request.Url, client.Category ?? string.Empty);
                    if (success)
                    {
                        _logger.Info("[DownloadClient] torrent added to Transmission");
                        return Ok(new { message = "Torrent added to Transmission" });
                    }
                    else
                    {
                        _logger.Error("[DownloadClient] Failed to add torrent to Transmission");
                        return StatusCode(500, new { message = "Failed to add torrent to Transmission" });
                    }
                }
                else if (client.Implementation.Equals("SABnzbd", StringComparison.OrdinalIgnoreCase))
                {
                    var sabClient = new SabnzbdClient(
                        client.Host,
                        client.Port,
                        client.ApiKey ?? string.Empty,
                        client.UrlBase
                    );
                    
                    bool success = await sabClient.AddNzbAsync(request.Url, client.Category ?? string.Empty);
                    if (success)
                    {
                        _logger.Info("[DownloadClient] NZB added to SABnzbd");
                        return Ok(new { message = "NZB added to SABnzbd" });
                    }
                    else
                    {
                         return StatusCode(500, new { message = "Failed to add NZB to SABnzbd" });
                    }
                }
                else if (client.Implementation.Equals("NZBGet", StringComparison.OrdinalIgnoreCase))
                {
                    var nzbClient = new NzbgetClient(
                        client.Host,
                        client.Port,
                        client.Username ?? string.Empty,
                        client.Password ?? string.Empty,
                        client.UrlBase
                    );
                    
                    bool success = await nzbClient.AddNzbAsync(request.Url, client.Category ?? string.Empty);
                    if (success)
                    {
                        _logger.Info("[DownloadClient] NZB added to NZBGet");
                        return Ok(new { message = "NZB added to NZBGet" });
                    }
                    else
                    {
                         return StatusCode(500, new { message = "Failed to add NZB to NZBGet" });
                    }
                }
                else if (client.Implementation.Equals("Deluge", StringComparison.OrdinalIgnoreCase))
                {
                    var delugeClient = new DelugeClient(
                        client.Host,
                        client.Port,
                        client.Password ?? string.Empty,
                        client.UseSsl
                    );
                    
                    bool success = await delugeClient.AddTorrentAsync(request.Url, client.Category ?? string.Empty);
                    if (success)
                    {
                        _logger.Info("[DownloadClient] torrent added to Deluge");
                        return Ok(new { message = "Torrent added to Deluge" });
                    }
                    else
                    {
                        _logger.Error("[DownloadClient] Failed to add torrent to Deluge");
                        return StatusCode(500, new { message = "Failed to add torrent to Deluge" });
                    }
                }
                
                return BadRequest(new { message = $"Unsupported download client: {client.Implementation}" });
            }
            catch (Exception ex)
            {
                _logger.Error($"[DownloadClient] Error adding torrent: {ex.Message}");
                return StatusCode(500, new { message = $"Error adding torrent: {ex.Message}" });
            }
        }
    }

    #region History / Failed / Blacklist / Unmapped / Counts endpoints

    [ApiController]
    [Route("api/v3/downloadclient/history")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DownloadHistoryController : ControllerBase
    {
        private readonly DownloadHistoryRepository _historyRepo;
        private readonly DownloadBlacklistRepository _blacklistRepo;
        private readonly TrackedDownloadService _trackedDownloadService;

        public DownloadHistoryController(
            DownloadHistoryRepository historyRepo,
            DownloadBlacklistRepository blacklistRepo,
            TrackedDownloadService trackedDownloadService)
        {
            _historyRepo = historyRepo;
            _blacklistRepo = blacklistRepo;
            _trackedDownloadService = trackedDownloadService;
        }

        [HttpGet]
        public async Task<ActionResult> Search(
            [FromQuery] string? query = null,
            [FromQuery] string? platform = null,
            [FromQuery] string? state = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] string sortBy = "importedAt",
            [FromQuery] bool sortDescending = true,
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 25)
        {
            var (items, totalCount) = await _historyRepo.SearchAsync(
                query, platform, state, fromDate, toDate, sortBy, sortDescending, page, pageSize);

            return Ok(new
            {
                items,
                totalCount,
                page,
                pageSize,
                totalPages = (int)Math.Ceiling((double)totalCount / pageSize)
            });
        }

        [HttpGet("failed")]
        public async Task<ActionResult> GetFailed()
        {
            var failed = await _historyRepo.GetFailedAsync();
            return Ok(failed);
        }

        [HttpPost("{id}/dismiss")]
        public async Task<ActionResult> Dismiss(int id)
        {
            await _historyRepo.UpdateStateAsync(id, DownloadHistoryState.Ignored, "Dismissed by user");
            return Ok(new { message = "Dismissed" });
        }

        [HttpPost("{id}/blacklist")]
        public async Task<ActionResult> BlacklistFromHistory(int id, [FromBody] BlacklistReasonRequest? request = null)
        {
            var entry = await _historyRepo.GetByIdAsync(id);
            if (entry == null) return NotFound();

            await _blacklistRepo.AddAsync(new DownloadBlacklistEntry
            {
                DownloadId = entry.DownloadId,
                Title = entry.Title,
                Platform = entry.Platform,
                Reason = request?.Reason ?? "Blacklisted by user",
                BlacklistedAt = DateTime.UtcNow,
                ClientName = entry.ClientName
            });

            await _historyRepo.UpdateStateAsync(id, DownloadHistoryState.Ignored, "Moved to blacklist");
            return Ok(new { message = "Blacklisted" });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var deleted = await _historyRepo.DeleteAsync(id);
            if (!deleted) return NotFound();
            return Ok(new { message = "Deleted" });
        }
    }

    [ApiController]
    [Route("api/v3/downloadclient/blacklist")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DownloadBlacklistController : ControllerBase
    {
        private readonly DownloadBlacklistRepository _blacklistRepo;

        public DownloadBlacklistController(DownloadBlacklistRepository blacklistRepo)
        {
            _blacklistRepo = blacklistRepo;
        }

        [HttpGet]
        public async Task<ActionResult> GetAll([FromQuery] string? query = null)
        {
            var items = await _blacklistRepo.GetAllAsync(query);
            return Ok(items);
        }

        [HttpPost]
        public async Task<ActionResult> Add([FromBody] AddBlacklistRequest request)
        {
            if (string.IsNullOrWhiteSpace(request.Title))
                return BadRequest("Title is required.");

            await _blacklistRepo.AddAsync(new DownloadBlacklistEntry
            {
                DownloadId = request.DownloadId,
                Title = request.Title,
                Platform = request.Platform,
                Reason = request.Reason ?? "Manually blacklisted",
                BlacklistedAt = DateTime.UtcNow,
                ClientName = request.ClientName
            });

            return Ok(new { message = "Added to blacklist" });
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id)
        {
            var deleted = await _blacklistRepo.DeleteAsync(id);
            if (!deleted) return NotFound();
            return Ok(new { message = "Removed from blacklist" });
        }
    }

    [ApiController]
    [Route("api/v3/downloadclient/unmapped")]
    public class DownloadUnmappedController : ControllerBase
    {
        private readonly TrackedDownloadService _trackedDownloadService;
        private readonly RetroArr.Core.Games.IGameRepository _gameRepository;

        public DownloadUnmappedController(TrackedDownloadService trackedDownloadService, RetroArr.Core.Games.IGameRepository gameRepository)
        {
            _trackedDownloadService = trackedDownloadService;
            _gameRepository = gameRepository;
        }

        [HttpGet]
        public async Task<ActionResult> GetUnmapped()
        {
            var tracked = _trackedDownloadService.GetTrackedDownloads()
                .Where(t => t.IsUnmapped ||
                    (string.IsNullOrEmpty(t.PlatformFolder) &&
                     (t.State == TrackedDownloadState.ImportPending || t.State == TrackedDownloadState.ImportBlocked)))
                .ToList();

            List<RetroArr.Core.Games.Game>? allGames = null;
            var unmapped = new List<object>();
            foreach (var t in tracked)
            {
                string? gameTitle = null;
                string? gamePlatform = null;
                if (t.GameId.HasValue)
                {
                    allGames ??= await _gameRepository.GetAllAsync();
                    var game = allGames.FirstOrDefault(g => g.Id == t.GameId.Value);
                    if (game != null)
                    {
                        gameTitle = game.Title;
                        var plat = RetroArr.Core.Games.PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                        gamePlatform = plat?.FolderName;
                    }
                }
                unmapped.Add(new
                {
                    t.DownloadId,
                    t.DownloadClientId,
                    t.DownloadClientName,
                    t.Title,
                    t.Size,
                    t.OutputPath,
                    t.Added,
                    State = t.State.ToString(),
                    t.StatusMessages,
                    t.GameId,
                    GameTitle = gameTitle,
                    GamePlatform = gamePlatform
                });
            }

            return Ok(unmapped);
        }
    }

    [ApiController]
    [Route("api/v3/downloadclient/counts")]
    public class DownloadCountsController : ControllerBase
    {
        private readonly TrackedDownloadService _trackedDownloadService;
        private readonly DownloadHistoryRepository _historyRepo;
        private readonly DownloadBlacklistRepository _blacklistRepo;

        public DownloadCountsController(
            TrackedDownloadService trackedDownloadService,
            DownloadHistoryRepository historyRepo,
            DownloadBlacklistRepository blacklistRepo)
        {
            _trackedDownloadService = trackedDownloadService;
            _historyRepo = historyRepo;
            _blacklistRepo = blacklistRepo;
        }

        [HttpGet]
        public async Task<ActionResult> GetCounts()
        {
            var tracked = _trackedDownloadService.GetTrackedDownloads();

            var active = tracked.Count(t =>
                t.State == TrackedDownloadState.Downloading ||
                t.State == TrackedDownloadState.ImportPending ||
                t.State == TrackedDownloadState.ImportBlocked ||
                t.State == TrackedDownloadState.Importing);

            var failed = tracked.Count(t => t.State == TrackedDownloadState.ImportFailed);

            var unmapped = tracked.Count(t => t.IsUnmapped ||
                (string.IsNullOrEmpty(t.PlatformFolder) &&
                 (t.State == TrackedDownloadState.ImportPending || t.State == TrackedDownloadState.ImportBlocked)));

            var blacklisted = await _blacklistRepo.GetCountAsync();

            return Ok(new { active, failed, unmapped, blacklisted });
        }
    }

    #endregion

    #region Request/Response Models

    public class BlacklistReasonRequest
    {
        public string? Reason { get; set; }
    }

    public class AddBlacklistRequest
    {
        public string? DownloadId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Platform { get; set; }
        public string? Reason { get; set; }
        public string? ClientName { get; set; }
    }

    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class AddTorrentRequest
    {
        public string Url { get; set; } = string.Empty;
        public string? Protocol { get; set; } // "torrent", "nzb"
        public string? PlatformFolder { get; set; } // e.g. "windows", "switch", "psx"
        public int? GameId { get; set; } // Link download to a specific game for targeted import
        public string? ImportSubfolder { get; set; } // Auto-detected: "Patches", "DLC", or null (main game)
    }

    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class TestDownloadClientRequest
    {
        public string Implementation { get; set; } = string.Empty;
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; }
        public string? Username { get; set; }
        public string? Password { get; set; }
        public string? UrlBase { get; set; }
        public string? ApiKey { get; set; }
        public bool UseSsl { get; set; }
    }

    public class MapPlatformRequest
    {
        public string DownloadName { get; set; } = string.Empty;
        public string PlatformFolder { get; set; } = string.Empty;
        public int? GameId { get; set; }
        public string? ImportSubfolder { get; set; }
    }

    #endregion
}
