using System;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.Launcher;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using RetroArr.Core.Configuration;
using RetroArr.Core.MetadataSource.Gog;
using RetroArr.SignalR;

namespace RetroArr.Api.V3.Games
{
    [ApiController]
    [Route("api/v3/[controller]")]
    public class GameController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.LibraryOverview);
        private readonly IGameRepository _repository;
        private readonly IGameMetadataServiceFactory _metadataServiceFactory;
        private readonly RetroArr.Core.IO.IArchiveService _archiveService;
        private readonly ILauncherService _launcherService;
        private readonly ConfigurationService _configService;
        private readonly InstallerScannerService _installerScanner;
        private readonly LocalMediaExportService _localMediaExport;
        private readonly RetroArr.Core.MetadataSource.Gog.GogDownloadTracker _gogDownloadTracker;
        private readonly TrashService _trash;
        private readonly MediaScannerService _scannerService;
        private readonly RetroArr.Core.Download.PostDownloadProcessor _postDownloadProcessor;
        private readonly IProgressNotifier? _progressNotifier;
        private readonly RetroArr.Core.Download.History.DownloadHistoryRepository? _history;

        private readonly ApiKeyService _apiKeyService;

        public GameController(IGameRepository repository, IGameMetadataServiceFactory metadataServiceFactory, RetroArr.Core.IO.IArchiveService archiveService, ILauncherService launcherService, ConfigurationService configService, InstallerScannerService installerScanner, LocalMediaExportService localMediaExport, RetroArr.Core.MetadataSource.Gog.GogDownloadTracker gogDownloadTracker, TrashService trash, MediaScannerService scannerService, RetroArr.Core.Download.PostDownloadProcessor postDownloadProcessor, ApiKeyService apiKeyService, IProgressNotifier? progressNotifier = null, RetroArr.Core.Download.History.DownloadHistoryRepository? history = null)
        {
            _apiKeyService = apiKeyService;
            _repository = repository;
            _metadataServiceFactory = metadataServiceFactory;
            _archiveService = archiveService;
            _launcherService = launcherService;
            _configService = configService;
            _installerScanner = installerScanner;
            _localMediaExport = localMediaExport;
            _gogDownloadTracker = gogDownloadTracker;
            _trash = trash;
            _scannerService = scannerService;
            _postDownloadProcessor = postDownloadProcessor;
            _progressNotifier = progressNotifier;
            _history = history;
        }

        [HttpGet]
        public async Task<IEnumerable<Game>> GetAll([FromQuery] string lang = "es")
        {
            _logger.Info("[API] GetAll Games Request Received");
            try 
            {
                var games = await _repository.GetAllLightAsync();
                
                var platformLookup = PlatformDefinitions.PlatformDictionary;
                foreach (var game in games)
                {
                    if (game.PlatformId > 0 && game.Platform == null)
                    {
                        if (platformLookup.TryGetValue(game.PlatformId, out var plat))
                            game.Platform = plat;
                    }
                }
                
                _logger.Info($"[API] Retrieved {games.Count()} games from DB");
                return games;
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error in GetAll: {ex.Message} - {ex.StackTrace}");
                throw;
            }
        }

        [HttpGet("counts")]
        public async Task<ActionResult<Dictionary<string, int>>> GetPlatformCounts()
        {
            var games = await _repository.GetAllLightAsync();
            var counts = new Dictionary<string, int>();
            int total = 0;
            foreach (var g in games)
            {
                var key = (g.PlatformId > 0 ? g.PlatformId : 0).ToString();
                counts[key] = counts.TryGetValue(key, out var c) ? c + 1 : 1;
                total++;
            }
            counts["__total"] = total;
            return Ok(counts);
        }

        [HttpGet("paged")]
        public async Task<ActionResult<PagedResult<GameListDto>>> GetPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] int? platformId = null,
            [FromQuery] string? search = null,
            [FromQuery] string sortOrder = "asc",
            [FromQuery] bool? missingOnly = null,
            [FromQuery] string? protonDbTier = null)
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 1000) pageSize = 1000;

            var result = await _repository.GetAllPagedAsync(page, pageSize, platformId, search, sortOrder, missingOnly, protonDbTier);
            return Ok(result);
        }

        [HttpGet("problems")]
        public async Task<ActionResult<IEnumerable<object>>> GetProblems()
        {
            var games = await _repository.GetAllAsync();
            var problems = new List<object>();
            
            // Define invalid file extensions (media files that shouldn't be games)
            var invalidExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
            {
                ".mp4", ".mkv", ".avi", ".mov", ".wmv", ".flv", ".webm", ".mpg", ".mpeg",
                ".mp3", ".wav", ".flac", ".ogg", ".aac", ".wma", ".m4a",
                ".jpg", ".jpeg", ".png", ".gif", ".bmp", ".webp", ".tiff",
                ".pdf", ".doc", ".docx", ".txt", ".nfo"
            };

            foreach (var game in games)
            {
                string? problemType = null;
                string? problemDescription = null;
                string? fileExtension = null;

                // Check for invalid file format
                if (!string.IsNullOrEmpty(game.Path))
                {
                    var ext = Path.GetExtension(game.Path);
                    if (!string.IsNullOrEmpty(ext) && invalidExtensions.Contains(ext))
                    {
                        problemType = "invalid_format";
                        problemDescription = $"File has invalid extension '{ext}' - this is not a valid game file.";
                        fileExtension = ext;
                    }
                    else if (!System.IO.File.Exists(game.Path) && !System.IO.Directory.Exists(game.Path))
                    {
                        problemType = "missing_file";
                        problemDescription = "The game file or folder no longer exists at the specified path.";
                    }
                    else if (game.MissingSince != null)
                    {
                        problemType = "missing_file";
                        problemDescription = "The game's files are missing from its folder.";
                    }
                }

                // Check for missing metadata
                if (problemType == null && !game.IgdbId.HasValue && string.IsNullOrEmpty(game.Overview))
                {
                    problemType = "no_metadata";
                    problemDescription = "Game has no IGDB ID and no description. Consider running metadata correction.";
                }

                if (problemType != null)
                {
                    var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                    problems.Add(new
                    {
                        id = game.Id,
                        title = game.Title,
                        path = game.Path ?? "",
                        platformId = game.PlatformId,
                        platformName = platform?.Name ?? "Unknown",
                        platformSlug = platform?.Slug,
                        problemType = problemType,
                        problemDescription = problemDescription,
                        fileExtension = fileExtension,
                        detectedAt = DateTime.UtcNow.ToString("o")
                    });
                }
            }

            return Ok(problems);
        }

        [HttpPost("{id}/resolve-problem")]
        public async Task<ActionResult> ResolveProblem(int id)
        {
            // This endpoint marks a problem as resolved by the user
            // For now, it just returns OK - could be extended to track resolved issues
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();
            
            return Ok(new { message = "Problem marked as resolved" });
        }

        [HttpGet("{id}")]
        public async Task<ActionResult<Game>> GetById(int id, [FromQuery] string? lang = null)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null)
            {
                return NotFound();
            }

            // Populate Platform from PlatformDefinitions based on PlatformId
            if (game.PlatformId > 0 && game.Platform == null)
            {
                game.Platform = PlatformDefinitions.AllPlatforms
                    .FirstOrDefault(p => p.Id == game.PlatformId);
            }

            // NOTE: localized game text is served straight from the stored metadata
            // (populated at scan/match time). We deliberately do NOT re-fetch IGDB
            // here: that live external call ran on every detail-page open and blocked
            // the response for several seconds ("Spiel wird geladen..." hanging).
            // Cheap, local platform-name localization is still applied.
            if (!string.IsNullOrEmpty(lang) && lang != "en" && game.Platform != null)
            {
                try
                {
                    // a copy: the entry from PlatformDefinitions is shared by every request
                    var localized = game.Platform.Clone();
                    localized.Name = _metadataServiceFactory.CreateService().LocalizePlatform(localized.Name, lang);
                    game.Platform = localized;
                }
                catch { /* keep stored platform name */ }
            }

            game.IsInstallable = IsPathInstallable(game.Path);

            var uninstallerPath = FindUninstaller(game.Path);

            var isInstaller = game.Status == GameStatus.InstallerDetected || 
                              (!string.IsNullOrEmpty(game.ExecutablePath) && 
                               (game.ExecutablePath.EndsWith("setup.exe", System.StringComparison.OrdinalIgnoreCase) || 
                                game.ExecutablePath.EndsWith("install.exe", System.StringComparison.OrdinalIgnoreCase)));

            bool canPlay = (game.SteamId.HasValue && game.SteamId.Value > 0) || 
                           !string.IsNullOrEmpty(game.GogId) ||
                           (!string.IsNullOrEmpty(game.ExecutablePath) && 
                            System.IO.File.Exists(game.ExecutablePath) && 
                            !isInstaller);

            _logger.Info($"[API] Game {id} GetById - canPlay: {canPlay} (Path: {game.ExecutablePath}, SteamId: {game.SteamId}, GogId: {game.GogId}, Status: {game.Status})");

            return Ok(new
            {
                game.Id,
                game.Title,
                game.AlternativeTitle,
                game.Year,
                game.Overview,
                game.Storyline,
                game.PlatformId,
                game.Platform,
                game.Added,
                game.Images,
                game.Genres,
                game.AvailablePlatforms,
                game.Developer,
                game.Publisher,
                game.ReleaseDate,
                game.Rating,
                game.RatingCount,
                game.Status,
                game.Monitored,
                game.Path,
                game.SizeOnDisk,
                game.IgdbId,
                game.SteamId,
                game.GogId,
                game.InstallPath,
                game.IsInstallable,
                game.ExecutablePath,
                game.IsExternal,
                game.Region,
                game.Languages,
                game.Revision,
                game.ProtonDbTier,
                uninstallerPath,
                canPlay = canPlay // Explicit property name
            });
        }

        [HttpPost]
        public async Task<ActionResult<Game>> Create([FromBody] Game game)
        {
            _logger.Info($"[GameController] [Create] Attempting to add game: '{game.Title}' (IGDB: {game.IgdbId})");

            if (game.PlatformId <= 0 || !PlatformDefinitions.PlatformDictionary.ContainsKey(game.PlatformId))
            {
                _logger.Warn($"[GameController] [Create] Rejected: PlatformId {game.PlatformId} is not a known platform.");
                return BadRequest(new { code = "invalid_platform", message = $"PlatformId {game.PlatformId} is not a known platform." });
            }

            try
            {
                // Create platform folder on disk if PlatformId is set
                if (game.PlatformId > 0 && string.IsNullOrEmpty(game.Path))
                {
                    var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                    if (platform != null)
                    {
                        var mediaSettings = _configService.LoadMediaSettings();
                        var libraryRoot = !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Directory.Exists(mediaSettings.DestinationPath)
                            ? mediaSettings.DestinationPath
                            : mediaSettings.FolderPath;

                        if (!string.IsNullOrEmpty(libraryRoot) && Directory.Exists(libraryRoot))
                        {
                            var effectiveFolder = platform.GetEffectiveFolderName(mediaSettings.FolderNamingMode);
                            var gamePath = mediaSettings.ResolveDestinationPath(
                                libraryRoot, effectiveFolder, game.Title,
                                game.Year > 0 ? game.Year : (int?)null);

                            Directory.CreateDirectory(gamePath);
                            game.Path = gamePath;
                            _logger.Info($"[GameController] [Create] Created game folder: {gamePath}");
                        }
                    }
                }

                var created = await _repository.AddAsync(game);
                _logger.Info($"[GameController] [Create] Success. Game ID: {created.Id}");
                return CreatedAtAction(nameof(GetById), new { id = created.Id }, created);
            }
            catch (Exception ex)
            {
                _logger.Error($"[GameController] [Create] FAILURE: {ex}");
                return StatusCode(500, $"Internal Server Error: {ex.Message}");
            }
        }

        [HttpPut("{id}")]
        public async Task<ActionResult<Game>> Update(int id, [FromBody] Game gameUpdate)
        {
            var existingGame = await _repository.GetByIdAsync(id);
            if (existingGame == null)
            {
                return NotFound();
            }

            // Check if IGDB ID has changed
            bool igdbIdChanged = gameUpdate.IgdbId.HasValue && gameUpdate.IgdbId != existingGame.IgdbId;

            // Apply updates
            if (gameUpdate.IgdbId.HasValue) existingGame.IgdbId = gameUpdate.IgdbId;
            if (!string.IsNullOrEmpty(gameUpdate.Title)) existingGame.Title = gameUpdate.Title;
            if (!string.IsNullOrEmpty(gameUpdate.InstallPath)) existingGame.InstallPath = gameUpdate.InstallPath;
            if (!string.IsNullOrEmpty(gameUpdate.ExecutablePath)) existingGame.ExecutablePath = gameUpdate.ExecutablePath;

            // If IGDB ID changed, fetch fresh metadata from IGDB
            if (igdbIdChanged)
            {
                try
                {
                    var metadataService = _metadataServiceFactory.CreateService();
                    var freshMetadata = await metadataService.GetGameMetadataAsync(existingGame.IgdbId.Value, "en");
                    
                    if (freshMetadata != null) {
                       existingGame.Title = freshMetadata.Title; 
                       existingGame.Overview = freshMetadata.Overview;
                       existingGame.Storyline = freshMetadata.Storyline;
                       existingGame.Year = freshMetadata.Year;
                       existingGame.ReleaseDate = freshMetadata.ReleaseDate;
                       existingGame.Rating = freshMetadata.Rating;
                       existingGame.Genres = freshMetadata.Genres;
                       
                       if (freshMetadata.Images != null) {
                           existingGame.Images = freshMetadata.Images;
                       }
                    }
                }
                catch (System.Exception ex)
                {
                    _logger.Error($"Error refreshing metadata: {ex.Message}");
                }
            }
            else if (!string.IsNullOrEmpty(gameUpdate.MetadataSource) && gameUpdate.MetadataSource == "ScreenScraper")
            {
                if (!string.IsNullOrEmpty(gameUpdate.Overview)) existingGame.Overview = gameUpdate.Overview;
                if (gameUpdate.Year > 0) existingGame.Year = gameUpdate.Year;
                if (!string.IsNullOrEmpty(gameUpdate.Developer)) existingGame.Developer = gameUpdate.Developer;
                if (!string.IsNullOrEmpty(gameUpdate.Publisher)) existingGame.Publisher = gameUpdate.Publisher;
                if (gameUpdate.Rating.HasValue) existingGame.Rating = gameUpdate.Rating;
                if (gameUpdate.Genres != null && gameUpdate.Genres.Count > 0) existingGame.Genres = gameUpdate.Genres;
                if (gameUpdate.Images != null)
                {
                    if (!string.IsNullOrEmpty(gameUpdate.Images.CoverUrl)) existingGame.Images.CoverUrl = gameUpdate.Images.CoverUrl;
                    if (!string.IsNullOrEmpty(gameUpdate.Images.CoverLargeUrl)) existingGame.Images.CoverLargeUrl = gameUpdate.Images.CoverLargeUrl;
                    if (!string.IsNullOrEmpty(gameUpdate.Images.BackgroundUrl)) existingGame.Images.BackgroundUrl = gameUpdate.Images.BackgroundUrl;
                    if (!string.IsNullOrEmpty(gameUpdate.Images.BannerUrl)) existingGame.Images.BannerUrl = gameUpdate.Images.BannerUrl;
                    if (!string.IsNullOrEmpty(gameUpdate.Images.BoxBackUrl)) existingGame.Images.BoxBackUrl = gameUpdate.Images.BoxBackUrl;
                    if (!string.IsNullOrEmpty(gameUpdate.Images.VideoUrl)) existingGame.Images.VideoUrl = gameUpdate.Images.VideoUrl;
                    if (gameUpdate.Images.Screenshots != null && gameUpdate.Images.Screenshots.Count > 0)
                        existingGame.Images.Screenshots = gameUpdate.Images.Screenshots;
                }
                existingGame.MetadataConfirmedByUser = true;
                existingGame.MetadataConfirmedAt = System.DateTime.UtcNow;
                existingGame.NeedsMetadataReview = false;
                _logger.Info($"[Game] Applied ScreenScraper metadata for game {id}: {existingGame.Title}");
            }

            // Pre-check: another game on this platform with the same Title + Region?
            // Catch it here so the caller gets a useful 409 instead of a raw
            // database violation. Region variants are allowed - the unique index
            // is (Title, PlatformId, Region), so only an exact 3-way match fires.
            try
            {
                var others = await _repository.GetAllLightAsync();
                var collision = others.FirstOrDefault(g =>
                    g.Id != id &&
                    g.PlatformId == existingGame.PlatformId &&
                    string.Equals(g.Title, existingGame.Title, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(g.Region ?? string.Empty, existingGame.Region ?? string.Empty, StringComparison.OrdinalIgnoreCase));
                if (collision != null)
                {
                    return Conflict(new
                    {
                        code = "duplicate_title",
                        message = $"Another library entry already has this title + platform + region. Rename this one, merge it into #{collision.Id}, or delete the duplicate.",
                        otherGameId = collision.Id,
                        otherGameTitle = collision.Title,
                        otherGamePath = collision.Path,
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Game] Pre-save duplicate check skipped: {ex.Message}");
            }

            Game? updated;
            try
            {
                updated = await _repository.UpdateAsync(id, existingGame);
            }
            catch (DuplicateGameException ex)
            {
                _logger.Warn($"[Game] Update blocked by unique constraint for id={id}: {ex.Message}");
                return Conflict(new
                {
                    code = "duplicate_title",
                    message = ex.Message,
                    conflictField = ex.ConflictField,
                });
            }

            try { await _localMediaExport.ExportMediaForGameAsync(existingGame); }
            catch (System.Exception ex) { _logger.Error($"[Game] Media export error: {ex.Message}"); }

            return Ok(updated);
        }

        /// Merge endpoint: take the `sourceId` game, carry its file/path info into
        /// `targetId`, and trash the source. Useful when the user discovers two
        /// rows for the same game (same region) and wants to keep only one entry.
        [HttpPost("{sourceId:int}/merge-into/{targetId:int}")]
        public async Task<ActionResult> MergeInto(int sourceId, int targetId)
        {
            if (sourceId == targetId) return BadRequest(new { message = "Source and target must differ." });

            var source = await _repository.GetByIdAsync(sourceId);
            var target = await _repository.GetByIdAsync(targetId);
            if (source == null || target == null) return NotFound();

            if (string.IsNullOrEmpty(target.Path) && !string.IsNullOrEmpty(source.Path))
                target.Path = source.Path;
            if (string.IsNullOrEmpty(target.ExecutablePath) && !string.IsNullOrEmpty(source.ExecutablePath))
                target.ExecutablePath = source.ExecutablePath;
            if (string.IsNullOrEmpty(target.Region) && !string.IsNullOrEmpty(source.Region))
                target.Region = source.Region;
            if (string.IsNullOrEmpty(target.Languages) && !string.IsNullOrEmpty(source.Languages))
                target.Languages = source.Languages;

            try
            {
                await _repository.UpdateAsync(target.Id, target);
            }
            catch (DuplicateGameException ex)
            {
                return Conflict(new { code = "duplicate_title", message = ex.Message });
            }

            // Delete the source metadata row. Files are kept in place - if the
            // user wants the file gone too, they can hit Remove on the row first.
            await _repository.DeleteAsync(sourceId);
            _logger.Info($"[Game] Merged {sourceId} into {targetId}");
            return Ok(new { merged = true, targetId });
        }

        // Fixes a row whose PlatformId disagrees with the folder the file
        // actually lives in. Walks up game.Path, finds the first platform folder
        // match, and writes that PlatformId back. Returns 409 if another row
        // already owns the title on the target platform (user should merge or
        // delete by hand in that case).
        [HttpPost("{id:int}/fix-platform-from-path")]
        public async Task<ActionResult> FixPlatformFromPath(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();
            if (string.IsNullOrEmpty(game.Path))
                return BadRequest(new { code = "no_path", message = "Game has no path on disk to infer platform from." });

            var resolved = PlatformDefinitions.ResolvePlatformFromPath(game.Path);
            if (resolved == null)
                return BadRequest(new { code = "no_match", message = $"Could not match any known platform folder inside '{game.Path}'." });

            if (resolved.Id == game.PlatformId)
                return Ok(new { changed = false, platformId = game.PlatformId, platformName = resolved.Name });

            // Look for an existing row on the target platform with the same
            // title - that's a collision we don't auto-merge. Let the user pick.
            var all = await _repository.GetAllLightAsync();
            var collision = all.FirstOrDefault(g => g.Id != id
                && g.PlatformId == resolved.Id
                && string.Equals(g.Title, game.Title, StringComparison.OrdinalIgnoreCase));
            if (collision != null)
            {
                return Conflict(new
                {
                    code = "duplicate_title",
                    message = $"'{game.Title}' already exists on {resolved.Name}.",
                    otherGameId = collision.Id,
                    otherGameTitle = collision.Title,
                    otherGamePath = collision.Path
                });
            }

            var previous = game.PlatformId;
            game.PlatformId = resolved.Id;
            try
            {
                await _repository.UpdateAsync(id, game);
            }
            catch (DuplicateGameException ex)
            {
                return Conflict(new { code = "duplicate_title", message = ex.Message });
            }

            _logger.Info($"[Game] Fixed platform for id={id}: {previous} → {resolved.Id} ({resolved.Name})");
            if (_progressNotifier != null)
            {
                try { await _progressNotifier.LibraryUpdatedAsync(); }
                catch (Exception ex) { _logger.Warn($"[Game] Hub notify failed: {ex.Message}"); }
            }
            return Ok(new { changed = true, platformId = resolved.Id, platformName = resolved.Name, previousPlatformId = previous });
        }

        // What the dialog showed: the delete only goes when that is still what would move
        public class DeleteExpectation
        {
            public List<string>? Paths { get; set; }
            public List<string>? Downloads { get; set; }
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id, [FromQuery] bool deleteFiles = false, [FromQuery] string? targetPath = null, [FromQuery] bool deleteDownloadFiles = false, [FromQuery] string? downloadPath = null,
            [FromBody(EmptyBodyBehavior = Microsoft.AspNetCore.Mvc.ModelBinding.EmptyBodyBehavior.Allow)] DeleteExpectation? expected = null)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var others = deleteFiles || deleteDownloadFiles ? (await _repository.GetAllLightAsync()).Where(g => g.Id != id).ToList() : new List<Game>();
            var paths = new List<string>();
            if (deleteFiles && !string.IsNullOrEmpty(game.Path))
            {
                var planned = PathsToTrash(game, targetPath, others);
                if (planned == null)
                {
                    _logger.Error($"[Delete] REFUSED to delete {game.Path}: it is a library or platform folder or holds other library entries.");
                    return Refused(game, others);
                }

                // A picked folder goes as it is or nothing happens, never something else in its place
                if (!string.IsNullOrEmpty(targetPath) && !(planned.Count == 1 && SamePath(planned[0], targetPath)))
                {
                    _logger.Warn($"[Delete] REFUSED picked folder {targetPath} for {game.Path}");
                    return Conflict(new { message = $"Not moving '{targetPath}' to the trash: only the game's folder, or a folder above it named after the game that holds no other games, can go as a whole. Nothing was deleted." });
                }
                paths = planned;
            }

            // The game's downloads are found here, a folder the caller names only goes when it is one of them
            var downloads = new List<string>();
            if (deleteDownloadFiles)
            {
                downloads = await DownloadsOfAsync(game, others);
                if (!string.IsNullOrEmpty(downloadPath) && !downloads.Any(d => SamePath(d, downloadPath)))
                {
                    _logger.Warn($"[Delete] REFUSED download folder {downloadPath} for {game.Title}");
                    return Conflict(new { message = $"Not moving '{downloadPath}' to the trash: it is not a download of this game. Only the game's own folder inside the download folder, holding no other download, can go. Nothing was deleted." });
                }
            }

            // What goes is what the dialog showed, or nothing
            if (expected != null && ((deleteFiles && !SameSet(paths, expected.Paths)) || (deleteDownloadFiles && !SameSet(downloads, expected.Downloads))))
            {
                _logger.Warn($"[Delete] REFUSED {game.Title}: what would go to the trash changed since the dialog showed it");
                return Conflict(new { message = "What would go to the trash changed since the dialog showed it. Nothing was deleted, open the dialog again to see what goes now.", paths, downloads });
            }

            var toTrash = paths.Concat(downloads).ToList();
            var moved = new List<TrashEntry>();
            var stuck = new List<TrashEntry>();
            string? error = null;
            foreach (var path in toTrash)
            {
                try
                {
                    var entry = await _trash.MoveAsync(path, id, game.Title);
                    if (entry != null)
                    {
                        moved.Add(entry);
                        _logger.Info($"[Delete] Moved {(entry.IsDirectory ? "directory" : "file")} to trash: {path} (entry {entry.Id})");
                    }
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    if (ex is TrashPartialMoveException partial) stuck.Add(partial.Entry);
                    _logger.Error($"[Delete] Error moving {path} to the trash: {ex.Message}");
                    break;
                }
            }

            // A path left behind means a move failed: what went comes back and the game stays, with all of its files
            var left = toTrash.FirstOrDefault(p => System.IO.File.Exists(p) || Directory.Exists(p));
            if (left != null)
            {
                foreach (var entry in moved)
                {
                    if (_trash.Restore(entry.Id)) continue;
                    stuck.Add(entry);
                    _logger.Error($"[Delete] Could not put {entry.OriginalPath} back from trash entry {entry.Id}");
                }
                var failed = $"Could not move '{left}' to the trash{(error == null ? "" : ": " + error)}.";
                return StatusCode(500, new
                {
                    message = stuck.Count == 0
                        ? $"{failed} The game stays in the library."
                        : $"{failed} The game stays in the library, but these are still in the trash: {string.Join(", ", stuck.Select(e => $"{e.OriginalPath} (entry {e.Id})"))}.",
                    stuck = stuck.Select(e => new { e.Id, e.OriginalPath }).ToList(),
                });
            }

            var removed = await _repository.DeleteAsync(id);
            if (!removed)
            {
                return NotFound();
            }

            return NoContent();
        }

        private static bool SameSet(List<string> planned, List<string>? shown) =>
            planned.Count == (shown?.Count ?? 0) && planned.All(p => shown!.Any(s => SamePath(p, s)));

        // What deleting the game moves to the trash, so the dialog can show it before anything happens: its files
        // (none, with the reason, when they can't go on their own) and its downloads
        [HttpGet("{id}/delete-plan")]
        public async Task<ActionResult> DeletePlan(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var others = (await _repository.GetAllLightAsync()).Where(g => g.Id != id).ToList();
            var paths = string.IsNullOrEmpty(game.Path) ? new List<string>() : PathsToTrash(game, null, others);
            return Ok(new { paths, refused = paths == null ? RefusedMessage(game) : null, downloads = await DownloadsOfAsync(game, others) });
        }

        private static string RefusedMessage(Game game) =>
            $"Refusing to delete '{game.Path}' - it is a library or platform folder or contains other games in the library. Fix the game's path or remove the game without deleting files.";

        private ActionResult Refused(Game game, List<Game> others)
        {
            var places = Places(game.Path!);
            return Conflict(new
            {
                message = RefusedMessage(game),
                otherGameIds = Held(others).Where(h => places.Any(p => IsSubPath(h.Path, p))).Select(h => h.Id).Distinct().Take(5).ToList(),
            });
        }

        // The downloads a game came from: where its grabs were imported from, else a folder in the download folder
        // named exactly like the game. Only paths inside a download folder that hold nothing of the library, no other
        // entry's files and no other download, so never a download root, category or platform folder.
        private async Task<List<string>> DownloadsOfAsync(Game game, List<Game> others)
        {
            var settings = _configService.LoadMediaSettings();
            var clients = _configService.LoadDownloadClients();
            var roots = new[] { settings.DownloadPath }.Concat(clients.Select(c => c.LocalPathMapping))
                .Where(r => !string.IsNullOrWhiteSpace(r) && Path.IsPathRooted(r)).Select(r => r!).ToList();
            if (roots.Count == 0) return new List<string>();

            var library = LibraryRoots().Select(l => RealDir(l)).ToList();
            var held = Held(others);
            var own = new[] { game.Path, game.ExecutablePath }.Where(p => !string.IsNullOrEmpty(p)).SelectMany(p => Places(p!)).ToList();
            var history = _history == null ? new List<RetroArr.Core.Download.History.DownloadHistoryEntry>() : await _history.GetWithSourcePathAsync();
            bool Own(string path)
            {
                if (!System.IO.File.Exists(path) && !Directory.Exists(path)) return false;
                var places = Places(path);
                return roots.Any(r => IsSubPath(path, r, PathCase) && !SamePath(path, r))
                    && !IsCriticalPath(path)
                    && !IsLibraryFolder(path, LibraryRoots()) && !places.Any(p => library.Any(l => IsSubPath(p, l)))
                    // a download named like a platform or a category is where the client sorts them
                    && !PlatformDefinitions.AllPlatforms.Any(p => p.MatchesFolderName(FolderName(path)))
                    && !clients.Any(c => string.Equals(c.Category, FolderName(path), StringComparison.OrdinalIgnoreCase))
                    // no other entry's files, and not the game's own library files either
                    && !held.Any(h => places.Any(p => IsSubPath(h.Path, p)))
                    && !places.Any(p => own.Any(o => IsSubPath(o, p) || IsSubPath(p, o)))
                    // not another game's download, and holding no other download
                    && !history.Any(h => h.GameId != game.Id && (IsSubPath(h.SourcePath!, path) || IsSubPath(path, h.SourcePath!)))
                    && !history.Any(h => IsSubPath(h.SourcePath!, path) && !SamePath(h.SourcePath!, path));
            }

            // A grab counts when its files went to this game: the import may have taken another entry of the title
            bool ImportedHere(RetroArr.Core.Download.History.DownloadHistoryEntry h) =>
                string.IsNullOrEmpty(h.DestinationPath) || IsSubPath(h.DestinationPath, game.Path ?? "") || IsSubPath(game.Path ?? "", h.DestinationPath);
            var grabbed = history.Where(h => h.GameId == game.Id && ImportedHere(h)).Select(h => h.SourcePath!).Where(Own).Distinct().ToList();
            if (grabbed.Count > 0) return grabbed;

            var title = NameKey(game.Title);
            if (title.Length == 0) return new List<string>();
            try
            {
                var level1 = Directory.Exists(settings.DownloadPath) ? Directory.GetDirectories(settings.DownloadPath) : Array.Empty<string>();
                var match = level1.Concat(level1.SelectMany(d => { try { return Directory.GetDirectories(d); } catch { return Array.Empty<string>(); } }))
                    .FirstOrDefault(d => NameKey(Path.GetFileName(d)) == title && Own(d));
                return match == null ? new List<string>() : new List<string> { match };
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Delete] Could not look for the download folder of '{game.Title}': {ex.Message}");
                return new List<string>();
            }
        }

        // Linux tells /psx/FF7 from /psx/ff7, Windows and macOS usually do not
        private static readonly StringComparison PathCase =
            OperatingSystem.IsWindows() || OperatingSystem.IsMacOS() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        internal static bool IsSubPath(string candidate, string root, StringComparison comparison = StringComparison.OrdinalIgnoreCase)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root)) return false;
            try
            {
                var r = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar, System.IO.Path.AltDirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
                var c = System.IO.Path.GetFullPath(candidate);
                return c.Equals(r.TrimEnd(System.IO.Path.DirectorySeparatorChar), comparison)
                    || c.StartsWith(r, comparison);
            }
            catch { return false; }
        }

        private static bool SamePath(string a, string b)
        {
            try { return string.Equals(Path.TrimEndingDirectorySeparator(Path.GetFullPath(a)), Path.TrimEndingDirectorySeparator(Path.GetFullPath(b)), PathCase); }
            catch { return false; }
        }

        // Folders with the links on the way to them resolved, once per request
        private readonly Dictionary<string, string> _realDirs = new(StringComparer.Ordinal);

        // The path with every link on the way and the path itself resolved, a few hops at most so a loop ends
        private string RealDir(string path, int hops = 0)
        {
            string full;
            try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch { return path; }
            if (_realDirs.TryGetValue(full, out var known)) return known;
            var parent = Path.GetDirectoryName(full);
            var real = parent == null ? full : Path.Combine(RealDir(parent, hops), Path.GetFileName(full));
            string? target = null;
            try { target = hops < 8 ? (Directory.Exists(real) ? (FileSystemInfo)new DirectoryInfo(real) : new FileInfo(real)).LinkTarget : null; }
            catch { }
            if (target != null) real = RealDir(Path.Combine(Path.GetDirectoryName(real) ?? real, target), hops + 1);
            return _realDirs[full] = real;
        }

        // Where a path lies: the links on the way resolved, the path itself kept, as a move takes a link as it is
        private string Located(string path)
        {
            string full;
            try { full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)); }
            catch { return path; }
            var parent = Path.GetDirectoryName(full);
            return parent == null ? full : Path.Combine(RealDir(parent), Path.GetFileName(full));
        }

        // A path both where it lies and where it leads: a library reached through a link and its real folder are one place
        private string[] Places(string path) => new[] { Located(path), RealDir(path) }.Distinct().ToArray();

        // Where the other entries' paths are
        private List<(int Id, string Path)> Held(List<Game> others) =>
            others.SelectMany(g => new[] { g.Path, g.ExecutablePath }.Where(p => !string.IsNullOrEmpty(p)).SelectMany(p => Places(p!)).Distinct().Select(p => (g.Id, p))).ToList();

        private bool Holds(List<(int Id, string Path)> held, string path)
        {
            var places = Places(path);
            return held.Any(h => places.Any(p => IsSubPath(h.Path, p)));
        }

        private List<string> LibraryRoots()
        {
            var settings = _configService.LoadMediaSettings();
            return new[] { settings.FolderPath, settings.DestinationPath }
                .Where(r => !string.IsNullOrWhiteSpace(r) && Path.IsPathRooted(r))
                .ToList();
        }

        // Letters and digits only, so "Half-Life" and "half life" are the same name
        private static string NameKey(string? name) => new string((name ?? "").Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());

        private static string FolderName(string path) => Path.GetFileName(Path.TrimEndingDirectorySeparator(Path.GetFullPath(path)));

        // The game folder, or a folder above it inside the library that the caller picked and that is named after
        // the game, goes as a whole unless it is a library or platform folder or holds other library entries.
        // Otherwise only the game's own file set goes. Null when there are files but none of them can go on their own.
        private List<string>? PathsToTrash(Game game, string? targetPath, List<Game> others)
        {
            var roots = LibraryRoots();
            var held = Held(others);

            var folders = new List<string> { game.Path! };
            // A folder named otherwise may hold more than this game: untracked files, other collections, saves
            if (!string.IsNullOrEmpty(targetPath) && Directory.Exists(game.Path) && IsSubPath(game.Path!, targetPath, PathCase)
                && roots.Any(r => IsSubPath(targetPath, r) && !IsSubPath(r, targetPath))
                && NameKey(FolderName(targetPath)) == NameKey(game.Title))
            {
                folders.Insert(0, targetPath);
            }

            foreach (var folder in folders)
            {
                if (IsCriticalPath(folder) || IsLibraryFolder(folder, roots) || Holds(held, folder)) continue;
                if (Directory.Exists(folder)) return new List<string> { folder };
                if (System.IO.File.Exists(folder)) return FileSetOf(folder, others);
            }

            var exe = game.ExecutablePath;
            if (!string.IsNullOrEmpty(exe) && System.IO.File.Exists(exe) && IsSubPath(Located(exe), RealDir(game.Path!), PathCase) && !Holds(held, exe))
            {
                return FileSetOf(exe, others);
            }

            return System.IO.File.Exists(game.Path) || Directory.Exists(game.Path) ? null : new List<string>();
        }

        // Library roots, the folders above them and folders named like a platform never belong to one game, unless the
        // folder lies where a game's own does: in a platform folder right below a root (windows/Pegasus).
        // Compared where links lead, so a root set through a link and its real folder are one place.
        private bool IsLibraryFolder(string path, List<string> roots)
        {
            var realRoots = roots.Select(r => RealDir(r)).ToList();
            bool Named(string folder) => PlatformDefinitions.AllPlatforms.Any(p => p.MatchesFolderName(Path.GetFileName(folder)));
            bool PlatformFolder(string? folder) => folder != null && Named(folder)
                && Path.GetDirectoryName(folder) is { } up && realRoots.Any(r => IsSubPath(r, up) && IsSubPath(up, r));
            return Places(path).Any(place => realRoots.Any(r => IsSubPath(r, place))
                || (Named(place) && !PlatformFolder(Path.GetDirectoryName(place))));
        }

        // Saves, states, screenshots, art and docs named like a disc image are the player's, not part of the game
        private static readonly Regex _playerFiles = new(@"^\.(srm|sav|dsv|mcr|mcd|rtc|state\d*|png|jpe?g|gif|bmp|webp|mp4|mkv|avi|webm|txt|nfo|pdf|md|html?|docx?)$", RegexOptions.IgnoreCase);

        // The file plus the tracks, discs and same-name files the resolver ties to it, taken only from its own
        // folder and only when no other library entry points at them or holds them in its own file set
        private List<string> FileSetOf(string file, List<Game> others)
        {
            var folder = RealDir(Path.GetDirectoryName(Path.GetFullPath(file))!);
            // a leftover entry for disc 2 keeps its cue and the tracks the cue names, and a playlist or cue in a folder
            // above keeps what it names from here
            var theirs = others.SelectMany(g => new[] { g.Path, g.ExecutablePath })
                .Where(p => System.IO.File.Exists(p) && Places(p!).Any(x => IsSubPath(x, folder)
                    || (Path.GetExtension(x).ToLowerInvariant() is ".m3u" or ".cue" or ".gdi" && IsSubPath(folder, Path.GetDirectoryName(x) ?? ""))))
                .SelectMany(p => FileSetResolver.Resolve(p!).AllFiles.Prepend(p!))
                .SelectMany(Places)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            var set = FileSetResolver.Resolve(file);
            return set.CompanionFiles.Where(f => !_playerFiles.IsMatch(Path.GetExtension(f))).Prepend(set.PrimaryFile)
                .Select(f => Path.GetFullPath(f))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Where(f => System.IO.File.Exists(f) && IsSubPath(Located(f), folder, PathCase) && !Places(f).Any(theirs.Contains))
                .ToList();
        }

        internal static bool IsCriticalPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var full = System.IO.Path.GetFullPath(path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
            var root = System.IO.Path.GetPathRoot(full);
            
            // 1. Root
            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;
            
            // 2. Common System Folders (Linux/Mac/Win)
            var sensitive = new[] { 
                "/", "/bin", "/boot", "/dev", "/etc", "/home", "/lib", "/proc", "/root", "/run", "/sbin", "/sys", "/tmp", "/usr", "/var",
                "/Users", "/Users/imaik", "/Users/imaik/Desktop", "/Users/imaik/Documents", "/Users/imaik/Downloads",
                "C:\\", "C:\\Windows", "C:\\Program Files", "C:\\Users"
            };

            foreach (var s in sensitive)
            {
                 // Exact match blocking
                 if (full.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
                 
                 // Also block if it's a DIRECT child of a very sensitive root? 
                 // e.g. /Users/imaik/Desktop/Juegos is OK. 
                 // /Users/imaik/Desktop is BLOCKED (Safe).
            }

            return false;
        }


        [HttpPost("{id}/uninstall")]
        public async Task<ActionResult> Uninstall(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound("Game not found");
            
            if (string.IsNullOrEmpty(game.Path) || !Directory.Exists(game.Path))
                return BadRequest("Game path not found or invalid.");

            var uninstaller = FindUninstaller(game.Path);
            if (!string.IsNullOrEmpty(uninstaller))
            {
                // Reuse LaunchInstaller logic but for uninstaller
                return LaunchInstaller(uninstaller);
            }

            return NotFound("No uninstaller found.");
        }


        [HttpPost("{id}/install")]
        public async Task<ActionResult> Install(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound("Game not found in repository");

            if (string.IsNullOrEmpty(game.Path)) return BadRequest("Game path is not set.");
            
            string targetPath = game.Path;
            _logger.Info($"[Install] Target Path: {targetPath}");

            // Case 0.1: Archive (Zip, Rar, 7z)
            if (_archiveService.IsArchive(targetPath))
            {
                var extractDir = Path.Combine(Path.GetDirectoryName(targetPath), Path.GetFileNameWithoutExtension(targetPath));
                if (_archiveService.Extract(targetPath, extractDir))
                {
                     // Update the game path to the new directory so subsequent scans/installs work
                     game.Path = extractDir;
                     await _repository.UpdateAsync(id, game);
                     
                     return Ok(new { message = $"Archive extracted to {extractDir}. Please Scan or Install again from the new folder.", path = extractDir });
                }
                else
                {
                    return BadRequest("Failed to extract archive.");
                }
            }

            // Case 0.2: ISO Image (MacOS and Windows supported)
            if (System.IO.File.Exists(targetPath) && 
                System.IO.Path.GetExtension(targetPath).Equals(".iso", System.StringComparison.OrdinalIgnoreCase))
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    var mountPoint = await MountIsoMacOS(targetPath);
                    if (!string.IsNullOrEmpty(mountPoint))
                    {
                        _logger.Info($"[Install] ISO Mounted at: {mountPoint}");
                        targetPath = mountPoint; 
                    }
                    else
                    {
                        return BadRequest("Failed to mount ISO image on macOS.");
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    var mountPoint = await MountIsoWindows(targetPath);
                    if (!string.IsNullOrEmpty(mountPoint))
                    {
                        _logger.Info($"[Install] ISO Mounted at: {mountPoint}");
                        targetPath = mountPoint;
                    }
                    else
                    {
                        return BadRequest("Failed to mount ISO image on Windows.");
                    }
                }
                else
                {
                    return BadRequest("ISO mounting and installation is not supported in Docker/Headless mode. Please install manually.");
                }
            }

            // 0.3 Manual Installer Override (Set via UI)
            if (!string.IsNullOrEmpty(game.InstallPath) && System.IO.File.Exists(game.InstallPath))
            {
                _logger.Info($"[Install] Using Manual Installer Override: {game.InstallPath}");
                return LaunchInstaller(game.InstallPath);
            }

            // Common Installer Discovery (Fuzzy + Depth 1)
            var installerPath = FindInstaller(targetPath, game.Title);
            if (installerPath != null)
            {
                return LaunchInstaller(installerPath);
            }

            return BadRequest($"No valid installer found in: {targetPath}");
        }

        [HttpPost("{id}/play")]
        public async Task<ActionResult> Play(int id)
        {
            _logger.Info($"[API] Play Request Received for Game ID: {id}");
            var game = await _repository.GetByIdAsync(id);
            if (game == null) 
            {
                _logger.Info($"[API] Game ID {id} not found.");
                return NotFound("Game not found");
            }

            _logger.Info($"[API] Launching Game: {game.Title} (SteamID: {game.SteamId})");

            try
            {
                await _launcherService.LaunchGameAsync(game);
                return Ok(new { message = $"Launching {game.Title}..." });
            }
            catch (System.Exception ex)
            {
                _logger.Error($"[Play] Error: {ex.Message}");
                return BadRequest(new { error = ex.Message });
            }
        }

        private string? FindInstaller(string rootPath, string? gameTitleHint = null)
        {
            if (string.IsNullOrEmpty(rootPath)) return null;

            // 1. If path is already an .exe, use it
            if (System.IO.File.Exists(rootPath) && System.IO.Path.GetExtension(rootPath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
            {
                return rootPath;
            }

            // 2. If directory, look for patterns
            if (System.IO.Directory.Exists(rootPath))
            {
                try
                {
                    var patterns = new[] { "setup*.exe", "install*.exe", "installer.exe", "game.exe" };
                    var candidates = new List<string>();

                    // Depth 0: Root
                    foreach (var pattern in patterns)
                        candidates.AddRange(System.IO.Directory.GetFiles(rootPath, pattern, System.IO.SearchOption.TopDirectoryOnly));

                    // Depth 1: Immediate subdirs
                    var subDirs = System.IO.Directory.GetDirectories(rootPath);
                    foreach (var subDir in subDirs)
                    {
                        foreach (var pattern in patterns)
                            candidates.AddRange(System.IO.Directory.GetFiles(subDir, pattern, System.IO.SearchOption.TopDirectoryOnly));
                    }

                    if (!candidates.Any()) return null;

                    // Prioritization logic:
                    // 1. Exact match if possible (or containing game title)
                    if (!string.IsNullOrEmpty(gameTitleHint))
                    {
                        var bestMatch = candidates.FirstOrDefault(c => 
                            System.IO.Path.GetFileNameWithoutExtension(c).Contains(gameTitleHint, StringComparison.OrdinalIgnoreCase));
                        if (bestMatch != null) return bestMatch;
                    }

                    // 2. Smart default prioritized names
                    var defaults = new[] { "setup.exe", "install.exe", "installer.exe" };
                    foreach (var def in defaults)
                    {
                        var match = candidates.FirstOrDefault(c => System.IO.Path.GetFileName(c).Equals(def, StringComparison.OrdinalIgnoreCase));
                        if (match != null) return match;
                    }

                    // 3. Fallback: Heaviest file (usually the main installer)
                    return candidates.OrderByDescending(c => new System.IO.FileInfo(c).Length).FirstOrDefault();
                }
                catch { return null; }
            }

            return null;
        }



        private ActionResult LaunchInstaller(string path)
        {
            try 
            {
                var startInfo = new System.Diagnostics.ProcessStartInfo();
                
                // GOG / Inno Setup Detection
                var fileName = System.IO.Path.GetFileName(path).ToLower();
                var isGog = fileName.StartsWith("setup_") || fileName.StartsWith("setup.exe");
                var silentArgs = "/VERYSILENT /SUPPRESSMSGBOXES /NORESTART /SP-";

                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    startInfo.FileName = path;
                    startInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(path);
                    startInfo.UseShellExecute = true; // Use shell for .exe on Windows
                    
                    if (isGog) 
                    {
                        _logger.Info("[Install] Detected likely GOG/Inno Installer. Applying Silent Flags.");
                        startInfo.Arguments = silentArgs;
                    }
                }
                else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
                {
                    // macOS -> Use 'open' command which delegates to system association (Crossover, Wine, etc.)
                    _logger.Info($"[Install-Debug] macOS detected. Delegating to system 'open' for: {path}");
                    _logger.Info($"[Install-Debug] Command: /usr/bin/open \"{path}\"");
                    
                    startInfo.FileName = "open";
                    startInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(path);
                    
                    // Arguments for open: just the file path.
                    startInfo.Arguments = $"\"{path}\"";
                    startInfo.UseShellExecute = false;
                    
                    // Capture output to log potential OS errors
                    startInfo.RedirectStandardError = true;
                    startInfo.RedirectStandardOutput = true;
                }
                else
                {
                    // Linux/Docker -> Try Wine
                    _logger.Info($"[Install] Linux/Docker detected. Attempting to launch via Wine: {path}");
                    
                    startInfo.FileName = "wine";
                    startInfo.WorkingDirectory = System.IO.Path.GetDirectoryName(path);
                    
                    var wineArgs = $"\"{path}\"";
                    if (isGog) wineArgs += $" {silentArgs}";
                    
                    startInfo.Arguments = wineArgs;
                    startInfo.UseShellExecute = false; 
                }

                System.Diagnostics.Process.Start(startInfo);
                return Ok(new { message = $"Installer launched: {System.IO.Path.GetFileName(path)}" });
            }
            catch (System.Exception ex)
            {
                _logger.Error($"[Install] Launch error: {ex.Message}");
                return StatusCode(500, $"Error launching installer: {ex.Message}");
            }
        }

        [HttpDelete("all")]
        public async Task<ActionResult> DeleteAll()
        {
            await _repository.DeleteAllAsync();
            return NoContent();
        }

        private bool IsPathInstallable(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // 1. Handle file directly (Archive or ISO or EXE)
            if (System.IO.File.Exists(path))
            {
                var ext = System.IO.Path.GetExtension(path).ToLower();
                if (ext == ".exe" || ext == ".iso") return true;
                if (_archiveService.IsArchive(path)) return true;
                return false;
            }

            // 2. Handle directory via FindInstaller
            return FindInstaller(path) != null;
        }

        private async Task<string?> MountIsoMacOS(string isoPath)
        {
            try
            {
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "hdiutil",
                        Arguments = $"mount \"{isoPath}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0)
                {
                    // Output format: /dev/diskXsY   Apple_HFS   /Volumes/VolumeName
                    // We need to capture the /Volumes/... part
                    var match = Regex.Match(output, @"(/Volumes/.+)");
                    if (match.Success)
                    {
                        return match.Groups[1].Value.Trim();
                    }
                }
                else
                {
                     string error = await process.StandardError.ReadToEndAsync();
                     _logger.Error($"[Mount] Error: {error}");
                }
            }
            catch (System.Exception ex)
            {
                _logger.Error($"[Mount] Exception: {ex.Message}");
            }
            return null;
        }

        private async Task<string?> MountIsoWindows(string isoPath)
        {
            try
            {
                // PowerShell command to mount and get the drive letter
                // Mount-DiskImage -ImagePath "C:\path\to.iso" -PassThru | Get-Volume | Select-Object -ExpandProperty DriveLetter
                var psCommand = $"Mount-DiskImage -ImagePath \"{isoPath}\" -PassThru | Get-Volume | Select-Object -ExpandProperty DriveLetter";
                
                var process = new System.Diagnostics.Process
                {
                    StartInfo = new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = "powershell",
                        Arguments = $"-Command \"{psCommand}\"",
                        RedirectStandardOutput = true,
                        RedirectStandardError = true,
                        UseShellExecute = false,
                        CreateNoWindow = true
                    }
                };

                process.Start();
                string output = await process.StandardOutput.ReadToEndAsync();
                await process.WaitForExitAsync();

                if (process.ExitCode == 0 && !string.IsNullOrWhiteSpace(output))
                {
                    var driveLetter = output.Trim().Substring(0, 1);
                    return $"{driveLetter}:\\";
                }
                else
                {
                     string error = await process.StandardError.ReadToEndAsync();
                     _logger.Error($"[Mount-Win] Error: {error}");
                }
            }
            catch (System.Exception ex)
            {
                _logger.Error($"[Mount-Win] Exception: {ex.Message}");
            }
            return null;
        }

        private string? FindUninstaller(string? rootPath)
        {
            if (string.IsNullOrEmpty(rootPath) || !System.IO.Directory.Exists(rootPath)) return null;

            try
            {
                var patterns = new[] { "unins*.exe", "uninstall.exe", "*uninstall*.exe", "setup.exe" }; // setup.exe is sometimes also the uninstaller
                var candidates = new List<string>();

                foreach (var pattern in patterns)
                {
                    candidates.AddRange(System.IO.Directory.GetFiles(rootPath, pattern, System.IO.SearchOption.TopDirectoryOnly));
                }

                // Look in common subfolders
                var subDirs = new[] { "bin", "bin64", "tools" };
                foreach (var sub in subDirs)
                {
                    var subPath = System.IO.Path.Combine(rootPath, sub);
                    if (System.IO.Directory.Exists(subPath))
                    {
                        foreach (var pattern in patterns)
                            candidates.AddRange(System.IO.Directory.GetFiles(subPath, pattern, System.IO.SearchOption.TopDirectoryOnly));
                    }
                }

                if (!candidates.Any()) return null;

                // Prioritize "unins" followed by "uninstall"
                var prioritized = candidates
                    .OrderBy(c => {
                        var name = System.IO.Path.GetFileName(c).ToLower();
                        if (name.StartsWith("unins")) return 0;
                        if (name.Contains("uninstall")) return 1;
                        return 2;
                    })
                    .ThenByDescending(c => new System.IO.FileInfo(c).Length)
                    .FirstOrDefault();

                return prioritized;
            }
            catch { return null; }
        }

        [HttpGet("{id}/similar")]
        public async Task<ActionResult> GetSimilarGames(int id, [FromQuery] int limit = 10)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null)
                return NotFound();

            if (!game.IgdbId.HasValue)
                return Ok(new List<object>()); // No IGDB ID, can't find similar games

            try
            {
                var metadataService = _metadataServiceFactory.CreateService();
                var igdbClient = GetIgdbClient();
                
                if (igdbClient == null)
                    return Ok(new List<object>());

                var similarGames = await igdbClient.GetSimilarGamesAsync(game.IgdbId.Value, limit);

                var results = similarGames.Select(g => new
                {
                    IgdbId = g.Id,
                    g.Name,
                    CoverUrl = g.Cover != null ? RetroArr.Core.MetadataSource.Igdb.IgdbClient.GetImageUrl(g.Cover.ImageId, RetroArr.Core.MetadataSource.Igdb.ImageSize.CoverBig) : null,
                    Year = g.FirstReleaseDate.HasValue ? DateTimeOffset.FromUnixTimeSeconds(g.FirstReleaseDate.Value).Year : (int?)null,
                    Rating = g.Rating,
                    Genres = g.Genres.Select(ge => ge.Name).ToList(),
                    Platforms = g.Platforms.Select(p => !string.IsNullOrEmpty(p.Abbreviation) ? p.Abbreviation : p.Name).ToList()
                });

                return Ok(results);
            }
            catch (Exception ex)
            {
                _logger.Error($"[API] Error getting similar games: {ex.Message}");
                return Ok(new List<object>());
            }
        }

        // Deterministic resolution from game.PlatformId + game.Title via MediaSettings pattern.
        // Multi-platform entries each get their own folder.
        private string? ResolveGameFolder(Game game)
        {
            _logger.Info($"[ResolveGameFolder] Resolving for '{game.Title}' (ID: {game.Id}, PlatformId: {game.PlatformId}, game.Path: '{game.Path ?? "null"}')");

            // 1. If game already has a valid directory path, use it
            if (!string.IsNullOrEmpty(game.Path) && Directory.Exists(game.Path))
            {
                _logger.Info($"[ResolveGameFolder] '{game.Title}' -> using game.Path (dir): {game.Path}");
                return game.Path;
            }

            // 2. If game.Path is a file, return its parent directory
            if (!string.IsNullOrEmpty(game.Path) && System.IO.File.Exists(game.Path))
            {
                var parentDir = Path.GetDirectoryName(game.Path);
                _logger.Info($"[ResolveGameFolder] '{game.Title}' -> using game.Path (file parent): {parentDir}");
                return parentDir;
            }

            // 3. Compute deterministically from game.PlatformId + game.Title + MediaSettings
            var mediaSettings = _configService.LoadMediaSettings();

            // Accept any non-empty rooted path. The folder will be created on
            // demand by /folder or /gog-download; a missing-on-disk root just
            // means the user hasn't run a scan yet, not a misconfiguration.
            var libraryRoot = !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Path.IsPathRooted(mediaSettings.DestinationPath)
                ? mediaSettings.DestinationPath
                : !string.IsNullOrEmpty(mediaSettings.FolderPath) && Path.IsPathRooted(mediaSettings.FolderPath)
                    ? mediaSettings.FolderPath
                    : null;

            _logger.Info($"[ResolveGameFolder] MediaSettings: DestinationPath='{mediaSettings.DestinationPath}', FolderPath='{mediaSettings.FolderPath}', libraryRoot='{libraryRoot ?? "null"}'");

            if (string.IsNullOrEmpty(libraryRoot))
            {
                _logger.Info($"[ResolveGameFolder] '{game.Title}' -> null (no library root configured)");
                return null;
            }

            // Platform folder from game's PlatformId (e.g. windows, steam, linux, macos)
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
            var platformFolder = platform?.GetEffectiveFolderName(mediaSettings.FolderNamingMode) ?? "windows";
            _logger.Info($"[ResolveGameFolder] Platform: Id={game.PlatformId}, FolderName='{platformFolder}'");

            // {Library}/{Platform}/{Title} computed from settings pattern
            var resolvedPath = mediaSettings.ResolveDestinationPath(libraryRoot, platformFolder, game.Title, game.Year > 0 ? game.Year : (int?)null);
            _logger.Info($"[ResolveGameFolder] '{game.Title}' -> computed: {resolvedPath} (exists: {Directory.Exists(resolvedPath)})");
            return resolvedPath;
        }

        [HttpGet("{id}/files")]
        public async Task<ActionResult> GetGameFiles(int id)
        {
            try
            {
                var game = await _repository.GetByIdAsync(id);
                if (game == null) return NotFound();

                _logger.Info($"[GetGameFiles] id={id}, title='{game.Title}', platformId={game.PlatformId}, path='{game.Path ?? "null"}'");

                var gamePath = ResolveGameFolder(game);
                var resolvedFromSettings = string.IsNullOrEmpty(game.Path) || !Directory.Exists(game.Path);
                bool folderExists = !string.IsNullOrEmpty(gamePath) && Directory.Exists(gamePath);

                _logger.Info($"[GetGameFiles] gamePath='{gamePath ?? "null"}', folderExists={folderExists}, resolvedFromSettings={resolvedFromSettings}");

                bool isSingleFile = !string.IsNullOrEmpty(game.Path) && System.IO.File.Exists(game.Path) && !Directory.Exists(game.Path);

                // A folder computed from the settings only serves the listing. The scanner links a game to its files.
                if (string.IsNullOrEmpty(gamePath) && !isSingleFile)
                    return Ok(new { files = Array.Empty<object>(), gamePath = (string?)null, resolvedPath = (string?)null, folderExists = false });

                var files = new List<object>();
                long totalSizeBytes = 0;

                // A game without a folder of its own (a single file, or one in a library or platform folder) lists what
                // the download serves: the file set of its main file
                var shared = !isSingleFile && folderExists && (IsCriticalPath(gamePath!) || IsLibraryFolder(gamePath!, LibraryRoots()));
                var main = isSingleFile ? game.Path : shared && System.IO.File.Exists(game.ExecutablePath) ? game.ExecutablePath : null;

                if (main != null)
                {
                    // pull cue/gdi/m3u companions so the bin tracks show up too
                    var fileSet = FileSetResolver.Resolve(main);
                    var parentDir = gamePath!;
                    var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var member in fileSet.AllFiles)
                    {
                        if (string.IsNullOrEmpty(member)) continue;
                        if (!System.IO.File.Exists(member)) continue;
                        var fullMember = Path.GetFullPath(member);
                        // only what the download serves: members inside the game's folder
                        if (!IsSubPath(fullMember, parentDir, PathCase) || !seen.Add(fullMember)) continue;

                        var fi = new FileInfo(member);
                        totalSizeBytes += fi.Length;
                        files.Add(new
                        {
                            name = fi.Name,
                            // relative to the folder the download resolves against, so a disc in CD1/ keeps its folder
                            relativePath = Path.GetRelativePath(parentDir, fi.FullName).Replace('\\', '/'),
                            fullPath = fi.FullName,
                            size = fi.Length,
                            formattedSize = FormatFileSize(fi.Length),
                            extension = fi.Extension,
                            lastModified = fi.LastWriteTimeUtc,
                            fileType = "Main"
                        });
                    }
                }
                else if (folderExists && !shared)
                {
                    try
                    {
                        var rootDir = new DirectoryInfo(gamePath);
                        foreach (var fi in rootDir.EnumerateFiles("*", SearchOption.AllDirectories))
                        {
                            if (fi.Name.StartsWith(".")) continue;

                            totalSizeBytes += fi.Length;
                            var relativePath = Path.GetRelativePath(gamePath, fi.FullName).Replace('\\', '/');
                            var fileType = ClassifyFileType(relativePath, fi.Name);
                            files.Add(new
                            {
                                name = fi.Name,
                                relativePath,
                                fullPath = fi.FullName,
                                size = fi.Length,
                                formattedSize = FormatFileSize(fi.Length),
                                extension = fi.Extension,
                                lastModified = fi.LastWriteTimeUtc,
                                fileType
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[GetGameFiles] Error listing files: {ex.Message}");
                        return Ok(new { files = Array.Empty<object>(), gamePath, resolvedPath = gamePath, folderExists, error = ex.Message });
                    }
                }

                // Also load DB-tracked supplementary files (from shared folders like Updates+DLCs)
                var dbFiles = game.GameFiles?
                    .Where(gf => gf.FileType != "Main")
                    .Select(gf => new
                    {
                        name = Path.GetFileName(gf.RelativePath),
                        relativePath = gf.RelativePath,
                        fullPath = (string?)null,
                        size = gf.Size,
                        formattedSize = FormatFileSize(gf.Size),
                        extension = Path.GetExtension(gf.RelativePath),
                        lastModified = gf.DateAdded,
                        fileType = gf.FileType,
                        version = gf.Version,
                        contentName = gf.ContentName,
                        titleId = gf.TitleId,
                        serial = gf.Serial
                    }).ToList() ?? new();

                int mainCount = files.Count(f => ((dynamic)f).fileType == "Main");
                int patchCount = files.Count(f => ((dynamic)f).fileType == "Patch") + dbFiles.Count(f => f.fileType == "Patch");
                int dlcCount = files.Count(f => ((dynamic)f).fileType == "DLC") + dbFiles.Count(f => f.fileType == "DLC");

                return Ok(new
                {
                    files,
                    supplementaryFiles = dbFiles,
                    gamePath = game.Path,
                    resolvedPath = gamePath,
                    folderExists,
                    resolvedFromSettings,
                    totalFiles = files.Count,
                    totalSize = FormatFileSize(totalSizeBytes),
                    counts = new { main = mainCount, patches = patchCount, dlc = dlcCount }
                });
            }
            catch (Exception ex)
            {
                _logger.Info($"[GetGameFiles] UNHANDLED ERROR for game {id}: {ex}");
                return StatusCode(500, new { error = ex.Message, detail = ex.StackTrace });
            }
        }

        // Sonarr/Radarr-style {Library}/{platformFolder}/{title}/ - assigns to game.Path if not set
        [HttpPost("{id}/folder")]
        public async Task<ActionResult> CreateGameFolder(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var resolvedPath = ResolveGameFolder(game);
            if (string.IsNullOrEmpty(resolvedPath))
            {
                var ms = _configService.LoadMediaSettings();
                return BadRequest(new
                {
                    success = false,
                    message = $"Could not resolve game folder. FolderPath='{ms.FolderPath}', DestinationPath='{ms.DestinationPath}'. Set Library Folder under Settings → Media."
                });
            }

            try
            {
                if (!Directory.Exists(resolvedPath))
                {
                    Directory.CreateDirectory(resolvedPath);
                    _logger.Info($"[API] Created game folder: {resolvedPath}");
                }

                // Assign path to game if not already set
                if (string.IsNullOrEmpty(game.Path) || !Directory.Exists(game.Path))
                {
                    game.Path = resolvedPath;
                    await _repository.UpdateAsync(game.Id, game);
                    _logger.Info($"[API] Assigned folder to game '{game.Title}': {resolvedPath}");
                }

                return Ok(new { success = true, path = resolvedPath, message = $"Folder ready: {resolvedPath}" });
            }
            catch (Exception ex)
            {
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        // A plain download link can't carry the API key header, so the app asks for a signed one
        [HttpGet("{id}/files/download-link")]
        public ActionResult GetDownloadLink(int id, [FromQuery] string path)
        {
            if (string.IsNullOrEmpty(path))
                return BadRequest("Path parameter is required");
            return Ok(new { url = SignedLink($"/api/v3/game/{id}/files/download?path={Uri.EscapeDataString(path)}") });
        }

        private string SignedLink(string url) =>
            RetroArr.Api.V3.Auth.SignedUrl.Sign(url, _apiKeyService.GetApiKey(), DateTimeOffset.UtcNow);

        [HttpGet("{id}/files/download")]
        public async Task<ActionResult> DownloadGameFile(int id, [FromQuery] string path)
        {
            if (string.IsNullOrEmpty(path))
                return BadRequest("Path parameter is required");

            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var gameFolderPath = ResolveGameFolder(game);
            if (string.IsNullOrEmpty(gameFolderPath))
                return BadRequest("Game has no file path configured");
            if (!Directory.Exists(gameFolderPath))
                return NotFound("Game folder does not exist");

            var folder = Path.GetFullPath(gameFolderPath);
            var fullPath = Path.GetFullPath(Path.Combine(folder, path));
            // Block .. escape out of the game folder, also into a sibling whose name starts with the folder's or differs
            // only in case, and links in it that lead out
            if (!IsSubPath(fullPath, folder, PathCase) || LeadsOut(fullPath, folder))
                return BadRequest("Invalid file path");

            // A game without a folder of its own (a single file, or one in a library or platform folder) serves only
            // the file set of its main file
            var shared = IsCriticalPath(folder) || IsLibraryFolder(folder, LibraryRoots());
            var main = System.IO.File.Exists(game.Path) && !Directory.Exists(game.Path) ? game.Path : shared ? game.ExecutablePath : null;
            if (main != null ? !FileSetResolver.Resolve(main).AllFiles.Any(f => SamePath(f, fullPath)) : shared)
                return BadRequest("Invalid file path");

            if (!System.IO.File.Exists(fullPath))
                return NotFound("File not found");

            var fileName = Path.GetFileName(fullPath);
            var contentType = "application/octet-stream";
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, contentType, fileName, enableRangeProcessing: true);
        }

        // A link below the folder can point anywhere, so each one on the way has to resolve back inside it
        private static bool LeadsOut(string fullPath, string folder)
        {
            try
            {
                for (string? p = fullPath; p != null && !SamePath(p, folder); p = Path.GetDirectoryName(p))
                {
                    FileSystemInfo info = Directory.Exists(p) ? new DirectoryInfo(p) : new FileInfo(p);
                    if (info.LinkTarget != null && !IsSubPath(info.ResolveLinkTarget(true)?.FullName ?? "", folder, PathCase)) return true;
                }
                return false;
            }
            catch { return true; }
        }

        // RetroBat/Batocera convention: {platform}/images/ + {platform}/videos/,
        // matched against ROM filename without extension.
        [HttpGet("{id}/local-media")]
        public async Task<ActionResult> GetLocalMedia(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var images = new List<object>();
            var videos = new List<object>();

            // Determine platform directory from game path
            string? platformDir = null;
            string? romBaseName = null;

            // Single-file ROM: game.Path points to the ROM file
            if (!string.IsNullOrEmpty(game.Path) && System.IO.File.Exists(game.Path))
            {
                platformDir = Path.GetDirectoryName(game.Path);
                romBaseName = Path.GetFileNameWithoutExtension(game.Path);
            }
            // Folder-based game: game.Path is the game folder, parent is platform dir
            else if (!string.IsNullOrEmpty(game.Path) && Directory.Exists(game.Path))
            {
                platformDir = Path.GetDirectoryName(game.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
                romBaseName = Path.GetFileName(game.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
            }

            if (string.IsNullOrEmpty(platformDir) || string.IsNullOrEmpty(romBaseName))
                return Ok(new { images, videos });

            // Scan images/ subdirectory
            var imagesDir = Path.Combine(platformDir, "images");
            if (Directory.Exists(imagesDir))
            {
                var imageExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".webp", ".bmp", ".gif" };
                try
                {
                    foreach (var file in Directory.GetFiles(imagesDir))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var ext = Path.GetExtension(file);
                        if (!imageExtensions.Contains(ext)) continue;

                        // Match: filename starts with ROM base name
                        if (!fileName.StartsWith(romBaseName, StringComparison.OrdinalIgnoreCase)) continue;

                        // Determine image type from suffix
                        var suffix = fileName.Substring(romBaseName.Length);
                        var imageType = suffix.ToLowerInvariant() switch
                        {
                            "-thumb" => "cover",
                            "-image" => "screenshot",
                            "-boxback" => "boxback",
                            "-marquee" => "marquee",
                            "-wheel" => "wheel",
                            "-fanart" => "fanart",
                            "-bezel" => "bezel",
                            "-mix" => "mix",
                            "" => "cover",
                            _ => suffix.TrimStart('-')
                        };

                        images.Add(new
                        {
                            type = imageType,
                            fileName = Path.GetFileName(file),
                            fullPath = file,
                            url = SignedLink($"/api/v3/game/{id}/local-media/file?path={Uri.EscapeDataString(Path.GetFileName(file))}&folder=images")
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[LocalMedia] Error scanning images dir: {ex.Message}");
                }
            }

            // Scan videos/ subdirectory
            var videosDir = Path.Combine(platformDir, "videos");
            if (Directory.Exists(videosDir))
            {
                var videoExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".mp4", ".mkv", ".avi", ".webm", ".mov" };
                try
                {
                    foreach (var file in Directory.GetFiles(videosDir))
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        var ext = Path.GetExtension(file);
                        if (!videoExtensions.Contains(ext)) continue;

                        if (!fileName.StartsWith(romBaseName, StringComparison.OrdinalIgnoreCase)) continue;

                        var suffix = fileName.Substring(romBaseName.Length);
                        var videoType = suffix.ToLowerInvariant() switch
                        {
                            "-video" => "gameplay",
                            "" => "gameplay",
                            _ => suffix.TrimStart('-')
                        };

                        videos.Add(new
                        {
                            type = videoType,
                            fileName = Path.GetFileName(file),
                            fullPath = file,
                            size = new FileInfo(file).Length,
                            url = SignedLink($"/api/v3/game/{id}/local-media/file?path={Uri.EscapeDataString(Path.GetFileName(file))}&folder=videos")
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error($"[LocalMedia] Error scanning videos dir: {ex.Message}");
                }
            }

            return Ok(new { images, videos, platformDir, romBaseName });
        }

        // HTTP range requests supported so video can stream
        [HttpGet("{id}/local-media/file")]
        public async Task<ActionResult> ServeLocalMediaFile(int id, [FromQuery] string path, [FromQuery] string folder = "images")
        {
            if (string.IsNullOrEmpty(path))
                return BadRequest("path parameter is required");

            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            // Determine platform directory
            string? platformDir = null;
            if (!string.IsNullOrEmpty(game.Path) && System.IO.File.Exists(game.Path))
                platformDir = Path.GetDirectoryName(game.Path);
            else if (!string.IsNullOrEmpty(game.Path) && Directory.Exists(game.Path))
                platformDir = Path.GetDirectoryName(game.Path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));

            if (string.IsNullOrEmpty(platformDir))
                return NotFound("Cannot determine platform directory");

            // Only allow images/ or videos/ subdirectories
            if (folder != "images" && folder != "videos")
                return BadRequest("folder must be 'images' or 'videos'");

            var mediaDir = Path.Combine(platformDir, folder);
            var fullPath = Path.GetFullPath(Path.Combine(mediaDir, path));

            // Security: ensure path stays within the media directory
            if (!fullPath.StartsWith(Path.GetFullPath(mediaDir), StringComparison.OrdinalIgnoreCase))
                return BadRequest("Invalid file path");

            if (!System.IO.File.Exists(fullPath))
                return NotFound("File not found");

            var ext = Path.GetExtension(fullPath).ToLowerInvariant();
            var contentType = ext switch
            {
                ".jpg" or ".jpeg" => "image/jpeg",
                ".png" => "image/png",
                ".webp" => "image/webp",
                ".gif" => "image/gif",
                ".bmp" => "image/bmp",
                ".mp4" => "video/mp4",
                ".mkv" => "video/x-matroska",
                ".avi" => "video/x-msvideo",
                ".webm" => "video/webm",
                ".mov" => "video/quicktime",
                _ => "application/octet-stream"
            };

            var fileInfo = new FileInfo(fullPath);

            // Support range requests for video streaming
            if (contentType.StartsWith("video/"))
            {
                return PhysicalFile(fullPath, contentType, enableRangeProcessing: true);
            }

            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, contentType);
        }

        [HttpPost("{id}/gog-download")]
        public async Task<ActionResult> DownloadGogToGameFolder(int id, [FromBody] GogGameDownloadRequest request)
        {
            try
            {
                var game = await _repository.GetByIdAsync(id);
                if (game == null) return NotFound();

                var gogSettings = _configService.LoadGogSettings();
                if (!gogSettings.IsConfigured || string.IsNullOrEmpty(gogSettings.RefreshToken))
                    return BadRequest(new { success = false, message = "GOG not configured. Please authenticate in Settings." });

                var mediaSettings = _configService.LoadMediaSettings();

                // Stage to MediaSettings.DownloadPath. Final move to the library
                // folder happens after completion via PostDownloadProcessor, which
                // also routes Patches/DLC/Updates into subfolders.
                var stagingRoot = !string.IsNullOrWhiteSpace(mediaSettings.DownloadPath)
                    ? mediaSettings.DownloadPath
                    : Path.Combine(_configService.GetConfigDirectory(), "downloads");
                var stagingDir = Path.Combine(stagingRoot, "gog", SanitizeForPath(game.Title));

                try { Directory.CreateDirectory(stagingDir); }
                catch (Exception ex)
                {
                    _logger.Error($"[GOG] Could not create staging dir '{stagingDir}': {ex.Message}");
                    return BadRequest(new { success = false, message = $"Cannot create download staging folder '{stagingDir}': {ex.Message}" });
                }

                const string GogClientId = "46899977096215655";
                const string GogClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";

                _logger.Info($"[GOG] gog-download: game={id}, manualUrl={request.ManualUrl}, staging={stagingDir}");

                var client = new GogClient(gogSettings.RefreshToken);
                var refreshed = await client.RefreshTokenAsync(GogClientId, GogClientSecret);
                if (!refreshed)
                {
                    _logger.Error("[GOG] token refresh failed");
                    return BadRequest(new { success = false, message = "Failed to authenticate with GOG" });
                }

                var downloadUrl = await client.GetDownloadUrlAsync(request.ManualUrl);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _logger.Error($"[GOG] GetDownloadUrlAsync returned null for {request.ManualUrl}");
                    return BadRequest(new { success = false, message = "Failed to get download URL from GOG" });
                }

                var fileName = ResolveGogFileName(request, downloadUrl);
                var filePath = Path.Combine(stagingDir, fileName);
                var trackId = Guid.NewGuid().ToString("N")[..8];
                var tracker = _gogDownloadTracker;

                _ = Task.Run(async () =>
                {
                    System.Threading.CancellationToken ct = default;
                    try
                    {
                        using var httpClient = new System.Net.Http.HttpClient();
                        httpClient.Timeout = TimeSpan.FromHours(2);
                        using var response = await httpClient.GetAsync(downloadUrl, System.Net.Http.HttpCompletionOption.ResponseHeadersRead);
                        response.EnsureSuccessStatusCode();

                        var cdHeader = response.Content.Headers.ContentDisposition?.FileName?.Trim('"', ' ');
                        if (!string.IsNullOrEmpty(cdHeader) && !Path.HasExtension(fileName) && Path.HasExtension(cdHeader))
                        {
                            fileName += Path.GetExtension(cdHeader);
                            filePath = Path.Combine(stagingDir, fileName);
                            _logger.Info($"[GOG] Filename ext from Content-Disposition: {fileName}");
                        }

                        var totalBytes = response.Content.Headers.ContentLength;
                        ct = tracker.Start(trackId, game.Title, fileName, filePath, totalBytes);

                        long totalRead = 0;
                        // Scoped using so the file handle closes BEFORE
                        // PostDownloadProcessor tries to move/copy it.
                        using (var contentStream = await response.Content.ReadAsStreamAsync())
                        using (var fs = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            var buffer = new byte[81920];
                            int bytesRead;
                            while ((bytesRead = await contentStream.ReadAsync(buffer.AsMemory(0, buffer.Length), ct)) > 0)
                            {
                                await fs.WriteAsync(buffer.AsMemory(0, bytesRead), ct);
                                totalRead += bytesRead;
                                tracker.UpdateProgress(trackId, totalRead);
                            }
                            await fs.FlushAsync(ct);
                        }

                        tracker.MarkCompleted(trackId);
                        _logger.Info($"[GOG] Download complete: {filePath} ({totalRead} bytes). Handing to PostDownloadProcessor.");

                        // Hand the staged file to the same pipeline the indexer
                        // downloads use: it picks the right platform folder,
                        // routes Updates/DLC/Patches into subfolders, attaches
                        // GameFiles to the existing game row.
                        try
                        {
                            var status = new RetroArr.Core.Download.DownloadStatus
                            {
                                Id = trackId,
                                Name = Path.GetFileNameWithoutExtension(fileName),
                                Size = totalRead,
                                Progress = 1f,
                                State = RetroArr.Core.Download.DownloadState.Completed,
                                DownloadPath = filePath,
                                ClientName = "GOG",
                                PlatformFolder = "gog",
                                GameId = game.Id,
                                GameTitle = game.Title
                            };
                            var result = await _postDownloadProcessor.ProcessCompletedDownloadAsync(status);
                            if (result.Success)
                            {
                                _logger.Info($"[GOG] Post-download import OK: {result.DestinationPath}");
                            }
                            else
                            {
                                _logger.Warn($"[GOG] Post-download import failed: {result.Reason}");
                                // Flip the tracker entry so the UI shows the import error
                                // instead of a "Completed" row that never moved.
                                tracker.MarkFailed(trackId, $"Import failed: {result.Reason}");
                            }
                        }
                        catch (Exception ex) { _logger.Error($"[GOG] PostDownloadProcessor threw: {ex.Message}"); }
                    }
                    catch (OperationCanceledException)
                    {
                        tracker.MarkFailed(trackId, "Download cancelled");
                        try { if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); } catch { }
                    }
                    catch (Exception ex)
                    {
                        tracker.MarkFailed(trackId, ex.Message);
                        _logger.Error($"[GOG] Staged download failed: {ex.Message}");
                        try { if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); } catch { }
                    }
                });

                return Ok(new { success = true, message = $"Download started: {fileName}", staging = filePath, trackId });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] gog-download exception: {ex.Message}");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        private static string ResolveGogFileName(GogGameDownloadRequest request, string downloadUrl)
        {
            string cdnFileName = string.Empty;
            try { cdnFileName = Path.GetFileName(new Uri(downloadUrl).LocalPath); }
            catch { }

            var fileName = request.FileName;
            if (string.IsNullOrEmpty(fileName))
                fileName = !string.IsNullOrEmpty(cdnFileName) ? cdnFileName : "gog_setup";
            else if (!Path.HasExtension(fileName) && !string.IsNullOrEmpty(cdnFileName) && Path.HasExtension(cdnFileName))
                fileName += Path.GetExtension(cdnFileName);

            if (!Path.HasExtension(fileName))
            {
                var ext = (request.Platform?.ToLowerInvariant()) switch
                {
                    "windows" => ".exe",
                    "linux" => ".sh",
                    "mac" or "osx" => ".dmg",
                    _ => ".bin"
                };
                fileName += ext;
            }
            return fileName;
        }

        private static string SanitizeForPath(string input)
        {
            // Windows-reserved superset so folder names are valid on SMB/NTFS shares,
            // not just on the Linux container FS.
            return RetroArr.Core.IO.FileNameSanitizer.Sanitize(input, "unknown");
        }

        // Scans {LibraryRoot}/{platformFolder}/ and diffs against known game paths
        [HttpGet("unmapped-files")]
        public async Task<ActionResult> GetUnmappedFiles()
        {
            var mediaSettings = _configService.LoadMediaSettings();
            var libraryRoot = !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Directory.Exists(mediaSettings.DestinationPath)
                ? mediaSettings.DestinationPath
                : mediaSettings.FolderPath;

            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
                return Ok(new List<object>());

            var allGames = await _repository.GetAllAsync();
            var gamePaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in allGames)
            {
                if (!string.IsNullOrEmpty(g.Path))
                {
                    gamePaths.Add(Path.GetFullPath(g.Path).TrimEnd(Path.DirectorySeparatorChar));
                    if (System.IO.File.Exists(g.Path))
                        gamePaths.Add(Path.GetFullPath(g.Path));
                }
                if (!string.IsNullOrEmpty(g.ExecutablePath))
                    gamePaths.Add(Path.GetFullPath(g.ExecutablePath));
            }

            var unmapped = new List<object>();
            var platformDirs = Directory.GetDirectories(libraryRoot);

            foreach (var platformDir in platformDirs)
            {
                var platformFolderName = Path.GetFileName(platformDir);
                var gameDirs = Directory.GetDirectories(platformDir);

                foreach (var gameDir in gameDirs)
                {
                    var normalizedDir = Path.GetFullPath(gameDir).TrimEnd(Path.DirectorySeparatorChar);
                    if (gamePaths.Contains(normalizedDir))
                        continue;

                    // Check if any game path is a parent or child of this dir
                    bool isMapped = false;
                    foreach (var gp in gamePaths)
                    {
                        if (normalizedDir.StartsWith(gp, StringComparison.OrdinalIgnoreCase) ||
                            gp.StartsWith(normalizedDir, StringComparison.OrdinalIgnoreCase))
                        {
                            isMapped = true;
                            break;
                        }
                    }
                    if (isMapped) continue;

                    try
                    {
                        var files = Directory.GetFiles(gameDir, "*.*", SearchOption.AllDirectories);
                        foreach (var file in files)
                        {
                            var fi = new FileInfo(file);
                            unmapped.Add(new
                            {
                                fileName = fi.Name,
                                fullPath = fi.FullName,
                                folder = Path.GetFileName(gameDir),
                                platformFolder = platformFolderName,
                                size = fi.Length,
                                formattedSize = FormatFileSize(fi.Length),
                                extension = fi.Extension,
                                lastModified = fi.LastWriteTimeUtc.ToString("o")
                            });
                        }

                        // Also include empty folders as entries
                        if (files.Length == 0)
                        {
                            unmapped.Add(new
                            {
                                fileName = "(empty folder)",
                                fullPath = gameDir,
                                folder = Path.GetFileName(gameDir),
                                platformFolder = platformFolderName,
                                size = 0L,
                                formattedSize = "0 B",
                                extension = "",
                                lastModified = new DirectoryInfo(gameDir).LastWriteTimeUtc.ToString("o")
                            });
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[API] Error scanning unmapped dir {gameDir}: {ex.Message}");
                    }
                }
            }

            return Ok(unmapped);
        }

        [HttpPost("{id}/map-file")]
        public async Task<ActionResult> MapFileToGame(int id, [FromBody] MapFileRequest request)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            if (string.IsNullOrEmpty(request.FilePath))
                return BadRequest(new { message = "filePath is required" });

            // Resolve: if the path points to a file, use its parent directory as game.Path
            string gamePath;
            if (System.IO.File.Exists(request.FilePath))
            {
                gamePath = Path.GetDirectoryName(request.FilePath) ?? request.FilePath;
            }
            else if (Directory.Exists(request.FilePath))
            {
                gamePath = request.FilePath;
            }
            else
            {
                return BadRequest(new { message = "Path does not exist" });
            }

            game.Path = gamePath;
            await _repository.UpdateAsync(game.Id, game);
            _logger.Info($"[API] Mapped file to game: '{game.Title}' -> {gamePath}");

            return Ok(new { success = true, message = $"Mapped to '{game.Title}'", path = gamePath });
        }

        [HttpGet("{id}/patches")]
        public async Task<ActionResult> GetPatches(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var gameFolder = ResolveGameFolder(game);
            if (string.IsNullOrEmpty(gameFolder))
                return Ok(new { patches = new List<object>(), patchesFolder = (string?)null, folderExists = false });

            var patchesDir = Path.Combine(gameFolder, "Patches");
            var folderExists = Directory.Exists(patchesDir);

            if (!folderExists)
                return Ok(new { patches = new List<object>(), patchesFolder = patchesDir, folderExists = false });

            var files = Directory.GetFiles(patchesDir, "*.*", SearchOption.AllDirectories);
            var patches = files.Select(f =>
            {
                var fi = new FileInfo(f);
                return new
                {
                    name = fi.Name,
                    relativePath = Path.GetRelativePath(patchesDir, f),
                    fullPath = fi.FullName,
                    size = fi.Length,
                    formattedSize = FormatFileSize(fi.Length),
                    extension = fi.Extension,
                    lastModified = fi.LastWriteTimeUtc.ToString("o")
                };
            }).ToList();

            var totalSize = patches.Sum(p => p.size);
            return Ok(new
            {
                patches,
                patchesFolder = patchesDir,
                folderExists = true,
                totalSize = FormatFileSize(totalSize)
            });
        }

        [HttpPost("{id}/patches/folder")]
        public async Task<ActionResult> CreatePatchesFolder(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var gameFolder = ResolveGameFolder(game);
            if (string.IsNullOrEmpty(gameFolder))
                return BadRequest(new { message = "Cannot resolve game folder. Configure Library Folder in Media Management settings." });

            // Ensure the game folder itself exists
            if (!Directory.Exists(gameFolder))
                Directory.CreateDirectory(gameFolder);

            // Update game.Path if not set
            if (string.IsNullOrEmpty(game.Path) || !Directory.Exists(game.Path))
            {
                game.Path = gameFolder;
                await _repository.UpdateAsync(game.Id, game);
            }

            var patchesDir = Path.Combine(gameFolder, "Patches");
            if (!Directory.Exists(patchesDir))
                Directory.CreateDirectory(patchesDir);

            _logger.Info($"[API] Created patches folder: {patchesDir}");
            return Ok(new { success = true, message = "Patches folder created", path = patchesDir });
        }

        // Title from folder name, platform from parent folder, path linked
        [HttpPost("create-from-file")]
        public async Task<ActionResult> CreateGameFromFile([FromBody] CreateGameFromFileRequest request)
        {
            if (string.IsNullOrEmpty(request.FilePath))
                return BadRequest(new { message = "filePath is required" });

            if (!System.IO.File.Exists(request.FilePath) && !Directory.Exists(request.FilePath))
                return BadRequest(new { message = "Path does not exist" });

            // Resolve game path (folder)
            string gamePath;
            string gameTitle;
            if (Directory.Exists(request.FilePath))
            {
                gamePath = request.FilePath;
                gameTitle = new DirectoryInfo(request.FilePath).Name;
            }
            else
            {
                gamePath = Path.GetDirectoryName(request.FilePath) ?? request.FilePath;
                gameTitle = Path.GetFileNameWithoutExtension(request.FilePath);
            }

            // Use provided title if given
            if (!string.IsNullOrEmpty(request.Title))
                gameTitle = request.Title;

            // Detect platform from parent folder structure
            int platformId = request.PlatformId;
            if (platformId == 0 && !string.IsNullOrEmpty(request.PlatformFolder))
            {
                var plat = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                    p => p.MatchesFolderName(request.PlatformFolder));
                if (plat != null) platformId = plat.Id;
            }

            if (platformId <= 0 || !PlatformDefinitions.PlatformDictionary.ContainsKey(platformId))
            {
                _logger.Warn($"[API] CreateFromFile: Rejected - could not resolve platform for '{gameTitle}' (folder '{request.PlatformFolder}').");
                return BadRequest(new { code = "invalid_platform", message = "Could not resolve a valid platform. Pass platformId or a folder that matches a known platform." });
            }

            // Check for existing game with same title+platform
            var allGames = await _repository.GetAllAsync();
            var existing = allGames.FirstOrDefault(g =>
                g.Title.Equals(gameTitle, StringComparison.OrdinalIgnoreCase) &&
                (platformId == 0 || g.PlatformId == platformId));

            if (existing != null)
            {
                // Update existing game's path instead of creating duplicate
                existing.Path = gamePath;
                await _repository.UpdateAsync(existing.Id, existing);
                _logger.Info($"[API] CreateFromFile: Updated existing game '{existing.Title}' path -> {gamePath}");
                return Ok(new { success = true, gameId = existing.Id, title = existing.Title, path = gamePath, created = false });
            }

            var newGame = new Game
            {
                Title = gameTitle,
                Path = gamePath,
                PlatformId = platformId,
                Status = GameStatus.Released,
                Added = DateTime.UtcNow,
                Images = new GameImages()
            };

            var saved = await _repository.AddAsync(newGame);
            _logger.Info($"[API] CreateFromFile: Created game '{saved.Title}' (ID: {saved.Id}) -> {gamePath}");

            return Ok(new { success = true, gameId = saved.Id, title = saved.Title, path = gamePath, created = true });
        }

        private static string ClassifyFileType(string relativePath, string fileName)
        {
            if (relativePath.StartsWith("Patches/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("Updates/", StringComparison.OrdinalIgnoreCase))
                return "Patch";
            if (relativePath.StartsWith("DLC/", StringComparison.OrdinalIgnoreCase) ||
                relativePath.StartsWith("DLCs/", StringComparison.OrdinalIgnoreCase))
                return "DLC";

            var lowerPath = relativePath.ToLowerInvariant();
            var lowerName = fileName.ToLowerInvariant();

            // Check path segments for DLC/Update keywords
            if (lowerPath.Contains("/dlc/") || lowerPath.Contains("/add-on/") || lowerPath.Contains("/addon/"))
                return "DLC";
            if (lowerPath.Contains("/update/") || lowerPath.Contains("/patch/") || lowerPath.Contains("/patches/") || lowerPath.Contains("/updates/"))
                return "Patch";

            // Check filename for generic update names
            if (lowerName == "update.pkg" || lowerName == "patch.pkg" || lowerName == "update.nsp" || lowerName == "patch.nsp")
                return "Patch";

            // Check filename keywords
            if (lowerName.Contains("dlc") || lowerName.Contains("add-on") || lowerName.Contains("season pass"))
                return "DLC";
            if (lowerName.Contains("update") || lowerName.Contains("patch") || lowerName.Contains("hotfix"))
                return "Patch";

            return "Main";
        }

        private static string FormatFileSize(long bytes)
        {
            if (bytes >= 1024L * 1024 * 1024) return $"{bytes / (1024.0 * 1024 * 1024):F2} GB";
            if (bytes >= 1024L * 1024) return $"{bytes / (1024.0 * 1024):F2} MB";
            if (bytes >= 1024L) return $"{bytes / 1024.0:F2} KB";
            return $"{bytes} B";
        }

        private RetroArr.Core.MetadataSource.Igdb.IgdbClient? GetIgdbClient()
        {
            var igdbSettings = _configService.LoadIgdbSettings();
            if (!igdbSettings.IsConfigured)
                return null;

            return new RetroArr.Core.MetadataSource.Igdb.IgdbClient(igdbSettings.ClientId, igdbSettings.ClientSecret);
        }
    }

    public class GogGameDownloadRequest
    {
        public string ManualUrl { get; set; } = string.Empty;
        public string? FileName { get; set; }
        public string? Platform { get; set; }
    }

    public class MapFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
    }

    public class CreateGameFromFileRequest
    {
        public string FilePath { get; set; } = string.Empty;
        public string? Title { get; set; }
        public int PlatformId { get; set; }
        public string? PlatformFolder { get; set; }
    }
}
