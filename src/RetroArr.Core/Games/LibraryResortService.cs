using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Games
{
    public class LibraryResortService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.LibraryOverview);
        private readonly IGameRepository _gameRepository;
        private readonly ConfigurationService _configService;
        private readonly object _lock = new();
        private List<StructureIssue> _lastScanResults = new();
        private OperationPlan? _activePlan;
        private ResortProgress _progress = new();
        private readonly string _planPersistDir;

        private static readonly HashSet<string> ContainerExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            ".ps3", ".ps3dir", ".psn", ".ps4"
        };

        public LibraryResortService(IGameRepository gameRepository, ConfigurationService configService)
        {
            _gameRepository = gameRepository;
            _configService = configService;
            _planPersistDir = Path.Combine(configService.GetConfigDirectory(), "resort");
            Directory.CreateDirectory(_planPersistDir);
            TryLoadPendingPlan();
        }

        public IReadOnlyList<StructureIssue> LastScanResults
        {
            get { lock (_lock) { return _lastScanResults.ToList(); } }
        }

        public OperationPlan? ActivePlan
        {
            get { lock (_lock) { return _activePlan; } }
        }

        public ResortProgress Progress
        {
            get { lock (_lock) { return _progress; } }
        }

        // ── BATCH PLATFORM FIX ────────────────────────────────────────────

        public async Task<List<(int GameId, string Title, int OldPlatformId, int NewPlatformId, string NewPlatformName)>> FixPlatformAssignmentsAsync()
        {
            var settings = _configService.LoadMediaSettings();
            var libraryRoot = ResolveLibraryRoot(settings);
            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
                return new();

            var allGames = await _gameRepository.GetAllLightAsync();
            var allPlatforms = PlatformDefinitions.AllPlatforms;
            var fixes = new List<(int GameId, string Title, int OldPlatformId, int NewPlatformId, string NewPlatformName)>();

            foreach (var game in allGames)
            {
                if (string.IsNullOrEmpty(game.Path)) continue;

                var folderName = ExtractPlatformFolder(game.Path, libraryRoot);
                if (string.IsNullOrEmpty(folderName)) continue;

                var detectedPlatform = allPlatforms.FirstOrDefault(p => p.MatchesFolderName(folderName));
                if (detectedPlatform == null) continue;

                if (detectedPlatform.Id != game.PlatformId)
                {
                    // Check that changing platform won't violate the UNIQUE(Title, PlatformId) constraint
                    bool wouldCollide = allGames.Any(g =>
                        g.Id != game.Id &&
                        g.Title.Equals(game.Title, StringComparison.OrdinalIgnoreCase) &&
                        g.PlatformId == detectedPlatform.Id);
                    if (wouldCollide) continue;

                    var oldId = game.PlatformId;
                    game.PlatformId = detectedPlatform.Id;
                    try
                    {
                        await _gameRepository.UpdateAsync(game.Id, game);
                        fixes.Add((game.Id, game.Title, oldId, detectedPlatform.Id, detectedPlatform.Name));
                    }
                    catch { game.PlatformId = oldId; }
                }
            }

            return fixes;
        }

        // ── SCAN ────────────────────────────────────────────────────────

        public async Task<List<StructureIssue>> ScanAsync(ResortScanRequest? request = null)
        {
            var settings = _configService.LoadMediaSettings();
            var libraryRoot = ResolveLibraryRoot(settings);
            if (string.IsNullOrEmpty(libraryRoot) || !Directory.Exists(libraryRoot))
            {
                return new List<StructureIssue>();
            }

            var allGames = await _gameRepository.GetAllLightAsync();
            var issues = new List<StructureIssue>();

            // Filter by request
            if (request?.GameId.HasValue == true)
            {
                allGames = allGames.Where(g => g.Id == request.GameId.Value).ToList();
            }
            if (request?.PlatformId.HasValue == true)
            {
                allGames = allGames.Where(g => g.PlatformId == request.PlatformId.Value).ToList();
            }

            var allPlatforms = PlatformDefinitions.AllPlatforms;

            foreach (var game in allGames)
            {
                var platform = allPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
                if (platform == null) continue;

                var expectedPath = ComputeExpectedGamePath(settings, libraryRoot, platform, game);

                // D7: Missing game folder
                if (!string.IsNullOrEmpty(game.Path) && !Directory.Exists(game.Path) && !File.Exists(game.Path))
                {
                    issues.Add(new StructureIssue
                    {
                        GameId = game.Id,
                        GameTitle = game.Title,
                        PlatformId = game.PlatformId,
                        PlatformName = platform.Name,
                        IssueType = IssueType.MissingGameFolder,
                        RuleFailed = "R5",
                        Description = $"Game path no longer exists on disk.",
                        CurrentPath = game.Path,
                        ExpectedPath = expectedPath,
                        CurrentFolder = ExtractPlatformFolder(game.Path, libraryRoot),
                        ProposedAction = OperationType.UpdateDbPath
                    });
                    continue;
                }

                if (string.IsNullOrEmpty(game.Path)) continue;

                var currentPath = NormalizePath(game.Path);
                bool isFileMode = File.Exists(game.Path);

                var expectedPlatformFolder = platform.GetEffectiveFolderName(settings.FolderNamingMode);
                var platformDir = NormalizePath(Path.Combine(libraryRoot, expectedPlatformFolder));

                // Guard: if game.Path IS the platform directory itself, it's bad data
                if (currentPath.Equals(platformDir, PathComparison))
                {
                    issues.Add(new StructureIssue
                    {
                        GameId = game.Id,
                        GameTitle = game.Title,
                        PlatformId = game.PlatformId,
                        PlatformName = platform.Name,
                        IssueType = IssueType.DbPathMismatch,
                        RuleFailed = "R5",
                        Description = $"Game path points to the platform directory itself, not a specific game.",
                        CurrentPath = game.Path,
                        ExpectedPath = expectedPath,
                        CurrentFolder = expectedPlatformFolder,
                        ProposedAction = OperationType.UpdateDbPath
                    });
                    continue;
                }

                // For file-mode games (single ROM files), the expected path keeps
                // the original filename — ROM names carry region/revision metadata
                // that must not be stripped. Only the platform folder is checked.
                string effectiveExpected;
                if (isFileMode)
                {
                    var originalFileName = Path.GetFileName(game.Path);
                    effectiveExpected = Path.Combine(libraryRoot, expectedPlatformFolder, originalFileName);
                }
                else
                {
                    // For folder-mode games with container extensions (.ps3 etc.),
                    // preserve the container extension in the expected path.
                    var currentFolderName = Path.GetFileName(currentPath.TrimEnd(Path.DirectorySeparatorChar));
                    var containerExt = Path.GetExtension(currentFolderName);
                    if (!string.IsNullOrEmpty(containerExt) && ContainerExtensions.Contains(containerExt))
                    {
                        var expectedTitle = Path.GetFileName(NormalizePath(expectedPath).TrimEnd(Path.DirectorySeparatorChar));
                        // Strip trailing platform suffix that matches the container ext
                        // e.g. title "Game ps3" + ext ".ps3" → "Game.ps3" (not "Game ps3.ps3")
                        var platformSuffix = " " + containerExt.TrimStart('.');
                        if (expectedTitle.EndsWith(platformSuffix, StringComparison.OrdinalIgnoreCase))
                        {
                            expectedTitle = expectedTitle.Substring(0, expectedTitle.Length - platformSuffix.Length);
                        }
                        effectiveExpected = Path.Combine(libraryRoot, expectedPlatformFolder, expectedTitle + containerExt);
                    }
                    else
                    {
                        effectiveExpected = expectedPath;
                    }
                }

                var normalizedExpected = NormalizePath(effectiveExpected);

                // D5: DB path mismatch — game exists on disk but path differs from expected
                if (!currentPath.Equals(normalizedExpected, PathComparison))
                {
                    // Determine sub-type: wrong platform folder (D1/D8) vs wrong game name (D2)
                    var currentPlatformFolder = ExtractPlatformFolder(currentPath, libraryRoot);

                    if (!string.IsNullOrEmpty(currentPlatformFolder) &&
                        !currentPlatformFolder.Equals(expectedPlatformFolder, StringComparison.OrdinalIgnoreCase))
                    {
                        // Check if the current folder is a recognized platform folder
                        // (any platform, not just the one in the DB). If it is, the game
                        // was intentionally placed there — multi-platform titles often
                        // exist on disk under a different platform than their DB entry.
                        bool currentFolderIsKnownPlatform = allPlatforms.Any(p => p.MatchesFolderName(currentPlatformFolder));

                        if (currentFolderIsKnownPlatform)
                        {
                            // The game is in a valid platform folder that differs from
                            // its DB PlatformId. This is a DB metadata mismatch, not a
                            // filesystem problem. D8 only applies when the SAME platform
                            // has a different naming convention for the active mode.
                            bool isCompatMismatch = platform.MatchesFolderName(currentPlatformFolder);

                            if (isCompatMismatch)
                            {
                                issues.Add(new StructureIssue
                                {
                                    GameId = game.Id,
                                    GameTitle = game.Title,
                                    PlatformId = game.PlatformId,
                                    PlatformName = platform.Name,
                                    IssueType = IssueType.CompatibilityModeMismatch,
                                    RuleFailed = "R1",
                                    Description = $"Platform folder '{currentPlatformFolder}' doesn't match current mode '{settings.FolderNamingMode}' (expected '{expectedPlatformFolder}').",
                                    CurrentPath = game.Path,
                                    ExpectedPath = effectiveExpected,
                                    CurrentFolder = currentPlatformFolder,
                                    ProposedAction = isFileMode ? OperationType.MoveFile : OperationType.MoveGameFolder
                                });
                            }
                            // else: game is in a different but valid platform folder
                            // (e.g. DB says Steam but file is in xbox360/) — skip,
                            // the filesystem placement is intentional.
                        }
                        else
                        {
                            // Current folder is NOT any known platform (e.g. typo like
                            // "xbox36" or "nintendoswitch"). Flag as wrong platform.
                            issues.Add(new StructureIssue
                            {
                                GameId = game.Id,
                                GameTitle = game.Title,
                                PlatformId = game.PlatformId,
                                PlatformName = platform.Name,
                                IssueType = IssueType.WrongPlatformFolder,
                                RuleFailed = "R1",
                                Description = $"Game is under '{currentPlatformFolder}' which is not a recognized platform folder.",
                                CurrentPath = game.Path,
                                ExpectedPath = effectiveExpected,
                                CurrentFolder = currentPlatformFolder,
                                ProposedAction = isFileMode ? OperationType.MoveFile : OperationType.MoveGameFolder
                            });
                        }
                    }
                    else if (!isFileMode)
                    {
                        // D2: Wrong game folder name — only for folder-mode games.
                        // File-mode games (ROMs) keep their original filenames because
                        // they contain region, revision, and format metadata.
                        var currentGameFolder = Path.GetFileName(currentPath.TrimEnd(Path.DirectorySeparatorChar));
                        var expectedGameFolder = Path.GetFileName(normalizedExpected.TrimEnd(Path.DirectorySeparatorChar));

                        // For container-format folders (.ps3 etc.), compare base names
                        // without the container extension. The extension is emulator-required.
                        var currentBase = currentGameFolder;
                        var expectedBase = expectedGameFolder;
                        var currentContainerExt = Path.GetExtension(currentGameFolder);
                        if (!string.IsNullOrEmpty(currentContainerExt) && ContainerExtensions.Contains(currentContainerExt))
                        {
                            currentBase = Path.GetFileNameWithoutExtension(currentGameFolder);
                        }
                        var expectedContainerExt = Path.GetExtension(expectedGameFolder);
                        if (!string.IsNullOrEmpty(expectedContainerExt) && ContainerExtensions.Contains(expectedContainerExt))
                        {
                            expectedBase = Path.GetFileNameWithoutExtension(expectedGameFolder);
                            // Also strip trailing platform suffix from expected base
                            // e.g. "Game ps3" → "Game" when container is .ps3
                            var extName = expectedContainerExt.TrimStart('.');
                            if (expectedBase.EndsWith(" " + extName, StringComparison.OrdinalIgnoreCase))
                            {
                                expectedBase = expectedBase.Substring(0, expectedBase.Length - extName.Length - 1);
                            }
                        }

                        if (!currentBase.Equals(expectedBase, PathComparison))
                        {
                            issues.Add(new StructureIssue
                            {
                                GameId = game.Id,
                                GameTitle = game.Title,
                                PlatformId = game.PlatformId,
                                PlatformName = platform.Name,
                                IssueType = IssueType.WrongGameFolderName,
                                RuleFailed = "R2",
                                Description = $"Game folder '{currentGameFolder}' doesn't match expected name '{expectedGameFolder}'.",
                                CurrentPath = game.Path,
                                ExpectedPath = effectiveExpected,
                                CurrentFolder = ExtractPlatformFolder(currentPath, libraryRoot),
                                ProposedAction = OperationType.RenameGameFolder
                            });
                        }
                    }
                }
            }

            // D6: Orphaned files — scan library folders for items not linked to any game
            var gamePaths = new HashSet<string>(
                allGames.Where(g => !string.IsNullOrEmpty(g.Path)).Select(g => NormalizePath(g.Path!)),
                PathComparer);

            foreach (var platformDef in allPlatforms)
            {
                var effectiveFolder = platformDef.GetEffectiveFolderName(settings.FolderNamingMode);
                var platformDir = Path.Combine(libraryRoot, effectiveFolder);
                if (!Directory.Exists(platformDir)) continue;

                // Also check alternative folder names for the same platform
                CheckOrphansInDir(platformDir, platformDef, gamePaths, issues, request);

                // Check other folder name variants that might exist on disk
                foreach (var altName in GetAllFolderNames(platformDef))
                {
                    if (altName.Equals(effectiveFolder, StringComparison.OrdinalIgnoreCase)) continue;
                    var altDir = Path.Combine(libraryRoot, altName);
                    if (Directory.Exists(altDir))
                    {
                        CheckOrphansInDir(altDir, platformDef, gamePaths, issues, request);
                    }
                }
            }

            lock (_lock)
            {
                _lastScanResults = issues;
            }

            return issues;
        }

        // ── PREVIEW ─────────────────────────────────────────────────────

        public OperationPlan GeneratePreview(List<string> issueIds, ConflictResolution defaultResolution)
        {
            List<StructureIssue> selectedIssues;
            lock (_lock)
            {
                selectedIssues = _lastScanResults.Where(i => issueIds.Contains(i.Id)).ToList();
            }

            var plan = new OperationPlan();

            foreach (var issue in selectedIssues)
            {
                var effectiveType = issue.ProposedAction;
                var companionFiles = new List<string>();

                if (effectiveType == OperationType.MoveFile && File.Exists(issue.CurrentPath))
                {
                    var fileSet = FileSetResolver.Resolve(issue.CurrentPath);
                    if (fileSet.CompanionFiles.Count > 0)
                    {
                        effectiveType = OperationType.MoveFileSet;
                        companionFiles = fileSet.CompanionFiles;
                    }
                }

                var op = new StructureOperation
                {
                    IssueId = issue.Id,
                    Type = effectiveType,
                    SourcePath = issue.CurrentPath,
                    TargetPath = issue.ExpectedPath,
                    GameId = issue.GameId,
                    IssueType = issue.IssueType.ToString(),
                    CompanionFiles = companionFiles
                };

                // Conflict detection
                if (!string.IsNullOrEmpty(op.TargetPath) && op.SourcePath != op.TargetPath)
                {
                    bool targetExists = Directory.Exists(op.TargetPath) || File.Exists(op.TargetPath);
                    if (targetExists)
                    {
                        op.Conflict = defaultResolution;
                        if (defaultResolution == ConflictResolution.RenameSuffix)
                        {
                            op.TargetPath = FindAvailablePath(op.TargetPath);
                        }
                    }
                }

                plan.Operations.Add(op);
            }

            return plan;
        }

        // ── APPLY ───────────────────────────────────────────────────────

        public async Task<OperationPlan> ApplyAsync(
            List<string> issueIds,
            ConflictResolution defaultResolution,
            CancellationToken ct = default)
        {
            var plan = GeneratePreview(issueIds, defaultResolution);

            lock (_lock)
            {
                _activePlan = plan;
                _progress = new ResortProgress
                {
                    IsRunning = true,
                    Total = plan.TotalCount
                };
            }

            PersistPlan(plan);

            var bulkSettings = _configService.LoadMediaSettings();
            var bulkLibraryRoot = ResolveLibraryRoot(bulkSettings);

            foreach (var op in plan.Operations)
            {
                if (ct.IsCancellationRequested)
                {
                    op.Status = OperationStatus.Skipped;
                    op.ErrorMessage = "Cancelled by user.";
                    continue;
                }

                lock (_lock)
                {
                    _progress.CurrentOperation = $"{op.Type}: {Path.GetFileName(op.SourcePath)}";
                }

                try
                {
                    await ExecuteOperation(op, bulkLibraryRoot);
                }
                catch (Exception ex)
                {
                    op.Status = OperationStatus.Failed;
                    op.ErrorMessage = ex.Message;
                    _logger.Error($"[Resort] Operation failed: {op.Type} {op.SourcePath} -> {ex.Message}");
                }

                op.CompletedAt = DateTime.UtcNow;
                PersistPlan(plan);

                lock (_lock)
                {
                    _progress.Completed = plan.AppliedCount + plan.SkippedCount + plan.FailedCount;
                    _progress.Failed = plan.FailedCount;
                }
            }

            lock (_lock)
            {
                _progress.IsRunning = false;
                _progress.CurrentOperation = null;
            }

            ArchivePlan(plan);

            return plan;
        }

        // ── HISTORY ─────────────────────────────────────────────────────

        public List<OperationPlan> GetHistory()
        {
            var historyDir = Path.Combine(_planPersistDir, "history");
            if (!Directory.Exists(historyDir)) return new List<OperationPlan>();

            var plans = new List<OperationPlan>();
            foreach (var file in Directory.GetFiles(historyDir, "*.json").OrderByDescending(f => f))
            {
                try
                {
                    var json = File.ReadAllText(file);
                    var plan = JsonSerializer.Deserialize<OperationPlan>(json);
                    if (plan != null) plans.Add(plan);
                }
                catch { }
            }
            return plans;
        }

        // ── PRIVATE: Execution ──────────────────────────────────────────

        private async Task ExecuteOperation(StructureOperation op, string? cachedLibraryRoot = null)
        {
            if (op.Type == OperationType.MoveGameFolder
                || op.Type == OperationType.RenameGameFolder
                || op.Type == OperationType.MoveFile
                || op.Type == OperationType.MoveFileSet)
            {
                var libraryRoot = cachedLibraryRoot;
                if (string.IsNullOrEmpty(libraryRoot))
                {
                    var settings = _configService.LoadMediaSettings();
                    libraryRoot = ResolveLibraryRoot(settings);
                }
                if (string.IsNullOrEmpty(libraryRoot))
                {
                    op.Status = OperationStatus.Failed;
                    op.ErrorMessage = "Library root is not configured.";
                    return;
                }
                if (!IsPathWithinRoot(op.SourcePath, libraryRoot) || !IsPathWithinRoot(op.TargetPath, libraryRoot))
                {
                    op.Status = OperationStatus.Failed;
                    op.ErrorMessage = "Refused: operation would leave the library root.";
                    _logger.Warn($"[Resort] Refused {op.Type}: {op.SourcePath} -> {op.TargetPath} (escape from {libraryRoot})");
                    return;
                }
                if (IsSymlink(op.SourcePath))
                {
                    op.Status = OperationStatus.Failed;
                    op.ErrorMessage = "Refused: source is a symlink or reparse point.";
                    _logger.Warn($"[Resort] Refused {op.Type} on symlinked source: {op.SourcePath}");
                    return;
                }
            }

            // Idempotency: if source is gone and target exists, it's already done
            bool sourceExists = Directory.Exists(op.SourcePath) || File.Exists(op.SourcePath);
            bool targetExists = Directory.Exists(op.TargetPath) || File.Exists(op.TargetPath);

            if (!sourceExists && targetExists)
            {
                op.Status = OperationStatus.Skipped;
                op.ErrorMessage = "Already completed (source gone, target exists).";
                // Still update DB path if needed
                if (op.GameId.HasValue)
                {
                    await UpdateGamePath(op.GameId.Value, op.TargetPath);
                }
                return;
            }

            if (!sourceExists && !targetExists)
            {
                op.Status = OperationStatus.Failed;
                op.ErrorMessage = "Source path does not exist.";
                return;
            }

            // Handle conflict
            if (targetExists)
            {
                if (op.Conflict == ConflictResolution.Skip || op.Conflict == null)
                {
                    op.Status = OperationStatus.Skipped;
                    op.ErrorMessage = "Target already exists (conflict skipped).";
                    return;
                }
                if (op.Conflict == ConflictResolution.RenameSuffix)
                {
                    op.TargetPath = FindAvailablePath(op.TargetPath);
                }
                // Overwrite: proceed, target will be replaced
            }

            switch (op.Type)
            {
                case OperationType.MoveGameFolder:
                case OperationType.RenameGameFolder:
                    MoveDirectory(op.SourcePath, op.TargetPath);
                    if (op.GameId.HasValue)
                    {
                        await UpdateGamePath(op.GameId.Value, op.TargetPath);
                    }
                    op.Status = OperationStatus.Applied;
                    break;

                case OperationType.MoveFile:
                    var targetDir = Path.GetDirectoryName(op.TargetPath);
                    if (targetDir != null) Directory.CreateDirectory(targetDir);
                    File.Move(op.SourcePath, op.TargetPath);
                    op.Status = OperationStatus.Applied;
                    break;

                case OperationType.MoveFileSet:
                    var setTargetDir = Path.GetDirectoryName(op.TargetPath);
                    if (setTargetDir != null) Directory.CreateDirectory(setTargetDir);
                    File.Move(op.SourcePath, op.TargetPath);
                    foreach (var companion in op.CompanionFiles)
                    {
                        if (File.Exists(companion))
                        {
                            var companionName = Path.GetFileName(companion);
                            var companionTarget = Path.Combine(setTargetDir ?? string.Empty, companionName);
                            File.Move(companion, companionTarget);
                        }
                    }
                    if (op.GameId.HasValue)
                    {
                        await UpdateGamePath(op.GameId.Value, op.TargetPath);
                    }
                    op.Status = OperationStatus.Applied;
                    break;

                case OperationType.UpdateDbPath:
                    if (op.GameId.HasValue)
                    {
                        await UpdateGamePath(op.GameId.Value, op.TargetPath);
                    }
                    op.Status = OperationStatus.Applied;
                    break;

                case OperationType.DeleteEmptyDir:
                    if (Directory.Exists(op.SourcePath) && !Directory.EnumerateFileSystemEntries(op.SourcePath).Any())
                    {
                        Directory.Delete(op.SourcePath);
                    }
                    op.Status = OperationStatus.Applied;
                    break;

                default:
                    op.Status = OperationStatus.Skipped;
                    op.ErrorMessage = $"Unsupported operation type: {op.Type}";
                    break;
            }
        }

        private void MoveDirectory(string source, string target)
        {
            var targetParent = Path.GetDirectoryName(target);
            if (targetParent != null) Directory.CreateDirectory(targetParent);

            // Same volume: instant rename
            try
            {
                Directory.Move(source, target);
                _logger.Info($"[Resort] Moved directory: {source} -> {target}");
                return;
            }
            catch (IOException)
            {
                // Cross-volume: copy + delete
                _logger.Info($"[Resort] Cross-volume move (copy+delete): {source} -> {target}");
            }

            // Fallback: recursive copy then delete
            CopyDirectoryRecursive(source, target);
            Directory.Delete(source, recursive: true);
            _logger.Info($"[Resort] Cross-volume move complete: {source} -> {target}");
        }

        private static void CopyDirectoryRecursive(string source, string target)
        {
            Directory.CreateDirectory(target);
            foreach (var file in Directory.GetFiles(source))
            {
                File.Copy(file, Path.Combine(target, Path.GetFileName(file)), overwrite: true);
            }
            foreach (var dir in Directory.GetDirectories(source))
            {
                CopyDirectoryRecursive(dir, Path.Combine(target, Path.GetFileName(dir)));
            }
        }

        private async Task UpdateGamePath(int gameId, string newPath)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return;

            var oldPath = game.Path;
            game.Path = newPath;

            // Update relative paths in GameFiles
            if (!string.IsNullOrEmpty(oldPath))
            {
                foreach (var gf in game.GameFiles)
                {
                    if (!string.IsNullOrEmpty(gf.RelativePath))
                    {
                        // RelativePath is relative to game.Path — stays the same if only the root moved
                        // But verify it still resolves
                        var fullPath = Path.Combine(newPath, gf.RelativePath);
                        if (!File.Exists(fullPath))
                        {
                            _logger.Warn($"[Resort] Warning: GameFile '{gf.RelativePath}' not found at new path for game {gameId}.");
                        }
                    }
                }
            }

            await _gameRepository.UpdateAsync(game.Id, game);
            _logger.Info($"[Resort] Updated DB path for game {gameId}: {oldPath} -> {newPath}");
        }

        // ── PRIVATE: Path Helpers ───────────────────────────────────────

        private static string ComputeExpectedGamePath(
            MediaSettings settings, string libraryRoot, Platform platform, Game game)
        {
            var effectiveFolder = platform.GetEffectiveFolderName(settings.FolderNamingMode);
            return settings.ResolveDestinationPath(libraryRoot, effectiveFolder, game.Title, game.Year > 0 ? game.Year : (int?)null);
        }

        private static string ResolveLibraryRoot(MediaSettings settings)
        {
            if (!string.IsNullOrEmpty(settings.DestinationPath) && Directory.Exists(settings.DestinationPath))
                return settings.DestinationPath;
            return settings.FolderPath;
        }

        private static string NormalizePath(string path)
        {
            return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        }

        private static string ExtractPlatformFolder(string gamePath, string libraryRoot)
        {
            if (string.IsNullOrEmpty(gamePath) || string.IsNullOrEmpty(libraryRoot))
                return string.Empty;

            string fullLib, fullGame;
            try
            {
                fullLib = Path.GetFullPath(libraryRoot).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                fullGame = Path.GetFullPath(gamePath);
            }
            catch
            {
                return string.Empty;
            }

            if (!fullGame.StartsWith(fullLib, PathComparison))
                return string.Empty;

            var relative = fullGame.Substring(fullLib.Length);
            var sepIndex = relative.IndexOf(Path.DirectorySeparatorChar);
            return sepIndex >= 0 ? relative.Substring(0, sepIndex) : relative;
        }

        private static bool IsPathWithinRoot(string candidate, string root)
        {
            if (string.IsNullOrEmpty(candidate) || string.IsNullOrEmpty(root))
                return false;
            try
            {
                var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                var fullPath = Path.GetFullPath(candidate);
                return fullPath.StartsWith(fullRoot, PathComparison) || fullPath.Equals(fullRoot.TrimEnd(Path.DirectorySeparatorChar), PathComparison);
            }
            catch
            {
                return false;
            }
        }

        private static bool IsSymlink(string path)
        {
            try
            {
                if (Directory.Exists(path))
                {
                    var di = new DirectoryInfo(path);
                    return (di.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                }
                if (File.Exists(path))
                {
                    var fi = new FileInfo(path);
                    return (fi.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
                }
            }
            catch { }
            return false;
        }

        private static IEnumerable<string> GetAllFolderNames(Platform platform)
        {
            yield return platform.FolderName;
            if (!string.IsNullOrEmpty(platform.Slug) && !platform.Slug.Equals(platform.FolderName, StringComparison.OrdinalIgnoreCase))
                yield return platform.Slug;
            if (!string.IsNullOrEmpty(platform.RetroBatFolderName))
                yield return platform.RetroBatFolderName;
            if (!string.IsNullOrEmpty(platform.BatoceraFolderName))
                yield return platform.BatoceraFolderName;
        }

        private void CheckOrphansInDir(
            string platformDir,
            Platform platform,
            HashSet<string> gamePaths,
            List<StructureIssue> issues,
            ResortScanRequest? request)
        {
            if (request?.PlatformId.HasValue == true && request.PlatformId.Value != platform.Id)
                return;

            try
            {
                foreach (var entry in Directory.GetDirectories(platformDir))
                {
                    var normalized = NormalizePath(entry);
                    if (!gamePaths.Contains(normalized))
                    {
                        issues.Add(new StructureIssue
                        {
                            GameId = null,
                            GameTitle = Path.GetFileName(entry),
                            PlatformId = platform.Id,
                            PlatformName = platform.Name,
                            IssueType = IssueType.OrphanedFile,
                            RuleFailed = "",
                            Description = $"Folder exists in '{platform.Name}' library but is not linked to any game.",
                            CurrentPath = entry,
                            ExpectedPath = string.Empty,
                            CurrentFolder = Path.GetFileName(platformDir),
                            ProposedAction = OperationType.LinkOrphan
                        });
                    }
                }

                foreach (var file in Directory.GetFiles(platformDir))
                {
                    var normalized = NormalizePath(file);
                    if (!gamePaths.Contains(normalized))
                    {
                        issues.Add(new StructureIssue
                        {
                            GameId = null,
                            GameTitle = Path.GetFileNameWithoutExtension(file),
                            CurrentFolder = Path.GetFileName(platformDir),
                            PlatformId = platform.Id,
                            PlatformName = platform.Name,
                            IssueType = IssueType.OrphanedFile,
                            RuleFailed = "",
                            Description = $"File exists in '{platform.Name}' library but is not linked to any game.",
                            CurrentPath = file,
                            ExpectedPath = string.Empty,
                            ProposedAction = OperationType.LinkOrphan
                        });
                    }
                }
            }
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }

        private static string FindAvailablePath(string path)
        {
            if (!Directory.Exists(path) && !File.Exists(path))
                return path;

            var dir = Path.GetDirectoryName(path) ?? string.Empty;
            var name = Path.GetFileNameWithoutExtension(path);
            var ext = Path.GetExtension(path);
            bool isDir = Directory.Exists(path);

            for (int i = 2; i < 100; i++)
            {
                var candidate = isDir
                    ? Path.Combine(dir, $"{name} ({i})")
                    : Path.Combine(dir, $"{name} ({i}){ext}");
                if (!Directory.Exists(candidate) && !File.Exists(candidate))
                    return candidate;
            }

            return Path.Combine(dir, $"{name} ({Guid.NewGuid().ToString().Substring(0, 8)}){ext}");
        }

        // ── PRIVATE: Plan Persistence ───────────────────────────────────

        private void PersistPlan(OperationPlan plan)
        {
            try
            {
                var path = Path.Combine(_planPersistDir, "active_plan.json");
                var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(path, json);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Resort] Warning: Could not persist plan: {ex.Message}");
            }
        }

        private void ArchivePlan(OperationPlan plan)
        {
            try
            {
                var historyDir = Path.Combine(_planPersistDir, "history");
                Directory.CreateDirectory(historyDir);
                var archivePath = Path.Combine(historyDir, $"plan_{plan.CreatedAt:yyyyMMdd_HHmmss}.json");
                var json = JsonSerializer.Serialize(plan, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(archivePath, json);

                // Remove active plan
                var activePath = Path.Combine(_planPersistDir, "active_plan.json");
                if (File.Exists(activePath)) File.Delete(activePath);
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Resort] Warning: Could not archive plan: {ex.Message}");
            }
        }

        private void TryLoadPendingPlan()
        {
            try
            {
                var path = Path.Combine(_planPersistDir, "active_plan.json");
                if (!File.Exists(path)) return;

                var json = File.ReadAllText(path);
                var plan = JsonSerializer.Deserialize<OperationPlan>(json);
                if (plan != null && !plan.IsComplete)
                {
                    lock (_lock)
                    {
                        _activePlan = plan;
                    }
                    _logger.Info($"[Resort] Loaded pending plan with {plan.PendingCount} remaining operations.");
                }
                else
                {
                    File.Delete(path);
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Resort] Warning: Could not load pending plan: {ex.Message}");
            }
        }

        // ── SUPPLEMENTARY FILE RENAME ────────────────────────────────────

        public async Task<List<SupplementaryRenameOp>> PreviewSupplementaryRenameAsync(int gameId)
        {
            var game = await _gameRepository.GetByIdAsync(gameId);
            if (game == null) return new();

            var settings = _configService.LoadMediaSettings();
            var libraryRoot = ResolveLibraryRoot(settings);
            if (string.IsNullOrEmpty(libraryRoot)) return new();

            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Id == game.PlatformId);
            if (platform == null) return new();

            var platformFolder = platform.GetEffectiveFolderName(settings.FolderNamingMode);
            var platformPath = Path.Combine(libraryRoot, platformFolder);

            var ops = new List<SupplementaryRenameOp>();

            foreach (var gf in game.GameFiles ?? new())
            {
                if (gf.FileType == "Main") continue;
                if (string.IsNullOrEmpty(gf.RelativePath)) continue;

                var currentFileName = Path.GetFileName(gf.RelativePath);
                var ext = Path.GetExtension(currentFileName);
                var dir = Path.GetDirectoryName(gf.RelativePath) ?? "";

                string expectedFileName;
                if (gf.FileType == "Patch")
                {
                    var ver = gf.Version?.TrimStart('v', 'V');
                    expectedFileName = BuildSupplementaryFileName(game.Title, "Patch", ver, null, ext);
                }
                else
                {
                    expectedFileName = BuildSupplementaryFileName(game.Title, "DLC", null, gf.ContentName, ext);
                }

                if (currentFileName.Equals(expectedFileName, PathComparison))
                    continue;

                var currentFullPath = Path.Combine(platformPath, gf.RelativePath.Replace('/', Path.DirectorySeparatorChar));
                var newRelativePath = string.IsNullOrEmpty(dir) ? expectedFileName : $"{dir}/{expectedFileName}";
                var newFullPath = Path.Combine(platformPath, newRelativePath.Replace('/', Path.DirectorySeparatorChar));

                ops.Add(new SupplementaryRenameOp
                {
                    GameFileId = gf.Id,
                    FileType = gf.FileType,
                    Version = gf.Version,
                    ContentName = gf.ContentName,
                    CurrentPath = currentFullPath,
                    CurrentFileName = currentFileName,
                    NewFileName = expectedFileName,
                    NewPath = newFullPath,
                    NewRelativePath = newRelativePath,
                    Conflict = File.Exists(newFullPath) && !currentFullPath.Equals(newFullPath, PathComparison)
                });
            }

            return ops;
        }

        public async Task<SupplementaryRenameResult> ApplySupplementaryRenameAsync(int gameId)
        {
            var ops = await PreviewSupplementaryRenameAsync(gameId);
            var result = new SupplementaryRenameResult();

            foreach (var op in ops)
            {
                if (op.Conflict)
                {
                    op.Status = "Skipped";
                    op.Error = "Target file already exists.";
                    result.Skipped++;
                    continue;
                }

                try
                {
                    if (File.Exists(op.CurrentPath))
                    {
                        var targetDir = Path.GetDirectoryName(op.NewPath);
                        if (targetDir != null) Directory.CreateDirectory(targetDir);
                        File.Move(op.CurrentPath, op.NewPath);
                    }
                    else
                    {
                        op.Status = "Skipped";
                        op.Error = "Source file not found on disk.";
                        result.Skipped++;
                        continue;
                    }

                    await _gameRepository.UpdateGameFilePathAsync(op.GameFileId, op.NewRelativePath);
                    op.Status = "Applied";
                    result.Applied++;
                }
                catch (Exception ex)
                {
                    op.Status = "Failed";
                    op.Error = ex.Message;
                    result.Failed++;
                }
            }

            result.Operations = ops;
            return result;
        }

        internal static string BuildSupplementaryFileName(string gameTitle, string type, string? version, string? contentName, string extension)
        {
            var invalidChars = Path.GetInvalidFileNameChars();
            string raw;

            if (type == "Patch")
            {
                var versionPart = !string.IsNullOrEmpty(version) ? $"-v{version}" : "";
                raw = $"{gameTitle}-Patch{versionPart}";
            }
            else
            {
                // For DLC: strip the game title from contentName if it's a prefix
                var name = contentName;
                if (!string.IsNullOrEmpty(name) && name.StartsWith(gameTitle, StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(gameTitle.Length).TrimStart(' ', '-', '_');
                }
                var namePart = !string.IsNullOrEmpty(name) ? $"-{name}" : "";
                raw = $"{gameTitle}-DLC{namePart}";
            }

            var sanitized = string.Join("_", raw.Split(invalidChars, StringSplitOptions.RemoveEmptyEntries)).Trim();
            return sanitized + extension;
        }

        // ── Platform comparison ─────────────────────────────────────────

        private static readonly StringComparison PathComparison =
            Environment.OSVersion.Platform == PlatformID.Win32NT
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;

        private static readonly StringComparer PathComparer =
            Environment.OSVersion.Platform == PlatformID.Win32NT
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
    }
}
