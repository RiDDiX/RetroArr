using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Linq;
using RetroArr.Core.Prowlarr; // Reuse SearchResult model

namespace RetroArr.Core.Indexers
{
    public class HydraIndexer
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ReleaseSearch);
        private readonly HttpClient _httpClient;
        private readonly string _name;
        private readonly string _url;

        public HydraIndexer(HttpClient httpClient, string name, string url)
        {
            _httpClient = httpClient;
            _name = name;
            _url = url;
        }

        public async Task<List<SearchResult>> SearchAsync(string query)
        {
            try
            {
               _logger.Info($"[Hydra] Fetching source: {_url}");
               var json = await _httpClient.GetStringAsync(_url);
               
               // Hydra format: "downloads": [ { "title": "...", "uris": ["magnet:..."], "fileSize": "..." } ]
               // Or simple list of objects.
               // Based on previous analysis of https://hydralinks.pages.dev/sources/fitgirl.json:
               // { "downloads": [ ... ] }

               using (JsonDocument doc = JsonDocument.Parse(json))
               {
                   var root = doc.RootElement;
                   if (root.TryGetProperty("downloads", out var downloads))
                   {
                       return ParseDownloads(downloads, query);
                   }
                   else if (root.ValueKind == JsonValueKind.Array)
                   {
                       return ParseDownloads(root, query);
                   }
               }
               
               return new List<SearchResult>();
            }
            catch (Exception ex)
            {
                _logger.Error($"[Hydra] Error searching {_name}: {ex.Message}");
                return new List<SearchResult>();
            }
        }

        private List<SearchResult> ParseDownloads(JsonElement items, string query)
        {
            var results = new List<SearchResult>();
            var normalizedQuery = query.ToLowerInvariant();

            foreach (var item in items.EnumerateArray())
            {
                var title = item.TryGetProperty("title", out var t) ? t.GetString() : "Unknown";
                if (string.IsNullOrWhiteSpace(title)) continue;

                // Client-side filtering because JSON sources are static lists
                if (!title.ToLowerInvariant().Contains(normalizedQuery)) continue;

                var uris = item.TryGetProperty("uris", out var u) ? u : default;
                var uploadDate = item.TryGetProperty("uploadDate", out var d) ? d.GetString() : null;
                var fileSize = item.TryGetProperty("fileSize", out var f) ? f.GetString() : null;
                
                string magnet = "";
                string downloadUrl = "";

                if (uris.ValueKind == JsonValueKind.Array)
                {
                    foreach (var uri in uris.EnumerateArray())
                    {
                        var uriStr = uri.GetString();
                        if (string.IsNullOrEmpty(uriStr)) continue;

                        if (uriStr.StartsWith("magnet:")) magnet = uriStr;
                        else if (uriStr.StartsWith("http")) downloadUrl = uriStr;
                    }
                }

                // Parse size string (e.g. "5.4 GB")
                long sizeBytes = ParseSize(fileSize);

                results.Add(new SearchResult
                {
                    Title = title,
                    Guid = magnet ?? downloadUrl ?? Guid.NewGuid().ToString(),
                    Size = sizeBytes,
                    IndexerName = _name,
                    Protocol = !string.IsNullOrEmpty(magnet) ? "torrent" : "http",
                    PublishDate = DateTime.TryParse(uploadDate, out var date) ? date : DateTime.UtcNow,
                    MagnetUrl = magnet,
                    DownloadUrl = downloadUrl ?? magnet,
                    InfoUrl = _url,
                    Provider = "Hydra"
                });
            }

            return results;
        }

        private long ParseSize(string sizeStr)
        {
            if (string.IsNullOrWhiteSpace(sizeStr)) return 0;
            sizeStr = sizeStr.ToUpperInvariant();
            
            double multiplier = 1;
            if (sizeStr.Contains("GB")) multiplier = 1024 * 1024 * 1024;
            else if (sizeStr.Contains("MB")) multiplier = 1024 * 1024;
            else if (sizeStr.Contains("KB")) multiplier = 1024;

            var numberPart = new string(sizeStr.Where(c => char.IsDigit(c) || c == '.').ToArray());
            if (double.TryParse(numberPart, out var sizeVal))
            {
                return (long)(sizeVal * multiplier);
            }
            return 0;
        }
    }
}
