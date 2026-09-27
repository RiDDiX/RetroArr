using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Net.Http.Headers;
using System.Diagnostics.CodeAnalysis;
using System.Web;

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
        // NZBGet before 25.0 doesn't decode \uXXXX escapes, and the default encoder escapes '+' (all over base64), '&' and quotes
        private static readonly JsonSerializerOptions _json = new() { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

        public NzbgetClient(string host, int port, string username, string password, string? urlBase = null)
        {
            _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
            
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
                
                var json = JsonSerializer.Serialize(request, _json);
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
                using var nzbResponse = await nzbDownloader.GetAsync(nzbUrl);
                nzbResponse.EnsureSuccessStatusCode();
                var nzbBytes = await nzbResponse.Content.ReadAsByteArrayAsync();
                var nzbBase64 = Convert.ToBase64String(nzbBytes);
                
                var appendRequest = new
                {
                    method = "append",
                    @params = new object[] 
                    { 
                        BuildNzbFilename(nzbUrl, nzbResponse.Content.Headers.ContentDisposition), // Filename, becomes NZBName
                        nzbBase64,              // Content (Base64)
                        category ?? "",         // Category
                        0,                      // Priority
                        false,                  // AddToTop
                        false,                  // Paused
                        "",                     // DupeKey
                        0,                      // DupeScore
                        "SCORE",                // DupeMode
                        new object[0]           // Parameters. NZBGet before 26.3 reads past the last param into "id" and rejects the call unless an array ends the list
                    },
                    id = 2
                };

                var json = JsonSerializer.Serialize(appendRequest, _json);
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

        public async Task<bool> RemoveDownloadAsync(string id, bool deleteFiles)
        {
            try
            {
                if (!int.TryParse(id, out var nzbId)) return false;

                // Delete from the queue, then drop the history entry. Files go either way: HistoryDelete also removes a parked job's files
                var fromQueue = await EditQueueAsync("GroupDelete", nzbId);
                var fromHistory = await EditQueueAsync("HistoryDelete", nzbId);
                return fromQueue || fromHistory;
            }
            catch
            {
                return false;
            }
        }

        public async Task<bool> PauseDownloadAsync(string id)
        {
             try {
                if (!int.TryParse(id, out var nzbId)) return false;
                return await EditQueueAsync("GroupPause", nzbId);
            } catch { return false; }
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
             try {
                if (!int.TryParse(id, out var nzbId)) return false;
                return await EditQueueAsync("GroupResume", nzbId);
            } catch { return false; }
        }

        // editqueue(Command, Offset, Param, IDs): NZBGet before 18 requires the int offset, 18+ still accepts it,
        // and every version needs the string param. The result is false when no job matched.
        private async Task<bool> EditQueueAsync(string command, int nzbId)
        {
            var result = await SendRpcRequestAsync(new { method = "editqueue", @params = new object[] { command, 0, "", new[] { nzbId } }, id = 10 });
            if (result?.ValueKind == JsonValueKind.True) return true;
            _logger.Debug($"[NZBGet] editqueue {command} for {nzbId} did not apply");
            return false;
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
                            Size = ReadSize(group, "FileSize"),
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
                            Size = ReadSize(item, "FileSize"),
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
            var json = JsonSerializer.Serialize(request, _json);
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
            long fileSize = ReadSize(group, "FileSize");
            long remaining = ReadSize(group, "RemainingSize");
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

        // NZBGet splits 64-bit values into unsigned 32-bit Hi/Lo fields
        private static long ReadSize(JsonElement item, string name)
        {
            // Lo is unsigned 32-bit; NZBGet before 21.1 could send it negative
            return (item.GetProperty(name + "Hi").GetInt64() << 32) | (item.GetProperty(name + "Lo").GetInt64() & 0xFFFFFFFFL);
        }

        // History status is "<total>/<detail>": SUCCESS/ALL, WARNING/SCRIPT, FAILURE/HEALTH, DELETED/MANUAL, ...
        private static DownloadState MapHistoryStatus(string? status)
        {
            var upper = status?.ToUpperInvariant() ?? string.Empty;
            // Only a post-processing script failed, the download itself is complete
            if (upper == "WARNING/SCRIPT") return DownloadState.Completed;

            return upper.Split('/')[0] switch
            {
                "SUCCESS" => DownloadState.Completed,
                "WARNING" => DownloadState.Error, // damaged, repair or unpack skipped, password, no space
                "FAILURE" => DownloadState.Error,
                "DELETED" => DownloadState.Deleted,
                _ => DownloadState.Unknown
            };
        }

        // NZBGet names the job after the file name and runs its duplicate check on that name, so every job
        // needs its release name: the NZB response's file name (Prowlarr and most indexers send one), else
        // Prowlarr's file= parameter, else a *.nzb URL path segment, else something unique.
        internal static string BuildNzbFilename(string nzbUrl, ContentDispositionHeaderValue? disposition)
        {
            var name = disposition?.FileNameStar ?? disposition?.FileName;
            if (string.IsNullOrWhiteSpace(name) && Uri.TryCreate(nzbUrl, UriKind.Absolute, out var uri))
            {
                name = HttpUtility.ParseQueryString(uri.Query)["file"];
                var segment = Uri.UnescapeDataString(uri.Segments[^1]);
                if (string.IsNullOrWhiteSpace(name) && segment.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase))
                    name = segment;
            }

            name = Path.GetFileName((name ?? string.Empty).Trim().Trim('"').Replace('\\', '/'));
            name = new string(name.Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (name.EndsWith(".nzb", StringComparison.OrdinalIgnoreCase)) name = name[..^4].TrimEnd();
            // NZBGet stores the file and later a folder under this name, keep it well below the 255 byte limit
            while (Encoding.UTF8.GetByteCount(name) > 200) name = name[..^1];
            name = name.TrimEnd();
            if (name.Length == 0) name = "RetroArr_" + Guid.NewGuid().ToString("N");
            return name + ".nzb";
        }
    }
}
