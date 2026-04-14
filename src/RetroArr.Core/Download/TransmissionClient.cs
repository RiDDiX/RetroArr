using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    public class TransmissionClient : IDownloadClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadClient);
        private readonly HttpClient _httpClient;
        private readonly string _rpcUrl;
        private readonly string _username;
        private readonly string _password;
        private string? _sessionId;

        public TransmissionClient(string host, int port, string username, string password)
        {
            _httpClient = new HttpClient();
            
            string cleanHost = host.Trim();
            if (!cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !cleanHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = $"http://{cleanHost}";
            }
            cleanHost = cleanHost.TrimEnd('/');
            
            _rpcUrl = $"{cleanHost}:{port}/transmission/rpc";
            _username = username;
            _password = password;
        }

        private void SetupHeaders()
        {
            _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue(
                "Basic", 
                Convert.ToBase64String(Encoding.ASCII.GetBytes($"{_username}:{_password}")));

            if (!string.IsNullOrEmpty(_sessionId))
            {
                if (_httpClient.DefaultRequestHeaders.Contains("X-Transmission-Session-Id"))
                {
                    _httpClient.DefaultRequestHeaders.Remove("X-Transmission-Session-Id");
                }
                _httpClient.DefaultRequestHeaders.Add("X-Transmission-Session-Id", _sessionId);
            }
        }

        private async Task<HttpResponseMessage> SendRequestAsync(string method, object? arguments = null)
        {
            SetupHeaders();

            var requestBody = new
            {
                method = method,
                arguments = arguments
            };

            var json = JsonSerializer.Serialize(requestBody);
            var content = new StringContent(json, Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync(_rpcUrl, content);

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict) // 409
            {
                if (response.Headers.TryGetValues("X-Transmission-Session-Id", out var values))
                {
                    foreach (var value in values)
                    {
                        _sessionId = value;
                        break;
                    }
                    
                    // Retry with new session ID
                    SetupHeaders();
                    var retryContent = new StringContent(json, Encoding.UTF8, "application/json");
                    response = await _httpClient.PostAsync(_rpcUrl, retryContent);
                }
            }

            return response;
        }

        public async Task<bool> TestConnectionAsync()
        {
            try
            {
                var response = await SendRequestAsync("session-get");
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Error($"[Transmission] Test connection exception: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetVersionAsync()
        {
            var response = await SendRequestAsync("session-get");
            if (!response.IsSuccessStatusCode) return string.Empty;

            var json = await response.Content.ReadAsStringAsync();
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("arguments", out var args))
            {
                if (args.TryGetProperty("version", out var version))
                {
                    return version.GetString() ?? string.Empty;
                }
            }
            return string.Empty;
        }

        public async Task<bool> AddTorrentAsync(string url, string? category = null)
        {
            var args = new Dictionary<string, object>();
            
            if (url.StartsWith("magnet:?", StringComparison.OrdinalIgnoreCase))
            {
                args["filename"] = url;
            }
            else
            {
                // Download the torrent file manually to handle redirects (e.g. from Prowlarr)
                // because Transmission often struggles with 301/302 redirects when fetching by URL.
                try 
                {
                    var cleanUrl = url.Trim();
                    _logger.Info($"[Transmission] Manually downloading torrent from: {cleanUrl}");
                    
                    // Use a fresh client to avoid sending Transmission headers (Auth, SessionId) to Prowlarr
                    using var downloadClient = new HttpClient();
                    var torrentBytes = await downloadClient.GetByteArrayAsync(cleanUrl);
                    _logger.Info($"[Transmission] Manual download successful. Bytes: {torrentBytes.Length}");
                    
                    var base64 = Convert.ToBase64String(torrentBytes);
                    args["metainfo"] = base64;
                }
                catch (Exception ex)
                {
                    _logger.Error($"[Transmission] Failed to download torrent file content: {ex.Message}");
                    _logger.Info($"[Transmission] Stack Trace: {ex.StackTrace}");
                    // Fallback to URL if download fails
                    args["filename"] = url; 
                }
            }

            _logger.Info($"[Transmission] Sending arguments: {string.Join(", ", args.Keys)}");

            // Transmission doesn't support categories natively in the same way qBittorrent does
            // Usually path is used, but for now we will just add the torrent.
            // If category mapping to download-dir is needed, it would go here.

            var response = await SendRequestAsync("torrent-add", args);
            
            var json = await response.Content.ReadAsStringAsync();
            _logger.Info($"[Transmission] AddTorrent Response Code: {response.StatusCode}");
            _logger.Info($"[Transmission] AddTorrent Response Body: {json}");

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("result", out var result))
                {
                    var resultStr = result.GetString();
                    _logger.Info($"[Transmission] RPC Result: {resultStr}");
                    return resultStr == "success";
                }
            }
            else
            {
                _logger.Error($"[Transmission] Error adding torrent. Status: {response.StatusCode}");
            }

            return false;
        }

        public Task<bool> AddNzbAsync(string url, string? category = null)
        {
            throw new NotSupportedException("Transmission does not handle NZB downloads. Configure SABnzbd or NZBGet as a Usenet client.");
        }

        public async Task<bool> RemoveDownloadAsync(string id)
        {
            var args = new Dictionary<string, object>
            {
                { "ids", new[] { id } },
                { "delete-local-data", true }
            };

            var response = await SendRequestAsync("torrent-remove", args);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> PauseDownloadAsync(string id)
        {
            var args = new Dictionary<string, object> { { "ids", new[] { id } } };
            var response = await SendRequestAsync("torrent-stop", args);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
            var args = new Dictionary<string, object> { { "ids", new[] { id } } };
            var response = await SendRequestAsync("torrent-start", args);
            return response.IsSuccessStatusCode;
        }

        public async Task<List<DownloadStatus>> GetDownloadsAsync()
        {
            var args = new
            {
                fields = new[] { "id", "name", "totalSize", "percentDone", "status", "downloadDir", "error", "errorString" }
            };

            var response = await SendRequestAsync("torrent-get", args);
            if (!response.IsSuccessStatusCode) return new List<DownloadStatus>();

            var json = await response.Content.ReadAsStringAsync();
            var statusList = new List<DownloadStatus>();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("arguments", out var arguments) &&
                arguments.TryGetProperty("torrents", out var torrents))
            {
                foreach (var torrent in torrents.EnumerateArray())
                {
                    statusList.Add(new DownloadStatus
                    {
                        Id = torrent.GetProperty("id").GetInt32().ToString(),
                        Name = torrent.GetProperty("name").GetString() ?? string.Empty,
                        Size = torrent.GetProperty("totalSize").GetInt64(),
                        Progress = (float)torrent.GetProperty("percentDone").GetDouble() * 100,
                        State = MapState(torrent.GetProperty("status").GetInt32()),
                        DownloadPath = torrent.GetProperty("downloadDir").GetString()
                    });
                }
            }

            return statusList;
        }

        private DownloadState MapState(int status)
        {
            return status switch
            {
                0 => DownloadState.Paused,     // TR_STATUS_STOPPED
                1 => DownloadState.Checking,   // TR_STATUS_CHECK_WAIT
                2 => DownloadState.Checking,   // TR_STATUS_CHECK
                3 => DownloadState.Queued,     // TR_STATUS_DOWNLOAD_WAIT
                4 => DownloadState.Downloading, // TR_STATUS_DOWNLOAD
                5 => DownloadState.Queued,     // TR_STATUS_SEED_WAIT
                6 => DownloadState.Completed,   // TR_STATUS_SEED
                _ => DownloadState.Unknown
            };
        }
    }
}
