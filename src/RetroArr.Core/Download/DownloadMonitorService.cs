using System;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using RetroArr.Core.Configuration;
using RetroArr.Core.Download.TrackedDownloads;

namespace RetroArr.Core.Download
{
    /// <summary>
    /// Background service that periodically polls download clients and processes completed downloads.
    /// Flow:
    ///   1. Refresh all enabled download clients
    ///   2. Filter by configured category
    ///   3. Track each download via TrackedDownloadService (state machine)
    ///   4. For completed downloads: CompletedDownloadService.CheckAsync() validates path/platform/blacklist
    ///   5. For import-pending downloads: CompletedDownloadService.ImportAsync() runs PostDownloadProcessor
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DownloadMonitorService : BackgroundService
    {
        private readonly ConfigurationService _configService;
        private readonly TrackedDownloadService _trackedDownloadService;
        private readonly CompletedDownloadService _completedDownloadService;
        private readonly DownloadPlatformTracker _platformTracker;
        private readonly ImportStatusService _importStatus;
        private readonly ILogger<DownloadMonitorService> _logger;

        public DownloadMonitorService(
            ConfigurationService configService,
            TrackedDownloadService trackedDownloadService,
            CompletedDownloadService completedDownloadService,
            DownloadPlatformTracker platformTracker,
            ImportStatusService importStatus,
            ILogger<DownloadMonitorService> logger)
        {
            _configService = configService;
            _trackedDownloadService = trackedDownloadService;
            _completedDownloadService = completedDownloadService;
            _platformTracker = platformTracker;
            _importStatus = importStatus;
            _logger = logger;
        }

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            _logger.LogInformation("[DownloadMonitor] Service started.");

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var settings = _configService.LoadPostDownloadSettings();
                    var clients = _configService.LoadDownloadClients();
                    var enabledClients = clients.Where(c => c.Enable).ToList();

                    if (enabledClients.Any())
                    {
                        // Phase 1: Refresh tracked downloads from all clients
                        var seenDownloadIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        foreach (var clientConfig in enabledClients)
                        {
                            await RefreshClientDownloadsAsync(clientConfig, seenDownloadIds);
                        }

                        // Phase 1.5: Reconcile - remove stale entries no longer in any client
                        _trackedDownloadService.ReconcileWithClientIds(seenDownloadIds);

                        // Phase 2: Process tracked downloads (Check + Import)
                        if (settings.EnableAutoMove)
                        {
                            await ProcessTrackedDownloadsAsync(enabledClients);
                        }
                    }

                    await Task.Delay(TimeSpan.FromSeconds(settings.MonitorIntervalSeconds), stoppingToken);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DownloadMonitor] Error in monitor loop");
                    await Task.Delay(TimeSpan.FromSeconds(30), stoppingToken);
                }
            }

            _logger.LogInformation("[DownloadMonitor] Service stopping.");
        }

        /// <summary>
        /// Phase 1: Poll a download client and update tracked downloads.
        /// </summary>
        private async Task RefreshClientDownloadsAsync(DownloadClient config, HashSet<string> seenDownloadIds)
        {
            IDownloadClient? client = CreateClient(config);
            if (client == null) return;

            try
            {
                var downloads = await client.GetDownloadsAsync();
                _logger.LogDebug("[DownloadMonitor] Client {Name} returned {Count} downloads.", config.Name, downloads.Count);

                // Filter by configured category
                if (!string.IsNullOrEmpty(config.Category))
                {
                    downloads = downloads.Where(d =>
                        !string.IsNullOrEmpty(d.Category) &&
                        d.Category.Equals(config.Category, StringComparison.OrdinalIgnoreCase)
                    ).ToList();
                }

                foreach (var download in downloads)
                {
                    // Track which IDs are still reported by clients
                    seenDownloadIds.Add(download.Id);

                    // Resolve platform folder, game ID, and import subfolder from tracker
                    download.PlatformFolder = _platformTracker.LookupByName(download.Name);
                    download.GameId = _platformTracker.LookupGameId(download.Name);
                    download.ImportSubfolder = _platformTracker.LookupImportSubfolder(download.Name);

                    // Track the download (creates or updates)
                    var tracked = _trackedDownloadService.TrackDownload(download, config.Id, config.Name);

                    // Propagate platform folder if resolved later
                    if (!string.IsNullOrEmpty(download.PlatformFolder) && string.IsNullOrEmpty(tracked.PlatformFolder))
                    {
                        tracked.PlatformFolder = download.PlatformFolder;
                    }

                    // Propagate game ID if resolved
                    if (download.GameId.HasValue && !tracked.GameId.HasValue)
                    {
                        tracked.GameId = download.GameId;
                    }

                    // Propagate import subfolder (Patches, DLC, etc.)
                    if (!string.IsNullOrEmpty(download.ImportSubfolder) && string.IsNullOrEmpty(tracked.ImportSubfolder))
                    {
                        tracked.ImportSubfolder = download.ImportSubfolder;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[DownloadMonitor] Error refreshing {Name} ({Implementation})", config.Name, config.Implementation);
            }
        }

        /// <summary>
        /// Phase 2: Process all tracked downloads through the state machine.
        /// </summary>
        private async Task ProcessTrackedDownloadsAsync(List<DownloadClient> clients)
        {
            var trackedDownloads = _trackedDownloadService.GetTrackedDownloads();

            foreach (var tracked in trackedDownloads)
            {
                try
                {
                    var clientConfig = clients.FirstOrDefault(c => c.Id == tracked.DownloadClientId);
                    if (clientConfig == null) continue;

                    // Check: Validate completed downloads (ImportPending / ImportBlocked -> ready?)
                    if (tracked.State == TrackedDownloadState.ImportPending ||
                        tracked.State == TrackedDownloadState.ImportBlocked)
                    {
                        await _completedDownloadService.CheckAsync(tracked, clientConfig);
                    }

                    // Import: Process downloads that are ready
                    if (tracked.State == TrackedDownloadState.ImportPending)
                    {
                        _importStatus.MarkImporting(tracked.DownloadId);
                        try
                        {
                            await _completedDownloadService.ImportAsync(tracked);
                        }
                        finally
                        {
                            _importStatus.MarkFinished(tracked.DownloadId);
                        }
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "[DownloadMonitor] Error processing tracked download '{Title}'", tracked.Title);
                }
            }
        }

        private static IDownloadClient? CreateClient(DownloadClient config)
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
    }
}
