using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Xml.Linq;
using RetroArr.Core.Indexers;
using System.Diagnostics.CodeAnalysis;
using System.Buffers;

namespace RetroArr.Core.Prowlarr
{
    [SuppressMessage("Microsoft.Globalization", "CA1307:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Globalization", "CA1310:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    [SuppressMessage("Microsoft.Performance", "CA1866:UseCharOverload")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class ProwlarrClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly HttpClient _httpClient;
        private readonly string _apiKey;
        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public ProwlarrClient(string baseUrl, string apiKey)
        {
            _httpClient = new HttpClient 
            { 
                BaseAddress = new Uri(baseUrl),
                Timeout = TimeSpan.FromSeconds(120) // Increased timeout for slow indexers
            };
            _apiKey = apiKey;
        }

        public async Task<List<ProwlarrIndexer>> GetIndexersAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/indexer");
            request.Headers.Add("X-Api-Key", _apiKey);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<List<ProwlarrIndexer>>(content, _jsonOptions) ?? new List<ProwlarrIndexer>();
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int[]? indexerIds = null, int[]? categories = null)
        {
            // NOTE: We intentionally DON'T filter by categories in the API call
            // Many indexers don't properly tag game categories, causing empty results
            // Instead, we search all and let the frontend filter by keywords/extensions
            // Categories are kept for potential future use but not applied to query
            
            _logger.Info($"[Prowlarr] SearchAsync called with query='{query}', categories={string.Join(",", categories ?? Array.Empty<int>())}");
            _logger.Info($"[Prowlarr] NOTE: Category filtering disabled - searching all categories for better results");

            var indexerQuery = indexerIds != null && indexerIds.Length > 0 
                ? "&" + string.Join("&", indexerIds.Select(id => $"indexerIds={id}")) 
                : "";
                
            var fullUrl = $"/api/v1/search?query={Uri.EscapeDataString(query)}&limit=100&offset=0{indexerQuery}";
            
            _logger.Info($"[Prowlarr] Search URL: {fullUrl}");
            
            using var request = new HttpRequestMessage(HttpMethod.Get, fullUrl);
            request.Headers.Add("X-Api-Key", _apiKey);

            _logger.Info($"[Prowlarr] Sending request to Prowlarr (timeout: 120s)...");
            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            
            HttpResponseMessage response;
            try 
            {
                response = await _httpClient.SendAsync(request);
                stopwatch.Stop();
                _logger.Info($"[Prowlarr] Response Status: {response.StatusCode} (took {stopwatch.ElapsedMilliseconds}ms)");
            }
            catch (TaskCanceledException ex) when (ex.InnerException is TimeoutException || ex.CancellationToken.IsCancellationRequested == false)
            {
                stopwatch.Stop();
                _logger.Info($"[Prowlarr] Request timed out after {stopwatch.ElapsedMilliseconds}ms");
                throw new TimeoutException($"Prowlarr search timed out after {stopwatch.ElapsedMilliseconds}ms. Check if Prowlarr is running and accessible at {_httpClient.BaseAddress}", ex);
            }
            catch (HttpRequestException ex)
            {
                stopwatch.Stop();
                _logger.Error($"[Prowlarr] HTTP error after {stopwatch.ElapsedMilliseconds}ms: {ex.Message}");
                throw new Exception($"Failed to connect to Prowlarr at {_httpClient.BaseAddress}: {ex.Message}", ex);
            }
            
            response.EnsureSuccessStatusCode();

            var content = await response.Content.ReadAsStringAsync();
            
            // Always log response preview for debugging
            var preview = content.Length > 500 ? content.Substring(0, 500) + "..." : content;
            _logger.Info($"[Prowlarr] Raw Content Length: {content.Length}");
            _logger.Info($"[Prowlarr] Response Preview: {preview}");

            try 
            {
                var results = new List<SearchResult>();
                var trimmed = content.TrimStart();

                // Detect XML (RSS/Newznab)
                if (trimmed.StartsWith("<"))
                {
                    _logger.Info("[Prowlarr] Detected XML response. Parsing as RSS/Newznab...");
                    var doc = XDocument.Parse(content);
                    XNamespace newznab = "http://www.newznab.com/DTD/2010/feeds/attributes/";

                    // Find all 'item' elements in 'channel'
                    var items = doc.Descendants("item");

                    foreach (var item in items)
                    {
                        var result = new SearchResult();
                        result.Title = item.Element("title")?.Value ?? "Unknown Title";
                        result.Guid = item.Element("guid")?.Value ?? Guid.NewGuid().ToString();
                        result.Link = item.Element("link")?.Value ?? string.Empty; // Use Link property for internal mapping
                        result.DownloadUrl = result.Link; // Map <link> to DownloadUrl per user requirement
                        result.InfoUrl = item.Element("comments")?.Value ?? result.Guid;
                        result.PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.UtcNow;
                        result.Provider = "Prowlarr"; // Will be overridden by indexer name if found
                        
                        // Parse enclosure for size and protocol
                        var enclosure = item.Element("enclosure");
                        if (enclosure != null)
                        {
                            var lengthStr = enclosure.Attribute("length")?.Value;
                            if (long.TryParse(lengthStr, out var length))
                            {
                                result.Size = length;
                            }
                            
                            var type = enclosure.Attribute("type")?.Value;
                            if (!string.IsNullOrEmpty(type) && type.Equals("application/x-nzb", StringComparison.OrdinalIgnoreCase))
                            {
                                result.Protocol = "nzb";
                            }
                        }

                        // Parse newznab attributes for category and indexer
                        // <newznab:attr name="category" value="4050" />
                        var attrs = item.Elements(newznab + "attr");
                        foreach (var attr in attrs)
                        {
                            var name = attr.Attribute("name")?.Value;
                            var value = attr.Attribute("value")?.Value;

                            if (name == "category" && int.TryParse(value, out var catId))
                            {
                                result.Categories.Add(new ProwlarrCategory { Id = catId, Name = catId.ToString() });
                            }
                            else if (name == "indexer") // Some indexers might provide this?
                            {
                                // result.IndexerName = value; // Typically generic
                            }
                        }
                        
                        // Fallback protocol detection if not set by enclosure
                        if (result.Protocol == "torrent") // Default
                        {
                             if (result.Title.Contains("nzb", StringComparison.OrdinalIgnoreCase) || 
                                 result.DownloadUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
                             {
                                 result.Protocol = "nzb";
                             }
                        }

                        results.Add(result);
                    }
                     _logger.Info($"[Prowlarr] Parsed {results.Count} items from XML.");
                     return results;
                }
                
                List<SearchResult> resultsJson;

                // Handle wrapped JSON object format (newer Prowlarr versions may use pagination)
                if (trimmed.StartsWith("{"))
                {
                    _logger.Info("[Prowlarr] Detected JSON object response (possible pagination wrapper)");
                    using var doc = JsonDocument.Parse(content);
                    var root = doc.RootElement;
                    
                    // Log all top-level property names for debugging
                    var propNames = string.Join(", ", root.EnumerateObject().Select(p => p.Name));
                    _logger.Info($"[Prowlarr] JSON object properties: {propNames}");

                    // Try common wrapper fields: "records", "results", "releases"
                    JsonElement? arrayElement = null;
                    foreach (var fieldName in new[] { "records", "results", "releases" })
                    {
                        if (root.TryGetProperty(fieldName, out var elem) && elem.ValueKind == JsonValueKind.Array)
                        {
                            arrayElement = elem;
                            _logger.Info($"[Prowlarr] Found '{fieldName}' array with {elem.GetArrayLength()} items");
                            break;
                        }
                    }

                    if (arrayElement.HasValue)
                    {
                        resultsJson = JsonSerializer.Deserialize<List<SearchResult>>(arrayElement.Value.GetRawText(), _jsonOptions) ?? new List<SearchResult>();
                    }
                    else
                    {
                        _logger.Warn("[Prowlarr] WARNING: JSON object has no recognized array field, treating as empty");
                        resultsJson = new List<SearchResult>();
                    }
                }
                else if (trimmed.StartsWith("["))
                {
                    _logger.Info("[Prowlarr] Detected JSON array response");
                    if (content.Length > 2)
                    {
                        int firstBrace = content.IndexOf('{');
                        int endBrace = content.IndexOf("},", StringComparison.Ordinal);
                        if (firstBrace >= 0 && endBrace > firstBrace)
                        {
                            _logger.Info($"[Prowlarr] First Object Raw: {content.Substring(firstBrace, Math.Min(endBrace - firstBrace + 1, 500))}");
                        }
                    }
                    resultsJson = JsonSerializer.Deserialize<List<SearchResult>>(content, _jsonOptions) ?? new List<SearchResult>();
                }
                else
                {
                    _logger.Warn($"[Prowlarr] WARNING: Unexpected response format, starts with: '{trimmed.Substring(0, Math.Min(20, trimmed.Length))}'");
                    resultsJson = new List<SearchResult>();
                }
                
                // Debug: Log deserialization success and first item fields
                _logger.Info($"[Prowlarr] Deserialized {resultsJson.Count} results.");
                
                foreach (var result in resultsJson)
                {
                    result.Provider = "Prowlarr";
                    
                    // Improved Protocol Detection
                    if (result.Protocol == "torrent") 
                    {
                        bool isNzb = false;
                        
                        // Check DownloadUrl for .nzb
                        if (!string.IsNullOrEmpty(result.DownloadUrl) && result.DownloadUrl.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
                        {
                            isNzb = true;
                        }
                        // Check Indexer Name for "nzb"
                        else if (!string.IsNullOrEmpty(result.IndexerName) && result.IndexerName.Contains("nzb", StringComparison.OrdinalIgnoreCase))
                        {
                            isNzb = true;
                        }
                        // Check GUID for .nzb
                        else if (!string.IsNullOrEmpty(result.Guid) && result.Guid.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
                        {
                            isNzb = true;
                        }

                        if (isNzb)
                        {
                            result.Protocol = "nzb";
                        }
                    }
                }

                return resultsJson;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Prowlarr] JSON/XML Error: {ex.Message}");
                _logger.Info($"[Prowlarr] Stack: {ex.StackTrace}");
                throw new Exception($"Failed to parse Prowlarr response: {ex.Message}", ex);
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/health");
                request.Headers.Add("X-Api-Key", _apiKey);

                var response = await _httpClient.SendAsync(request);
                return response.IsSuccessStatusCode;
            }
            catch
            {
                return false;
            }
        }

        public async Task<int> GetIndexerCountAsync()
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, "/api/v1/indexer");
                request.Headers.Add("X-Api-Key", _apiKey);

                var response = await _httpClient.SendAsync(request);
                if (!response.IsSuccessStatusCode) return 0;

                var content = await response.Content.ReadAsStringAsync();
                var indexers = JsonSerializer.Deserialize<List<ProwlarrIndexer>>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return indexers?.Count(i => i.Enable) ?? 0;
            }
            catch
            {
                return 0;
            }
        }
    }

    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    public class ProwlarrIndexer
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public bool Enable { get; set; }
        public string Protocol { get; set; } = string.Empty;
    }

    // Enhanced SearchResult based on Radarr's ReleaseResource and actual Prowlarr API
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1819:PropertiesShouldNotReturnArrays")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    [SuppressMessage("Microsoft.Naming", "CA1720:IdentifiersShouldNotContainTypeNames")]
    public class SearchResult
    {
        // Basic info
        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;
        
        [JsonPropertyName("guid")]
        public string Guid { get; set; } = string.Empty;
        
        [JsonPropertyName("downloadUrl")]
        public string DownloadUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("magnetUrl")] 
        public string MagnetUrl { get; set; } = string.Empty;
        
        // Helper property for XML parsing
        [JsonIgnore]
        public string Link { get; set; } = string.Empty;

        [JsonPropertyName("infoUrl")]
        public string InfoUrl { get; set; } = string.Empty;
        
        [JsonPropertyName("indexerId")]
        public int IndexerId { get; set; }
        
        [JsonPropertyName("indexer")]
        public string IndexerName { get; set; } = string.Empty;
        
        [JsonPropertyName("indexerFlags")]
        public string[] IndexerFlags { get; set; } = Array.Empty<string>();
        
        [JsonPropertyName("grabs")]
        public int? Grabs { get; set; }
        
        [JsonPropertyName("size")]
        public long Size { get; set; }
        
        [JsonPropertyName("seeders")]
        public int? Seeders { get; set; }
        
        [JsonPropertyName("leechers")] 
        public int? Leechers { get; set; }
        
        [JsonPropertyName("peers")]
        public int? PeersFromIndexer { get; set; }
        
        [JsonIgnore]
        public int EffectiveSeeders => Seeders ?? 0;
        
        [JsonIgnore]
        public int EffectiveLeechers => Leechers ?? (PeersFromIndexer.HasValue && Seeders.HasValue ? Math.Max(0, PeersFromIndexer.Value - Seeders.Value) : 0);
        
        [JsonIgnore]
        public int TotalPeers => PeersFromIndexer ?? (EffectiveSeeders + EffectiveLeechers);
        
        [JsonPropertyName("publishDate")]
        public DateTime PublishDate { get; set; }

        public string Provider { get; set; } = string.Empty;
        
        [JsonPropertyName("publishedAt")]
        public DateTime? PublishedAt { get; set; }
        
        [JsonPropertyName("pubDate")]
        public DateTime? PubDate { get; set; }
        
        public DateTime EffectivePublishDate => 
            PublishDate != default(DateTime) ? PublishDate :
            PublishedAt ?? PubDate ?? DateTime.UtcNow.AddDays(-1);
        
        public int Age => CalculateAge(EffectivePublishDate);
        public double AgeHours => (DateTime.UtcNow - EffectivePublishDate).TotalHours;
        public double AgeMinutes => (DateTime.UtcNow - EffectivePublishDate).TotalMinutes;
        
        [JsonPropertyName("categories")]
        public List<ProwlarrCategory> Categories { get; set; } = new List<ProwlarrCategory>();
        
        public string Category => Categories?.Count > 0 
            ? string.Join(", ", Categories.Select(c => 
                c.SubCategories?.Count > 0 
                    ? c.SubCategories.First().Name ?? c.Name 
                    : c.Name)) 
            : string.Empty;
        
        [JsonPropertyName("protocol")]
        [JsonConverter(typeof(ProtocolJsonConverter))]
        public string Protocol { get; set; } = "torrent";
        
        /// <summary>
        /// Detected or manually set platform for the release
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? DetectedPlatform { get; set; }
        
        /// <summary>
        /// Platform folder name for path resolution (e.g., "switch", "ps4", "windows")
        /// </summary>
        [System.Text.Json.Serialization.JsonIgnore]
        public string? PlatformFolder { get; set; }
        
        [JsonPropertyName("languages")]
        public string[] Languages { get; set; } = Array.Empty<string>();
        
        [JsonPropertyName("quality")]
        public string Quality { get; set; } = string.Empty;
        
        [JsonPropertyName("releaseGroup")]
        public string ReleaseGroup { get; set; } = string.Empty;
        
        [JsonPropertyName("source")]
        public string Source { get; set; } = string.Empty;
        
        [JsonPropertyName("container")]
        public string Container { get; set; } = string.Empty;
        
        [JsonPropertyName("codec")]
        public string Codec { get; set; } = string.Empty;
        
        [JsonPropertyName("resolution")]
        public string Resolution { get; set; } = string.Empty;

        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
        public string FormattedSize => FormatBytes(Size);
        public string FormattedAge => FormatAge();
        
        private static int CalculateAge(DateTime publishDate)
        {
            if (publishDate == DateTime.MinValue || publishDate == default(DateTime))
                return 0;
                
            var age = (int)(DateTime.UtcNow - publishDate).TotalDays;
            return Math.Max(0, age);
        }
        
        [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
        private static string FormatBytes(long bytes)
        {
            if (bytes == 0) return "0 B";
            
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int counter = 0;
            decimal number = bytes;
            
            while (Math.Round(number / 1024) >= 1)
            {
                number = number / 1024;
                counter++;
            }
            
            return string.Format("{0:n1} {1}", number, suffixes[counter]);
        }
        
        private string FormatAge()
        {
            var publishDate = EffectivePublishDate;
            
            if (publishDate == DateTime.MinValue || publishDate == default(DateTime))
                return "Unknown";
                
            var timeSpan = DateTime.UtcNow.Subtract(publishDate);
            
            if (timeSpan.TotalDays < 0)
                return "Unknown";
            
            if (timeSpan.TotalDays >= 365)
                return $"{(int)(timeSpan.TotalDays / 365)}y";
            if (timeSpan.TotalDays >= 1)
                return $"{(int)timeSpan.TotalDays}d";
            if (timeSpan.TotalHours >= 1)
                return $"{(int)timeSpan.TotalHours}h";
            return $"{(int)timeSpan.TotalMinutes}m";
        }
    }

    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    public class ProwlarrCategory
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; } = string.Empty;

        [JsonPropertyName("subCategories")]
        public List<ProwlarrCategory>? SubCategories { get; set; }
    }

    /// <summary>
    /// Handles Prowlarr returning protocol as string ("torrent"/"usenet"),
    /// PascalCase enum name ("Torrent"/"Usenet"), or integer enum value (1=usenet, 2=torrent).
    /// </summary>
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    public class ProtocolJsonConverter : JsonConverter<string>
    {
        public override string Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString() ?? "torrent";
                return value.ToLowerInvariant() switch
                {
                    "usenet" => "usenet",
                    "torrent" => "torrent",
                    "unknown" => "unknown",
                    _ => value.ToLowerInvariant()
                };
            }
            
            if (reader.TokenType == JsonTokenType.Number)
            {
                var intValue = reader.GetInt32();
                return intValue switch
                {
                    1 => "usenet",
                    2 => "torrent",
                    _ => "torrent"
                };
            }
            
            return "torrent";
        }

        public override void Write(Utf8JsonWriter writer, string value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value);
        }
    }
}
