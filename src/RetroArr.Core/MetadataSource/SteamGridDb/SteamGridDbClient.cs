using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroArr.Core.MetadataSource.SteamGridDb
{
    public enum SteamGridDbStatus
    {
        Ok,
        Empty,
        AuthFailed,
        NetworkError,
        Unconfigured
    }

    /// <summary>
    /// Image-only client for SteamGridDB. Auth is "Authorization: Bearer {key}".
    /// Returns grids (covers), heroes (banners), logos and icons. Does not deliver
    /// classical metadata (synopsis, genre, year, developer, publisher).
    /// </summary>
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    public class SteamGridDbClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private const string BaseUrl = "https://www.steamgriddb.com/api/v2";

        public SteamGridDbClient(HttpClient httpClient, string? apiKey)
        {
            _httpClient = httpClient;
            _apiKey = apiKey ?? string.Empty;
        }

        private bool IsConfigured => !string.IsNullOrWhiteSpace(_apiKey);

        // shared classifier so tests can hit it without an http server
        internal static SteamGridDbStatus ClassifyResponse(HttpStatusCode httpCode, string? body)
        {
            if (httpCode == HttpStatusCode.Unauthorized || httpCode == HttpStatusCode.Forbidden)
                return SteamGridDbStatus.AuthFailed;
            if ((int)httpCode >= 500) return SteamGridDbStatus.NetworkError;
            if (httpCode == HttpStatusCode.NotFound) return SteamGridDbStatus.Empty;
            if (httpCode != HttpStatusCode.OK) return SteamGridDbStatus.NetworkError;

            var raw = body ?? string.Empty;
            var trimmed = raw.TrimStart();
            if (trimmed.Length < 2 || (trimmed[0] != '{' && trimmed[0] != '['))
                return SteamGridDbStatus.NetworkError;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                var root = doc.RootElement;
                if (root.ValueKind != JsonValueKind.Object) return SteamGridDbStatus.NetworkError;

                if (root.TryGetProperty("success", out var s) && s.ValueKind == JsonValueKind.False)
                    return SteamGridDbStatus.AuthFailed;
                return SteamGridDbStatus.Ok;
            }
            catch (JsonException)
            {
                return SteamGridDbStatus.NetworkError;
            }
        }

        private HttpRequestMessage BuildRequest(string url)
        {
            var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
            return req;
        }

        public async Task<(SteamGridDbStatus Status, List<SteamGridDbGame> Games)> SearchGamesAsync(string query)
        {
            if (!IsConfigured) return (SteamGridDbStatus.Unconfigured, new List<SteamGridDbGame>());

            try
            {
                var url = $"{BaseUrl}/search/autocomplete/{Uri.EscapeDataString(query)}";
                using var request = BuildRequest(url);
                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != SteamGridDbStatus.Ok)
                {
                    _logger.Info($"[SteamGridDb] search status={status} for '{query}'");
                    return (status, new List<SteamGridDbGame>());
                }

                var parsed = JsonSerializer.Deserialize<SteamGridDbSearchResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var data = parsed?.Data ?? new List<SteamGridDbGame>();
                return (data.Count > 0 ? SteamGridDbStatus.Ok : SteamGridDbStatus.Empty, data);
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamGridDb] search exception: {ex.Message}");
                return (SteamGridDbStatus.NetworkError, new List<SteamGridDbGame>());
            }
        }

        public Task<(SteamGridDbStatus Status, List<SteamGridDbAsset> Assets)> GetGridsAsync(int gameId) => GetAssetsAsync("grids", gameId);
        public Task<(SteamGridDbStatus Status, List<SteamGridDbAsset> Assets)> GetHeroesAsync(int gameId) => GetAssetsAsync("heroes", gameId);
        public Task<(SteamGridDbStatus Status, List<SteamGridDbAsset> Assets)> GetLogosAsync(int gameId) => GetAssetsAsync("logos", gameId);
        public Task<(SteamGridDbStatus Status, List<SteamGridDbAsset> Assets)> GetIconsAsync(int gameId) => GetAssetsAsync("icons", gameId);

        private async Task<(SteamGridDbStatus Status, List<SteamGridDbAsset> Assets)> GetAssetsAsync(string kind, int gameId)
        {
            if (!IsConfigured) return (SteamGridDbStatus.Unconfigured, new List<SteamGridDbAsset>());

            try
            {
                var url = $"{BaseUrl}/{kind}/game/{gameId}";
                using var request = BuildRequest(url);
                var response = await _httpClient.SendAsync(request);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != SteamGridDbStatus.Ok)
                {
                    _logger.Info($"[SteamGridDb] {kind} status={status} for game={gameId}");
                    return (status, new List<SteamGridDbAsset>());
                }

                var parsed = JsonSerializer.Deserialize<SteamGridDbAssetResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var data = parsed?.Data ?? new List<SteamGridDbAsset>();
                return (data.Count > 0 ? SteamGridDbStatus.Ok : SteamGridDbStatus.Empty, data);
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamGridDb] {kind} exception: {ex.Message}");
                return (SteamGridDbStatus.NetworkError, new List<SteamGridDbAsset>());
            }
        }

        // pick the highest scored asset, prefer non-nsfw and non-humor unless nothing else is left
        public static SteamGridDbAsset? PickBest(List<SteamGridDbAsset> assets)
        {
            if (assets == null || assets.Count == 0) return null;
            var safe = assets.Where(a => a.Nsfw != true && a.Humor != true).ToList();
            var pool = safe.Count > 0 ? safe : assets;
            return pool.OrderByDescending(a => a.Score).FirstOrDefault();
        }
    }

    #region DTOs

    public class SteamGridDbSearchResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public List<SteamGridDbGame>? Data { get; set; }
    }

    public class SteamGridDbAssetResponse
    {
        [JsonPropertyName("success")] public bool Success { get; set; }
        [JsonPropertyName("data")] public List<SteamGridDbAsset>? Data { get; set; }
    }

    public class SteamGridDbGame
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("types")] public List<string>? Types { get; set; }
        [JsonPropertyName("verified")] public bool? Verified { get; set; }
        [JsonPropertyName("release_date")] public long? ReleaseDate { get; set; }
    }

    public class SteamGridDbAsset
    {
        [JsonPropertyName("id")] public int Id { get; set; }
        [JsonPropertyName("score")] public int Score { get; set; }
        [JsonPropertyName("style")] public string? Style { get; set; }
        [JsonPropertyName("width")] public int? Width { get; set; }
        [JsonPropertyName("height")] public int? Height { get; set; }
        [JsonPropertyName("nsfw")] public bool? Nsfw { get; set; }
        [JsonPropertyName("humor")] public bool? Humor { get; set; }
        [JsonPropertyName("notes")] public string? Notes { get; set; }
        [JsonPropertyName("mime")] public string? Mime { get; set; }
        [JsonPropertyName("language")] public string? Language { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("thumb")] public string? Thumb { get; set; }
        [JsonPropertyName("upvotes")] public int? Upvotes { get; set; }
        [JsonPropertyName("downvotes")] public int? Downvotes { get; set; }
    }

    #endregion
}
