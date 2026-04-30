using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.MetadataSource.Gog
{
    // GOG Galaxy client: public API + local Galaxy DB
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    public class GogClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.GogDownloads);
        private readonly HttpClient _httpClient;
        private readonly string? _refreshToken;
        private string? _accessToken;
        private DateTime _tokenExpiration;

        private const string GogAuthUrl = "https://auth.gog.com";
        private const string GogApiUrl = "https://api.gog.com";
        private const string GogEmbedUrl = "https://embed.gog.com";

        public GogClient(string? refreshToken = null)
        {
            _refreshToken = refreshToken;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "RetroArr/1.0");
        }

        public static string GetLoginUrl(string clientId, string redirectUri)
        {
            return $"{GogAuthUrl}/auth?client_id={clientId}&redirect_uri={Uri.EscapeDataString(redirectUri)}&response_type=code&layout=client2";
        }

        public async Task<GogTokenResponse?> ExchangeCodeAsync(string code, string clientId, string clientSecret, string redirectUri)
        {
            var url = $"{GogAuthUrl}/token?client_id={clientId}&client_secret={clientSecret}&grant_type=authorization_code&code={code}&redirect_uri={Uri.EscapeDataString(redirectUri)}";
            
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[GOG] Token exchange failed: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GogTokenResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        public async Task<bool> RefreshTokenAsync(string clientId, string clientSecret)
        {
            if (string.IsNullOrEmpty(_refreshToken))
                return false;

            var url = $"{GogAuthUrl}/token?client_id={clientId}&client_secret={clientSecret}&grant_type=refresh_token&refresh_token={_refreshToken}";
            
            var response = await _httpClient.GetAsync(url);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[GOG] Token refresh failed: {response.StatusCode}");
                return false;
            }

            var content = await response.Content.ReadAsStringAsync();
            var tokenResponse = JsonSerializer.Deserialize<GogTokenResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            if (tokenResponse != null)
            {
                _accessToken = tokenResponse.AccessToken;
                _tokenExpiration = DateTime.UtcNow.AddSeconds(tokenResponse.ExpiresIn - 60);
                return true;
            }

            return false;
        }

        public async Task<List<GogOwnedGame>> GetOwnedGamesAsync()
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.Info("[GOG] No access token available");
                return new List<GogOwnedGame>();
            }

            var games = new List<GogOwnedGame>();
            var page = 1;
            var hasMore = true;

            while (hasMore)
            {
                var request = new HttpRequestMessage(HttpMethod.Get, $"{GogEmbedUrl}/account/getFilteredProducts?mediaType=1&page={page}");
                request.Headers.Add("Authorization", $"Bearer {_accessToken}");

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Error($"[GOG] Failed to get owned games: {response.StatusCode}");
                    break;
                }

                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<GogProductsResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (result?.Products != null)
                {
                    games.AddRange(result.Products);
                    hasMore = page < result.TotalPages;
                    page++;
                }
                else
                {
                    hasMore = false;
                }
            }

            return games;
        }

        // public API, no auth
        public async Task<GogGameDetails?> GetGameDetailsAsync(string gogId)
        {
            var response = await _httpClient.GetAsync($"{GogApiUrl}/products/{gogId}?expand=description,downloads,expanded_dlcs,related_products,changelog");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[GOG] Failed to get game details for {gogId}: {response.StatusCode}");
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<GogGameDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }

        // public API, no auth
        public async Task<List<GogSearchResult>> SearchGamesAsync(string query, int limit = 20)
        {
            var encodedQuery = Uri.EscapeDataString(query);
            var response = await _httpClient.GetAsync($"{GogEmbedUrl}/games/ajax/filtered?mediaType=game&search={encodedQuery}&limit={limit}");
            
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[GOG] Search failed: {response.StatusCode}");
                return new List<GogSearchResult>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<GogSearchResponse>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            
            return result?.Products ?? new List<GogSearchResult>();
        }

        public void SetAccessToken(string accessToken, int expiresIn)
        {
            _accessToken = accessToken;
            _tokenExpiration = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        }

        // auth required
        public async Task<List<GogDownloadFile>> GetGameDownloadsAsync(string gogId)
        {
            if (string.IsNullOrEmpty(_accessToken))
            {
                _logger.Info("[GOG] No access token available for downloads");
                return new List<GogDownloadFile>();
            }

            var request = new HttpRequestMessage(HttpMethod.Get, $"{GogEmbedUrl}/account/gameDetails/{gogId}.json");
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[GOG] Failed to get downloads for {gogId}: {response.StatusCode}");
                return new List<GogDownloadFile>();
            }

            var content = await response.Content.ReadAsStringAsync();
            _logger.Info($"[GOG] Downloads response for {gogId}: {content.Substring(0, Math.Min(500, content.Length))}...");
            
            var details = JsonSerializer.Deserialize<GogAccountGameDetails>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            var downloads = new List<GogDownloadFile>();
            
            if (details?.Downloads == null)
            {
                _logger.Info($"[GOG] No downloads property found for {gogId}");
                return downloads;
            }
            
            var downloadsKind = details.Downloads.Value.ValueKind;
            _logger.Info($"[GOG] Downloads property type: {downloadsKind}");
            
            if (downloadsKind == JsonValueKind.String || downloadsKind == JsonValueKind.Null || downloadsKind == JsonValueKind.Undefined)
            {
                _logger.Info($"[GOG] Downloads is not an object/array (type: {downloadsKind}), returning empty list");
                return downloads;
            }
            
            try
            {
                if (downloadsKind == JsonValueKind.Object)
                {
                    // Format A: { "downloads": { "windows": [...], "mac": [...], "linux": [...] } }
                    foreach (var platform in details.Downloads.Value.EnumerateObject())
                    {
                        if (platform.Value.ValueKind == JsonValueKind.Array)
                            ExtractPlatformDownloads(platform.Value, platform.Name, downloads);
                    }
                }
                else if (downloadsKind == JsonValueKind.Array)
                {
                    // Format B: [ ["English", { "windows": [...], ... }], ... ]
                    foreach (var item in details.Downloads.Value.EnumerateArray())
                    {
                        if (item.ValueKind == JsonValueKind.Object)
                        {
                            // Could be a direct download or a platform container
                            if (IsDownloadObject(item))
                            {
                                downloads.Add(ParseDownloadObject(item, "windows"));
                            }
                            else
                            {
                                // Platform container: {"windows": [...], "mac": [...]}
                                foreach (var platform in item.EnumerateObject())
                                {
                                    if (platform.Value.ValueKind == JsonValueKind.Array)
                                        ExtractPlatformDownloads(platform.Value, platform.Name, downloads);
                                }
                            }
                        }
                        else if (item.ValueKind == JsonValueKind.Array)
                        {
                            // Language-wrapped: ["English", {"windows": [...]}] or ["English", {download}]
                            foreach (var subItem in item.EnumerateArray())
                            {
                                if (subItem.ValueKind != JsonValueKind.Object) continue;
                                
                                if (IsDownloadObject(subItem))
                                {
                                    downloads.Add(ParseDownloadObject(subItem, "windows"));
                                }
                                else
                                {
                                    foreach (var platform in subItem.EnumerateObject())
                                    {
                                        if (platform.Value.ValueKind == JsonValueKind.Array)
                                            ExtractPlatformDownloads(platform.Value, platform.Name, downloads);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[GOG] Error parsing downloads: {ex.Message}");
            }
            
            _logger.Info($"[GOG] Found {downloads.Count} downloads for {gogId}");

            return downloads;
        }

        // auth required
        public async Task<string?> GetDownloadUrlAsync(string manualUrl)
        {
            if (string.IsNullOrEmpty(_accessToken) || string.IsNullOrEmpty(manualUrl))
            {
                _logger.Error($"[GOG] GetDownloadUrl: missing accessToken={_accessToken != null} manualUrl={manualUrl}");
                return null;
            }

            var fullUrl = manualUrl.StartsWith("http") ? manualUrl : $"{GogEmbedUrl}{manualUrl}";
            _logger.Info($"[GOG] Resolving download URL: {fullUrl}");

            // Use a separate handler that does NOT follow redirects so we can capture CDN URLs
            using var handler = new HttpClientHandler { AllowAutoRedirect = false };
            using var noRedirectClient = new HttpClient(handler);
            noRedirectClient.DefaultRequestHeaders.Add("User-Agent", "RetroArr/1.0");

            var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var response = await noRedirectClient.SendAsync(request);
            _logger.Info($"[GOG] Download URL response: {(int)response.StatusCode} {response.StatusCode}");

            // If GOG returns a redirect, the Location header IS the CDN download URL
            if ((int)response.StatusCode >= 300 && (int)response.StatusCode < 400)
            {
                var location = response.Headers.Location?.ToString();
                _logger.Info($"[GOG] Redirect to: {location?.Substring(0, Math.Min(120, location?.Length ?? 0))}...");
                return location;
            }

            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[GOG] Failed to get download URL: {response.StatusCode}");
                return null;
            }

            // 200 response - expect JSON with downlink
            var content = await response.Content.ReadAsStringAsync();
            _logger.Info($"[GOG] Download URL response body: {content.Substring(0, Math.Min(200, content.Length))}...");

            try
            {
                var result = JsonSerializer.Deserialize<GogDownloadUrl>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                if (!string.IsNullOrEmpty(result?.Downlink))
                {
                    _logger.Info($"[GOG] Resolved downlink: {result.Downlink.Substring(0, Math.Min(120, result.Downlink.Length))}...");
                    return result.Downlink;
                }
            }
            catch (JsonException ex)
            {
                _logger.Error($"[GOG] Failed to parse download URL response as JSON: {ex.Message}");
            }

            _logger.Error("[GOG] Could not resolve download URL from response");
            return null;
        }

        private static void ExtractPlatformDownloads(JsonElement platformArray, string platformName, List<GogDownloadFile> downloads)
        {
            foreach (var item in platformArray.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Object)
                {
                    downloads.Add(ParseDownloadObject(item, platformName));
                }
                else if (item.ValueKind == JsonValueKind.Array)
                {
                    // Language-wrapped: ["English", {manualUrl, name, size}]
                    foreach (var subItem in item.EnumerateArray())
                    {
                        if (subItem.ValueKind == JsonValueKind.Object)
                            downloads.Add(ParseDownloadObject(subItem, platformName));
                    }
                }
            }
        }

        private static bool IsDownloadObject(JsonElement obj)
        {
            return obj.TryGetProperty("manualUrl", out _) || obj.TryGetProperty("manual_url", out _);
        }

        private static GogDownloadFile ParseDownloadObject(JsonElement obj, string platform)
        {
            // Handle both camelCase (manualUrl) and snake_case (manual_url)
            string? manualUrl = null;
            if (obj.TryGetProperty("manualUrl", out var urlEl))
                manualUrl = urlEl.GetString();
            else if (obj.TryGetProperty("manual_url", out urlEl))
                manualUrl = urlEl.GetString();

            string name = "Unknown";
            if (obj.TryGetProperty("name", out var nameEl) && nameEl.ValueKind == JsonValueKind.String)
                name = nameEl.GetString() ?? "Unknown";

            string? size = null;
            if (obj.TryGetProperty("size", out var sizeEl) && sizeEl.ValueKind == JsonValueKind.String)
                size = sizeEl.GetString();

            return new GogDownloadFile
            {
                Platform = platform,
                Name = name,
                Size = size,
                ManualUrl = manualUrl
            };
        }
    }

    public class GogTokenResponse
    {
        [JsonPropertyName("access_token")]
        public string AccessToken { get; set; } = string.Empty;
        
        [JsonPropertyName("refresh_token")]
        public string RefreshToken { get; set; } = string.Empty;
        
        [JsonPropertyName("expires_in")]
        public int ExpiresIn { get; set; }
        
        [JsonPropertyName("token_type")]
        public string TokenType { get; set; } = string.Empty;
        
        [JsonPropertyName("user_id")]
        public string? UserId { get; set; }
    }

    public class GogProductsResponse
    {
        [JsonPropertyName("totalPages")]
        public int TotalPages { get; set; }
        
        [JsonPropertyName("products")]
        public List<GogOwnedGame> Products { get; set; } = new();
    }

    public class GogOwnedGame
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("image")]
        public string? Image { get; set; }
        
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        
        [JsonPropertyName("category")]
        public string? Category { get; set; }
        
        [JsonPropertyName("rating")]
        public int? Rating { get; set; }
        
        [JsonPropertyName("isGame")]
        public bool IsGame { get; set; }

        public string? GetCoverUrl()
        {
            if (string.IsNullOrEmpty(Image)) return null;
            return $"https:{Image}_product_card_v2_mobile_slider_639.jpg";
        }

        public string? GetBackgroundUrl()
        {
            if (string.IsNullOrEmpty(Image)) return null;
            return $"https:{Image}_background_1920.jpg";
        }
    }

    public class GogGameDetails
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
        
        [JsonPropertyName("description")]
        public GogDescription? Description { get; set; }
        
        [JsonPropertyName("images")]
        public GogImages? Images { get; set; }
        
        [JsonPropertyName("release_date")]
        public string? ReleaseDate { get; set; }
        
        [JsonPropertyName("genres")]
        public List<GogGenre>? Genres { get; set; }
        
        [JsonPropertyName("developers")]
        public List<GogCompany>? Developers { get; set; }
        
        [JsonPropertyName("publishers")]
        public List<GogCompany>? Publishers { get; set; }
    }

    public class GogDescription
    {
        [JsonPropertyName("full")]
        public string? Full { get; set; }
        
        [JsonPropertyName("lead")]
        public string? Lead { get; set; }
    }

    public class GogImages
    {
        [JsonPropertyName("background")]
        public string? Background { get; set; }
        
        [JsonPropertyName("logo")]
        public string? Logo { get; set; }
        
        [JsonPropertyName("logo2x")]
        public string? Logo2x { get; set; }
        
        [JsonPropertyName("icon")]
        public string? Icon { get; set; }
        
        [JsonPropertyName("sidebarIcon")]
        public string? SidebarIcon { get; set; }
        
        [JsonPropertyName("sidebarIcon2x")]
        public string? SidebarIcon2x { get; set; }
    }

    public class GogGenre
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
        
        [JsonPropertyName("slug")]
        public string? Slug { get; set; }
    }

    public class GogCompany
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;
    }

    public class GogSearchResponse
    {
        [JsonPropertyName("products")]
        public List<GogSearchResult> Products { get; set; } = new();
        
        [JsonPropertyName("totalResults")]
        public int TotalResults { get; set; }
    }

    public class GogSearchResult
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("image")]
        public string? Image { get; set; }
        
        [JsonPropertyName("url")]
        public string? Url { get; set; }
        
        [JsonPropertyName("price")]
        public GogPrice? Price { get; set; }
        
        [JsonPropertyName("rating")]
        public int? Rating { get; set; }
        
        [JsonPropertyName("category")]
        public string? Category { get; set; }
    }

    public class GogPrice
    {
        [JsonPropertyName("amount")]
        public string? Amount { get; set; }
        
        [JsonPropertyName("baseAmount")]
        public string? BaseAmount { get; set; }
        
        [JsonPropertyName("isFree")]
        public bool IsFree { get; set; }
        
        [JsonPropertyName("isDiscounted")]
        public bool IsDiscounted { get; set; }
        
        [JsonPropertyName("discountPercentage")]
        public int? DiscountPercentage { get; set; }
    }

    public class GogAccountGameDetails
    {
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("downloads")]
        public JsonElement? Downloads { get; set; }
    }

    public class GogDownloadFile
    {
        public string Platform { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public string? Size { get; set; }
        public string? ManualUrl { get; set; }
    }

    public class GogDownloadUrl
    {
        [JsonPropertyName("downlink")]
        public string? Downlink { get; set; }
    }
}
