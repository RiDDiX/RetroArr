using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroArr.Core.MetadataSource.Epic
{
    public enum EpicMetadataStatus
    {
        Ok,
        Empty,
        NetworkError,
        Unconfigured
    }

    // anonymous Store GraphQL client, no login required
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    public class EpicMetadataClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly HttpClient _httpClient;

        private const string GraphqlEndpoint = "https://www.epicgames.com/graphql";

        private const string SearchStoreQuery =
            "query searchStoreQuery($keywords: String, $country: String!, $locale: String, $count: Int, $start: Int) " +
            "{ Catalog { searchStore(keywords: $keywords country: $country locale: $locale count: $count start: $start) " +
            "{ elements { title id namespace description seller { name } developer urlSlug effectiveDate keyImages { type url } categories { path } } paging { count total } } } }";

        public EpicMetadataClient(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        // Akamai blocks requests without a real browser UA
        private const string DefaultUserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/124.0.0.0 Safari/537.36";

        internal static EpicMetadataStatus ClassifyResponse(HttpStatusCode httpCode, string? body)
        {
            if ((int)httpCode >= 500) return EpicMetadataStatus.NetworkError;
            if (httpCode != HttpStatusCode.OK) return EpicMetadataStatus.NetworkError;

            var raw = body ?? string.Empty;
            var trimmed = raw.TrimStart();
            if (trimmed.Length < 2 || trimmed[0] != '{') return EpicMetadataStatus.NetworkError;

            try
            {
                using var doc = JsonDocument.Parse(raw);
                if (doc.RootElement.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Array && errors.GetArrayLength() > 0)
                {
                    return EpicMetadataStatus.NetworkError;
                }
                return EpicMetadataStatus.Ok;
            }
            catch (JsonException)
            {
                return EpicMetadataStatus.NetworkError;
            }
        }

        public async Task<(EpicMetadataStatus Status, List<EpicStoreElement> Elements)> SearchAsync(string query, string locale = "en-US", string country = "US", int count = 10)
        {
            if (string.IsNullOrWhiteSpace(query))
                return (EpicMetadataStatus.Empty, new List<EpicStoreElement>());

            try
            {
                var payload = new
                {
                    operationName = "searchStoreQuery",
                    query = SearchStoreQuery,
                    variables = new
                    {
                        keywords = query,
                        country,
                        locale,
                        count,
                        start = 0
                    }
                };
                var json = JsonSerializer.Serialize(payload);
                using var req = new HttpRequestMessage(HttpMethod.Post, GraphqlEndpoint)
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json")
                };
                req.Headers.UserAgent.ParseAdd(DefaultUserAgent);
                req.Headers.Accept.ParseAdd("application/json");
                req.Headers.Referrer = new Uri("https://www.epicgames.com/store/");
                req.Headers.TryAddWithoutValidation("Origin", "https://www.epicgames.com");
                req.Headers.ExpectContinue = false;

                using var resp = await _httpClient.SendAsync(req);
                var body = await resp.Content.ReadAsStringAsync();

                var status = ClassifyResponse(resp.StatusCode, body);
                if (status != EpicMetadataStatus.Ok)
                {
                    var snippet = body.Length > 200 ? body.Substring(0, 200) : body;
                    _logger.Warn($"[EpicStore] search '{query}' status={status} http={(int)resp.StatusCode}. Body[0..200]: {snippet}");
                    return (status, new List<EpicStoreElement>());
                }

                var parsed = JsonSerializer.Deserialize<EpicGraphqlResponse>(body, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                var elements = parsed?.Data?.Catalog?.SearchStore?.Elements ?? new List<EpicStoreElement>();
                // base games only, drop addons/dlc/digitalextras
                elements = elements.Where(e => e.LooksLikeGame()).ToList();
                return (elements.Count > 0 ? EpicMetadataStatus.Ok : EpicMetadataStatus.Empty, elements);
            }
            catch (Exception ex)
            {
                _logger.Error($"[EpicStore] search '{query}' exception: {ex.GetType().Name}: {ex.Message}");
                return (EpicMetadataStatus.NetworkError, new List<EpicStoreElement>());
            }
        }
    }

    #region DTOs

    public class EpicGraphqlResponse
    {
        [JsonPropertyName("data")] public EpicGraphqlData? Data { get; set; }
    }

    public class EpicGraphqlData
    {
        [JsonPropertyName("Catalog")] public EpicGraphqlCatalog? Catalog { get; set; }
    }

    public class EpicGraphqlCatalog
    {
        [JsonPropertyName("searchStore")] public EpicSearchStoreResult? SearchStore { get; set; }
    }

    public class EpicSearchStoreResult
    {
        [JsonPropertyName("elements")] public List<EpicStoreElement>? Elements { get; set; }
    }

    public class EpicStoreElement
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("id")] public string? Id { get; set; }
        [JsonPropertyName("namespace")] public string? Namespace { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("seller")] public EpicSeller? Seller { get; set; }
        [JsonPropertyName("developer")] public string? Developer { get; set; }
        [JsonPropertyName("urlSlug")] public string? UrlSlug { get; set; }
        [JsonPropertyName("effectiveDate")] public string? EffectiveDate { get; set; }
        [JsonPropertyName("keyImages")] public List<EpicKeyImage>? KeyImages { get; set; }
        [JsonPropertyName("categories")] public List<EpicCategory>? Categories { get; set; }

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
            if (string.IsNullOrEmpty(EffectiveDate) || EffectiveDate.Length < 4) return null;
            return int.TryParse(EffectiveDate.Substring(0, 4), out var y) ? y : null;
        }

        public bool LooksLikeGame()
        {
            if (Categories == null || Categories.Count == 0) return true;
            var paths = Categories.Select(c => c.Path?.ToLowerInvariant() ?? string.Empty).ToList();
            if (paths.Any(p => p == "addons" || p.StartsWith("addons/"))) return false;
            if (paths.Any(p => p == "digitalextras" || p.StartsWith("digitalextras/"))) return false;
            if (paths.Any(p => p == "soundtrack" || p.StartsWith("soundtrack/"))) return false;
            return true;
        }
    }

    public class EpicSeller
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
    }

    #endregion
}
