using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using RetroArr.Core.Configuration;
using RetroArr.Core.IO;
using RetroArr.Core.Games;
using System.Diagnostics.CodeAnalysis;
using SharpCompress.Archives;
using SharpCompress.Archives.Rar;
using SharpCompress.Common;
using System.Text.RegularExpressions;
using RetroArr.Core.MetadataSource;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    public class PostDownloadProcessor
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadsImport);
        private readonly ConfigurationService _configService;
        private readonly IFileMoverService _fileMover;
        private readonly IGameRepository _gameRepository;
        private readonly IGameMetadataServiceFactory _metadataFactory;
        private readonly IArchiveService _archiveService;
        private readonly TitleCleanerService _titleCleaner;

        public PostDownloadProcessor(
            ConfigurationService configService,
            IFileMoverService fileMover,
            IGameRepository gameRepository,
            IGameMetadataServiceFactory metadataFactory,
            IArchiveService archiveService,
            TitleCleanerService titleCleaner)
        {
            _configService = configService;
            _fileMover = fileMover;
            _gameRepository = gameRepository;
            _metadataFactory = metadataFactory;
            _archiveService = archiveService;
            _titleCleaner = titleCleaner;
        }

        public async System.Threading.Tasks.Task<PostDownloadResult> ProcessCompletedDownloadAsync(DownloadStatus download)
        {
            if (string.IsNullOrEmpty(download.DownloadPath) || !Directory.Exists(download.DownloadPath))
            {
                if (!File.Exists(download.DownloadPath))
                {
                    _logger.Info($"[PostDownload] Skip: Path not found or empty for {download.Name}");
                    return PostDownloadResult.Fail($"Path not found: '{download.DownloadPath}'");
                }
            }

            var settings = _configService.LoadPostDownloadSettings();
            _logger.Info($"[PostDownload] Processing completed download: {download.Name} at {download.DownloadPath}");

            // 1. Auto-Extract
            List<string>? extractionFailures = null;
            if (settings.EnableAutoExtract && Directory.Exists(download.DownloadPath))
            {
                extractionFailures = ExtractArchives(download.DownloadPath);
                if (extractionFailures.Count > 0 && !settings.EnableAutoMove)
                {
                    var failedList = string.Join(", ", extractionFailures.Select(Path.GetFileName));
                    return PostDownloadResult.Fail($"Extraction failed after retries: {failedList}");
                }
            }

            // 2. Deep Clean
            if (settings.EnableDeepClean && Directory.Exists(download.DownloadPath))
            {
                DeepClean(download.DownloadPath, settings.UnwantedExtensions);
            }

            // 3. Auto-Move / Import
            if (settings.EnableAutoMove)
            {
                return await AutoMoveToLibrary(download);
            }

            return PostDownloadResult.Fail("Auto-move is disabled in post-download settings.");
        }

        private List<string> ExtractArchives(string path)
        {
            var failed = new List<string>();
            var archives = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories)
                .Where(f => _archiveService.IsArchive(f))
                .ToList();

            const int maxAttempts = 3;

            foreach (var archivePath in archives)
            {
                if (IsMultiPartNotFirst(archivePath)) continue;

                bool success = false;
                for (int attempt = 1; attempt <= maxAttempts && !success; attempt++)
                {
                    try
                    {
                        if (_archiveService.Extract(archivePath, path))
                        {
                            _logger.Info($"[PostDownload] Extraction successful on attempt {attempt}. Deleting archive: {archivePath}");
                            try { File.Delete(archivePath); } catch { }
                            success = true;
                        }
                        else
                        {
                            _logger.Warn($"[PostDownload] Extraction returned false for {archivePath} (attempt {attempt}/{maxAttempts})");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[PostDownload] Extract attempt {attempt}/{maxAttempts} failed for {archivePath}: {ex.Message}");
                    }

                    if (!success && attempt < maxAttempts)
                    {
                        var delayMs = 500 * (1 << (attempt - 1));
                        System.Threading.Thread.Sleep(delayMs);
                    }
                }

                if (!success)
                {
                    _logger.Error($"[PostDownload] Extraction failed after {maxAttempts} attempts for {archivePath} — leaving archive in place for manual recovery.");
                    failed.Add(archivePath);
                }
            }

            return failed;
        }

        private bool IsMultiPartNotFirst(string path)
        {
            var fileName = Path.GetFileName(path).ToLower();
            
            // Standard RAR parts: .part01.rar, .part1.rar
            if (fileName.Contains(".part"))
            {
                return !fileName.Contains(".part01.") && 
                       !fileName.Contains(".part1.") && 
                       !fileName.EndsWith(".part01.rar") && 
                       !fileName.EndsWith(".part1.rar");
            }
            
            // Numerical parts: .001, .002
            if (System.Text.RegularExpressions.Regex.IsMatch(fileName, @"\.\d{3}$"))
            {
                return !fileName.EndsWith(".001");
            }

            return false;
        }

        private void DeepClean(string path, List<string> unwantedExtensions)
        {
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLower();
                if (unwantedExtensions.Contains(ext))
                {
                    try
                    {
                        _logger.Info($"[PostDownload] Deleting unwanted file: {file}");
                        File.Delete(file);
                    }
                    catch { }
                }
            }
        }

        private async System.Threading.Tasks.Task<PostDownloadResult> AutoMoveToLibrary(DownloadStatus download)
        {
            var mediaSettings = _configService.LoadMediaSettings();
            var libraryRoot = !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Directory.Exists(mediaSettings.DestinationPath)
                ? mediaSettings.DestinationPath 
                : mediaSettings.FolderPath;

            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                _logger.Info("[PostDownload] Skip Auto-Move: Library path not configured.");
                return PostDownloadResult.Fail($"Library path not configured or does not exist. Configure 'Library Folder' in Media Management settings. Current: '{libraryRoot}'");
            }

            // Game-targeted import: if download is linked to a specific game, import directly to its folder
            if (download.GameId.HasValue)
            {
                return await ImportToGameFolder(download, mediaSettings, libraryRoot);
            }

            var platformFolder = download.PlatformFolder;

            var validExtensions = new[] {
                // Nintendo
                ".nsp", ".xci", ".cia", ".3ds", ".nds", ".gba", ".gbc", ".gb", ".nes", ".sfc", ".smc", ".n64", ".z64", ".v64", ".gcm", ".wbfs", ".wad",
                // PlayStation
                ".pkg", ".iso", ".bin", ".cue", ".chd", ".pbp", ".cso",
                // PC / Windows
                ".exe", ".msi",
                // macOS
                ".dmg", ".app",
                // Linux
                ".appimage", ".sh",
                // Archives (may contain game files)
                ".zip", ".rar", ".7z", ".tar", ".gz",
                // Sega
                ".md", ".smd", ".gen", ".cdi", ".gdi",
                // Other
                ".rom", ".img"
            };
            bool isDirectory = Directory.Exists(download.DownloadPath);
            
            // Resolve clean name via IGDB
            string containerName = download.Name; // Fallback
            var cleanName = CleanReleaseName(download.Name);
            bool shouldNest = false; // Only nest if we resolve a clean name

            _logger.Info($"[PostDownload] Resolving game name for: '{cleanName}' (Original: '{download.Name}', Platform: '{platformFolder ?? "unknown"}')");

            try 
            {
                var metadataService = _metadataFactory.CreateService();
                var searchResults = await metadataService.SearchGamesAsync(cleanName);
                if (searchResults.Any())
                {
                    containerName = SanitizeFileName(searchResults.First().Title);
                    shouldNest = true;
                    _logger.Info($"[PostDownload] Resolved game name: '{download.Name}' -> '{containerName}'");
                }
                else
                {
                    _logger.Info($"[PostDownload] No match found for '{cleanName}'. Using original name.");
                    containerName = SanitizeFileName(isDirectory ? new DirectoryInfo(download.DownloadPath!).Name : download.Name);
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[PostDownload] Error resolving game name: {ex.Message}. Using original.");
                containerName = SanitizeFileName(isDirectory ? new DirectoryInfo(download.DownloadPath!).Name : download.Name);
            }
            
            // Compute game folder using DestinationPathPattern (respects UseDestinationPattern)
            var gameFolder = mediaSettings.ResolveDestinationPath(libraryRoot, platformFolder ?? "unknown", containerName);
            _logger.Info($"[PostDownload] Resolved game folder: {gameFolder}");
            Directory.CreateDirectory(gameFolder);

            if (isDirectory)
            {
                var files = Directory.GetFiles(download.DownloadPath!, "*.*", SearchOption.AllDirectories);
                bool hasGameFile = files.Any(f => validExtensions.Contains(Path.GetExtension(f).ToLower()));

                if (!hasGameFile)
                {
                    // Also check for macOS .app bundles (directories ending in .app)
                    var appBundles = Directory.GetDirectories(download.DownloadPath!, "*.app", SearchOption.AllDirectories);
                    if (appBundles.Length == 0)
                    {
                        _logger.Info($"[PostDownload] No valid game files found in {download.DownloadPath}");
                        return PostDownloadResult.Fail($"No valid game files found in '{download.DownloadPath}'. Supported formats: {string.Join(", ", validExtensions)}");
                    }
                    _logger.Info($"[PostDownload] Found {appBundles.Length} .app bundle(s) in {download.DownloadPath}");
                }

                var originalFolderName = new DirectoryInfo(download.DownloadPath!).Name;
                bool gameAdded = false;

                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(download.DownloadPath!, file);
                    string destPath;
                    
                    if (shouldNest)
                    {
                        // {gameFolder}/OriginalReleaseName/File
                        destPath = Path.Combine(gameFolder, originalFolderName, relativePath);
                    }
                    else
                    {
                        // {gameFolder}/File (Fallback)
                        destPath = Path.Combine(gameFolder, relativePath);
                    }

                    _logger.Info($"[PostDownload] Moving to library: {relativePath} -> {destPath}");
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    
                    if (_fileMover.ImportFile(file, destPath))
                    {
                        // If this looks like the main setup or exe, let's track it
                        var lowerName = Path.GetFileName(file).ToLowerInvariant();
                        if (!gameAdded && (lowerName.Contains("setup") || lowerName.Contains("install") || lowerName.EndsWith(".exe")))
                        {
                            var metadataSvc = _metadataFactory.CreateService();
                            await AddMovedGameToLibraryAsync(containerName, destPath, metadataSvc, download.PlatformFolder);
                            gameAdded = true;
                        }
                    }
                }

                // If no game was added yet (no setup/install exe found), add the first valid game file
                if (!gameAdded)
                {
                    var firstGameFile = files.FirstOrDefault(f => validExtensions.Contains(Path.GetExtension(f).ToLower()));
                    if (firstGameFile != null)
                    {
                        string gameDestPath;
                        var rel = Path.GetRelativePath(download.DownloadPath!, firstGameFile);
                        if (shouldNest)
                            gameDestPath = Path.Combine(gameFolder, new DirectoryInfo(download.DownloadPath!).Name, rel);
                        else
                            gameDestPath = Path.Combine(gameFolder, rel);

                        var metadataSvc = _metadataFactory.CreateService();
                        await AddMovedGameToLibraryAsync(containerName, gameDestPath, metadataSvc, download.PlatformFolder);
                    }
                }

                // Cleanup source directory after successful import
                if (!IsCriticalPath(download.DownloadPath))
                {
                    try
                    {
                        _logger.Info($"[PostDownload] Cleaning up source directory: {download.DownloadPath}");
                        Directory.Delete(download.DownloadPath, true);
                    }
                    catch (Exception ex)
                    {
                        _logger.Warn($"[PostDownload] Warning: Could not delete source directory {download.DownloadPath}: {ex.Message}");
                    }
                }
                else
                {
                    _logger.Info($"[PostDownload] BLOCKED: Refusing to delete critical path: {download.DownloadPath}");
                }

                return PostDownloadResult.Ok(gameFolder);
            }
            else if (File.Exists(download.DownloadPath))
            {
                var file = download.DownloadPath!;
                if (validExtensions.Contains(Path.GetExtension(file).ToLower()))
                {
                    // For single files, we put them directly in the container or maybe nest?
                    var destPath = Path.Combine(gameFolder, Path.GetFileName(file));
                    _logger.Info($"[PostDownload] Moving to library: {Path.GetFileName(file)} -> {destPath}");
                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
                    
                    if (_fileMover.ImportFile(file, destPath))
                    {
                        var metadataSvc = _metadataFactory.CreateService();
                        await AddMovedGameToLibraryAsync(containerName, destPath, metadataSvc, download.PlatformFolder);

                        // Cleanup source file
                        if (!IsCriticalPath(file))
                        {
                            try { File.Delete(file); } catch { }
                        }
                        else
                        {
                            _logger.Info($"[PostDownload] BLOCKED: Refusing to delete critical path: {file}");
                        }

                        return PostDownloadResult.Ok(destPath);
                    }
                    else
                    {
                        return PostDownloadResult.Fail($"File mover failed: '{file}' -> '{destPath}'");
                    }
                }
            }

            return PostDownloadResult.Fail($"No importable content found for '{download.Name}'");
        }

        /// <summary>
        /// Import downloaded files directly into an existing game's folder.
        /// Resolves the game's canonical folder from its Path or MediaSettings pattern.
        /// </summary>
        private async System.Threading.Tasks.Task<PostDownloadResult> ImportToGameFolder(DownloadStatus download, MediaSettings mediaSettings, string libraryRoot)
        {
            var allGames = await _gameRepository.GetAllLightAsync();
            var game = allGames.FirstOrDefault(g => g.Id == download.GameId);
            if (game == null)
            {
                _logger.Info($"[PostDownload] GameId {download.GameId} not found in DB. Falling back to generic import.");
                download.GameId = null;
                return await AutoMoveToLibrary(download);
            }

            // Determine effective platform: the user-selected download platform takes priority
            var gamePlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
            var folderMode = mediaSettings.FolderNamingMode;
            var gamePlatformFolder = gamePlatform?.GetEffectiveFolderName(folderMode);
            var downloadPlatformFolder = download.PlatformFolder;

            // If download platform differs from game platform, look for a matching game entry on that platform
            if (!string.IsNullOrEmpty(downloadPlatformFolder) &&
                !string.Equals(downloadPlatformFolder, gamePlatformFolder, StringComparison.OrdinalIgnoreCase))
            {
                var downloadPlatformDef = PlatformDefinitions.AllPlatforms.FirstOrDefault(p =>
                    p.MatchesFolderName(downloadPlatformFolder));

                if (downloadPlatformDef != null)
                {
                    // Try to find an existing game entry with the same title on the download's platform
                    var platformGame = allGames.FirstOrDefault(g =>
                        g.PlatformId == downloadPlatformDef.Id &&
                        string.Equals(g.Title, game.Title, StringComparison.OrdinalIgnoreCase));

                    if (platformGame != null)
                    {
                        _logger.Info($"[PostDownload] Multi-platform: found existing '{game.Title}' entry for {downloadPlatformFolder} (ID: {platformGame.Id})");
                        game = platformGame;
                        gamePlatform = downloadPlatformDef;
                        gamePlatformFolder = downloadPlatformFolder;
                    }
                    else
                    {
                        // Create a new game entry for the download's platform
                        var newGame = new Games.Game
                        {
                            Title = game.Title,
                            AlternativeTitle = game.AlternativeTitle,
                            PlatformId = downloadPlatformDef.Id,
                            Year = game.Year,
                            Overview = game.Overview,
                            Images = new Games.GameImages
                            {
                                CoverUrl = game.Images?.CoverUrl,
                                CoverLargeUrl = game.Images?.CoverLargeUrl,
                                BackgroundUrl = game.Images?.BackgroundUrl,
                                BannerUrl = game.Images?.BannerUrl,
                            },
                            Rating = game.Rating,
                            IgdbId = game.IgdbId,
                            Status = game.Status,
                            IsExternal = false,
                            Added = DateTime.UtcNow,
                        };
                        var saved = await _gameRepository.AddAsync(newGame);
                        _logger.Info($"[PostDownload] Multi-platform: created new '{game.Title}' entry for {downloadPlatformFolder} (ID: {saved.Id})");
                        game = saved;
                        gamePlatform = downloadPlatformDef;
                        gamePlatformFolder = downloadPlatformFolder;
                    }
                }
            }

            // Resolve target folder: prefer download platform, fall back to game platform
            var effectivePlatformFolder = downloadPlatformFolder ?? gamePlatformFolder ?? "windows";
            string targetFolder;
            if (!string.IsNullOrEmpty(game.Path) && Directory.Exists(game.Path))
            {
                targetFolder = game.Path;
            }
            else
            {
                targetFolder = mediaSettings.ResolveDestinationPath(libraryRoot, effectivePlatformFolder, game.Title, game.Year > 0 ? game.Year : (int?)null);
            }

            // Auto-detect content type from download name for routing and rename
            var (contentType, detectedVersion, detectedDlcName) = DetectContentType(download.Name);

            // Route to subfolder: explicit ImportSubfolder takes priority over auto-detection
            var importFolder = targetFolder;
            if (!string.IsNullOrEmpty(download.ImportSubfolder))
            {
                importFolder = Path.Combine(targetFolder, download.ImportSubfolder);
                // If frontend said "Patches" but we also detected a version, keep it
                if (download.ImportSubfolder.Equals("Patches", StringComparison.OrdinalIgnoreCase))
                    contentType = DownloadContentType.Patch;
                else if (download.ImportSubfolder.Equals("DLC", StringComparison.OrdinalIgnoreCase))
                    contentType = DownloadContentType.DLC;
                _logger.Info($"[PostDownload] Subfolder import ({download.ImportSubfolder}): routing to {importFolder}");
            }
            else if (contentType == DownloadContentType.Patch)
            {
                importFolder = Path.Combine(targetFolder, "Patches");
                _logger.Info($"[PostDownload] Auto-detected patch (v{detectedVersion ?? "?"}): routing to {importFolder}");
            }
            else if (contentType == DownloadContentType.DLC)
            {
                importFolder = Path.Combine(targetFolder, "DLC");
                _logger.Info($"[PostDownload] Auto-detected DLC ({detectedDlcName ?? "?"}): routing to {importFolder}");
            }

            _logger.Info($"[PostDownload] Game-targeted import: '{game.Title}' (ID: {game.Id}) -> {importFolder} [Type: {contentType}]");
            Directory.CreateDirectory(importFolder);

            bool isDirectory = Directory.Exists(download.DownloadPath);
            int movedCount = 0;
            string? firstMovedFile = null;

            if (isDirectory)
            {
                var files = Directory.GetFiles(download.DownloadPath!, "*.*", SearchOption.AllDirectories);
                foreach (var file in files)
                {
                    var relativePath = Path.GetRelativePath(download.DownloadPath!, file);
                    var ext = Path.GetExtension(file);

                    // Rename primary file based on content type
                    string destFileName;
                    if (contentType == DownloadContentType.Patch)
                        destFileName = BuildPatchFileName(game.Title, detectedVersion, ext);
                    else if (contentType == DownloadContentType.DLC)
                        destFileName = BuildDlcFileName(game.Title, detectedDlcName, ext);
                    else if (contentType == DownloadContentType.MainGame && files.Length == 1)
                        destFileName = SanitizeFileName(game.Title) + ext;
                    else
                        destFileName = Path.GetFileName(file); // keep original for multi-file directories

                    // For multi-file dirs, only rename the first file; keep relative structure for the rest
                    string destPath;
                    if (files.Length == 1)
                    {
                        destPath = Path.Combine(importFolder, destFileName);
                    }
                    else
                    {
                        destPath = Path.Combine(importFolder, relativePath);
                    }

                    Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

                    if (_fileMover.ImportFile(file, destPath))
                    {
                        movedCount++;
                        firstMovedFile ??= destPath;
                        _logger.Info($"[PostDownload] Moved: {relativePath} -> {destPath}");
                    }
                }

                // Cleanup source directory
                if (movedCount > 0 && !IsCriticalPath(download.DownloadPath))
                {
                    try { Directory.Delete(download.DownloadPath, true); }
                    catch (Exception ex) { _logger.Warn($"[PostDownload] Warning: Could not delete source: {ex.Message}"); }
                }
            }
            else if (File.Exists(download.DownloadPath))
            {
                var ext = Path.GetExtension(download.DownloadPath!);
                string destFileName;
                if (contentType == DownloadContentType.Patch)
                    destFileName = BuildPatchFileName(game.Title, detectedVersion, ext);
                else if (contentType == DownloadContentType.DLC)
                    destFileName = BuildDlcFileName(game.Title, detectedDlcName, ext);
                else
                    destFileName = SanitizeFileName(game.Title) + ext;

                var destPath = Path.Combine(importFolder, destFileName);
                if (_fileMover.ImportFile(download.DownloadPath!, destPath))
                {
                    movedCount = 1;
                    firstMovedFile = destPath;
                    _logger.Info($"[PostDownload] Moved: {Path.GetFileName(download.DownloadPath)} -> {destPath}");

                    if (!IsCriticalPath(download.DownloadPath))
                    {
                        try { File.Delete(download.DownloadPath!); } catch { }
                    }
                }
            }

            if (movedCount == 0)
            {
                return PostDownloadResult.Fail($"No files imported for game '{game.Title}'");
            }

            // Update game.Path if not set or not pointing to a valid directory
            if (string.IsNullOrEmpty(game.Path) || !Directory.Exists(game.Path))
            {
                game.Path = targetFolder;
                await _gameRepository.UpdateAsync(game.Id, game);
                _logger.Info($"[PostDownload] Updated game path: '{game.Title}' -> {targetFolder}");
            }

            // Update executable path for main game imports only
            if (contentType == DownloadContentType.MainGame && firstMovedFile != null && string.IsNullOrEmpty(game.ExecutablePath))
            {
                game.ExecutablePath = firstMovedFile;
                await _gameRepository.UpdateAsync(game.Id, game);
            }

            _logger.Info($"[PostDownload] Game-targeted import complete: {movedCount} file(s) -> {importFolder} [Type: {contentType}]");
            return PostDownloadResult.Ok(targetFolder);
        }

        private async System.Threading.Tasks.Task AddMovedGameToLibraryAsync(string title, string path, GameMetadataService metadataService, string? platformFolder)
        {
            try
            {
                // Only add if not already present
                var allGames = await _gameRepository.GetAllLightAsync();
                if (allGames.Any(g => g.ExecutablePath == path)) return;

                // Resolve PlatformId from folder name
                int platformId = ResolvePlatformId(platformFolder);

                // Check for existing game with same title+platform to avoid unique constraint violation
                var existingByTitlePlatform = allGames.FirstOrDefault(g =>
                    g.Title.Equals(title, StringComparison.OrdinalIgnoreCase) && g.PlatformId == platformId);
                if (existingByTitlePlatform != null)
                {
                    if (string.IsNullOrEmpty(existingByTitlePlatform.Path))
                        existingByTitlePlatform.Path = Path.GetDirectoryName(path);
                    if (string.IsNullOrEmpty(existingByTitlePlatform.ExecutablePath))
                        existingByTitlePlatform.ExecutablePath = path;
                    await _gameRepository.UpdateAsync(existingByTitlePlatform.Id, existingByTitlePlatform);
                    _logger.Info($"[PostDownload] Updated existing game '{existingByTitlePlatform.Title}' (ID: {existingByTitlePlatform.Id}) with new paths.");
                    return;
                }

                // CLEAN TITLE for search using the same logic as the scanner
                var (cleanTitle, _) = _titleCleaner.CleanGameTitle(title);

                var searchResults = await metadataService.SearchGamesAsync(cleanTitle);
                Game? game = null;

                if (searchResults.Any())
                {
                    game = await metadataService.GetGameMetadataAsync(searchResults.First().IgdbId!.Value);
                }

                if (game != null)
                {
                    game.Path = Path.GetDirectoryName(path);
                    game.ExecutablePath = path;
                    game.Added = DateTime.UtcNow;
                    game.PlatformId = platformId;
                    
                    // Installer tagging
                    var fileName = Path.GetFileNameWithoutExtension(path).ToLowerInvariant();
                    if (fileName.Contains("setup") || fileName.Contains("install"))
                    {
                        game.Status = GameStatus.InstallerDetected;
                    }

                    await _gameRepository.AddAsync(game);
                    _logger.Info($"[PostDownload] added '{game.Title}' to library (Platform: {platformFolder}, PlatformId: {platformId}, IGDB: {game.IgdbId}).");
                }
                else
                {
                    // Fallback: add without metadata so the game at least appears in the library
                    var fallbackGame = new Game
                    {
                        Title = title,
                        Path = Path.GetDirectoryName(path),
                        ExecutablePath = path,
                        Added = DateTime.UtcNow,
                        PlatformId = platformId,
                        Status = GameStatus.Released,
                        Overview = "Imported via download client. Metadata not found."
                    };
                    await _gameRepository.AddAsync(fallbackGame);
                    _logger.Info($"[PostDownload] Added '{title}' to library without metadata (Platform: {platformFolder}, PlatformId: {platformId}).");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[PostDownload] Error adding game to library: {ex.Message}");
            }
        }

        private static int ResolvePlatformId(string? platformFolder)
        {
            if (string.IsNullOrEmpty(platformFolder)) return 1; // Default: PC

            var match = Games.PlatformDefinitions.AllPlatforms
                .FirstOrDefault(p => p.MatchesFolderName(platformFolder));
            return match?.Id ?? 1;
        }

        private string CleanReleaseName(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            var (cleaned, _) = _titleCleaner.CleanGameTitle(input);

            // Additional import-time cleaning: strip platform suffixes and leftover version fragments
            cleaned = _platformSuffixRegex.Replace(cleaned, " ");
            cleaned = _hotfixRegex.Replace(cleaned, " ");
            cleaned = _trailingNumbersRegex.Replace(cleaned, "");
            cleaned = System.Text.RegularExpressions.Regex.Replace(cleaned, @"\s+", " ").Trim();

            return cleaned;
        }

        // Platform suffixes to strip at import time
        private static readonly System.Text.RegularExpressions.Regex _platformSuffixRegex = new System.Text.RegularExpressions.Regex(
            @"\b(MacOS|Mac OS X|Mac OS|macOS|Windows|Win64|Win32|Linux|Android|iOS)\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        // Leftover version fragments: "hotfix 2", "patch 3", "build 123"
        private static readonly System.Text.RegularExpressions.Regex _hotfixRegex = new System.Text.RegularExpressions.Regex(
            @"\b(hotfix|patch|build|fix|rev)\s*\d*\b",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.Compiled);

        // Trailing standalone numbers left after other cleaning (e.g. "7 Days to Die 5 2" -> "7 Days to Die")
        // Only strip trailing numbers that are clearly not part of the title (single/double digit at end)
        private static readonly System.Text.RegularExpressions.Regex _trailingNumbersRegex = new System.Text.RegularExpressions.Regex(
            @"(\s+\d{1,2})+\s*$",
            System.Text.RegularExpressions.RegexOptions.Compiled);

        private string SanitizeFileName(string name)
        {
             var invalidChars = Path.GetInvalidFileNameChars();
             return string.Join("_", name.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
        }

        // ── CONTENT TYPE DETECTION ───────────────────────────────────────

        internal enum DownloadContentType { MainGame, Patch, DLC }

        private static readonly Regex _patchKeywordRegex = new(
            @"\b(update|patch|hotfix|fix)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _patchVersionRegex = new(
            @"(?:\b(?:update|patch|hotfix|fix)\s*[v.]?\s*)(\d+(?:\.\d+)+)",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _standaloneVersionRegex = new(
            @"\bv(\d+(?:\.\d+)+)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        private static readonly Regex _dlcKeywordRegex = new(
            @"\b(dlc|season\s*pass|expansion|add[- ]?on|bonus\s*content)\b",
            RegexOptions.IgnoreCase | RegexOptions.Compiled);

        internal static (DownloadContentType Type, string? Version, string? ContentName) DetectContentType(string downloadName)
        {
            if (string.IsNullOrEmpty(downloadName))
                return (DownloadContentType.MainGame, null, null);

            // 1. Patch with explicit version: "Game Update v1.05", "Game Patch 1.02"
            var patchVersionMatch = _patchVersionRegex.Match(downloadName);
            if (patchVersionMatch.Success)
            {
                return (DownloadContentType.Patch, patchVersionMatch.Groups[1].Value, null);
            }

            // 2. Patch keyword + standalone version: "Game Update v1.05"
            if (_patchKeywordRegex.IsMatch(downloadName))
            {
                var vMatch = _standaloneVersionRegex.Match(downloadName);
                var version = vMatch.Success ? vMatch.Groups[1].Value : null;
                return (DownloadContentType.Patch, version, null);
            }

            // 3. DLC detection
            var dlcMatch = _dlcKeywordRegex.Match(downloadName);
            if (dlcMatch.Success)
            {
                // Extract DLC name: text after the DLC keyword, cleaned
                var afterKeyword = downloadName.Substring(dlcMatch.Index + dlcMatch.Length).Trim();
                afterKeyword = Regex.Replace(afterKeyword, @"^[\s\-_.]+", "");
                // Strip trailing platform/noise
                afterKeyword = _platformSuffixRegex.Replace(afterKeyword, "").Trim();
                var dlcName = string.IsNullOrEmpty(afterKeyword) ? dlcMatch.Groups[1].Value : afterKeyword;
                // Clean separators
                dlcName = dlcName.Replace('.', ' ').Replace('-', ' ').Replace('_', ' ');
                dlcName = Regex.Replace(dlcName, @"\s+", " ").Trim();
                return (DownloadContentType.DLC, null, dlcName);
            }

            return (DownloadContentType.MainGame, null, null);
        }

        internal static string BuildPatchFileName(string gameTitle, string? version, string extension)
        {
            var versionPart = !string.IsNullOrEmpty(version) ? $"-v{version}" : "";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", $"{gameTitle}-Patch{versionPart}".Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return sanitized + extension;
        }

        internal static string BuildDlcFileName(string gameTitle, string? dlcName, string extension)
        {
            var namePart = !string.IsNullOrEmpty(dlcName) ? $"-{dlcName}" : "";
            var invalidChars = Path.GetInvalidFileNameChars();
            var sanitized = string.Join("_", $"{gameTitle}-DLC{namePart}".Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return sanitized + extension;
        }

        private static bool IsCriticalPath(string? path)
        {
            if (string.IsNullOrEmpty(path)) return true;
            var full = Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar);
            var root = Path.GetPathRoot(full);

            if (full.Equals(root, StringComparison.OrdinalIgnoreCase)) return true;

            var sensitive = new[]
            {
                "/", "/bin", "/boot", "/dev", "/etc", "/home", "/lib", "/proc", "/root",
                "/run", "/sbin", "/sys", "/tmp", "/usr", "/var", "/Users",
                "C:\\", "C:\\Windows", "C:\\Program Files", "C:\\Users"
            };

            foreach (var s in sensitive)
            {
                if (full.Equals(s, StringComparison.OrdinalIgnoreCase)) return true;
            }

            return false;
        }
    }
}
