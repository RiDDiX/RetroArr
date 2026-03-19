using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Web;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Jackett
{
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Globalization", "CA1303:DoNotPassLiteralsAsLocalizedParameters")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1867:UseCharOverload")]
    public class JackettClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private readonly string _baseUrl;

        public JackettClient(string baseUrl, string apiKey)
        {
            _baseUrl = baseUrl.TrimEnd('/');
            _apiKey = apiKey;
            _httpClient = new HttpClient();
        }

        public async Task<List<JackettResult>> SearchAsync(string query, int[]? categories = null)
        {
            // Jackett API: /api/v2.0/indexers/all/results?apikey=...&Query=...&Category=...
            var uriBuilder = new UriBuilder($"{_baseUrl}/api/v2.0/indexers/all/results");
            var queryParams = HttpUtility.ParseQueryString(uriBuilder.Query);
            
            queryParams["apikey"] = _apiKey;
            queryParams["Query"] = query;
            
            if (categories != null && categories.Length > 0)
            {
                // Jackett accepts comma separated categories
                queryParams["Category"] = string.Join(",", categories);
            }

            uriBuilder.Query = queryParams.ToString();
            
            try
            {
                var response = await _httpClient.GetAsync(uriBuilder.Uri);
                response.EnsureSuccessStatusCode();

                var content = await response.Content.ReadAsStringAsync();
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var root = JsonSerializer.Deserialize<JackettSearchResponse>(content, options);

                return root?.Results ?? new List<JackettResult>();
            }
            catch (Exception ex)
            {
                _logger.Error($"Jackett Search Error: {ex.Message}");
                throw new Exception($"Jackett search failed: {ex.Message}", ex);
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try 
            {
                // Simple health check or try to get configured indexers
                var url = $"{_baseUrl}/api/v2.0/indexers/all/results?apikey={_apiKey}&t=caps"; 
                // t=caps usually returns capabilities xml/json, lightweight check
                
                var response = await _httpClient.GetAsync(url);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class JackettSearchResponse
    {
        [JsonPropertyName("Results")]
        public List<JackettResult> Results { get; set; } = new List<JackettResult>();
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class JackettResult
    {
        public string Title { get; set; } = string.Empty;
        public string Guid { get; set; } = string.Empty; // Often a URL or PermaLink
        public string Link { get; set; } = string.Empty; // .torrent download link or magnet
        
        [JsonPropertyName("MagnetUri")]
        public string MagnetUri { get; set; } = string.Empty;
        
        public string Tracker { get; set; } = string.Empty;
        public long Size { get; set; }
        public DateTime PublishDate { get; set; }
        
        public List<int> Category { get; set; } = new List<int>();
        
        public int Seeders { get; set; }
        public int Peers { get; set; }
        
        public int Leechers => Math.Max(0, Peers - Seeders);
        
        // Helper to get best download link
        public string DownloadUrl => !string.IsNullOrEmpty(MagnetUri) ? MagnetUri : Link;
        public string Protocol
        {
            get
            {
                if (!string.IsNullOrEmpty(Link) && Link.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
                    return "usenet";
                if (!string.IsNullOrEmpty(MagnetUri) || (!string.IsNullOrEmpty(Link) && Link.EndsWith(".torrent", StringComparison.OrdinalIgnoreCase)))
                    return "torrent";
                // Check categories: 5000-5999 = TV, 7000-7999 = Other — not reliable for protocol
                // Check tracker name for NZB indicators
                if (Tracker.Contains("nzb", StringComparison.OrdinalIgnoreCase))
                    return "usenet";
                return "torrent";
            }
        }
    }
}
