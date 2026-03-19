using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Xml.Linq;
using RetroArr.Core.Prowlarr; // Using existing models

namespace RetroArr.Core.Indexers
{
    public class NewznabClient : IIndexerClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly HttpClient _httpClient;
        private readonly string _proxyUrl;
        private readonly string _apiKey;

        public NewznabClient(HttpClient httpClient, string proxyUrl, string apiKey)
        {
            _httpClient = httpClient;
            _proxyUrl = proxyUrl.TrimEnd('/');
            _apiKey = apiKey;
        }

        public async Task<List<SearchResult>> SearchAsync(string query, int[]? categories = null)
        {
            try
            {
                // Construct Newznab URL: ?t=search&q=query&cat=1000,2000&apikey=...
                var catString = categories != null && categories.Length > 0 
                    ? string.Join(",", categories) // Newznab standard uses comma ?t=search&cat=1000,2000
                    : "";

                // Standard Newznab uses comma-separated categories.
                // Category expansion (subcats) included as comma-separated IDs.
                
                var expandedCats = new HashSet<int>();
                if (categories != null) 
                {
                    foreach(var c in categories) 
                    {
                        expandedCats.Add(c);
                        if(c == 4000) { expandedCats.Add(4050); expandedCats.Add(4030); expandedCats.Add(4040); }
                        if(c == 1000) { 
                            expandedCats.Add(1010); expandedCats.Add(1020); expandedCats.Add(1030); 
                            expandedCats.Add(1040); expandedCats.Add(1050); expandedCats.Add(1060);
                            expandedCats.Add(1070); expandedCats.Add(1080); expandedCats.Add(1110);
                            expandedCats.Add(1140); expandedCats.Add(1180);
                        }
                    }
                }
                
                var catParam = expandedCats.Count > 0 ? $"&cat={string.Join(",", expandedCats)}" : "";
                
                // Use 'search' function (or 'tvsearch'/'movie' if we had specific types, but 'search' is generic)
                // Use 'extended=1' to get more attributes
                var url = $"{_proxyUrl}?t=search&q={Uri.EscapeDataString(query)}{catParam}&extended=1&apikey={_apiKey}";
                
                _logger.Info($"[NewznabClient] Requesting: {url}");
                
                var response = await _httpClient.GetStringAsync(url);
                
                // Parse XML
                XDocument doc = XDocument.Parse(response);
                XNamespace newznab = "http://www.newznab.com/DTD/2010/feeds/attributes/";

                var results = new List<SearchResult>();

                foreach (var item in doc.Descendants("item"))
                {
                    var result = new SearchResult
                    {
                        Title = item.Element("title")?.Value ?? "Unknown",
                        Guid = item.Element("guid")?.Value ?? Guid.NewGuid().ToString(),
                        Link = item.Element("link")?.Value ?? "",
                        PublishDate = DateTime.TryParse(item.Element("pubDate")?.Value, out var date) ? date : DateTime.UtcNow,
                        Provider = "Usenet", // Placeholder, ideally get from indexer title
                        Protocol = "usenet"
                    };

                    result.DownloadUrl = result.Link;
                    result.InfoUrl = item.Element("comments")?.Value ?? result.Guid;

                    // Enclosure parsing
                    var enclosure = item.Element("enclosure");
                    if (enclosure != null)
                    {
                        if (long.TryParse(enclosure.Attribute("length")?.Value, out var size))
                            result.Size = size;
                        
                        var type = enclosure.Attribute("type")?.Value;
                        if (!string.IsNullOrEmpty(type) && type.Equals("application/x-nzb", StringComparison.OrdinalIgnoreCase))
                        {
                            result.Protocol = "nzb";
                        }
                    }

                    // Attributes parsing
                    foreach (var attr in item.Elements(newznab + "attr"))
                    {
                        var name = attr.Attribute("name")?.Value;
                        var val = attr.Attribute("value")?.Value;
                        
                        if (name == "category" && int.TryParse(val, out var cid))
                        {
                            result.Categories.Add(new ProwlarrCategory { Id = cid, Name = cid.ToString() });
                        }
                        else if (name == "size" && result.Size == 0 && long.TryParse(val, out var s))
                        {
                            result.Size = s;
                        }
                        else if (name == "guid")
                        {
                             // sometimes guid is here
                        }
                    }

                    results.Add(result);
                }

                _logger.Info($"[NewznabClient] Found {results.Count} results.");
                return results;

            }
            catch (Exception ex)
            {
                _logger.Error($"[NewznabClient] Error: {ex.Message}");
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
