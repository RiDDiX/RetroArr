using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1867:UseCharOverload")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    public class SabnzbdClient : IDownloadClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadClient);
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _apiKey;

        public SabnzbdClient(string host, int port, string apiKey, string? urlBase = null)
        {
            _httpClient = new HttpClient();
            
            // Handle host formatting
            string cleanHost = host.Trim();
            if (!cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !cleanHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = $"http://{cleanHost}";
            }
            cleanHost = cleanHost.TrimEnd('/');
            
            // Process UrlBase
            string finalUrlBase = "";
            if (!string.IsNullOrWhiteSpace(urlBase))
            {
                finalUrlBase = urlBase.Trim();
                if (!finalUrlBase.StartsWith("/", StringComparison.OrdinalIgnoreCase)) finalUrlBase = "/" + finalUrlBase;
                finalUrlBase = finalUrlBase.TrimEnd('/');
            }
            
            // SABnzbd API endpoint
            _baseUrl = $"{cleanHost}:{port}{finalUrlBase}/api";
            _apiKey = apiKey;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                // Simple version check to test connection
                var version = await GetVersionAsync();
                return !string.IsNullOrEmpty(version);
            }
            catch
            {
                return false;
            }
        }

        public async Task<string> GetVersionAsync()
        {
            try 
            {
                var url = $"{_baseUrl}?mode=version&apikey={_apiKey}&output=json";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<SabnzbdVersionResponse>(content);
                return result?.Version ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public Task<bool> AddTorrentAsync(string url, string? category = null)
        {
            return Task.FromResult(false); // Not supported
        }

        public async Task<bool> AddNzbAsync(string nzbUrl, string? category = null)
        {
            try
            {
                // Add NZB by URL
                var url = $"{_baseUrl}?mode=addurl&name={Uri.EscapeDataString(nzbUrl)}&cat={Uri.EscapeDataString(category ?? "default")}&apikey={_apiKey}&output=json";
                var response = await _httpClient.GetAsync(url);
                response.EnsureSuccessStatusCode();
                
                var content = await response.Content.ReadAsStringAsync();
                var result = JsonSerializer.Deserialize<SabnzbdAddResponse>(content);
                
                return result?.Status == true;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Sabnzbd] Error adding NZB: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveDownloadAsync(string id)
        {
            try
            {
                // Try deleting from Queue (Active)
                var queueUrl = $"{_baseUrl}?mode=queue&name=delete&value={Uri.EscapeDataString(id)}&apikey={_apiKey}&output=json";
                await _httpClient.GetAsync(queueUrl);

                // Try deleting from History (Completed/Failed)
                var historyUrl = $"{_baseUrl}?mode=history&name=delete&value={Uri.EscapeDataString(id)}&apikey={_apiKey}&output=json";
                await _httpClient.GetAsync(historyUrl);

                return true;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PauseDownloadAsync(string id)
        {
            try {
                var url = $"{_baseUrl}?mode=queue&name=pause&value={Uri.EscapeDataString(id)}&apikey={_apiKey}&output=json";
                await _httpClient.GetAsync(url);
                return true;
            } catch { return false; }
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
            try {
                var url = $"{_baseUrl}?mode=queue&name=resume&value={Uri.EscapeDataString(id)}&apikey={_apiKey}&output=json";
                await _httpClient.GetAsync(url);
                return true;
            } catch { return false; }
        }

        public async Task<List<DownloadStatus>> GetDownloadsAsync()
        {
            var statusList = new List<DownloadStatus>();
            
            try
            {
                // 1. Get Queue (Items currently downloading or paused)
                var queueUrl = $"{_baseUrl}?mode=queue&apikey={_apiKey}&output=json";
                var queueResponse = await _httpClient.GetAsync(queueUrl);
                if (queueResponse.IsSuccessStatusCode)
                {
                    var queueContent = await queueResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(queueContent);
                    if (doc.RootElement.TryGetProperty("queue", out var queue) && 
                        queue.TryGetProperty("slots", out var slots))
                    {
                        foreach (var slot in slots.EnumerateArray())
                        {
                            statusList.Add(new DownloadStatus
                            {
                                Id = slot.GetProperty("nzo_id").GetString() ?? string.Empty,
                                Name = slot.GetProperty("filename").GetString() ?? string.Empty,
                                Size = ParseSize(slot.GetProperty("size").GetString()),
                                Progress = float.Parse(slot.GetProperty("percentage").GetString() ?? "0"),
                                State = slot.GetProperty("status").GetString()?.ToLower() == "paused" ? DownloadState.Paused : DownloadState.Downloading,
                                Category = slot.GetProperty("cat").GetString(),
                                DownloadPath = null // Queue doesn't always show final path
                            });
                        }
                    }
                }

                // 2. Get History (Completed items)
                var historyUrl = $"{_baseUrl}?mode=history&apikey={_apiKey}&output=json";
                var historyResponse = await _httpClient.GetAsync(historyUrl);
                if (historyResponse.IsSuccessStatusCode)
                {
                    var historyContent = await historyResponse.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(historyContent);
                    if (doc.RootElement.TryGetProperty("history", out var history) && 
                        history.TryGetProperty("slots", out var slots))
                    {
                        foreach (var slot in slots.EnumerateArray())
                        {
                            statusList.Add(new DownloadStatus
                            {
                                Id = slot.TryGetProperty("nzo_id", out var nzoId) ? nzoId.GetString() ?? string.Empty : string.Empty,
                                Name = slot.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty,
                                Size = slot.TryGetProperty("size", out var size) ? ParseSize(size.GetString()) : 0,
                                Progress = 100,
                                State = slot.TryGetProperty("status", out var status) ? MapHistoryStatus(status.GetString()) : DownloadState.Unknown,
                                Category = slot.TryGetProperty("category", out var cat) ? cat.GetString() : null,
                                DownloadPath = slot.TryGetProperty("storage", out var storage) ? storage.GetString() : null
                            });
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Sabnzbd] Error getting downloads: {ex.Message}");
            }

            return statusList;
        }

        private long ParseSize(string? sizeStr)
        {
            if (string.IsNullOrEmpty(sizeStr)) return 0;
            // Sabnzbd uses "1.2 GB", "500 MB", etc. 
            // For monitoring we just need a rough estimate or 0 if parsing fails
            try 
            {
               var parts = sizeStr.Split(' ');
               if (parts.Length < 2) return 0;
               double val = double.Parse(parts[0]);
               return parts[1].ToUpper() switch
               {
                   "KB" => (long)(val * 1024),
                   "MB" => (long)(val * 1024 * 1024),
                   "GB" => (long)(val * 1024 * 1024 * 1024),
                   "TB" => (long)(val * 1024 * 1024 * 1024 * 1024),
                   _ => (long)val
               };
            }
            catch { return 0; }
        }

        private DownloadState MapHistoryStatus(string? status)
        {
            return status?.ToLower() switch
            {
                "completed" => DownloadState.Completed,
                "failed" => DownloadState.Error,
                "verifying" => DownloadState.Checking,
                "repairing" => DownloadState.Checking,
                "extracting" => DownloadState.Checking,
                "moving" => DownloadState.Checking,
                _ => DownloadState.Unknown
            };
        }

        [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
        [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
        private class SabnzbdVersionResponse
        {
            [JsonPropertyName("version")]
            public string Version { get; set; } = string.Empty;
        }

        [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
        [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
        private class SabnzbdAddResponse
        {
            [JsonPropertyName("status")]
            public bool Status { get; set; }
            
            [JsonPropertyName("nzo_ids")]
            public string[] NzoIds { get; set; } = Array.Empty<string>();
        }
    }
}
