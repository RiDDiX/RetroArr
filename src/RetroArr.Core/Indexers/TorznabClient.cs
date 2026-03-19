using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Xml.Linq;
using RetroArr.Core.Prowlarr;

namespace RetroArr.Core.Indexers
{
    public class TorznabClient : IIndexerClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly HttpClient _httpClient;
        private readonly string _proxyUrl;
        private readonly string _apiKey;

        public TorznabClient(HttpClient httpClient, string proxyUrl, string apiKey)
        {
            _httpClient = httpClient;
            _proxyUrl = proxyUrl.TrimEnd('/');
            _apiKey = apiKey;
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int[]? categories = null)
        {
            try
            {
                var catString = categories != null && categories.Length > 0 ? string.Join(",", categories) : "";
                
                // Standard Torznab URL; XML response expected, JSON fallback supported.
                var url = $"{_proxyUrl}?t=search&q={Uri.EscapeDataString(query)}&cat={catString}&extended=1&apikey={_apiKey}";

                _logger.Info($"[TorznabClient] Requesting: {url}");
                var content = await _httpClient.GetStringAsync(url);
                
                if (content.TrimStart().StartsWith("<"))
                {
                    // XML (Standard Torznab)
                    return ParseXml(content);
                }
                else if (content.TrimStart().StartsWith("[") || content.TrimStart().StartsWith("{"))
                {
                    // JSON (Maybe Jackett direct?)
                    return ParseJson(content); 
                }
                else 
                {
                    _logger.Info("[TorznabClient] Unknown format.");
                    return new List<SearchResult>();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[TorznabClient] Error: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private List<SearchResult> ParseXml(string xmlContent)
        {
             XDocument doc = XDocument.Parse(xmlContent);
             XNamespace torznab = "http://torznab.com/schemas/2015/feed";
             var results = new List<SearchResult>();

             foreach (var item in doc.Descendants("item"))
             {
                 var result = new SearchResult
                 {
                     Title = item.Element("title")?.Value ?? "Unknown",
                     Guid = item.Element("guid")?.Value ?? Guid.NewGuid().ToString(),
                     Link = item.Element("link")?.Value ?? "",
                     PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.UtcNow,
                     Protocol = "torrent",
                     Provider = "Torrent"
                 };
                 
                 result.DownloadUrl = result.Link;

                 // Parse Torznab attributes
                 foreach (var attr in item.Elements(torznab + "attr"))
                 {
                     var name = attr.Attribute("name")?.Value;
                     var val = attr.Attribute("value")?.Value;
                     
                     if (string.Equals(name, "seeders", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var seeders)) result.Seeders = seeders;
                     if (string.Equals(name, "peers", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var peers)) result.PeersFromIndexer = peers; 
                     if (string.Equals(name, "leechers", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var leechers)) result.Leechers = leechers;
                     if (string.Equals(name, "size", StringComparison.OrdinalIgnoreCase) && long.TryParse(val, out var size)) result.Size = size;
                     if (string.Equals(name, "magneturl", StringComparison.OrdinalIgnoreCase)) result.MagnetUrl = val;
                     if (string.Equals(name, "category", StringComparison.OrdinalIgnoreCase) && int.TryParse(val, out var cid))
                        result.Categories.Add(new ProwlarrCategory { Id = cid, Name = cid.ToString() });
                 }

                 // Calculate leechers from total peers if missing
                 if (result.PeersFromIndexer.HasValue && (result.Leechers ?? 0) == 0)
                 {
                     result.Leechers = Math.Max(0, result.PeersFromIndexer.Value - (result.Seeders ?? 0));
                 }
                 
                 // Enclosure fallback for Magnet?
                 var enclosure = item.Element("enclosure");
                 if (enclosure != null)
                 {
                      var type = enclosure.Attribute("type")?.Value;
                      if (!string.IsNullOrEmpty(type) && type == "application/x-bittorrent")
                      {
                          if(long.TryParse(enclosure.Attribute("length")?.Value, out var len)) result.Size = len;
                          // Standard torments don't put magnet here usually, but check url
                      }
                 }

                 results.Add(result);
             }
             return results;
        }

        private List<SearchResult> ParseJson(string jsonContent)
        {
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            // Try list deserialization first, then { "item": [...] } wrapper fallback.
            
            try 
            {
                 return JsonSerializer.Deserialize<List<SearchResult>>(jsonContent, options) ?? new List<SearchResult>();
            }
            catch 
            {
                 return new List<SearchResult>(); 
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
             try
            {
                var response = await _httpClient.GetAsync($"{_proxyUrl}?t=caps&apikey={_apiKey}");
                return response.IsSuccessStatusCode;
            }
            catch { return false; }
        }
    }
}
