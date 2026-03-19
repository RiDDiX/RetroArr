using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Globalization", "CA1303:Do not pass literals as localized parameters")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1305:SpecifyIFormatProvider")]
    [SuppressMessage("Microsoft.Globalization", "CA1307:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Globalization", "CA1310:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Globalization", "CA1309:UseOrdinalStringComparison")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Design", "CA1054:UriParametersShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    [SuppressMessage("Microsoft.Performance", "CA1866:UseCharOverload")]
    public class QBittorrentClient : IDownloadClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadClient);
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _username;
        private readonly string _password;
        private string? _cookie;

        public QBittorrentClient(string host, int port, string username, string password, string? urlBase = null)
        {
            _httpClient = new HttpClient();
            
            // Handle case where host might already contain http:// or https://
            string cleanHost = host.Trim();
            if (!cleanHost.StartsWith("http://", StringComparison.OrdinalIgnoreCase) && 
                !cleanHost.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                cleanHost = $"http://{cleanHost}";
            }
            
            // Remove trailing slash if present
            cleanHost = cleanHost.TrimEnd('/');
            
            // Process UrlBase
            string finalUrlBase = "";
            if (!string.IsNullOrWhiteSpace(urlBase))
            {
                finalUrlBase = urlBase.Trim();
                if (!finalUrlBase.StartsWith("/")) finalUrlBase = "/" + finalUrlBase;
                finalUrlBase = finalUrlBase.TrimEnd('/');
            }
            
            _baseUrl = $"{cleanHost}:{port}{finalUrlBase}/api/v2";
            _username = username;
            _password = password;
        }

        private async Task EnsureAuthenticatedAsync()
        {
            if (!string.IsNullOrEmpty(_cookie))
            {
                return;
            }

            // Set default headers for CSRF and identification
            _httpClient.DefaultRequestHeaders.Remove("Referer");
            _httpClient.DefaultRequestHeaders.Add("Referer", _baseUrl.Replace("/api/v2", "/", StringComparison.OrdinalIgnoreCase));
            _httpClient.DefaultRequestHeaders.Remove("Origin");
            _httpClient.DefaultRequestHeaders.Add("Origin", _baseUrl.Replace("/api/v2", "", StringComparison.OrdinalIgnoreCase));
            _httpClient.DefaultRequestHeaders.Remove("User-Agent");
            _httpClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");

            var data = new Dictionary<string, string>
            {
                { "username", _username },
                { "password", _password }
            };

            var content = new FormUrlEncodedContent(data);
            var response = await _httpClient.PostAsync($"{_baseUrl}/auth/login", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.Error($"[qBittorrent] Login failed. Status: {response.StatusCode}, Body: {errorBody}");
                response.EnsureSuccessStatusCode();
            }

            if (response.Headers.TryGetValues("Set-Cookie", out var cookies))
            {
                foreach (var cookie in cookies)
                {
                    if (cookie.StartsWith("SID=", StringComparison.Ordinal))
                    {
                        _cookie = cookie.Split(';')[0];
                        _httpClient.DefaultRequestHeaders.Remove("Cookie");
                        _httpClient.DefaultRequestHeaders.Add("Cookie", _cookie);
                        break;
                    }
                }
            }
        }

        public async Task<bool> TestConnectionAsync()
        {
            try 
            {
                await EnsureAuthenticatedAsync();
                var response = await _httpClient.GetAsync($"{_baseUrl}/app/version");
                if (!response.IsSuccessStatusCode)
                {
                    var errorBody = await response.Content.ReadAsStringAsync();
                    _logger.Error($"[qBittorrent] Test connection failed. Status: {response.StatusCode}, Body: {errorBody}");
                }
                return response.IsSuccessStatusCode;
            }
            catch (Exception ex)
            {
                _logger.Error($"[qBittorrent] Test connection exception: {ex.Message}");
                return false;
            }
        }

        public async Task<string> GetVersionAsync()
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync($"{_baseUrl}/app/version");
            response.EnsureSuccessStatusCode();
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<bool> AddTorrentAsync(string url, string? category = null)
        {
            await EnsureAuthenticatedAsync();

            var content = new MultipartFormDataContent();
            
            // urls field must be strings separated by newlines
            content.Add(new StringContent(url), "urls");

            if (!string.IsNullOrEmpty(category))
            {
                content.Add(new StringContent(category), "category");
            }

            var response = await _httpClient.PostAsync($"{_baseUrl}/torrents/add", content);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync();
                _logger.Error($"[qBittorrent] Failed to add torrent. Status: {response.StatusCode}, Body: {errorBody}");
                return false;
            }

            return true;
        }

        public Task<bool> AddNzbAsync(string url, string? category = null)
        {
            return Task.FromResult(false); // Not supported
        }

        public async Task<bool> RemoveDownloadAsync(string id)
        {
            await EnsureAuthenticatedAsync();
            
            // hashes format: id|id|id... but we only have one
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("hashes", id),
                new KeyValuePair<string, string>("deleteFiles", "true")
            });

            var response = await _httpClient.PostAsync($"{_baseUrl}/torrents/delete", content);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[qBittorrent] Failed to delete torrent {id}. Status: {response.StatusCode}");
                return false;
            }
            return true;
        }

        public async Task<bool> PauseDownloadAsync(string id)
        {
            await EnsureAuthenticatedAsync();
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", id) });
            var response = await _httpClient.PostAsync($"{_baseUrl}/torrents/stop", content);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
            await EnsureAuthenticatedAsync();
            var content = new FormUrlEncodedContent(new[] { new KeyValuePair<string, string>("hashes", id) });
            var response = await _httpClient.PostAsync($"{_baseUrl}/torrents/start", content);
            return response.IsSuccessStatusCode;
        }

        public async Task<List<DownloadStatus>> GetDownloadsAsync()
        {
            var torrents = await GetTorrentsAsync();
            var statusList = new List<DownloadStatus>();

            foreach (var torrent in torrents)
            {
                statusList.Add(new DownloadStatus
                {
                    Id = torrent.Hash,
                    Name = torrent.Name,
                    Size = torrent.Size,
                    Progress = torrent.Progress * 100, // qBittorrent returns 0.0 to 1.0
                    State = MapState(torrent.State),
                    Category = torrent.Category,
                    DownloadPath = !string.IsNullOrEmpty(torrent.Content_Path)
                        ? torrent.Content_Path
                        : (!string.IsNullOrEmpty(torrent.Save_Path) 
                            ? System.IO.Path.Combine(torrent.Save_Path, torrent.Name) 
                            : null)
                });
            }

            return statusList;
        }

        private DownloadState MapState(string state)
        {
            return state.ToLower() switch
            {
                "downloading" => DownloadState.Downloading,
                "stalleddl" => DownloadState.Downloading,
                "pauseddl" => DownloadState.Paused,
                "stoppeddl" => DownloadState.Paused,
                "queueddl" => DownloadState.Queued,
                "checkingdl" => DownloadState.Checking,
                "checkingresumeData" => DownloadState.Checking,
                "uploading" => DownloadState.Completed,
                "stalledup" => DownloadState.Completed,
                "pausedup" => DownloadState.Completed, // Technically completed
                "stoppedup" => DownloadState.Completed,
                "queuedup" => DownloadState.Completed,
                "checkingup" => DownloadState.Completed,
                "moving" => DownloadState.Completed,
                "missingfiles" => DownloadState.Error,
                "error" => DownloadState.Error,
                _ => DownloadState.Unknown
            };
        }

        public async Task<List<TorrentInfo>> GetTorrentsAsync()
        {
            await EnsureAuthenticatedAsync();
            var response = await _httpClient.GetAsync($"{_baseUrl}/torrents/info");
            response.EnsureSuccessStatusCode();

            var json = await response.Content.ReadAsStringAsync();
            var torrents = JsonSerializer.Deserialize<List<TorrentInfo>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return torrents ?? new List<TorrentInfo>();
        }
    }

    public class TorrentInfo
    {
        public string Hash { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Size { get; set; }
        public float Progress { get; set; }
        public string State { get; set; } = string.Empty;
        public int DlSpeed { get; set; }
        public int UpSpeed { get; set; }
        public int Priority { get; set; }
        public int NumSeeds { get; set; }
        public int NumLeechs { get; set; }
        public string Category { get; set; } = string.Empty;
        [JsonPropertyName("save_path")]
        public string Save_Path { get; set; } = string.Empty;
        [JsonPropertyName("content_path")]
        public string Content_Path { get; set; } = string.Empty;
    }
}
