using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RetroArr.Core.MetadataSource.TheGamesDb
{
    public enum TheGamesDbStatus
    {
        Ok,
        Empty,
        QuotaExceeded,
        AuthFailed,
        NetworkError,
        Unconfigured
    }

    /// <summary>
    /// Thin client for thegamesdb.net api v1. apikey is passed as a query parameter
    /// per the official spec at https://api.thegamesdb.net/spec.yaml.
    /// Reference data (genres, developers, publishers) is fetched once and cached
    /// for the lifetime of the client because the lists change rarely.
    /// </summary>
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    public class TheGamesDbClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://api.thegamesdb.net";

        private Dictionary<int, string>? _genreCache;
        private Dictionary<int, string>? _developerCache;
        private Dictionary<int, string>? _publisherCache;
        private readonly SemaphoreSlim _refLock = new SemaphoreSlim(1, 1);

        public TheGamesDbClient(HttpClient httpClient, string? apiKey)
        {
            _httpClient = httpClient;
            _apiKey = apiKey ?? string.Empty;
        }

        private bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        // shared classifier so tests can hit it without spinning up an http server
        internal static TheGamesDbStatus ClassifyResponse(HttpStatusCode httpCode, string? body)
        {
            if (httpCode == HttpStatusCode.Unauthorized) return TheGamesDbStatus.AuthFailed;
            if (httpCode == HttpStatusCode.Forbidden) return TheGamesDbStatus.QuotaExceeded;
            if ((int)httpCode >= 500) return TheGamesDbStatus.NetworkError;
            if (httpCode != HttpStatusCode.OK) return TheGamesDbStatus.NetworkError;

            var raw = body ?? string.Empty;
            var trimmed = raw.TrimStart();
            if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '['))
                return TheGamesDbStatus.NetworkError;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return TheGamesDbStatus.NetworkError;

                int code = 200;
                if (root.TryGetProperty("code", out var codeProp) && codeProp.ValueKind == JsonValueKind.Number)
                    code = codeProp.GetInt32();

                return code switch
                {
                    200 => TheGamesDbStatus.Ok,
                    401 => TheGamesDbStatus.AuthFailed,
                    403 => TheGamesDbStatus.QuotaExceeded,
                    404 => TheGamesDbStatus.Empty,
                    _ when code >= 500 => TheGamesDbStatus.NetworkError,
                    _ => TheGamesDbStatus.NetworkError
                };
            }
            catch (JsonException)
            {
                return TheGamesDbStatus.NetworkError;
            }
        }

        public async Task<(TheGamesDbStatus Status, List<TheGamesDbGame> Games, TheGamesDbImageBaseUrls? BoxartBase)> SearchGamesByNameAsync(string query, int? platformId = null)
        {
            if (!IsConfigured) return (TheGamesDbStatus.Unconfigured, new List<TheGamesDbGame>(), null);

            try
            {
                var url = $"{BaseUrl}/v1.1/Games/ByGameName?apikey={Uri.EscapeDataString(_apiKey)}"
                        + $"&name={Uri.EscapeDataString(query)}"
                        + "&fields=players,publishers,genres,overview,last_updated,rating,platform,coop,youtube,alternates"
                        + "&include=boxart,platform";
                if (platformId.HasValue)
                    url += $"&filter[platform]={platformId.Value}";

                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != TheGamesDbStatus.Ok)
                {
                    _logger.Info($"[TheGamesDb] ByGameName status={status} for '{query}'");
                    return (status, new List<TheGamesDbGame>(), null);
                }

                var parsed = JsonSerializer.Deserialize<TheGamesDbGamesResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var games = parsed?.Data?.Games ?? new List<TheGamesDbGame>();
                var boxartBase = parsed?.Include?.Boxart?.BaseUrl;
                if (parsed?.Include?.Boxart?.Data != null)
                {
                    foreach (var g in games)
                    {
                        if (parsed.Include.Boxart.Data.TryGetValue(g.Id.ToString(), out var imgs))
                            g.BoxartImages = imgs;
                    }
                }
                return (games.Count > 0 ? TheGamesDbStatus.Ok : TheGamesDbStatus.Empty, games, boxartBase);
            }
            catch (Exception ex)
            {
                _logger.Error($"[TheGamesDb] ByGameName exception: {ex.Message}");
                return (TheGamesDbStatus.NetworkError, new List<TheGamesDbGame>(), null);
            }
        }

        public async Task<(TheGamesDbStatus Status, TheGamesDbGame? Game, TheGamesDbImageBaseUrls? BoxartBase)> GetGameByIdAsync(int id)
        {
            if (!IsConfigured) return (TheGamesDbStatus.Unconfigured, null, null);

            try
            {
                var url = $"{BaseUrl}/v1/Games/ByGameID?apikey={Uri.EscapeDataString(_apiKey)}"
                        + $"&id={id}"
                        + "&fields=players,publishers,genres,overview,last_updated,rating,platform,coop,youtube,alternates"
                        + "&include=boxart,platform";

                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != TheGamesDbStatus.Ok)
                {
                    _logger.Info($"[TheGamesDb] ByGameID id={id} status={status}");
                    return (status, null, null);
                }

                var parsed = JsonSerializer.Deserialize<TheGamesDbGamesResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var game = parsed?.Data?.Games?.FirstOrDefault();
                var boxartBase = parsed?.Include?.Boxart?.BaseUrl;
                if (game != null && parsed?.Include?.Boxart?.Data != null
                    && parsed.Include.Boxart.Data.TryGetValue(game.Id.ToString(), out var imgs))
                {
                    game.BoxartImages = imgs;
                }
                return (game != null ? TheGamesDbStatus.Ok : TheGamesDbStatus.Empty, game, boxartBase);
            }
            catch (Exception ex)
            {
                _logger.Error($"[TheGamesDb] ByGameID exception: {ex.Message}");
                return (TheGamesDbStatus.NetworkError, null, null);
            }
        }

        public async Task<(TheGamesDbStatus Status, List<TheGamesDbImage> Images, TheGamesDbImageBaseUrls? BaseUrls)> GetImagesAsync(int gameId, string? typeFilter = null)
        {
            if (!IsConfigured) return (TheGamesDbStatus.Unconfigured, new List<TheGamesDbImage>(), null);

            try
            {
                var url = $"{BaseUrl}/v1/Games/Images?apikey={Uri.EscapeDataString(_apiKey)}&games_id={gameId}";
                if (!string.IsNullOrEmpty(typeFilter))
                    url += $"&filter[type]={Uri.EscapeDataString(typeFilter)}";

                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != TheGamesDbStatus.Ok)
                {
                    _logger.Info($"[TheGamesDb] Images game={gameId} status={status}");
                    return (status, new List<TheGamesDbImage>(), null);
                }

                var parsed = JsonSerializer.Deserialize<TheGamesDbImagesResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var baseUrls = parsed?.Data?.BaseUrl;
                var images = new List<TheGamesDbImage>();
                if (parsed?.Data?.Images != null && parsed.Data.Images.TryGetValue(gameId.ToString(), out var list))
                    images.AddRange(list);

                return (images.Count > 0 ? TheGamesDbStatus.Ok : TheGamesDbStatus.Empty, images, baseUrls);
            }
            catch (Exception ex)
            {
                _logger.Error($"[TheGamesDb] Images exception: {ex.Message}");
                return (TheGamesDbStatus.NetworkError, new List<TheGamesDbImage>(), null);
            }
        }

        // reference data: small, rarely changing. fetch once, cache for service lifetime
        public async Task EnsureReferenceDataAsync()
        {
            if (!IsConfigured) return;
            if (_genreCache != null && _developerCache != null && _publisherCache != null) return;

            await _refLock.WaitAsync();
            try
            {
                if (_genreCache == null)
                    _genreCache = await FetchReferenceAsync("/v1/Genres", "genres");
                if (_developerCache == null)
                    _developerCache = await FetchReferenceAsync("/v1/Developers", "developers");
                if (_publisherCache == null)
                    _publisherCache = await FetchReferenceAsync("/v1/Publishers", "publishers");
            }
            finally
            {
                _refLock.Release();
            }
        }

        public string ResolveGenre(int id) => _genreCache != null && _genreCache.TryGetValue(id, out var n) ? n : string.Empty;
        public string ResolveDeveloper(int id) => _developerCache != null && _developerCache.TryGetValue(id, out var n) ? n : string.Empty;
        public string ResolvePublisher(int id) => _publisherCache != null && _publisherCache.TryGetValue(id, out var n) ? n : string.Empty;

        private async Task<Dictionary<int, string>> FetchReferenceAsync(string path, string node)
        {
            var dict = new Dictionary<int, string>();
            try
            {
                var url = $"{BaseUrl}{path}?apikey={Uri.EscapeDataString(_apiKey)}";
                var response = await _httpClient.GetAsync(url);
                if (!response.IsSuccessStatusCode) return dict;
                var body = await response.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(body);
                if (!doc.RootElement.TryGetProperty("data", out var data)) return dict;
                if (!data.TryGetProperty(node, out var entries) || entries.ValueKind != JsonValueKind.Object) return dict;

                foreach (var prop in entries.EnumerateObject())
                {
                    if (prop.Value.TryGetProperty("id", out var idProp) && prop.Value.TryGetProperty("name", out var nameProp))
                    {
                        if (idProp.ValueKind == JsonValueKind.Number && nameProp.ValueKind == JsonValueKind.String)
                            dict[idProp.GetInt32()] = nameProp.GetString() ?? string.Empty;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Warn($"[TheGamesDb] Failed to fetch reference {path}: {ex.Message}");
            }
            return dict;
        }

        // helper for callers: pick the best boxart url from a game's image list
        public static string? PickBoxartUrl(List<TheGamesDbImage>? images, TheGamesDbImageBaseUrls? baseUrls, string preferredSize = "large")
        {
            if (images == null || images.Count == 0 || baseUrls == null) return null;

            var boxart = images.FirstOrDefault(i => i.Type == "boxart" && i.Side == "front")
                      ?? images.FirstOrDefault(i => i.Type == "boxart");
            if (boxart == null || string.IsNullOrEmpty(boxart.Filename)) return null;

            var basePart = preferredSize switch
            {
                "thumb" => baseUrls.Thumb,
                "small" => baseUrls.Small,
                "medium" => baseUrls.Medium,
                "original" => baseUrls.Original,
                _ => baseUrls.Large
            };
            if (string.IsNullOrEmpty(basePart)) basePart = baseUrls.Original ?? baseUrls.Large ?? string.Empty;
            if (string.IsNullOrEmpty(basePart)) return null;

            return basePart.TrimEnd('/') + "/" + boxart.Filename.TrimStart('/');
        }
    }

    #region DTOs

    public class TheGamesDbGamesResponse
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("remaining_monthly_allowance")] public int RemainingMonthlyAllowance { get; set; }
        [JsonPropertyName("extra_allowance")] public int ExtraAllowance { get; set; }
        [JsonPropertyName("allowance_refresh_timer")] public int? AllowanceRefreshTimer { get; set; }
        [JsonPropertyName("data")] public TheGamesDbGamesData? Data { get; set; }
        [JsonPropertyName("include")] public TheGamesDbInclude? Include { get; set; }
    }

    public class TheGamesDbGamesData
    {
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("games")] public List<TheGamesDbGame>? Games { get; set; }
    }

    public class TheGamesDbInclude
    {
        [JsonPropertyName("boxart")] public TheGamesDbBoxartInclude? Boxart { get; set; }
    }

    public class TheGamesDbBoxartInclude
    {
        [JsonPropertyName("base_url")] public TheGamesDbImageBaseUrls? BaseUrl { get; set; }
        [JsonPropertyName("data")] public Dictionary<string, List<TheGamesDbImage>>? Data { get; set; }
    }

    public class TheGamesDbImageBaseUrls
    {
        [JsonPropertyName("original")] public string? Original { get; set; }
        [JsonPropertyName("small")] public string? Small { get; set; }
        [JsonPropertyName("thumb")] public string? Thumb { get; set; }
        [JsonPropertyName("cropped_center_thumb")] public string? CroppedCenterThumb { get; set; }
        [JsonPropertyName("medium")] public string? Medium { get; set; }
        [JsonPropertyName("large")] public string? Large { get; set; }
    }

    public class TheGamesDbGame
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("game_title")] public string? GameTitle { get; set; }
        [JsonPropertyName("release_date")] public string? ReleaseDate { get; set; }
        [JsonPropertyName("platform")] public int Platform { get; set; }
        [JsonPropertyName("region_id")] public int? RegionId { get; set; }
        [JsonPropertyName("country_id")] public int? CountryId { get; set; }
        [JsonPropertyName("players")] public int? Players { get; set; }
        [JsonPropertyName("overview")] public string? Overview { get; set; }
        [JsonPropertyName("last_updated")] public string? LastUpdated { get; set; }
        [JsonPropertyName("rating")] public string? Rating { get; set; }
        [JsonPropertyName("coop")] public string? Coop { get; set; }
        [JsonPropertyName("youtube")] public string? Youtube { get; set; }
        [JsonPropertyName("developers")] public List<int>? Developers { get; set; }
        [JsonPropertyName("genres")] public List<int>? Genres { get; set; }
        [JsonPropertyName("publishers")] public List<int>? Publishers { get; set; }
        [JsonPropertyName("alternates")] public List<string>? Alternates { get; set; }

        // Hydrated by SearchGamesByNameAsync / GetGameByIdAsync from include.boxart
        [JsonIgnore] public List<TheGamesDbImage>? BoxartImages { get; set; }

        public int? GetReleaseYear()
        {
            if (string.IsNullOrEmpty(ReleaseDate) || ReleaseDate.Length < 4) return null;
            return int.TryParse(ReleaseDate.Substring(0, 4), out var y) ? y : (int?)null;
        }
    }

    public class TheGamesDbImage
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("side")] public string? Side { get; set; }
        [JsonPropertyName("filename")] public string? Filename { get; set; }
        [JsonPropertyName("resolution")] public string? Resolution { get; set; }
    }

    public class TheGamesDbImagesResponse
    {
        [JsonPropertyName("code")] public int Code { get; set; }
        [JsonPropertyName("status")] public string? Status { get; set; }
        [JsonPropertyName("remaining_monthly_allowance")] public int RemainingMonthlyAllowance { get; set; }
        [JsonPropertyName("data")] public TheGamesDbImagesData? Data { get; set; }
    }

    public class TheGamesDbImagesData
    {
        [JsonPropertyName("count")] public int Count { get; set; }
        [JsonPropertyName("base_url")] public TheGamesDbImageBaseUrls? BaseUrl { get; set; }
        [JsonPropertyName("images")] public Dictionary<string, List<TheGamesDbImage>>? Images { get; set; }
    }

    #endregion
}
