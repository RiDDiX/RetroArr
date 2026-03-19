using RetroArr.Core.MetadataSource.Steam;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.ScreenScraper;
using RetroArr.Core.Games;
using System.Text.RegularExpressions;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.IO;
using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http;

namespace RetroArr.Core.MetadataSource
{
    /// <summary>
    /// Service for fetching game metadata from IGDB and other providers
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    public class GameMetadataService
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly IgdbClient _igdbClient;
        private readonly SteamClient _steamClient;
        private readonly ScreenScraperClient? _screenScraperClient;

        public GameMetadataService(IgdbClient igdbClient, SteamClient steamClient, ScreenScraperClient? screenScraperClient = null)
        {
            _igdbClient = igdbClient;
            _steamClient = steamClient;
            _screenScraperClient = screenScraperClient;
        }

        public async Task<List<Game>> SearchGamesAsync(string query, string? platformKey = null, string? lang = null, string? serial = null)
        {
            var results = new List<Game>();

            // Try IGDB first (may fail if not configured)
            try
            {
                int? igdbPlatformId = ResolveIgdbPlatformId(platformKey);
                var igdbGames = await _igdbClient.SearchGamesAsync(query, igdbPlatformId, lang, serial);
                foreach (var g in igdbGames)
                {
                    results.Add(await MapIgdbGameToGameAsync(g, lang));
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Metadata] IGDB search failed: {ex.Message}");
            }

            // Fallback to ScreenScraper when IGDB returns no results
            if (results.Count == 0 && _screenScraperClient != null)
            {
                int? systemId = null;
                int platformId = 0;
                if (!string.IsNullOrEmpty(platformKey))
                {
                    var platform = FindPlatform(platformKey);
                    if (platform != null)
                    {
                        systemId = platform.ScreenScraperSystemId;
                        platformId = platform.Id;
                    }
                }

                _logger.Info($"[Metadata] IGDB returned no results, trying ScreenScraper (systemId: {systemId})...");
                var ssGames = await _screenScraperClient.SearchGamesByNameAsync(query, systemId);
                foreach (var ssGame in ssGames)
                {
                    var game = MapScreenScraperGameToGame(ssGame, platformId, lang);
                    if (game != null)
                    {
                        game.MetadataSource = "ScreenScraper";
                        results.Add(game);
                    }
                }
            }

            return results;
        }

        /// <summary>
        /// Multi-variant search: tries each query variant in order with platform filter,
        /// then retries without platform filter if all variants return empty.
        /// Returns raw IgdbGame results for scoring by caller.
        /// </summary>
        public async Task<List<IgdbGame>> SearchWithVariantsAsync(List<string> queryVariants, string? platformKey = null, string? lang = null, string? serial = null)
        {
            int? igdbPlatformId = ResolveIgdbPlatformId(platformKey);

            // Phase 1: Try each variant WITH platform filter
            foreach (var variant in queryVariants)
            {
                try
                {
                    var results = await _igdbClient.SearchGamesAsync(variant, igdbPlatformId, lang, serial);
                    if (results.Count > 0)
                    {
                        _logger.Info($"[Metadata] Variant '{variant}' (platform={igdbPlatformId}) returned {results.Count} results.");
                        return results;
                    }
                }
                catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    await Task.Delay(500);
                }
            }

            // Phase 2: Retry without platform filter (broader search)
            if (igdbPlatformId.HasValue)
            {
                foreach (var variant in queryVariants)
                {
                    try
                    {
                        var results = await _igdbClient.SearchGamesAsync(variant, null, lang, serial);
                        if (results.Count > 0)
                        {
                            _logger.Info($"[Metadata] Variant '{variant}' (no platform filter) returned {results.Count} results.");
                            return results;
                        }
                    }
                    catch (HttpRequestException ex) when (ex.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                    {
                        await Task.Delay(500);
                    }
                }
            }

            return new List<IgdbGame>();
        }

        /// <summary>
        /// Score an IGDB game result against the search query variants and expected platform.
        /// Returns 0.0–1.0 confidence.
        /// </summary>
        public double ScoreCandidate(IgdbGame candidate, List<string> queryVariants, int? expectedIgdbPlatformId)
        {
            // Title similarity: best match across query variants and (name + alt names)
            double bestTitleScore = 0.0;
            var candidateNames = new List<string> { candidate.Name };
            foreach (var alt in candidate.AlternativeNames)
            {
                if (!string.IsNullOrWhiteSpace(alt.Name))
                    candidateNames.Add(alt.Name);
            }

            foreach (var variant in queryVariants)
            {
                foreach (var cName in candidateNames)
                {
                    var sim = TitleCleanerService.ComputeSimilarity(variant, cName);
                    if (sim > bestTitleScore) bestTitleScore = sim;
                }
            }

            // Platform match
            double platformScore = 0.5; // default: unknown
            if (expectedIgdbPlatformId.HasValue && candidate.Platforms.Count > 0)
            {
                bool exactMatch = candidate.Platforms.Any(p =>
                {
                    var def = PlatformDefinitions.AllPlatforms.FirstOrDefault(
                        pd => pd.Name == p.Name || pd.Slug == p.Abbreviation?.ToLower());
                    return def?.IgdbPlatformId == expectedIgdbPlatformId.Value;
                });
                platformScore = exactMatch ? 1.0 : 0.0;
            }

            // Has cover art
            double coverScore = (candidate.Cover != null && !string.IsNullOrEmpty(candidate.Cover.ImageId)) ? 1.0 : 0.0;

            // Has release year
            double yearScore = candidate.FirstReleaseDate.HasValue ? 1.0 : 0.0;

            // Weighted sum: title 40%, platform 30%, cover 15%, year 15%
            return (bestTitleScore * 0.40) + (platformScore * 0.30) + (coverScore * 0.15) + (yearScore * 0.15);
        }

        public async Task<Game> MapIgdbToGameAsync(IgdbGame igdbGame, string? lang = null, string? platformKey = null)
        {
            return await MapIgdbGameToGameAsync(igdbGame, lang, platformKey);
        }

        private int? ResolveIgdbPlatformId(string? platformKey)
        {
            if (string.IsNullOrEmpty(platformKey) || platformKey.ToLower() == "retro_emulation" || platformKey.ToLower() == "default")
                return null;

            var platform = FindPlatform(platformKey);
            if (platform?.IgdbPlatformId.HasValue == true)
                return platform.IgdbPlatformId;

            return platformKey.ToLower() switch
            {
                "nintendo_switch" => 130,
                "pc_windows" => 6,
                _ => null
            };
        }

        private static Platform? FindPlatform(string? platformKey)
        {
            if (string.IsNullOrEmpty(platformKey)) return null;
            return PlatformDefinitions.AllPlatforms
                .FirstOrDefault(p => p.MatchesFolderName(platformKey));
        }

        /// <summary>
        /// Search exclusively in ScreenScraper
        /// </summary>
        public async Task<List<Game>> SearchScreenScraperAsync(string query, string? platformKey = null, string? lang = null)
        {
            var results = new List<Game>();
            
            if (_screenScraperClient == null)
            {
                _logger.Info("[Metadata] ScreenScraper not configured");
                return results;
            }

            int? systemId = null;
            int platformId = 0;
            
            if (!string.IsNullOrEmpty(platformKey))
            {
                var platform = PlatformDefinitions.AllPlatforms
                    .FirstOrDefault(p => p.MatchesFolderName(platformKey));
                
                if (platform != null)
                {
                    systemId = platform.ScreenScraperSystemId;
                    platformId = platform.Id;
                }
            }

            _logger.Info($"[Metadata] Searching ScreenScraper for: {query} (systemId: {systemId})");
            var ssGames = await _screenScraperClient.SearchGamesByNameAsync(query, systemId);
            
            // jeuRecherche only returns minimal data (name + cover).
            // Fetch full details via jeuInfos for each result to get synopsis, developer, etc.
            var enrichedGames = new List<ScreenScraper.ScreenScraperGame>();
            foreach (var ssGame in ssGames.Take(5))
            {
                if (!string.IsNullOrEmpty(ssGame.Id))
                {
                    _logger.Info($"[Metadata] Fetching full details for ScreenScraper game id={ssGame.Id}");
                    var fullGame = await _screenScraperClient.GetGameByIdAsync(ssGame.Id);
                    if (fullGame != null)
                    {
                        enrichedGames.Add(fullGame);
                        continue;
                    }
                }
                enrichedGames.Add(ssGame);
            }

            foreach (var ssGame in enrichedGames)
            {
                _logger.Info($"[Metadata] Mapping ScreenScraper game: nom='{ssGame.Nom}', noms={ssGame.Names?.Count ?? 0}, id={ssGame.Id}, synopsis={ssGame.Synopsis?.Count ?? 0}");
                var game = MapScreenScraperGameToGame(ssGame, platformId, lang);
                if (game != null)
                {
                    game.MetadataSource = "ScreenScraper";
                    results.Add(game);
                }
                else
                {
                    _logger.Info($"[Metadata] ScreenScraper mapping returned null for id={ssGame.Id}");
                }
            }

            _logger.Info($"[Metadata] ScreenScraper search returning {results.Count} mapped game(s)");
            return results;
        }

        public async Task<Game?> GetGameMetadataAsync(int igdbId, string? lang = null, string? platformKey = null)
        {
            var results = await GetGamesMetadataAsync(new[] { igdbId }, lang, platformKey);
            return results.FirstOrDefault();
        }

        public async Task<List<Game>> GetGamesMetadataAsync(IEnumerable<int> igdbIds, string? lang = null, string? platformKey = null)
        {
            var igdbGames = await _igdbClient.GetGamesByIdsAsync(igdbIds, lang);
            // Process sequentially to avoid internal rate limit issues if Steam is called
            var results = new List<Game>();
            foreach (var igdbGame in igdbGames)
            {
                results.Add(await MapIgdbGameToGameAsync(igdbGame, lang, platformKey));
            }
            return results;
        }

        private async Task<Game> MapIgdbGameToGameAsync(IgdbGame igdbGame, string? lang = null, string? platformKey = null)
        {
            var title = igdbGame.Name;
            var summary = igdbGame.Summary;
            var storyline = igdbGame.Storyline;

            if (!string.IsNullOrEmpty(lang))
            {
                var targetLangId = GetIgdbLanguageId(lang);
                if (targetLangId.HasValue)
                {
                    // IGDB mostly only provides localized titles
                    var localization = igdbGame.Localizations.FirstOrDefault(l => l.Language == targetLangId.Value);
                    if (localization != null && !string.IsNullOrEmpty(localization.Name))
                    {
                        title = localization.Name;
                    }
                }

                // Try to get summary from Steam if available
                var steamId = igdbGame.ExternalGames.FirstOrDefault(eg => eg.Category == 1)?.Uid;
                if (!string.IsNullOrEmpty(steamId))
                {
                    // Category 1 is Steam in IGDB
                    var steamDetails = await _steamClient.GetGameDetailsAsync(steamId, lang);
                    if (steamDetails != null)
                    {
                        if (!string.IsNullOrEmpty(steamDetails.AboutTheGame))
                        {
                            summary = CleanHtml(steamDetails.AboutTheGame);
                        }
                        else if (!string.IsNullOrEmpty(steamDetails.DetailedDescription))
                        {
                            summary = CleanHtml(steamDetails.DetailedDescription);
                        }
                    }
                }
            }

            // Resolve PlatformId: folder-based platformKey takes absolute priority
            int resolvedPlatformId = 0;
            if (!string.IsNullOrEmpty(platformKey))
            {
                var folderPlatform = FindPlatform(platformKey);
                if (folderPlatform != null)
                    resolvedPlatformId = folderPlatform.Id;
            }

            // Fallback: best-effort from IGDB platform list (only if folder didn't resolve)
            if (resolvedPlatformId == 0)
            {
                foreach (var igdbPlat in igdbGame.Platforms)
                {
                    var match = PlatformDefinitions.AllPlatforms.FirstOrDefault(pd =>
                        pd.Name.Equals(igdbPlat.Name, StringComparison.OrdinalIgnoreCase) ||
                        (!string.IsNullOrEmpty(igdbPlat.Abbreviation) && pd.Slug.Equals(igdbPlat.Abbreviation, StringComparison.OrdinalIgnoreCase)));
                    if (match != null) { resolvedPlatformId = match.Id; break; }
                }
            }

            var game = new Game
            {
                Title = title,
                Overview = summary,
                Storyline = storyline,
                IgdbId = igdbGame.Id,
                PlatformId = resolvedPlatformId,
                Rating = igdbGame.Rating,
                RatingCount = igdbGame.RatingCount,
                Genres = igdbGame.Genres.Select(g => LocalizeGenre(g.Name, lang)).ToList(),
                AvailablePlatforms = igdbGame.Platforms.Select(p => !string.IsNullOrEmpty(p.Abbreviation) ? p.Abbreviation : p.Name).ToList(),
                Images = new GameImages()
            };

            // Release date
            if (igdbGame.FirstReleaseDate.HasValue)
            {
                game.ReleaseDate = DateTimeOffset.FromUnixTimeSeconds(igdbGame.FirstReleaseDate.Value).DateTime;
                game.Year = game.ReleaseDate.Value.Year;
            }

            // Alternative titles from IGDB
            var altNames = igdbGame.AlternativeNames
                .Where(a => !string.IsNullOrWhiteSpace(a.Name))
                .Select(a => a.Name)
                .Distinct()
                .ToList();
            if (altNames.Count > 0)
            {
                game.AlternativeTitle = string.Join("; ", altNames);
            }

            // Developer & Publisher
            var developer = igdbGame.InvolvedCompanies.FirstOrDefault(c => c.Developer);
            var publisher = igdbGame.InvolvedCompanies.FirstOrDefault(c => c.Publisher);
            game.Developer = developer?.Company.Name;
            game.Publisher = publisher?.Company.Name;

            // Cover/Poster
            if (igdbGame.Cover != null)
            {
                game.Images.CoverUrl = IgdbClient.GetImageUrl(igdbGame.Cover.ImageId, ImageSize.CoverBig);
                game.Images.CoverLargeUrl = IgdbClient.GetImageUrl(igdbGame.Cover.ImageId, ImageSize.HD);
            }

            // Screenshots
            game.Images.Screenshots = igdbGame.Screenshots
                .Select(s => IgdbClient.GetImageUrl(s.ImageId, ImageSize.ScreenshotHuge))
                .ToList();

            // Artworks
            game.Images.Artworks = igdbGame.Artworks
                .Select(a => IgdbClient.GetImageUrl(a.ImageId, ImageSize.HD))
                .ToList();

            // Background - use first artwork or screenshot as fallback
            game.Images.BackgroundUrl = game.Images.Artworks.FirstOrDefault() 
                                       ?? game.Images.Screenshots.FirstOrDefault();

            return game;
        }

        private Game? MapScreenScraperGameToGame(ScreenScraperGame ssGame, int platformId, string? lang = null)
        {
            var title = ssGame.GetName(lang ?? "en");
            if (string.IsNullOrEmpty(title))
                return null;

            var game = new Game
            {
                Title = title,
                Overview = ssGame.GetSynopsis(lang ?? "en"),
                PlatformId = platformId,
                Year = ssGame.GetYear() ?? 0,
                Developer = ssGame.Developer?.Text,
                Publisher = ssGame.Publisher?.Text,
                Status = GameStatus.Released,
                Images = new GameImages()
            };

            // Rating (ScreenScraper uses 0-20 scale, convert to 0-100)
            if (ssGame.Rating?.Text != null && double.TryParse(ssGame.Rating.Text, out var rating))
            {
                game.Rating = rating * 5; // 0-20 -> 0-100
            }

            // Genres
            if (ssGame.Genres != null)
            {
                game.Genres = ssGame.Genres.Select(g => g.GetName(lang ?? "en")).ToList();
            }

            // Images from ScreenScraper
            if (ssGame.Medias != null && ssGame.Medias.Count > 0)
            {
                // Cover (box-2D)
                game.Images.CoverUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "box-2D");
                game.Images.CoverLargeUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "box-2D");
                
                // Screenshot
                var screenshot = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "ss");
                if (!string.IsNullOrEmpty(screenshot))
                    game.Images.Screenshots.Add(screenshot);

                // Fanart/Background
                game.Images.BackgroundUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "fanart")
                                         ?? ScreenScraperClient.GetMediaUrl(ssGame.Medias, "ss");

                // Wheel/Logo as banner
                game.Images.BannerUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "wheel");

                // Box back
                game.Images.BoxBackUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "box-2D-back");

                // Gameplay video
                game.Images.VideoUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "video-normalized");
                if (string.IsNullOrEmpty(game.Images.VideoUrl))
                    game.Images.VideoUrl = ScreenScraperClient.GetMediaUrl(ssGame.Medias, "video");
            }

            _logger.Info($"[ScreenScraper] Found: {game.Title} ({game.Year})");
            return game;
        }

        private int? GetIgdbLanguageId(string lang)
        {
            return lang.ToLower() switch
            {
                "en" => 1,
                "zh" => 2,
                "fr" => 3,
                "de" => 4,
                "ja" => 6,
                "ru" => 9,
                "es" => 10,
                _ => null
            };
        }

        private string CleanHtml(string html)
        {
            if (string.IsNullOrEmpty(html)) return html;
            // Basic HTML cleaning
            var step1 = Regex.Replace(html, "<.*?>", string.Empty);
            var step2 = System.Net.WebUtility.HtmlDecode(step1);
            return step2.Trim();
        }

        private string LocalizeGenre(string genre, string? lang)
        {
            if (string.IsNullOrEmpty(lang) || lang == "en") return genre;

            var mappings = new Dictionary<string, Dictionary<string, string>>
            {
                ["es"] = new() {
                    ["Adventure"] = "Aventura", ["Role-playing (RPG)"] = "RPG", ["Shooter"] = "Disparos",
                    ["Fighting"] = "Lucha", ["Indie"] = "Indie", ["Racing"] = "Carreras",
                    ["Sport"] = "Deportes", ["Simulator"] = "Simulador", ["Strategy"] = "Estrategia",
                    ["Arcade"] = "Arcade", ["Platform"] = "Plataformas", ["Puzzle"] = "Puzle",
                    ["Music"] = "Música", ["Tactical"] = "Táctico", ["Turn-based strategy (TBS)"] = "Estrategia por turnos",
                    ["Real Time Strategy (RTS)"] = "Estrategia en tiempo real", ["Hack and slash/Beat 'em up"] = "Hack and slash",
                    ["Pinball"] = "Pinball", ["Point-and-click"] = "Point-and-click", ["Quiz/Trivia"] = "Preguntas",
                    ["Visual Novel"] = "Novela Visual", ["Card & Board Game"] = "Cartas y Tablero", ["MOBA"] = "MOBA"
                },
                ["fr"] = new() {
                    ["Adventure"] = "Aventure", ["Role-playing (RPG)"] = "RPG", ["Shooter"] = "Tir",
                    ["Fighting"] = "Combat", ["Indie"] = "Indé", ["Racing"] = "Course",
                    ["Sport"] = "Sport", ["Simulator"] = "Simulateur", ["Strategy"] = "Stratégie",
                    ["Arcade"] = "Arcade", ["Platform"] = "Plateforme", ["Puzzle"] = "Puzzle",
                    ["Music"] = "Musique", ["Tactical"] = "Tactique", ["Real Time Strategy (RTS)"] = "Stratégie en temps réel",
                    ["Hack and slash/Beat 'em up"] = "Hack and slash"
                },
                ["de"] = new() {
                    ["Adventure"] = "Abenteuer", ["Role-playing (RPG)"] = "Rollenspiel", ["Shooter"] = "Shooter",
                    ["Fighting"] = "Kampfspiel", ["Indie"] = "Indie", ["Racing"] = "Rennspiel",
                    ["Sport"] = "Sport", ["Simulator"] = "Simulator", ["Strategy"] = "Strategie",
                    ["Puzzle"] = "Rätsel", ["Platform"] = "Plattform"
                },
                ["ru"] = new() {
                    ["Adventure"] = "Приключения", ["Role-playing (RPG)"] = "RPG", ["Shooter"] = "Шутер",
                    ["Fighting"] = "Файтинг", ["Indie"] = "Инди", ["Racing"] = "Гонки",
                    ["Sport"] = "Спорт", ["Simulator"] = "Симулятор", ["Strategy"] = "Стратегия",
                    ["Puzzle"] = "Головоломка", ["Platform"] = "Платформер"
                },
                ["zh"] = new() {
                    ["Adventure"] = "冒险", ["Role-playing (RPG)"] = "角色扮演", ["Shooter"] = "射击",
                    ["Fighting"] = "格斗", ["Indie"] = "独立", ["Racing"] = "竞速",
                    ["Sport"] = "体育", ["Simulator"] = "模拟", ["Strategy"] = "策略",
                    ["Puzzle"] = "解谜", ["Platform"] = "平台"
                },
                ["ja"] = new() {
                    ["Adventure"] = "アドベンチャー", ["Role-playing (RPG)"] = "ロールプレイング", ["Shooter"] = "シューティング",
                    ["Fighting"] = "格闘", ["Indie"] = "インディー", ["Racing"] = "レース",
                    ["Sport"] = "スポーツ", ["Simulator"] = "シミュレーター", ["Strategy"] = "ストラテジー",
                    ["Puzzle"] = "パズル", ["Platform"] = "プラットフォーム"
                }
            };

            if (mappings.TryGetValue(lang.ToLower(), out var langMappings) && langMappings.TryGetValue(genre, out var localized))
            {
                return localized;
            }

            return genre;
        }

        public string LocalizePlatform(string platform, string? lang)
        {
            if (string.IsNullOrEmpty(lang) || lang == "en") return platform;

            var mappings = new Dictionary<string, Dictionary<string, string>>
            {
                ["es"] = new() { ["PC (Microsoft Windows)"] = "PC", ["PlayStation 5"] = "PS5", ["PlayStation 4"] = "PS4", ["Xbox Series X|S"] = "Xbox Series" },
                ["fr"] = new() { ["PC (Microsoft Windows)"] = "PC" },
                ["de"] = new() { ["PC (Microsoft Windows)"] = "PC" },
                ["ru"] = new() { ["PC (Microsoft Windows)"] = "PC" },
                ["zh"] = new() { ["PC (Microsoft Windows)"] = "PC" },
                ["ja"] = new() { ["PC (Microsoft Windows)"] = "PC" }
            };

            if (mappings.TryGetValue(lang.ToLower(), out var langMappings) && langMappings.TryGetValue(platform, out var localized))
            {
                return localized;
            }

            return platform;
        }
    }
}
