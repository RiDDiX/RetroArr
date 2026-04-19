using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using RetroArr.Core.Configuration;

namespace RetroArr.Core.Games
{
    public class InstallerScannerService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMedia);
        private readonly ConfigurationService _configService;
        private readonly IGameRepository _gameRepository;

        private static readonly string[] InstallerExtensions = { ".exe", ".bin", ".sh", ".dmg", ".pkg" };
        private static readonly string[] SwitchUpdateExtensions = { ".nsp", ".xci", ".nsz" };

        public InstallerScannerService(ConfigurationService configService, IGameRepository gameRepository)
        {
            _configService = configService;
            _gameRepository = gameRepository;
        }

        // walks /gog/downloads, /gog/dlc, /gog/patches, /gog/extras
        public async Task<List<InstallerMatch>> ScanGogInstallersAsync()
        {
            var matches = new List<InstallerMatch>();
            var mediaSettings = _configService.LoadMediaSettings();
            var gogBasePath = mediaSettings.ResolveGogDownloadPath();

            if (string.IsNullOrEmpty(gogBasePath))
            {
                _logger.Info("[InstallerScanner] GOG path not configured");
                return matches;
            }

            // Get base GOG folder (parent of downloads)
            var gogRootPath = Directory.GetParent(gogBasePath)?.FullName;
            if (string.IsNullOrEmpty(gogRootPath) || !Directory.Exists(gogRootPath))
            {
                gogRootPath = gogBasePath;
            }

            var allGames = await _gameRepository.GetAllAsync();
            var gogGames = allGames.Where(g => !string.IsNullOrEmpty(g.GogId) || g.PlatformId == 126).ToList();

            // Scan GOG folders: downloads, dlc, patches, extras
            var gogFolders = new Dictionary<string, string>
            {
                { "downloads", "Installer" },
                { "dlc", "DLC" },
                { "dlcs", "DLC" },
                { "patches", "Patch" },
                { "extras", "Extra" }
            };

            foreach (var (folderName, fileType) in gogFolders)
            {
                var folderPath = Path.Combine(gogRootPath, folderName);
                if (!Directory.Exists(folderPath)) continue;

                _logger.Info($"[InstallerScanner] Scanning GOG {folderName}: {folderPath}");

                foreach (var gameFolder in Directory.GetDirectories(folderPath))
                {
                    var gameFolderName = Path.GetFileName(gameFolder);
                    var installerFiles = Directory.GetFiles(gameFolder, "*.*", SearchOption.AllDirectories)
                        .Where(f => InstallerExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    if (installerFiles.Count == 0) continue;

                    // Only consider GOG games for this folder. Falling back to the whole
                    // library would attach a PC installer to a same-titled PS1 entry.
                    var matchedGame = FindBestGameMatch(gameFolderName, gogGames);

                    var match = new InstallerMatch
                    {
                        FolderPath = gameFolder,
                        FolderName = gameFolderName,
                        FileType = fileType,
                        InstallerFiles = installerFiles.Select(f => new InstallerFileInfo
                        {
                            FilePath = f,
                            FileName = Path.GetFileName(f),
                            Size = new FileInfo(f).Length,
                            Type = fileType
                        }).ToList(),
                        MatchedGame = matchedGame,
                        MatchConfidence = matchedGame != null ? CalculateSimilarity(gameFolderName, matchedGame.Title) : 0
                    };

                    matches.Add(match);

                    if (matchedGame != null)
                    {
                        _logger.Info($"[InstallerScanner] Found {fileType} for '{matchedGame.Title}': {installerFiles.Count} files");
                    }
                }
            }

            _logger.Info($"[InstallerScanner] Found {matches.Count} GOG files (installers/dlc/patches/extras)");
            return matches;
        }

        // walks /switch/updates, /switch/dlc, /switch/patches
        public async Task<List<UpdateMatch>> ScanSwitchUpdatesAsync()
        {
            var matches = new List<UpdateMatch>();
            var mediaSettings = _configService.LoadMediaSettings();
            
            var basePath = !string.IsNullOrEmpty(mediaSettings.FolderPath) && Directory.Exists(mediaSettings.FolderPath)
                ? mediaSettings.FolderPath
                : !string.IsNullOrEmpty(mediaSettings.DestinationPath) && Directory.Exists(mediaSettings.DestinationPath)
                    ? mediaSettings.DestinationPath
                    : null;

            if (string.IsNullOrEmpty(basePath))
                return matches;

            var allGames = await _gameRepository.GetAllAsync();
            var switchGames = allGames.Where(g => g.Platform?.FolderName == "switch" || g.Platform?.FolderName == "switch2").ToList();

            // Scan all Switch extra folders (switch and switch2)
            var switchFolders = new[] { "switch", "switch2" };
            var extraFolders = new Dictionary<string, UpdateFileType>
            {
                { "updates", UpdateFileType.Update },
                { "dlc", UpdateFileType.DLC },
                { "dlcs", UpdateFileType.DLC },
                { "patches", UpdateFileType.Patch }
            };

            foreach (var switchFolder in switchFolders)
            {
                var switchBasePath = Path.Combine(basePath, switchFolder);
                if (!Directory.Exists(switchBasePath)) continue;

                foreach (var (extraFolderName, defaultType) in extraFolders)
                {
                    var extraPath = Path.Combine(switchBasePath, extraFolderName);
                    if (!Directory.Exists(extraPath)) continue;

                    _logger.Info($"[InstallerScanner] Scanning {switchFolder}/{extraFolderName}: {extraPath}");

                    var files = Directory.GetFiles(extraPath, "*.*", SearchOption.AllDirectories)
                        .Where(f => SwitchUpdateExtensions.Any(ext => f.EndsWith(ext, StringComparison.OrdinalIgnoreCase)))
                        .ToList();

                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileName(file);
                        var parsedInfo = ParseSwitchUpdateFileName(fileName);

                        var matchedGame = parsedInfo.TitleId != null
                            ? switchGames.FirstOrDefault(g => g.Path?.Contains(parsedInfo.TitleId, StringComparison.OrdinalIgnoreCase) == true)
                            : null;

                        matchedGame ??= FindBestGameMatch(parsedInfo.CleanName, switchGames);

                        // Determine type from filename or use folder default
                        var fileType = DetermineUpdateType(fileName);
                        if (fileType == UpdateFileType.Update) fileType = defaultType;

                        var match = new UpdateMatch
                        {
                            FilePath = file,
                            FileName = fileName,
                            Size = new FileInfo(file).Length,
                            Version = parsedInfo.Version,
                            TitleId = parsedInfo.TitleId,
                            MatchedGame = matchedGame,
                            MatchConfidence = matchedGame != null ? CalculateSimilarity(parsedInfo.CleanName, matchedGame.Title) : 0,
                            Type = fileType
                        };

                        matches.Add(match);

                        if (matchedGame != null)
                        {
                            _logger.Info($"[InstallerScanner] Found {fileType} for '{matchedGame.Title}': {fileName}");
                        }
                    }
                }
            }

            _logger.Info($"[InstallerScanner] Found {matches.Count} Switch extras (updates/dlc/patches)");
            return matches;
        }

        public async Task EnrichGamesWithExtrasAsync(List<Game> games)
        {
            var installerMatches = await ScanGogInstallersAsync();
            var updateMatches = await ScanSwitchUpdatesAsync();

            foreach (var game in games)
            {
                // Check for GOG installers
                var installerMatch = installerMatches.FirstOrDefault(m => m.MatchedGame?.Id == game.Id);
                if (installerMatch != null)
                {
                    game.InstallerPath = installerMatch.InstallerFiles.FirstOrDefault()?.FilePath;
                    game.InstallerStatus = installerMatch.InstallerFiles.Count > 1 
                        ? InstallerStatus.Multiple 
                        : InstallerStatus.Found;
                }

                // Check for Switch updates
                var gameUpdates = updateMatches.Where(m => m.MatchedGame?.Id == game.Id).ToList();
                if (gameUpdates.Any())
                {
                    game.UpdateFiles = gameUpdates.Select(u => new GameUpdateFile
                    {
                        FileName = u.FileName,
                        FilePath = u.FilePath,
                        Version = u.Version,
                        Size = u.Size,
                        Type = u.Type
                    }).ToList();
                }
            }
        }

        private static Game? FindBestGameMatch(string searchName, List<Game> games)
        {
            if (string.IsNullOrEmpty(searchName) || games.Count == 0)
                return null;

            var cleanSearch = CleanGameName(searchName);
            Game? bestMatch = null;
            double bestScore = 0;

            foreach (var game in games)
            {
                var score = CalculateSimilarity(cleanSearch, CleanGameName(game.Title));
                if (score > bestScore && score >= 0.6) // 60% minimum threshold
                {
                    bestScore = score;
                    bestMatch = game;
                }
            }

            return bestMatch;
        }

        private static string CleanGameName(string name)
        {
            // Remove common suffixes and prefixes
            name = Regex.Replace(name, @"(?i)(setup|install|gog|update|dlc|v?\d+\.\d+.*)", " ");
            name = Regex.Replace(name, @"[_\-\.\[\]\(\)]", " ");
            name = Regex.Replace(name, @"\s+", " ").Trim().ToLowerInvariant();
            return name;
        }

        private static (string CleanName, string? Version, string? TitleId) ParseSwitchUpdateFileName(string fileName)
        {
            var name = Path.GetFileNameWithoutExtension(fileName);
            string? version = null;
            string? titleId = null;

            // Try to extract version (v1.2.3 or [v1.2.3])
            var versionMatch = Regex.Match(name, @"[vV]?(\d+\.\d+(?:\.\d+)?)", RegexOptions.IgnoreCase);
            if (versionMatch.Success)
            {
                version = versionMatch.Groups[1].Value;
            }

            // Try to extract Title ID (16 hex characters)
            var titleIdMatch = Regex.Match(name, @"\[?([0-9A-Fa-f]{16})\]?");
            if (titleIdMatch.Success)
            {
                titleId = titleIdMatch.Groups[1].Value;
            }

            // Clean the name
            var cleanName = Regex.Replace(name, @"\[.*?\]|\(.*?\)|[vV]?\d+\.\d+.*", " ");
            cleanName = Regex.Replace(cleanName, @"[_\-\.]", " ");
            cleanName = Regex.Replace(cleanName, @"\s+", " ").Trim();

            return (cleanName, version, titleId);
        }

        private static UpdateFileType DetermineUpdateType(string fileName)
        {
            var lower = fileName.ToLowerInvariant();
            if (lower.Contains("dlc")) return UpdateFileType.DLC;
            if (lower.Contains("patch")) return UpdateFileType.Patch;
            return UpdateFileType.Update;
        }

        private static double CalculateSimilarity(string source, string target)
        {
            if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(target))
                return 0;

            source = source.ToLowerInvariant();
            target = target.ToLowerInvariant();

            if (source == target) return 1.0;

            int sourceLength = source.Length;
            int targetLength = target.Length;

            var distance = new int[sourceLength + 1, targetLength + 1];

            for (int i = 0; i <= sourceLength; i++)
                distance[i, 0] = i;

            for (int j = 0; j <= targetLength; j++)
                distance[0, j] = j;

            for (int i = 1; i <= sourceLength; i++)
            {
                for (int j = 1; j <= targetLength; j++)
                {
                    int cost = (target[j - 1] == source[i - 1]) ? 0 : 1;
                    distance[i, j] = Math.Min(
                        Math.Min(distance[i - 1, j] + 1, distance[i, j - 1] + 1),
                        distance[i - 1, j - 1] + cost);
                }
            }

            int levenshteinDistance = distance[sourceLength, targetLength];
            int maxLength = Math.Max(sourceLength, targetLength);

            return 1.0 - (double)levenshteinDistance / maxLength;
        }
    }

    public class InstallerMatch
    {
        public string FolderPath { get; set; } = string.Empty;
        public string FolderName { get; set; } = string.Empty;
        public string FileType { get; set; } = "Installer"; // Installer, DLC, Patch, Extra
        public List<InstallerFileInfo> InstallerFiles { get; set; } = new();
        public Game? MatchedGame { get; set; }
        public double MatchConfidence { get; set; }
    }

    public class InstallerFileInfo
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public string Type { get; set; } = "Installer"; // Installer, DLC, Patch, Extra
    }

    public class UpdateMatch
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public long Size { get; set; }
        public string? Version { get; set; }
        public string? TitleId { get; set; }
        public Game? MatchedGame { get; set; }
        public double MatchConfidence { get; set; }
        public UpdateFileType Type { get; set; }
    }
}
