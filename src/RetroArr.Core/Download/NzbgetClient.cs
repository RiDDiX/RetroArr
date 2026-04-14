using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1867:UseCharOverload")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    [SuppressMessage("Microsoft.Performance", "CA1825:AvoidZeroLengthArrayAllocations")]
    public class NzbgetClient : IDownloadClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadClient);
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;

        public NzbgetClient(string host, int port, string username, string password, string? urlBase = null)
        {
            _httpClient = new HttpClient();
            
            // Basic Auth
            var authValue = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
            
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
            
            // NZBGet JSON-RPC endpoint
            _baseUrl = $"{cleanHost}:{port}{finalUrlBase}/jsonrpc";
            _username = username;
            _password = password;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
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
                var request = new
                {
                    method = "version",
                    @params = new object[] { },
                    id = 1
                };
                
                var json = JsonSerializer.Serialize(request);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(_baseUrl, content);
                response.EnsureSuccessStatusCode();
                
                var responseContent = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                
                if (root.TryGetProperty("result", out var resultElement))
                {
                    return resultElement.GetString() ?? string.Empty;
                }
                
                return string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public Task<bool> AddTorrentAsync(string url, string? category = null)
        {
            throw new NotSupportedException("NZBGet does not handle torrent downloads. Configure qBittorrent, Transmission or Deluge as a torrent client.");
        }

        public async Task<bool> AddNzbAsync(string nzbUrl, string? category = null)
        {
            try
            {
                // Downloading NZB content first to be safe and compatible
                using var nzbDownloader = new HttpClient();
                var nzbBytes = await nzbDownloader.GetByteArrayAsync(nzbUrl);
                var nzbBase64 = Convert.ToBase64String(nzbBytes);
                
                var appendRequest = new
                {
                    method = "append",
                    @params = new object[] 
                    { 
                        "RetroArr_download.nzb", // Filename
                        nzbBase64,              // Content (Base64)
                        category ?? "",         // Category
                        0,                      // Priority
                        false,                  // AddToTop
                        false,                  // Paused
                        "",                     // DupeKey
                        0,                      // DupeScore
                        "SCORE"                 // DupeMode
                    },
                    id = 2
                };

                var json = JsonSerializer.Serialize(appendRequest);
                var content = new StringContent(json, Encoding.UTF8, "application/json");
                
                var response = await _httpClient.PostAsync(_baseUrl, content);
                response.EnsureSuccessStatusCode();
                
                var responseContent = await response.Content.ReadAsStringAsync();
                // Check if result > 0 (ID of added file)
                using var doc = JsonDocument.Parse(responseContent);
                var root = doc.RootElement;
                if (root.TryGetProperty("result", out var resultElement))
                {
                   return resultElement.GetInt32() > 0;
                }
                return false;
            }
            catch (Exception ex)
            {
                _logger.Error($"[NZBGet] Error adding NZB: {ex.Message}");
                return false;
            }
        }

        public async Task<bool> RemoveDownloadAsync(string id)
        {
            try
            {
                int nzbId;
                if (!int.TryParse(id, out nzbId)) return false;

                // Try deleting from Queue
                var queueReq = new 
                { 
                    method = "editqueue", 
                    @params = new object[] { "GroupDelete", 0, new[] { nzbId } }, 
                    id = 10 
                };
                await SendRpcRequestAsync(queueReq);

                // Try deleting from History
                var historyReq = new 
                { 
                    method = "editqueue", 
                    @params = new object[] { "HistoryDelete", 0, new[] { nzbId } }, 
                    id = 11 
                };
                await SendRpcRequestAsync(historyReq);

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
                int nzbId;
                if (!int.TryParse(id, out nzbId)) return false;
                var req = new { method = "editqueue", @params = new object[] { "GroupPause", 0, new[] { nzbId } }, id = 20 };
                await SendRpcRequestAsync(req);
                return true;
            } catch { return false; }
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
             try {
                int nzbId;
                if (!int.TryParse(id, out nzbId)) return false;
                var req = new { method = "editqueue", @params = new object[] { "GroupResume", 0, new[] { nzbId } }, id = 21 };
                await SendRpcRequestAsync(req);
                return true;
            } catch { return false; }
        }

        public async Task<List<DownloadStatus>> GetDownloadsAsync()
        {
            var statusList = new List<DownloadStatus>();

            try
            {
                // 1. Get Queue (ListGroups)
                var listGroupsRequest = new { method = "listgroups", @params = new object[] { }, id = 3 };
                var queueContent = await SendRpcRequestAsync(listGroupsRequest);
                if (queueContent.HasValue)
                {
                    foreach (var group in queueContent.Value.EnumerateArray())
                    {
                        statusList.Add(new DownloadStatus
                        {
                            Id = group.GetProperty("NZBID").GetInt32().ToString(),
                            Name = group.GetProperty("NZBName").GetString() ?? string.Empty,
                            Size = group.GetProperty("FileSizeLo").GetInt64(), // Simple approach, NZBGet splits 64bit into Lo/Hi
                            Progress = CalculateProgress(group),
                            State = MapQueueStatus(group.GetProperty("Status").GetString()),
                            Category = group.GetProperty("Category").GetString(),
                            DownloadPath = group.GetProperty("DestDir").GetString()
                        });
                    }
                }

                // 2. Get History
                var historyRequest = new { method = "history", @params = new object[] { false }, id = 4 };
                var historyContent = await SendRpcRequestAsync(historyRequest);
                if (historyContent.HasValue)
                {
                    foreach (var item in historyContent.Value.EnumerateArray())
                    {
                        statusList.Add(new DownloadStatus
                        {
                            Id = item.GetProperty("NZBID").GetInt32().ToString(),
                            Name = item.GetProperty("NZBName").GetString() ?? string.Empty,
                            Size = item.GetProperty("FileSizeLo").GetInt64(),
                            Progress = 100,
                            State = MapHistoryStatus(item.GetProperty("Status").GetString()),
                            Category = item.GetProperty("Category").GetString(),
                            DownloadPath = item.GetProperty("DestDir").GetString()
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[NZBGet] Error getting downloads: {ex.Message}");
            }

            return statusList;
        }

        private async Task<JsonElement?> SendRpcRequestAsync(object request)
        {
            var json = JsonSerializer.Serialize(request);
            var content = new StringContent(json, Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync(_baseUrl, content);
            if (!response.IsSuccessStatusCode) return null;

            var responseContent = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(responseContent);
            if (doc.RootElement.TryGetProperty("result", out var result))
            {
                return result.Clone();
            }
            return null;
        }

        private float CalculateProgress(JsonElement group)
        {
            long fileSize = group.GetProperty("FileSizeLo").GetInt64();
            long remaining = group.GetProperty("RemainingSizeLo").GetInt64();
            if (fileSize == 0) return 100;
            return (float)((fileSize - remaining) / (double)fileSize * 100);
        }

        private DownloadState MapQueueStatus(string? status)
        {
            return status?.ToUpper() switch
            {
                "DOWNLOADING" => DownloadState.Downloading,
                "PAUSED" => DownloadState.Paused,
                "QUEUED" => DownloadState.Queued,
                _ => DownloadState.Unknown
            };
        }

        private DownloadState MapHistoryStatus(string? status)
        {
            return status?.ToUpper() switch
            {
                "SUCCESS" => DownloadState.Completed,
                "FAILURE" => DownloadState.Error,
                "DELETED" => DownloadState.Deleted,
                _ => DownloadState.Unknown
            };
        }
    }
}
