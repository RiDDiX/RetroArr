using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using RetroArr.Core.Configuration;
using RetroArr.Core.IO;
using RetroArr.Core.Games;
using RetroArr.Core.Rename;
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
        private readonly FileRenamer? _fileRenamer;

        public PostDownloadProcessor(
            ConfigurationService configService,
            IFileMoverService fileMover,
            IGameRepository gameRepository,
            IGameMetadataServiceFactory metadataFactory,
            IArchiveService archiveService,
            TitleCleanerService titleCleaner,
            FileRenamer? fileRenamer = null)
        {
            _configService = configService;
            _fileMover = fileMover;
            _gameRepository = gameRepository;
            _metadataFactory = metadataFactory;
            _archiveService = archiveService;
            _titleCleaner = titleCleaner;
            _fileRenamer = fileRenamer;
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

            // A torrent client keeps seeding from the download folder, so nothing in it
            // gets deleted. Files the cleanup would have removed are skipped on import instead.
            var keep = IsTorrentDownload(download) ? new HashSet<string>() : null;

            // 1. Auto-Extract
            List<string>? extractionFailures = null;
            HashSet<string>? extracted = null;
            if (settings.EnableAutoExtract && Directory.Exists(download.DownloadPath))
            {
                if (keep != null)
                {
                    extracted = new HashSet<string>(StringComparer.Ordinal);
                    RemoveStaleStaging(download.DownloadPath);
                }
                extractionFailures = ExtractArchives(download.DownloadPath, keep, extracted);
                if (extractionFailures.Count > 0 && !settings.EnableAutoMove)
                {
                    var failedList = string.Join(", ", extractionFailures.Select(Path.GetFileName));
                    return PostDownloadResult.Fail($"Extraction failed after retries: {failedList}");
                }
            }

            // 2. Deep Clean
            if (settings.EnableDeepClean && Directory.Exists(download.DownloadPath))
            {
                DeepClean(download.DownloadPath, settings.UnwantedExtensions, keep);
            }

            // 3. Auto-Move / Import
            if (settings.EnableAutoMove)
            {
                var result = await AutoMoveToLibrary(download, keep);
                if (extracted?.Count > 0)
                {
                    try { RemoveExtracted(extracted); }
                    catch (Exception ex) { _logger.Warn($"[PostDownload] Could not clean up extracted files in {download.DownloadPath}: {ex.Message}"); }
                }
                return result;
            }

            return PostDownloadResult.Fail("Auto-move is disabled in post-download settings.");
        }

        private const string ExtractStagingPrefix = ".retroarr-extract-";

        private List<string> ExtractArchives(string path, HashSet<string>? keep, HashSet<string>? extracted)
        {
            // A torrent's own files have to stay as they are, so extract beside them and only add what's new
            var staging = extracted != null ? Path.Combine(path, ExtractStagingPrefix + Guid.NewGuid().ToString("N")) : null;
            var failed = new List<string>();
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories).ToList();
            var archives = files.Where(f => _archiveService.IsArchive(f)).ToList();

            const int maxAttempts = 3;

            foreach (var archivePath in archives)
            {
                if (IsMultiPartNotFirst(archivePath)) continue;

                bool success = false;
                for (int attempt = 1; attempt <= maxAttempts && !success; attempt++)
                {
                    var target = staging ?? path;
                    try
                    {
                        if (staging != null && Directory.Exists(staging)) Directory.Delete(staging, true);
                        var read = new List<string>();
                        if (_archiveService.Extract(archivePath, target, read))
                        {
                            // Only files the extractor really read on into, and only ones the download shipped.
                            // Something merely named like a later volume stays and is imported as it is.
                            var spanned = read.Select(Path.GetFullPath).ToHashSet();
                            var volumes = files.Where(f => f != archivePath && spanned.Contains(Path.GetFullPath(f))).ToList();
                            if (keep != null && staging != null && extracted != null)
                            {
                                var clashes = MergeExtracted(staging, path, extracted);
                                if (clashes == 0)
                                {
                                    _logger.Info($"[PostDownload] Extraction successful on attempt {attempt}. Keeping seeded archive: {archivePath}");
                                    keep.Add(archivePath);
                                    keep.UnionWith(volumes);
                                }
                                else
                                {
                                    _logger.Warn($"[PostDownload] {archivePath} holds {clashes} file(s) named like files already in the download, left those alone and import the archive as it is");
                                }
                            }
                            else
                            {
                                _logger.Info($"[PostDownload] Extraction successful on attempt {attempt}. Deleting archive: {archivePath}");
                                foreach (var volume in volumes.Prepend(archivePath))
                                {
                                    try { File.Delete(volume); } catch { }
                                }
                            }
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
                    finally
                    {
                        if (staging != null)
                        {
                            try { if (Directory.Exists(staging)) Directory.Delete(staging, true); } catch { }
                        }
                    }

                    if (!success && attempt < maxAttempts)
                    {
                        var delayMs = 500 * (1 << (attempt - 1));
                        System.Threading.Thread.Sleep(delayMs);
                    }
                }

                if (!success)
                {
                    _logger.Error($"[PostDownload] Extraction failed after {maxAttempts} attempts for {archivePath} - leaving archive in place for manual recovery.");
                    failed.Add(archivePath);
                }
            }

            return failed;
        }

        // name.part2.rar and later (any padding) are read through name.part1.rar, not on their own.
        // Old style .r00/.s00 and split .001 volumes aren't archive extensions, so they never get here.
        private static readonly Regex _rarPart = new(@"^.+\.part(\d+)\.rar$", RegexOptions.IgnoreCase);

        private static bool IsMultiPartNotFirst(string path)
        {
            var m = _rarPart.Match(Path.GetFileName(path));
            return m.Success && m.Groups[1].Value.TrimStart('0') != "1";
        }

        private void DeepClean(string path, List<string> unwantedExtensions, HashSet<string>? keep)
        {
            var files = Directory.EnumerateFiles(path, "*.*", SearchOption.AllDirectories);
            foreach (var file in files)
            {
                var ext = Path.GetExtension(file).ToLower();
                if (unwantedExtensions.Contains(ext))
                {
                    if (keep != null)
                    {
                        keep.Add(file);
                        continue;
                    }
                    try
                    {
                        _logger.Info($"[PostDownload] Deleting unwanted file: {file}");
                        File.Delete(file);
                    }
                    catch { }
                }
            }
        }

        private async System.Threading.Tasks.Task<PostDownloadResult> AutoMoveToLibrary(DownloadStatus download, HashSet<string>? keep)
        {
            var mediaSettings = _configService.LoadMediaSettings();
            // Match ResolveGameFolder: any rooted path is good enough, mkdir does the rest.
            var libraryRoot = !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Path.IsPathRooted(mediaSettings.DestinationPath)
                ? mediaSettings.DestinationPath
                : !string.IsNullOrEmpty(mediaSettings.FolderPath) && Path.IsPathRooted(mediaSettings.FolderPath)
                    ? mediaSettings.FolderPath
                    : null;

            if (string.IsNullOrEmpty(libraryRoot))
            {
                _logger.Info("[PostDownload] Skip Auto-Move: Library path not configured.");
                return PostDownloadResult.Fail($"Library path not configured. FolderPath='{mediaSettings.FolderPath}', DestinationPath='{mediaSettings.DestinationPath}'.");
            }
            try { Directory.CreateDirectory(libraryRoot); }
            catch (Exception ex) { return PostDownloadResult.Fail($"Cannot create library root '{libraryRoot}': {ex.Message}"); }

            // Game-targeted import: if download is linked to a specific game, import directly to its folder
            if (download.GameId.HasValue)
            {
                return await ImportToGameFolder(download, mediaSettings, libraryRoot, keep);
            }

            var platformFolder = ResolvePlatformFolderName(download.PlatformFolder, mediaSettings.FolderNamingMode);

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
                var files = GetImportFiles(download.DownloadPath!, keep);
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
                int failedCount = 0;
                var sourceInLibrary = false;

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
                    sourceInLibrary |= SamePath(file, destPath);

                    if (CheckTarget(destPath, file) == ImportTarget.AlreadyThere || _fileMover.ImportFile(file, destPath))
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
                    else
                    {
                        failedCount++;
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

                // Anything that failed to import only exists in the download folder
                if (failedCount == 0 && !sourceInLibrary)
                {
                    DeleteSource(download.DownloadPath, keep);
                }
                else
                {
                    _logger.Warn($"[PostDownload] {failedCount} file(s) failed to import, keeping source: {download.DownloadPath}");
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

                    if (CheckTarget(destPath, file) == ImportTarget.AlreadyThere || _fileMover.ImportFile(file, destPath))
                    {
                        var metadataSvc = _metadataFactory.CreateService();
                        await AddMovedGameToLibraryAsync(containerName, destPath, metadataSvc, download.PlatformFolder);

                        if (!SamePath(file, destPath)) DeleteSource(file, keep);

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
        private async System.Threading.Tasks.Task<PostDownloadResult> ImportToGameFolder(DownloadStatus download, MediaSettings mediaSettings, string libraryRoot, HashSet<string>? keep)
        {
            var allGames = await _gameRepository.GetAllLightAsync();
            var game = allGames.FirstOrDefault(g => g.Id == download.GameId);
            if (game == null)
            {
                _logger.Info($"[PostDownload] GameId {download.GameId} not found in DB. Falling back to generic import.");
                download.GameId = null;
                return await AutoMoveToLibrary(download, keep);
            }

            // Determine effective platform: the user-selected download platform takes priority
            var gamePlatform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
            var folderMode = mediaSettings.FolderNamingMode;
            var gamePlatformFolder = gamePlatform?.GetEffectiveFolderName(folderMode);
            // "unknown" is the release detector's no-match marker, not a platform choice
            var downloadPlatformFolder = string.Equals(download.PlatformFolder, "unknown", StringComparison.OrdinalIgnoreCase)
                ? null
                : download.PlatformFolder;

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

            var gate = _importLocks.GetOrAdd(game.Id, _ => new System.Threading.SemaphoreSlim(1, 1));
            await gate.WaitAsync();
            try
            {
                // An import of the same game may have finished while this one waited. The light list is
                // cached for a shorter time than a game's detail.
                game = (await _gameRepository.GetAllLightAsync()).FirstOrDefault(g => g.Id == game.Id) ?? game;
                return await ImportIntoGameFolderAsync(download, mediaSettings, libraryRoot, keep, game, gamePlatform, downloadPlatformFolder ?? gamePlatformFolder ?? "windows");
            }
            finally
            {
                gate.Release();
            }
        }

        private async System.Threading.Tasks.Task<PostDownloadResult> ImportIntoGameFolderAsync(DownloadStatus download, MediaSettings mediaSettings, string libraryRoot, HashSet<string>? keep,
            Game game, Platform? gamePlatform, string effectivePlatformFolder)
        {
            // Resolve target folder: prefer download platform, fall back to game platform
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
            bool isDirectory = Directory.Exists(download.DownloadPath);
            if (isDirectory && OverlapsLibrary(download.DownloadPath!, importFolder, mediaSettings))
            {
                _logger.Warn($"[PostDownload] Download folder {download.DownloadPath} contains the library or the game folder, not importing from it");
                return PostDownloadResult.Fail($"Download folder overlaps the library: {download.DownloadPath}");
            }
            try { Directory.CreateDirectory(importFolder); }
            catch (Exception ex)
            {
                return PostDownloadResult.Fail($"Cannot create import folder '{importFolder}': {ex.Message}");
            }

            // Pre-flight: bail early if the target dir is not writable so the
            // user gets a clean chown hint instead of "no files imported".
            var perm = RetroArr.Core.IO.PathPermissionChecker.Check("ImportFolder", importFolder);
            if (!perm.Writable)
            {
                return PostDownloadResult.Fail($"Cannot write to '{importFolder}'. {perm.Hint}");
            }

            var source = download.DownloadPath!;
            var files = isDirectory ? GetImportFiles(source, keep) : File.Exists(source) ? new[] { source } : Array.Empty<string>();
            var (plan, lastMoveError, atomic) = PlanImport(source, files, importFolder, download.Name, game, gamePlatform, contentType, detectedVersion, detectedDlcName, mediaSettings);
            if (lastMoveError != null) _logger.Warn($"[PostDownload] {lastMoveError}");

            var done = new List<ImportEntry>();
            var created = new List<string>();
            foreach (var entry in plan)
            {
                if (entry.Target == ImportTarget.Taken)
                {
                    _logger.Info($"[PostDownload] Skipped {Path.GetFileName(entry.Source)}, {entry.Dest} is taken");
                    continue;
                }
                if (entry.Target == ImportTarget.AlreadyThere)
                {
                    done.Add(entry);
                    _logger.Info($"[PostDownload] Already in library: {entry.Dest}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(entry.Dest)!);
                string? reason;
                var ok = entry.Content != null
                    ? WriteNew(entry.Dest, entry.Content, out reason)
                    : _fileMover.ImportFile(entry.Source, entry.Dest, out reason);
                if (ok)
                {
                    done.Add(entry);
                    created.Add(entry.Dest);
                    _logger.Info($"[PostDownload] Moved: {Path.GetFileName(entry.Source)} -> {entry.Dest}");
                }
                else
                {
                    lastMoveError = reason ?? $"Could not import {entry.Source}";
                    if (atomic) break;
                }
            }

            // A disc set lands whole or not at all
            if (atomic && lastMoveError != null)
            {
                foreach (var file in created)
                {
                    try { File.Delete(file); }
                    catch (Exception ex) { _logger.Warn($"[PostDownload] Could not remove {file} after a failed import: {ex.Message}"); }
                }
                done.Clear();
            }

            if (done.Count > 0 && lastMoveError == null)
            {
                CleanUpSource(source, isDirectory, importFolder, done, keep);
            }
            else if (done.Count > 0)
            {
                _logger.Warn($"[PostDownload] Some files failed to import ({lastMoveError}), keeping source: {download.DownloadPath}");
            }

            if (done.Count == 0)
            {
                var msg = lastMoveError != null
                    ? $"No files imported for '{game.Title}'. {lastMoveError}"
                    : $"No files imported for '{game.Title}' (target: {importFolder}).";
                return PostDownloadResult.Fail(msg);
            }

            // Update game.Path if not set or not pointing to a valid directory
            if (string.IsNullOrEmpty(game.Path) || !Directory.Exists(game.Path))
            {
                game.Path = targetFolder;
                await _gameRepository.UpdateAsync(game.Id, game);
                _logger.Info($"[PostDownload] Updated game path: '{game.Title}' -> {targetFolder}");
            }

            // Point the game at the best file that landed. The file it points at now stays unless it is gone or an extra,
            // or ranks worse and is either part of this import or a GOG installer part whose exe came later.
            // Extras, and outside program folders a README without an extension, are not the game.
            var software = FileRenamer.IsSoftwarePlatform(gamePlatform);
            var content = done.Select(e => e.Dest).Where(p => !FileRenamer.IsExtra(p) && (software || Path.HasExtension(p))).ToList();
            if (contentType == DownloadContentType.MainGame && content.Count > 0)
            {
                var primary = PickPrimary(content, importFolder, software);
                var current = game.ExecutablePath;
                if (string.IsNullOrEmpty(current) || !(File.Exists(current) || Directory.Exists(current)) || FileRenamer.IsExtra(current)
                    || (Rank(primary, software) < Rank(current, software)
                        && (content.Any(p => SamePath(p, current))
                            || (software && FileRenamer.IsInstallerName(Path.GetFileName(current)) && !current.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)))))
                {
                    game.ExecutablePath = primary;
                    await _gameRepository.UpdateAsync(game.Id, game);
                }
            }

            // The game is in the library now, so the monitor stops searching for it. Patches and DLC don't count,
            // and an installer the scanner found keeps its status. The files are in place either way.
            if (contentType == DownloadContentType.MainGame && content.Count > 0
                && (game.Status is not (GameStatus.Downloaded or GameStatus.InstallerDetected) || game.MissingSince != null))
            {
                try
                {
                    if (game.Status != GameStatus.InstallerDetected) game.Status = GameStatus.Downloaded;
                    game.MissingSince = null;
                    await _gameRepository.UpdateAsync(game.Id, game);
                }
                catch (Exception ex)
                {
                    _logger.Warn($"[PostDownload] Imported '{game.Title}' but could not mark it downloaded: {ex.Message}");
                }
            }

            _logger.Info($"[PostDownload] Game-targeted import complete: {done.Count} file(s) -> {importFolder} [Type: {contentType}]");
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

                bool unresolvedPlatform = platformId == Games.PlatformDefinitions.UnknownPlatformId;
                if (game != null)
                {
                    game.Path = Path.GetDirectoryName(path);
                    game.ExecutablePath = path;
                    game.Added = DateTime.UtcNow;
                    game.PlatformId = platformId;
                    if (unresolvedPlatform)
                    {
                        game.NeedsMetadataReview = true;
                        if (string.IsNullOrEmpty(game.MetadataReviewReason))
                            game.MetadataReviewReason = "Platform unresolved: no folder match.";
                    }

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
                        Overview = "Imported via download client. Metadata not found.",
                        NeedsMetadataReview = unresolvedPlatform,
                        MetadataReviewReason = unresolvedPlatform ? "Platform unresolved: no folder match." : null
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
            if (string.IsNullOrEmpty(platformFolder)) return Games.PlatformDefinitions.UnknownPlatformId;

            var match = Games.PlatformDefinitions.AllPlatforms
                .FirstOrDefault(p => p.MatchesFolderName(platformFolder));
            return match?.Id ?? Games.PlatformDefinitions.UnknownPlatformId;
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
            // Cross-platform-safe: strips the Windows-reserved set even on Linux so the
            // on-disk folder/file name survives on SMB/NTFS (no "DTWOE3~0" mangling).
            return RetroArr.Core.IO.FileNameSanitizer.Sanitize(name, "unknown");
        }

        // Map a download's platform hint (free-form string from indexer/queue) to
        // the canonical folder name for the active naming mode (native/retrobat/
        // batocera). Mirrors what ImportToGameFolder does for the GameId path so
        // both code paths land in the same on-disk folder for a given platform.
        private static string? ResolvePlatformFolderName(string? hint, string? folderMode)
        {
            if (string.IsNullOrWhiteSpace(hint)) return hint;
            var match = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.MatchesFolderName(hint));
            return match?.GetEffectiveFolderName(folderMode) ?? hint;
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
            return RetroArr.Core.IO.FileNameSanitizer.Sanitize($"{gameTitle}-Patch{versionPart}", "unknown") + extension;
        }

        internal static string BuildDlcFileName(string gameTitle, string? dlcName, string extension)
        {
            var namePart = !string.IsNullOrEmpty(dlcName) ? $"-{dlcName}" : "";
            return RetroArr.Core.IO.FileNameSanitizer.Sanitize($"{gameTitle}-DLC{namePart}", "unknown") + extension;
        }

        private enum ImportTarget { Free, AlreadyThere, Taken }

        private sealed record ImportEntry(string Source, string Dest, byte[]? Content, bool Extra, ImportTarget Target);

        private readonly System.Collections.Concurrent.ConcurrentDictionary<int, System.Threading.SemaphoreSlim> _importLocks = new();

        // Decides where every file of a download lands before anything is written. Atomic plans (disc sets)
        // land whole or not at all. The error is set when nothing may be imported.
        private (List<ImportEntry> Plan, string? Error, bool Atomic) PlanImport(string source, string[] files, string importFolder, string release,
            Game game, Platform? platform, DownloadContentType type, string? version, string? dlcName, MediaSettings settings)
        {
            var renaming = _fileRenamer != null && FileRenamer.Applies(platform, settings);
            IEnumerable<string> Stems(string file) => Candidates(file, release, game, platform, type, version, dlcName, settings, renaming);

            if (files.Length == 0) return (new List<ImportEntry>(), null, false);
            if (files.Length == 1)
            {
                var (dest, target) = PickDestination(importFolder, files[0], Stems(files[0]));
                return target == ImportTarget.Taken
                    ? (new List<ImportEntry>(), $"Target exists, not overwriting: {dest}", false)
                    : (new List<ImportEntry> { new(files[0], dest, null, false, target) }, null, false);
            }
            files = files.OrderBy(f => f, StringComparer.Ordinal).ToArray();

            // Program folders, installers and romsets are found by their file names, so they keep them. So does a download
            // that sits in the game folder: its files can't be told apart from the ones that were there before.
            var dl = FullDir(source);
            var folder = FullDir(importFolder);
            var inGameFolder = string.Equals(dl, folder, StringComparison.OrdinalIgnoreCase) || IsUnder(dl, folder);
            if (renaming && !inGameFolder && !FileRenamer.IsRomsetPlatform(platform) && !FileRenamer.IsSoftwarePlatform(platform))
            {
                // a README without an extension is no more a game file than readme.txt
                var mains = files.Where(f => !FileRenamer.IsExtra(f) && Path.HasExtension(f)).ToList();
                if (mains.Count == 1 && SamePath(Path.GetDirectoryName(mains[0])!, dl))
                {
                    var main = mains[0];
                    var (dest, target) = PickDestination(importFolder, main, Stems(main));
                    if (target == ImportTarget.Taken) return (new List<ImportEntry>(), $"Target exists, not overwriting: {dest}", false);
                    var renamed = new[] { (main, Path.GetFileNameWithoutExtension(dest)) };
                    return (files.Select(f =>
                    {
                        if (f == main) return new ImportEntry(f, dest, null, false, target);
                        var extraDest = Path.Combine(importFolder, Path.GetDirectoryName(Path.GetRelativePath(source, f))!, FileRenamer.ExtraName(f, renamed));
                        return new ImportEntry(f, extraDest, null, true, CheckTarget(extraDest, f));
                    }).ToList(), null, false);
                }
                if (type == DownloadContentType.MainGame)
                {
                    var set = PlanDiscSet(source, files, importFolder, release, game, platform, settings);
                    if (set != null) return (set, null, true);
                }
            }
            return KeepNames(source, files, importFolder, release);
        }

        // Candidate stems for a file imported on its own, best first. The disc token always stays,
        // another release of the same game (a region, a revision) gets its tags instead of replacing a file.
        private IEnumerable<string> Candidates(string sourceFile, string release, Game game, Platform? platform,
            DownloadContentType type, string? version, string? dlcName, MediaSettings settings, bool renaming)
        {
            var fileName = Path.GetFileName(sourceFile);
            var stem = Path.GetFileNameWithoutExtension(sourceFile);
            if (FileRenamer.IsRomsetPlatform(platform) || (FileRenamer.IsSoftwarePlatform(platform) && FileRenamer.IsInstallerName(fileName)))
            {
                yield return stem;
                yield break;
            }

            var suffix = renaming ? _fileRenamer!.GroupSuffix(fileName, release, settings) : "";
            var disc = type == DownloadContentType.MainGame ? FileRenamer.DiscToken(stem) ?? FileRenamer.DiscToken(release) : null;
            var body = type switch
            {
                DownloadContentType.Patch => renaming
                    ? _fileRenamer!.Render(settings.UpdateFileTemplate, game, platform, fileName, release, null, version, null)
                    : BuildPatchFileName(game.Title, version, ""),
                DownloadContentType.DLC => renaming
                    ? _fileRenamer!.Render(settings.DlcFileTemplate, game, platform, fileName, release, null, null, dlcName)
                    : BuildDlcFileName(game.Title, dlcName, ""),
                _ => renaming
                    ? _fileRenamer!.Render(settings.MainFileTemplate, game, platform, fileName, release, disc, null, null)
                    : game.Title
            };
            var discPart = disc != null && !FileRenamer.HasDiscToken(body) ? $" ({disc})" : "";

            yield return body + discPart + suffix;
            yield return body + TagsFor(stem, release, body) + discPart + suffix;
            // an update without a version keeps its own name rather than replacing the last one
            if (type != DownloadContentType.MainGame) yield return stem;
            if (renaming && string.Equals(settings.FileConflictBehavior, "Suffix", StringComparison.OrdinalIgnoreCase))
            {
                for (var i = 1; i <= 999; i++) yield return $"{body}{discPart} ({i}){suffix}";
            }
        }

        // The file's own bracket tags (else the release's) that the name doesn't carry yet, with a leading space
        private static string TagsFor(string stem, string release, string body)
        {
            var tags = FileRenamer.BracketTags(stem);
            if (tags.Length == 0) tags = FileRenamer.BracketTags(release);
            var missing = tags.Where(t => body.IndexOf(t, StringComparison.OrdinalIgnoreCase) < 0).ToArray();
            return missing.Length == 0 ? "" : " " + string.Join(" ", missing);
        }

        // The first stem whose name is free, or already holds this very file. Taken with the last name tried otherwise.
        private static (string Path, ImportTarget Target) PickDestination(string folder, string sourceFile, IEnumerable<string> stems)
        {
            var ext = Path.GetExtension(sourceFile);
            string? last = null;
            foreach (var stem in stems)
            {
                var dest = Path.Combine(folder, RetroArr.Core.IO.FileNameSanitizer.Sanitize(stem, "unknown") + ext);
                if (dest == last) continue;
                last = dest;
                var target = CheckTarget(dest, sourceFile);
                if (target != ImportTarget.Taken) return (dest, target);
            }
            return (last!, ImportTarget.Taken);
        }

        // A disc set is named as one: plain names first, then with the files' tags. Null keeps the release's names.
        private List<ImportEntry>? PlanDiscSet(string source, string[] files, string importFolder, string release, Game game, Platform? platform, MediaSettings settings)
        {
            foreach (var withTags in new[] { false, true })
            {
                string StemFor(string name, string? disc)
                {
                    var body = _fileRenamer!.Render(settings.MainFileTemplate, game, platform, name, release, disc, null, null);
                    var discPart = disc != null && !FileRenamer.HasDiscToken(body) ? $" ({disc})" : "";
                    var tags = withTags ? TagsFor(Path.GetFileNameWithoutExtension(name), release, body) : "";
                    return RetroArr.Core.IO.FileNameSanitizer.Sanitize(body + tags + discPart + _fileRenamer.GroupSuffix(name, release, settings), "unknown");
                }

                List<FileRenamer.PlannedFile>? planned;
                try { planned = FileRenamer.PlanDiscRelease(source, files, StemFor); }
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException)
                {
                    _logger.Warn($"[PostDownload] Could not read the disc set in {source}, keeping its names: {ex.Message}");
                    return null;
                }
                if (planned == null) return null;

                var dests = planned.Select(p => Path.Combine(importFolder, p.Name)).ToList();
                if (dests.Distinct(StringComparer.OrdinalIgnoreCase).Count() < dests.Count) continue;
                var entries = planned.Select((p, i) => new ImportEntry(p.Source, dests[i], p.Content, FileRenamer.IsExtra(p.Source),
                    p.Content != null ? ContentTarget(dests[i], p.Content) : CheckTarget(dests[i], p.Source))).ToList();
                if (entries.Any(e => !e.Extra && e.Target == ImportTarget.Taken)) continue;
                return entries;
            }
            return null;
        }

        // Every file keeps its path inside the release. If one of them is taken by another file,
        // the whole release goes to a folder of its own instead, so nothing is replaced.
        private static (List<ImportEntry> Plan, string? Error, bool Atomic) KeepNames(string source, string[] files, string importFolder, string release)
        {
            List<ImportEntry> At(string folder, bool check) => files.Select(f =>
            {
                var dest = Path.Combine(folder, Path.GetRelativePath(source, f));
                return new ImportEntry(f, dest, null, FileRenamer.IsExtra(f), check ? CheckTarget(dest, f) : ImportTarget.Free);
            }).ToList();

            var plan = At(importFolder, true);
            var taken = plan.FirstOrDefault(e => !e.Extra && e.Target == ImportTarget.Taken);
            if (taken == null) return (plan, null, false);

            var name = RetroArr.Core.IO.FileNameSanitizer.Sanitize(release, "release");
            for (var i = 1; i <= 99; i++)
            {
                var alt = Path.Combine(importFolder, i == 1 ? name : $"{name} ({i})");
                if (File.Exists(alt)) continue;
                // An earlier import of this very release is there already, or nothing in the folder is in the way
                var into = At(alt, Directory.Exists(alt));
                if (into.Any(e => !e.Extra && e.Target == ImportTarget.Taken)) continue;
                _logger.Info($"[PostDownload] {taken.Dest} is taken by another file, importing the release into {alt}");
                return (into, null, false);
            }
            return (new List<ImportEntry>(), $"Target exists, not overwriting: {taken.Dest}", false);
        }

        private static readonly Regex _junkExeRegex = new(@"unins|crash|redist|prereq|dxsetup|directx|dotnet", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // Lower is a better pick for ExecutablePath: set descriptors first, then installers, then program files
        private static int Rank(string path, bool software)
        {
            switch (Path.GetExtension(path).ToLowerInvariant())
            {
                case ".m3u": return 0;
                case ".cue": return 1;
                case ".gdi": return 2;
                case ".exe" when software:
                    var stem = Path.GetFileNameWithoutExtension(path);
                    if (_junkExeRegex.IsMatch(stem)) return 8;
                    return stem.StartsWith("setup", StringComparison.OrdinalIgnoreCase) || stem.StartsWith("install", StringComparison.OrdinalIgnoreCase) ? 3 : 4;
            }
            return FileRenamer.IsExtra(path) ? 9 : 5;
        }

        private static string PickPrimary(IEnumerable<string> landed, string importFolder, bool software) =>
            landed.OrderBy(p => Rank(p, software))
                  .ThenBy(p => Path.GetRelativePath(importFolder, p).Count(c => c == Path.DirectorySeparatorChar))
                  .ThenBy(DiscNumber)
                  .ThenByDescending(SizeOf)
                  .ThenBy(p => p, StringComparer.Ordinal)
                  .First();

        private static int DiscNumber(string path)
        {
            var m = Regex.Match(FileRenamer.DiscToken(Path.GetFileName(path)) ?? "", @"\d+");
            return m.Success && int.TryParse(m.Value, out var n) ? n : 0;
        }

        private static long SizeOf(string path)
        {
            try { return new FileInfo(path).Length; }
            catch (Exception) { return 0; }
        }

        // The source itself, a hardlink or a byte-identical copy of it is already there and is left alone.
        private static ImportTarget CheckTarget(string dest, string sourceFile)
        {
            if (!File.Exists(dest)) return ImportTarget.Free;
            // Exact match only: on a case-sensitive disk "game.gba" is another file, the content check below decides.
            if (Path.GetFullPath(dest) == Path.GetFullPath(sourceFile)) return ImportTarget.AlreadyThere;
            try
            {
                using (var a = File.OpenRead(dest))
                using (var b = File.OpenRead(sourceFile))
                {
                    if (a.Length != b.Length) return ImportTarget.Taken;
                    var bufA = new byte[81920];
                    var bufB = new byte[81920];
                    int read;
                    while ((read = a.ReadAtLeast(bufA, bufA.Length, throwOnEndOfStream: false)) > 0)
                    {
                        b.ReadExactly(bufB, 0, read);
                        if (!bufA.AsSpan(0, read).SequenceEqual(bufB.AsSpan(0, read))) return ImportTarget.Taken;
                    }
                }
                // A symlink to the source is replaced, the source may be deleted after the import. Any other link stays.
                return RetroArr.Core.IO.FileMoverService.LinksTo(dest, sourceFile) ? ImportTarget.Free : ImportTarget.AlreadyThere;
            }
            catch (Exception ex)
            {
                _logger.Warn($"[PostDownload] Could not compare {sourceFile} with existing {dest}: {ex.Message}");
                return ImportTarget.Taken;
            }
        }

        // For a descriptor written with new references: the same bytes are already there, or the name is taken
        private static ImportTarget ContentTarget(string dest, byte[] content)
        {
            if (!File.Exists(dest)) return ImportTarget.Free;
            try { return File.ReadAllBytes(dest).AsSpan().SequenceEqual(content) ? ImportTarget.AlreadyThere : ImportTarget.Taken; }
            catch (Exception ex)
            {
                _logger.Warn($"[PostDownload] Could not read existing {dest}: {ex.Message}");
                return ImportTarget.Taken;
            }
        }

        private static bool WriteNew(string dest, byte[] content, out string? reason)
        {
            reason = null;
            FileStream stream;
            try { stream = new FileStream(dest, FileMode.CreateNew, FileAccess.Write); }
            catch (Exception ex)
            {
                reason = $"Could not create {dest}: {ex.Message}";
                return false;
            }
            try
            {
                using (stream) stream.Write(content);
                return true;
            }
            catch (Exception ex)
            {
                reason = $"Could not write {dest}: {ex.Message}";
                try { File.Delete(dest); } catch { }
                return false;
            }
        }

        // A client that completes into the library, a platform folder or the game folder itself would have
        // its folder imported onto itself and then deleted.
        private static bool OverlapsLibrary(string downloadFolder, string importFolder, MediaSettings settings)
        {
            var dl = FullDir(downloadFolder);
            if (IsUnder(FullDir(importFolder), dl)) return true;
            foreach (var root in new[] { settings.FolderPath, settings.DestinationPath })
            {
                if (string.IsNullOrEmpty(root) || !Path.IsPathRooted(root)) continue;
                var r = FullDir(root);
                if (string.Equals(r, dl, StringComparison.OrdinalIgnoreCase) || IsUnder(r, dl)) return true;
            }
            return false;
        }

        private static string FullDir(string path) => Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        private static bool IsUnder(string path, string folder)
        {
            var prefix = Path.EndsInDirectorySeparator(folder) ? folder : folder + Path.DirectorySeparatorChar;
            return path.Length > prefix.Length && path.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static void CleanUpSource(string source, bool isDirectory, string importFolder, List<ImportEntry> done, HashSet<string>? keep)
        {
            if (!isDirectory)
            {
                if (!SamePath(source, done[0].Dest)) DeleteSource(source, keep);
                return;
            }
            var dl = FullDir(source);
            var folder = FullDir(importFolder);
            if (keep != null || !(string.Equals(dl, folder, StringComparison.OrdinalIgnoreCase) || IsUnder(dl, folder)))
            {
                DeleteSource(source, keep);
            }
            else if (IsUnder(dl, folder))
            {
                _logger.Info($"[PostDownload] Download folder lies inside the game folder, leaving it in place: {source}");
            }
            else
            {
                // The download folder is the game folder: only the old names go
                foreach (var entry in done.Where(e => !SamePath(e.Source, e.Dest))) DeleteSource(entry.Source, null);
            }
        }

        private static bool SamePath(string a, string b) =>
            string.Equals(Path.GetFullPath(a), Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase);

        private static readonly string[] TorrentClients = { "qBittorrent", "Transmission", "Deluge" };

        private bool IsTorrentDownload(DownloadStatus download)
        {
            var implementation = _configService.LoadDownloadClients().FirstOrDefault(c => c.Id == download.ClientId)?.Implementation;
            return TorrentClients.Contains(implementation, StringComparer.OrdinalIgnoreCase);
        }

        // Moves nothing if any file would land on something that isn't ours (only files an earlier
        // archive of the same run put there may be replaced). 'extracted' collects the files and folders it creates.
        private static int MergeExtracted(string staging, string path, HashSet<string> extracted)
        {
            var moves = Directory.EnumerateFiles(staging, "*", SearchOption.AllDirectories)
                .Select(file => (File: file, Dest: Path.Combine(path, Path.GetRelativePath(staging, file))))
                .ToList();
            var clashes = moves.Count(m => Directory.Exists(m.Dest) || (File.Exists(m.Dest) && !extracted.Contains(m.Dest)) || ParentIsFile(m.Dest, path));
            if (clashes > 0) return clashes;

            foreach (var (file, dest) in moves)
            {
                var missing = new List<string>();
                for (var dir = Path.GetDirectoryName(dest); dir != null && !Directory.Exists(dir); dir = Path.GetDirectoryName(dir))
                {
                    missing.Add(dir);
                }
                Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                extracted.UnionWith(missing);
                // throws instead of replacing a file that showed up since the check
                File.Move(file, dest, overwrite: extracted.Contains(dest));
                extracted.Add(dest);
            }
            return 0;
        }

        private static bool ParentIsFile(string dest, string path)
        {
            // GetDirectoryName normalises separators, so compare against the download folder the same way
            var root = Path.GetDirectoryName(Path.Combine(path, "x"))!;
            for (var dir = Path.GetDirectoryName(dest); dir != null && dir.Length > root.Length; dir = Path.GetDirectoryName(dir))
            {
                if (File.Exists(dir)) return true;
            }
            return false;
        }

        // Extracted files aren't part of the torrent and the library has its own copy by now
        private static void RemoveExtracted(HashSet<string> extracted)
        {
            // deepest first, so a folder is empty once its files are gone
            foreach (var entry in extracted.OrderByDescending(e => e.Count(c => c == '/' || c == '\\')).ThenByDescending(e => e.Length))
            {
                try
                {
                    if (File.Exists(entry)) File.Delete(entry);
                    else if (Directory.Exists(entry) && !Directory.EnumerateFileSystemEntries(entry).Any()) Directory.Delete(entry);
                }
                catch (Exception ex) { _logger.Warn($"[PostDownload] Could not remove extracted {entry}: {ex.Message}"); }
            }
        }

        // Left behind if RetroArr stopped mid-extraction. Only names RetroArr generates, never a folder the torrent ships.
        private static void RemoveStaleStaging(string path)
        {
            try
            {
                foreach (var dir in Directory.EnumerateDirectories(path, ExtractStagingPrefix + "*").ToList())
                {
                    var suffix = Path.GetFileName(dir).Substring(ExtractStagingPrefix.Length);
                    if (suffix.Length != 32 || !suffix.All(Uri.IsHexDigit)) continue;
                    Directory.Delete(dir, true);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[PostDownload] Could not remove leftover extraction folder in {path}: {ex.Message}");
            }
        }

        private static string[] GetImportFiles(string path, HashSet<string>? keep)
        {
            var files = Directory.GetFiles(path, "*.*", SearchOption.AllDirectories);
            return keep == null ? files : files.Where(f => !keep.Contains(f)).ToArray();
        }

        private static void DeleteSource(string? path, HashSet<string>? keep)
        {
            if (keep != null)
            {
                _logger.Info($"[PostDownload] Leaving source in place, the torrent client still seeds from it: {path}");
                return;
            }
            if (IsCriticalPath(path))
            {
                _logger.Info($"[PostDownload] BLOCKED: Refusing to delete critical path: {path}");
                return;
            }
            try
            {
                _logger.Info($"[PostDownload] Cleaning up source: {path}");
                if (Directory.Exists(path)) Directory.Delete(path, true);
                else File.Delete(path!);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[PostDownload] Warning: Could not delete source {path}: {ex.Message}");
            }
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
