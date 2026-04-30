using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroArr.Core.MetadataSource.Epic
{
    // Epic Launcher OAuth, same id+secret every OSS Epic tool uses
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    public class EpicClient
    {
        public const string LauncherClientId = "34a02cf8f4414e29b15921876da36f9a";
        public const string LauncherClientSecret = "daafbccc737745039dffe53d94fc76cf";

        public const string LoginUrl = "https://www.epicgames.com/id/login?redirectUrl=https%3A%2F%2Fwww.epicgames.com%2Fid%2Fapi%2Fredirect%3FclientId%3D" + LauncherClientId + "%26responseType%3Dcode";

        private const string TokenEndpoint = "https://account-public-service-prod03.ol.epicgames.com/account/api/oauth/token";
        private const string AssetsEndpoint = "https://launcher-public-service-prod06.ol.epicgames.com/launcher/api/public/assets/Windows?label=Live";
        private const string CatalogEndpoint = "https://catalog-public-service-prod06.ol.epicgames.com/catalog/api/shared/namespace/{0}/bulk/items";

        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly HttpClient _httpClient;
        private string? _accessToken;
        private string? _refreshToken;

        public EpicClient(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        // UA the real launcher sends to ol.epicgames.com
        private const string LauncherUserAgent = "EpicGamesLauncher/13.3.0-30309001+++Portal+Release-Live Windows/10.0.22631.1.768.64bit";

        public string? AccessToken => _accessToken;
        public string? RefreshToken => _refreshToken;

        // POST grant_type=authorization_code with the launcher basic-auth header
        public async Task<EpicTokenResponse?> ExchangeCodeAsync(string code)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "authorization_code",
                ["code"] = code,
                ["token_type"] = "eg1"
            };
            return await PostTokenAsync(form);
        }

        public async Task<EpicTokenResponse?> RefreshAsync(string refreshToken)
        {
            var form = new Dictionary<string, string>
            {
                ["grant_type"] = "refresh_token",
                ["refresh_token"] = refreshToken,
                ["token_type"] = "eg1"
            };
            return await PostTokenAsync(form);
        }

        private async Task<EpicTokenResponse?> PostTokenAsync(Dictionary<string, string> form)
        {
            using var req = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint);
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{LauncherClientId}:{LauncherClientSecret}"));
            req.Headers.Authorization = new AuthenticationHeaderValue("basic", basic);
            req.Headers.UserAgent.ParseAdd(LauncherUserAgent);
            req.Headers.Accept.ParseAdd("application/json");
            req.Content = new FormUrlEncodedContent(form);

            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var snippet = body.Length > 200 ? body.Substring(0, 200) : body;
                _logger.Warn($"[Epic] token endpoint http={(int)resp.StatusCode}. Body[0..200]: {snippet}");
                return null;
            }

            var token = JsonSerializer.Deserialize<EpicTokenResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            if (token != null)
            {
                _accessToken = token.AccessToken;
                _refreshToken = token.RefreshToken;
            }
            return token;
        }

        // owned Windows assets, each carries namespace + catalogItemId for the catalog lookup
        public async Task<List<EpicAsset>> GetOwnedAssetsAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
                throw new InvalidOperationException("No access token. Call ExchangeCodeAsync or RefreshAsync first.");

            using var req = new HttpRequestMessage(HttpMethod.Get, AssetsEndpoint);
            req.Headers.Authorization = new AuthenticationHeaderValue("bearer", _accessToken);
            req.Headers.UserAgent.ParseAdd(LauncherUserAgent);
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var snippet = body.Length > 200 ? body.Substring(0, 200) : body;
                _logger.Warn($"[Epic] assets http={(int)resp.StatusCode}. Body[0..200]: {snippet}");
                return new List<EpicAsset>();
            }
            return JsonSerializer.Deserialize<List<EpicAsset>>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
                   ?? new List<EpicAsset>();
        }

        // catalog lookup by namespace + catalogItemId, response is keyed by catalogItemId
        public async Task<EpicCatalogItem?> GetCatalogItemAsync(string ns, string catalogItemId, string locale = "en-US", string country = "US")
        {
            if (string.IsNullOrEmpty(_accessToken))
                throw new InvalidOperationException("No access token. Call ExchangeCodeAsync or RefreshAsync first.");

            var url = string.Format(CatalogEndpoint, Uri.EscapeDataString(ns))
                    + $"?id={Uri.EscapeDataString(catalogItemId)}&country={country}&locale={locale}&includeDLCDetails=true&includeMainGameDetails=true";

            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            req.Headers.Authorization = new AuthenticationHeaderValue("bearer", _accessToken);
            req.Headers.UserAgent.ParseAdd(LauncherUserAgent);
            req.Headers.Accept.ParseAdd("application/json");
            using var resp = await _httpClient.SendAsync(req);
            var body = await resp.Content.ReadAsStringAsync();
            if (!resp.IsSuccessStatusCode)
            {
                var snippet = body.Length > 200 ? body.Substring(0, 200) : body;
                _logger.Warn($"[Epic] catalog {ns}/{catalogItemId} http={(int)resp.StatusCode}. Body[0..200]: {snippet}");
                return null;
            }

            try
            {
                using var doc = JsonDocument.Parse(body);
                if (doc.RootElement.TryGetProperty(catalogItemId, out var item))
                {
                    return JsonSerializer.Deserialize<EpicCatalogItem>(item.GetRawText(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                }
            }
            catch (JsonException ex)
            {
                _logger.Warn($"[Epic] Catalog parse error: {ex.Message}");
            }
            return null;
        }
    }

    #region DTOs

    public class EpicTokenResponse
    {
        [JsonPropertyName("access_token")] public string AccessToken { get; set; } = string.Empty;
        [JsonPropertyName("refresh_token")] public string RefreshToken { get; set; } = string.Empty;
        [JsonPropertyName("expires_in")] public int ExpiresIn { get; set; }
        [JsonPropertyName("refresh_expires_in")] public int RefreshExpiresIn { get; set; }
        [JsonPropertyName("account_id")] public string? AccountId { get; set; }
        [JsonPropertyName("displayName")] public string? DisplayName { get; set; }
        [JsonPropertyName("token_type")] public string? TokenType { get; set; }
    }

    public class EpicAsset
    {
        [JsonPropertyName("appName")] public string? AppName { get; set; }
        [JsonPropertyName("labelName")] public string? LabelName { get; set; }
        [JsonPropertyName("buildVersion")] public string? BuildVersion { get; set; }
        [JsonPropertyName("catalogItemId")] public string? CatalogItemId { get; set; }
        [JsonPropertyName("namespace")] public string? Namespace { get; set; }
        [JsonPropertyName("assetId")] public string? AssetId { get; set; }
    }

    public class EpicCatalogItem
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("longDescription")] public string? LongDescription { get; set; }
        [JsonPropertyName("technicalDetails")] public string? TechnicalDetails { get; set; }
        [JsonPropertyName("developer")] public string? Developer { get; set; }
        [JsonPropertyName("creationDate")] public string? CreationDate { get; set; }
        [JsonPropertyName("releaseInfo")] public List<EpicReleaseInfo>? ReleaseInfo { get; set; }
        [JsonPropertyName("keyImages")] public List<EpicKeyImage>? KeyImages { get; set; }
        [JsonPropertyName("categories")] public List<EpicCategory>? Categories { get; set; }
        [JsonPropertyName("entitlementName")] public string? EntitlementName { get; set; }
        [JsonPropertyName("entitlementType")] public string? EntitlementType { get; set; }

        public string? PickImage(params string[] preferredTypes)
        {
            if (KeyImages == null || KeyImages.Count == 0) return null;
            foreach (var t in preferredTypes)
            {
                var hit = KeyImages.FirstOrDefault(k => string.Equals(k.Type, t, StringComparison.OrdinalIgnoreCase));
                if (hit != null && !string.IsNullOrEmpty(hit.Url)) return hit.Url;
            }
            return KeyImages.FirstOrDefault(k => !string.IsNullOrEmpty(k.Url))?.Url;
        }

        public int? GetReleaseYear()
        {
            var date = ReleaseInfo?.FirstOrDefault()?.DateAdded ?? CreationDate;
            if (string.IsNullOrEmpty(date) || date.Length < 4) return null;
            return int.TryParse(date.Substring(0, 4), out var y) ? y : null;
        }

        public bool LooksLikeGame()
        {
            // only filter clear non-games. Epic tags real games with "applications" too
            if (Categories == null || Categories.Count == 0) return true;
            var paths = Categories.Select(c => c.Path?.ToLowerInvariant() ?? string.Empty).ToList();
            if (paths.Any(p => p == "addons" || p.StartsWith("addons/"))) return false;
            if (paths.Any(p => p == "digitalextras" || p.StartsWith("digitalextras/"))) return false;
            if (paths.Any(p => p == "soundtrack" || p.StartsWith("soundtrack/"))) return false;
            if (paths.Any(p => p == "software/edu")) return false;
            return true;
        }
    }

    public class EpicReleaseInfo
    {
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("appId")] public string? AppId { get; set; }
        [JsonPropertyName("platform")] public List<string>? Platform { get; set; }
        [JsonPropertyName("dateAdded")] public string? DateAdded { get; set; }
    }

    public class EpicKeyImage
    {
        [JsonPropertyName("type")] public string? Type { get; set; }
        [JsonPropertyName("url")] public string? Url { get; set; }
        [JsonPropertyName("width")] public int? Width { get; set; }
        [JsonPropertyName("height")] public int? Height { get; set; }
    }

    public class EpicCategory
    {
        [JsonPropertyName("path")] public string? Path { get; set; }
    }

    #endregion
}
