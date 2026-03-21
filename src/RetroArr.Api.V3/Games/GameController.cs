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

        public GameController(IGameRepository repository, IGameMetadataServiceFactory metadataServiceFactory, RetroArr.Core.IO.IArchiveService archiveService, ILauncherService launcherService, ConfigurationService configService, InstallerScannerService installerScanner, LocalMediaExportService localMediaExport, RetroArr.Core.MetadataSource.Gog.GogDownloadTracker gogDownloadTracker)
        {
            _repository = repository;
            _metadataServiceFactory = metadataServiceFactory;
            _archiveService = archiveService;
            _launcherService = launcherService;
            _configService = configService;
            _installerScanner = installerScanner;
            _localMediaExport = localMediaExport;
            _gogDownloadTracker = gogDownloadTracker;
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

        [HttpGet("paged")]
        public async Task<ActionResult<PagedResult<GameListDto>>> GetPaged(
            [FromQuery] int page = 1,
            [FromQuery] int pageSize = 50,
            [FromQuery] int? platformId = null,
            [FromQuery] string? search = null,
            [FromQuery] string sortOrder = "asc")
        {
            if (page < 1) page = 1;
            if (pageSize < 1) pageSize = 50;
            if (pageSize > 1000) pageSize = 1000;

            var result = await _repository.GetAllPagedAsync(page, pageSize, platformId, search, sortOrder);
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

            // If a language is requested and the game has an IgdbId, fetch localized metadata
            if (!string.IsNullOrEmpty(lang) && game.IgdbId.HasValue)
            {
                try
                {
                    var metadataService = _metadataServiceFactory.CreateService();
                    var localizedGame = await metadataService.GetGameMetadataAsync(game.IgdbId.Value, lang);
                    
                    if (localizedGame != null)
                    {
                        // Override localized fields for the display
                        game.Title = localizedGame.Title;
                        game.Overview = localizedGame.Overview;
                        game.Storyline = localizedGame.Storyline;
                        game.Genres = localizedGame.Genres;
                        if (game.Platform != null)
                        {
                            game.Platform.Name = metadataService.LocalizePlatform(game.Platform.Name, lang);
                        }
                    }
                }
                catch
                {
                    // Fallback to stored metadata if IGDB fetch fails
                }
            }

            game.IsInstallable = IsPathInstallable(game.Path);

            var uninstallerPath = FindUninstaller(game.Path);
            var downloadPathHint = FindDownloadFolder(game.Title, game.Path);

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
                downloadPath = downloadPathHint,
                canPlay = canPlay // Explicit property name
            });
        }

        [HttpPost]
        public async Task<ActionResult<Game>> Create([FromBody] Game game)
        {
            _logger.Info($"[GameController] [Create] Attempting to add game: '{game.Title}' (IGDB: {game.IgdbId})");
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

            var updated = await _repository.UpdateAsync(id, existingGame);

            try { await _localMediaExport.ExportMediaForGameAsync(existingGame); }
            catch (System.Exception ex) { _logger.Error($"[Game] Media export error: {ex.Message}"); }

            return Ok(updated);
        }

        [HttpDelete("{id}")]
        public async Task<ActionResult> Delete(int id, [FromQuery] bool deleteFiles = false, [FromQuery] string? targetPath = null, [FromQuery] bool deleteDownloadFiles = false, [FromQuery] string? downloadPath = null)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            if (deleteFiles && !string.IsNullOrEmpty(game.Path))
            {
                // Determine what to delete: targetPath override or game.Path default
                string pathToDelete = !string.IsNullOrEmpty(targetPath) ? targetPath : game.Path;
                
                // Security/Safety Check:
                // 1. If targetPath is provided, it MUST contain the game.Path (i.e. be a parent or the same path)
                //    Wait, checking "Contains" might be tricky with normalization. 
                //    A parent path P contains child C? No, C starts with P.
                //    game.Path (Child) starts with pathToDelete (Parent).
                
                bool isSafe = false;

                if (string.IsNullOrEmpty(targetPath) || targetPath == game.Path)
                {
                    isSafe = true; // Default behavior is safe-ish (deletes what we know)
                }
                else
                {
                    // Validate relationship
                    var normalizedGamePath = System.IO.Path.GetFullPath(game.Path).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                    var normalizedTarget = System.IO.Path.GetFullPath(pathToDelete).TrimEnd(System.IO.Path.DirectorySeparatorChar);
                    
                    if (normalizedGamePath.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase))
                    {
                        isSafe = true;
                    }
                }

                // Global Safety Blocklist to prevent deleting roots or critical folders
                if (IsCriticalPath(pathToDelete))
                {
                    _logger.Info($"[Delete] BLOCKED deletion of critical path: {pathToDelete}");
                    isSafe = false;
                }

                if (isSafe)
                {
                    try
                    {
                        if (System.IO.File.Exists(pathToDelete))
                        {
                            System.IO.File.Delete(pathToDelete);
                            _logger.Info($"[Delete] Deleted file: {pathToDelete}");
                        }
                        else if (System.IO.Directory.Exists(pathToDelete))
                        {
                            System.IO.Directory.Delete(pathToDelete, true);
                            _logger.Info($"[Delete] Deleted directory: {pathToDelete}");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Delete] Error deleting library files at {pathToDelete}: {ex.Message}");
                    }
                }
                else
                {
                    _logger.Error($"[Delete] Safety check failed for library path: {pathToDelete}");
                    // We don't abort the metadata delete, but we warn? 
                    // Or we shout abort? Ideally abort if user explicitly requested file delete and it failed safety.
                    // But for now, let's proceed to delete metadata so the "broken" game is gone.
                }
            }

            // --- Download Folder Deletion Logic ---
            if (deleteDownloadFiles && !string.IsNullOrEmpty(downloadPath))
            {
                bool isDownloadSafe = !IsCriticalPath(downloadPath);
                
                if (isDownloadSafe && System.IO.Directory.Exists(downloadPath))
                {
                    try
                    {
                        System.IO.Directory.Delete(downloadPath, true);
                        _logger.Info($"[Delete] Deleted download directory: {downloadPath}");
                    }
                    catch (Exception ex)
                    {
                        _logger.Error($"[Delete] Error deleting download folder at {downloadPath}: {ex.Message}");
                    }
                }
                else if (!isDownloadSafe)
                {
                    _logger.Info($"[Delete] BLOCKED deletion of critical download path: {downloadPath}");
                }
            }

            var removed = await _repository.DeleteAsync(id);
            if (!removed)
            {
                return NotFound();
            }

            return NoContent();
        }

        private bool IsCriticalPath(string path)
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

        private string? FindDownloadFolder(string gameTitle, string? gamePath)
        {
            try
            {
                var settings = _configService.LoadMediaSettings();
                var downloadRoot = settings.DownloadPath;

                if (string.IsNullOrEmpty(downloadRoot) || !System.IO.Directory.Exists(downloadRoot)) return null;

                // Look for directories in downloadRoot (Level 1 and Level 2)
                var level1Dirs = System.IO.Directory.GetDirectories(downloadRoot);
                var allDirs = new List<string>(level1Dirs);
                
                foreach (var l1 in level1Dirs)
                {
                    try { allDirs.AddRange(System.IO.Directory.GetDirectories(l1)); } catch { }
                }

                // Strategy 1: Match by immediate parent folder name of game.Path
                if (!string.IsNullOrEmpty(gamePath))
                {
                    var parentDir = System.IO.Path.GetDirectoryName(gamePath);
                    if (!string.IsNullOrEmpty(parentDir))
                    {
                        var folderName = new System.IO.DirectoryInfo(parentDir).Name;
                        var match = allDirs.FirstOrDefault(d => 
                            string.Equals(System.IO.Path.GetFileName(d), folderName, StringComparison.OrdinalIgnoreCase));
                        
                        if (match != null) return match;
                    }
                }

                // Strategy 2: Match by game title
                var titleMatch = allDirs.FirstOrDefault(d => 
                    System.IO.Path.GetFileName(d).Contains(gameTitle, StringComparison.OrdinalIgnoreCase));
                
                if (titleMatch != null) return titleMatch;

                // Strategy 3: Fuzzy match (alphanumeric only)
                var cleanTitle = System.Text.RegularExpressions.Regex.Replace(gameTitle, @"[^a-zA-Z0-9]", "");
                if (cleanTitle.Length > 2)
                {
                     var fuzzyMatch = allDirs.FirstOrDefault(d => {
                         var cleanDirName = System.Text.RegularExpressions.Regex.Replace(System.IO.Path.GetFileName(d), @"[^a-zA-Z0-9]", "");
                         return cleanDirName.Contains(cleanTitle, StringComparison.OrdinalIgnoreCase);
                     });
                     
                     if (fuzzyMatch != null) return fuzzyMatch;
                }

                return null;
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

        /// <summary>
        /// Resolve the canonical library folder for a game: {Library}/{platformFolder}/{title}/
        /// Deterministic: always computed from game.PlatformId + game.Title via MediaSettings pattern.
        /// Each game entry has its own platform, so multi-platform games each get their own folder.
        /// </summary>
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

            var libraryRoot = !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Directory.Exists(mediaSettings.DestinationPath)
                ? mediaSettings.DestinationPath
                : !string.IsNullOrEmpty(mediaSettings.FolderPath) && Directory.Exists(mediaSettings.FolderPath)
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

        /// <summary>
        /// List all files in a game's folder recursively.
        /// Resolves the game folder from game.Path or MediaSettings pattern.
        /// </summary>
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

                // Check single-file BEFORE any auto-persist to avoid overwriting file paths with parent dir
                bool isSingleFile = !string.IsNullOrEmpty(game.Path) && System.IO.File.Exists(game.Path) && !Directory.Exists(game.Path);

                // Auto-persist game.Path when folder was found via settings but game.Path was truly empty/missing
                // Do NOT overwrite when game.Path is a valid file (e.g. a ROM file in file-mode console scanning)
                if (folderExists && !isSingleFile && (string.IsNullOrEmpty(game.Path) || (!System.IO.File.Exists(game.Path) && !Directory.Exists(game.Path))))
                {
                    game.Path = gamePath;
                    await _repository.UpdateAsync(game.Id, game);
                    _logger.Info($"[GetGameFiles] Auto-set game.Path for '{game.Title}' -> {gamePath}");
                }

                if (string.IsNullOrEmpty(gamePath) && !isSingleFile)
                    return Ok(new { files = Array.Empty<object>(), gamePath = (string?)null, resolvedPath = (string?)null, folderExists = false });

                var files = new List<object>();
                long totalSizeBytes = 0;

                if (isSingleFile)
                {
                    var fi = new FileInfo(game.Path!);
                    totalSizeBytes += fi.Length;
                    files.Add(new
                    {
                        name = fi.Name,
                        relativePath = fi.Name,
                        fullPath = fi.FullName,
                        size = fi.Length,
                        formattedSize = FormatFileSize(fi.Length),
                        extension = fi.Extension,
                        lastModified = fi.LastWriteTimeUtc,
                        fileType = "Main"
                    });
                }
                else if (folderExists)
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

        /// <summary>
        /// Create the game's library folder and assign it to game.Path if not set.
        /// Follows Sonarr/Radarr pattern: {Library}/{platformFolder}/{title}/
        /// </summary>
        [HttpPost("{id}/folder")]
        public async Task<ActionResult> CreateGameFolder(int id)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            var resolvedPath = ResolveGameFolder(game);
            if (string.IsNullOrEmpty(resolvedPath))
                return BadRequest(new { success = false, message = "Could not resolve game folder. Configure Library Folder in Media Management settings." });

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

        /// <summary>
        /// Download a specific file from a game's folder
        /// </summary>
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

            // Resolve full path
            string fullPath;
            if (Directory.Exists(gameFolderPath))
            {
                fullPath = Path.GetFullPath(Path.Combine(gameFolderPath, path));
                // Security: ensure resolved path is still within game folder
                var normalizedGamePath = Path.GetFullPath(gameFolderPath).TrimEnd(Path.DirectorySeparatorChar);
                if (!fullPath.StartsWith(normalizedGamePath, StringComparison.OrdinalIgnoreCase))
                    return BadRequest("Invalid file path");
            }
            else if (System.IO.File.Exists(gameFolderPath))
            {
                fullPath = Path.GetFullPath(gameFolderPath);
            }
            else
            {
                return NotFound("Game folder does not exist");
            }

            if (!System.IO.File.Exists(fullPath))
                return NotFound("File not found");

            var fileName = Path.GetFileName(fullPath);
            var contentType = "application/octet-stream";
            var stream = new FileStream(fullPath, FileMode.Open, FileAccess.Read, FileShare.Read);
            return File(stream, contentType, fileName);
        }

        /// <summary>
        /// Discover local media files (images + videos) for a game.
        /// Follows RetroBat/Batocera convention: {platform}/images/ and {platform}/videos/
        /// Files are matched by ROM filename without extension.
        /// </summary>
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
                            url = $"/api/v3/game/{id}/local-media/file?path={Uri.EscapeDataString(Path.GetFileName(file))}&folder=images"
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
                            url = $"/api/v3/game/{id}/local-media/file?path={Uri.EscapeDataString(Path.GetFileName(file))}&folder=videos"
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

        /// <summary>
        /// Serve a local media file (image or video) from the platform's images/ or videos/ directory.
        /// Supports HTTP range requests for video streaming.
        /// </summary>
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

        /// <summary>
        /// Download a GOG file directly into the game's folder
        /// </summary>
        [HttpPost("{id}/gog-download")]
        public async Task<ActionResult> DownloadGogToGameFolder(int id, [FromBody] GogGameDownloadRequest request)
        {
            var game = await _repository.GetByIdAsync(id);
            if (game == null) return NotFound();

            if (string.IsNullOrEmpty(game.Path))
                return BadRequest(new { success = false, message = "Game has no folder path" });

            var gogSettings = _configService.LoadGogSettings();
            if (!gogSettings.IsConfigured || string.IsNullOrEmpty(gogSettings.RefreshToken))
                return BadRequest(new { success = false, message = "GOG not configured. Please authenticate in Settings." });

            // Ensure game folder exists
            var targetDir = Directory.Exists(game.Path) ? game.Path : Path.GetDirectoryName(game.Path);
            if (string.IsNullOrEmpty(targetDir))
                return BadRequest(new { success = false, message = "Could not resolve game folder" });

            if (!Directory.Exists(targetDir))
                Directory.CreateDirectory(targetDir);

            try
            {
                const string GogClientId = "46899977096215655";
                const string GogClientSecret = "9d85c43b1482497dbbce61f6e4aa173a433796eeae2ca8c5f6129f2dc4de46d9";

                _logger.Info($"[GOG] DownloadGogToGameFolder: game={id}, manualUrl={request.ManualUrl}, targetDir={targetDir}");

                var client = new GogClient(gogSettings.RefreshToken);
                var refreshed = await client.RefreshTokenAsync(GogClientId, GogClientSecret);
                if (!refreshed)
                {
                    _logger.Error("[GOG] DownloadGogToGameFolder: token refresh failed");
                    return BadRequest(new { success = false, message = "Failed to authenticate with GOG" });
                }

                var downloadUrl = await client.GetDownloadUrlAsync(request.ManualUrl);
                if (string.IsNullOrEmpty(downloadUrl))
                {
                    _logger.Error($"[GOG] DownloadGogToGameFolder: GetDownloadUrlAsync returned null for {request.ManualUrl}");
                    return BadRequest(new { success = false, message = "Failed to get download URL from GOG" });
                }

                // Resolve filename: use display name from frontend, but ensure it has a file extension
                // GOG CDN URLs contain the real filename with extension (e.g. setup_dungeons_2_1.0.exe)
                var fileName = request.FileName;
                var cdnFileName = "";
                try
                {
                    var uri = new Uri(downloadUrl);
                    cdnFileName = Path.GetFileName(uri.LocalPath);
                }
                catch { /* ignore malformed URL */ }

                if (string.IsNullOrEmpty(fileName))
                {
                    fileName = !string.IsNullOrEmpty(cdnFileName) ? cdnFileName : $"{game.Title}_gog_setup.exe";
                }
                else if (!Path.HasExtension(fileName) && !string.IsNullOrEmpty(cdnFileName) && Path.HasExtension(cdnFileName))
                {
                    // Display name has no extension — append extension from CDN URL
                    var ext = Path.GetExtension(cdnFileName);
                    fileName = fileName + ext;
                    _logger.Info($"[GOG] Appended extension from CDN: {ext} -> {fileName}");
                }

                // Ultimate fallback: if still no extension, use platform-based default
                if (!Path.HasExtension(fileName))
                {
                    var platformExt = (request.Platform?.ToLowerInvariant()) switch
                    {
                        "windows" => ".exe",
                        "linux" => ".sh",
                        "mac" or "osx" => ".dmg",
                        _ => ".bin"
                    };
                    fileName = fileName + platformExt;
                    _logger.Info($"[GOG] Platform fallback extension: {platformExt} -> {fileName}");
                }

                var filePath = Path.Combine(targetDir, fileName);
                _logger.Info($"[GOG] Starting download to game folder: {fileName} -> {filePath} (url={downloadUrl.Substring(0, Math.Min(120, downloadUrl.Length))}...)");

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

                        // Try to get real filename from Content-Disposition header
                        var cdHeader = response.Content.Headers.ContentDisposition?.FileName?.Trim('"', ' ');
                        if (!string.IsNullOrEmpty(cdHeader) && !Path.HasExtension(fileName) && Path.HasExtension(cdHeader))
                        {
                            var ext = Path.GetExtension(cdHeader);
                            fileName = fileName + ext;
                            var newFilePath = Path.Combine(targetDir, fileName);
                            _logger.Info($"[GOG] Content-Disposition extension: {ext} -> {fileName}");
                            filePath = newFilePath;
                        }

                        var totalBytes = response.Content.Headers.ContentLength;
                        ct = tracker.Start(trackId, game.Title, fileName, filePath, totalBytes);
                        _logger.Info($"[GOG] Download response OK, content-length={totalBytes}");

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
                        _logger.Error($"[GOG] Download to game folder failed: {ex.Message}");
                        try { if (System.IO.File.Exists(filePath)) System.IO.File.Delete(filePath); } catch { }
                    }
                });

                return Ok(new { success = true, message = $"Download started: {fileName}", downloadPath = filePath, folder = targetDir, trackId });
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] DownloadGogToGameFolder exception: {ex.Message}");
                return BadRequest(new { success = false, message = ex.Message });
            }
        }

        /// <summary>
        /// Returns local files in the library that are not mapped to any game.
        /// Scans {LibraryRoot}/{platformFolder}/ directories and compares against game paths.
        /// </summary>
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

        /// <summary>
        /// Map a local file/folder to an existing game by updating the game's Path.
        /// </summary>
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

        /// <summary>
        /// List patch files in the game's Patches/ subfolder.
        /// </summary>
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

        /// <summary>
        /// Create the Patches/ subfolder inside the game's folder.
        /// </summary>
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

        /// <summary>
        /// Create a new game entry from an unmapped local file/folder.
        /// Derives title from folder name, detects platform from parent folder, and links the path.
        /// </summary>
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
