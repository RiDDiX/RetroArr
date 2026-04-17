using System;
using System.IO;
using System.Linq;
using System.Diagnostics.CodeAnalysis;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using RetroArr.Core.Download.History;
using RetroArr.Core.Games;

namespace RetroArr.Core.Download.TrackedDownloads
{
    /// <summary>
    /// Two-phase completed download handler.
    /// Phase 1 (Check): Validates path, category, platform — sets ImportPending, ImportBlocked, or Unmapped.
    /// Phase 2 (Import): Runs PostDownloadProcessor, flushes result to History DB, enforces Blacklist.
    /// </summary>
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class CompletedDownloadService
    {
        private readonly PostDownloadProcessor _postDownloadProcessor;
        private readonly DownloadPlatformTracker _platformTracker;
        private readonly TrackedDownloadService _trackedDownloadService;
        private readonly DownloadHistoryRepository _historyRepo;
        private readonly DownloadBlacklistRepository _blacklistRepo;
        private readonly IGameRepository _gameRepository;
        private readonly ILogger<CompletedDownloadService> _logger;

        public CompletedDownloadService(
            PostDownloadProcessor postDownloadProcessor,
            DownloadPlatformTracker platformTracker,
            TrackedDownloadService trackedDownloadService,
            DownloadHistoryRepository historyRepo,
            DownloadBlacklistRepository blacklistRepo,
            IGameRepository gameRepository,
            ILogger<CompletedDownloadService> logger)
        {
            _postDownloadProcessor = postDownloadProcessor;
            _platformTracker = platformTracker;
            _trackedDownloadService = trackedDownloadService;
            _historyRepo = historyRepo;
            _blacklistRepo = blacklistRepo;
            _gameRepository = gameRepository;
            _logger = logger;
        }

        /// <summary>
        /// Phase 1: Validate the completed download and determine if it's ready for import.
        /// </summary>
        public async Task CheckAsync(TrackedDownload trackedDownload, DownloadClient clientConfig)
        {
            if (trackedDownload.State != TrackedDownloadState.ImportPending &&
                trackedDownload.State != TrackedDownloadState.ImportBlocked)
            {
                return;
            }

            // Blacklist enforcement
            if (await _blacklistRepo.IsBlacklistedAsync(trackedDownload.DownloadId, trackedDownload.Title))
            {
                trackedDownload.MarkIgnored();
                trackedDownload.Warn("This download is blacklisted and will not be processed.");
                _trackedDownloadService.Save();
                _logger.LogInformation("[CompletedDownload] '{Title}' is blacklisted — skipping.", trackedDownload.Title);
                return;
            }

            // Resolve platform folder from tracker if not already set
            if (string.IsNullOrEmpty(trackedDownload.PlatformFolder))
            {
                trackedDownload.PlatformFolder = _platformTracker.LookupByName(trackedDownload.Title);
            }

            // Validate output path
            if (!ValidatePath(trackedDownload, clientConfig))
            {
                return;
            }

            // Unmapped detection: path is valid but no platform assigned
            if (string.IsNullOrEmpty(trackedDownload.PlatformFolder))
            {
                // If GameId is set, resolve platform from the game entry
                if (trackedDownload.GameId.HasValue)
                {
                    var games = await _gameRepository.GetAllAsync();
                    var game = games.FirstOrDefault(g => g.Id == trackedDownload.GameId.Value);
                    if (game != null && game.PlatformId > 0)
                    {
                        var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                        if (platform != null)
                        {
                            trackedDownload.PlatformFolder = platform.FolderName;
                            _logger.LogInformation("[CompletedDownload] Resolved platform '{Platform}' from game '{Title}' (ID: {GameId})",
                                platform.FolderName, game.Title, game.Id);
                        }
                    }
                }

                // Still no platform after game lookup → mark as unmapped
                if (string.IsNullOrEmpty(trackedDownload.PlatformFolder))
                {
                    trackedDownload.Warn("Platform not detected. Assign a platform manually from the Unmapped tab.");
                    trackedDownload.State = TrackedDownloadState.ImportBlocked;
                    trackedDownload.IsUnmapped = true;
                    _logger.LogInformation("[CompletedDownload] '{Title}' has no platform — marked as unmapped.", trackedDownload.Title);
                    return;
                }
            }

            // Path is valid and platform assigned — ready for import
            trackedDownload.ClearWarnings();
            trackedDownload.IsUnmapped = false;
            trackedDownload.State = TrackedDownloadState.ImportPending;
            _logger.LogInformation("[CompletedDownload] '{Title}' is ready for import at '{Path}'",
                trackedDownload.Title, trackedDownload.OutputPath);
        }

        /// <summary>
        /// Phase 2: Actually import the completed download and flush result to History DB.
        /// </summary>
        public async Task ImportAsync(TrackedDownload trackedDownload)
        {
            if (trackedDownload.State != TrackedDownloadState.ImportPending)
            {
                return;
            }

            if (string.IsNullOrEmpty(trackedDownload.OutputPath))
            {
                trackedDownload.Warn("Download path is empty, cannot import.");
                trackedDownload.State = TrackedDownloadState.ImportBlocked;
                return;
            }

            // Guard: skip if already successfully imported (prevents overwriting Imported→Failed on retry)
            var existingHistory = await _historyRepo.FindByDownloadIdAsync(trackedDownload.DownloadId);
            if (existingHistory != null && existingHistory.State == DownloadHistoryState.Imported)
            {
                trackedDownload.MarkImported();
                _logger.LogInformation("[CompletedDownload] '{Title}' was already imported — skipping re-import.", trackedDownload.Title);
                return;
            }

            trackedDownload.MarkImporting();
            _logger.LogInformation("[CompletedDownload] Importing '{Title}' from '{Path}' (Platform: {Platform})",
                trackedDownload.Title, trackedDownload.OutputPath, trackedDownload.PlatformFolder ?? "unknown");

            try
            {
                var downloadStatus = new DownloadStatus
                {
                    Id = trackedDownload.DownloadId,
                    ClientId = trackedDownload.DownloadClientId,
                    Name = trackedDownload.Title,
                    DownloadPath = trackedDownload.OutputPath,
                    PlatformFolder = trackedDownload.PlatformFolder,
                    GameId = trackedDownload.GameId,
                    ImportSubfolder = trackedDownload.ImportSubfolder,
                    Size = trackedDownload.Size,
                    Progress = trackedDownload.Progress,
                    State = DownloadState.Completed,
                    ClientName = trackedDownload.DownloadClientName,
                    Category = trackedDownload.Category
                };

                var result = await _postDownloadProcessor.ProcessCompletedDownloadAsync(downloadStatus);

                if (result.Success)
                {
                    trackedDownload.MarkImported();
                    _platformTracker.MarkProcessed(trackedDownload.Title);
                    _logger.LogInformation("[CompletedDownload] imported '{Title}' -> '{Dest}'",
                        trackedDownload.Title, result.DestinationPath);

                    await FlushToHistoryAsync(trackedDownload, DownloadHistoryState.Imported,
                        null, result.DestinationPath);
                }
                else
                {
                    trackedDownload.MarkFailed(result.Reason ?? "Import failed without specific reason.");
                    _logger.LogWarning("[CompletedDownload] Import of '{Title}' failed: {Reason}",
                        trackedDownload.Title, result.Reason);

                    await FlushToHistoryAsync(trackedDownload, DownloadHistoryState.ImportFailed,
                        result.Reason, null);
                }

                _trackedDownloadService.Save();
            }
            catch (Exception ex)
            {
                trackedDownload.MarkFailed($"Import error: {ex.Message}");
                _trackedDownloadService.Save();
                _logger.LogError(ex, "[CompletedDownload] Failed to import '{Title}': {Message}",
                    trackedDownload.Title, ex.Message);

                await FlushToHistoryAsync(trackedDownload, DownloadHistoryState.ImportFailed,
                    ex.Message, null);
            }
        }

        // Upsert on DownloadId so a retried terminal state doesn't double-row.
        private async Task FlushToHistoryAsync(TrackedDownload tracked, DownloadHistoryState state,
            string? reason, string? destinationPath)
        {
            try
            {
                var entry = new DownloadHistoryEntry
                {
                    DownloadId = tracked.DownloadId,
                    ClientId = tracked.DownloadClientId,
                    ClientName = tracked.DownloadClientName,
                    Title = tracked.Title,
                    Platform = tracked.PlatformFolder,
                    Size = tracked.Size,
                    State = state,
                    Reason = reason,
                    SourcePath = tracked.OutputPath,
                    DestinationPath = destinationPath,
                    ImportedAt = DateTime.UtcNow,
                    AddedAt = tracked.Added
                };

                await _historyRepo.UpsertAsync(entry);
                _logger.LogDebug("[CompletedDownload] History flushed for '{Title}' as {State}", tracked.Title, state);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "[CompletedDownload] Failed to flush history for '{Title}': {Message}",
                    tracked.Title, ex.Message);
            }
        }

        /// <summary>
        /// Validates the download path, applies remote path mapping, and checks accessibility.
        /// Returns true if path is valid and accessible.
        /// </summary>
        private bool ValidatePath(TrackedDownload trackedDownload, DownloadClient clientConfig)
        {
            // Check if path is empty — client may still be post-processing
            if (string.IsNullOrEmpty(trackedDownload.OutputPath))
            {
                trackedDownload.Warn("Download path is empty. The download client may still be post-processing. Will retry.");
                trackedDownload.State = TrackedDownloadState.ImportBlocked;
                return false;
            }

            // Apply remote path mapping
            if (!string.IsNullOrEmpty(clientConfig.RemotePathMapping) &&
                !string.IsNullOrEmpty(clientConfig.LocalPathMapping))
            {
                if (trackedDownload.OutputPath.StartsWith(clientConfig.RemotePathMapping, StringComparison.OrdinalIgnoreCase))
                {
                    var relative = trackedDownload.OutputPath
                        .Substring(clientConfig.RemotePathMapping.Length)
                        .TrimStart('/', '\\');
                    var mappedPath = Path.Combine(clientConfig.LocalPathMapping, relative);
                    _logger.LogDebug("[CompletedDownload] Path mapping: '{Original}' -> '{Mapped}'",
                        trackedDownload.OutputPath, mappedPath);
                    trackedDownload.OutputPath = mappedPath;
                }
                else
                {
                    trackedDownload.Warn($"Download path '{trackedDownload.OutputPath}' does not match remote mapping '{clientConfig.RemotePathMapping}'. Check Remote Path Mapping in download client settings.");
                    trackedDownload.State = TrackedDownloadState.ImportBlocked;
                    return false;
                }
            }

            // Check path accessibility
            if (!Directory.Exists(trackedDownload.OutputPath) && !File.Exists(trackedDownload.OutputPath))
            {
                trackedDownload.Warn($"Path not accessible: '{trackedDownload.OutputPath}'. Ensure the path is mounted in the container or check Remote Path Mapping.");
                trackedDownload.State = TrackedDownloadState.ImportBlocked;
                return false;
            }

            return true;
        }
    }
}
