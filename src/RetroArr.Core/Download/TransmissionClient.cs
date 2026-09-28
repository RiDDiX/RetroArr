using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using RetroArr.Core.Logging;

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
        private readonly string? _category;
        private string? _sessionId;

        public TransmissionClient(string host, int port, string username, string password, string? category = null)
        {
            _httpClient = new HttpClient(new HttpClientHandler { AutomaticDecompression = System.Net.DecompressionMethods.All });
            
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
            _category = category;
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
                    _logger.Info($"[Transmission] Manually downloading torrent from: {LogRedactor.DescribeDownloadUrl(cleanUrl)}");
                    
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

            // The category becomes a label; 4.x rejects the whole add for a blank label or one with a comma
            var label = string.IsNullOrWhiteSpace(category) || category.Contains(',') ? null : category;
            if (label != null) args["labels"] = new[] { label };

            _logger.Info($"[Transmission] Sending arguments: {string.Join(", ", args.Keys)}");

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
                    if (resultStr == "success" && label != null)
                        await EnsureLabelAsync(doc.RootElement, label);
                    return resultStr == "success";
                }
            }
            else
            {
                _logger.Error($"[Transmission] Error adding torrent. Status: {response.StatusCode}");
            }

            return false;
        }

        // 3.00 ignores labels on torrent-add, so add the label when it is missing. A duplicate is left
        // alone: the torrent was already there and may belong to another app.
        private async Task EnsureLabelAsync(JsonElement addResult, string label)
        {
            try
            {
                if (!addResult.TryGetProperty("arguments", out var added)) return;
                if (added.TryGetProperty("torrent-duplicate", out var duplicate))
                {
                    var name = duplicate.TryGetProperty("name", out var n) ? n.GetString() : null;
                    var existing = duplicate.TryGetProperty("hashString", out var h) ? h.GetString() : null;
                    if (!string.IsNullOrEmpty(existing) && (await GetLabelsAsync(existing)).Contains(label, StringComparer.OrdinalIgnoreCase)) return;
                    _logger.Warn($"[Transmission] '{name}' is already in Transmission and was not labelled '{label}'. RetroArr won't show or import it unless you add the label in Transmission.");
                    return;
                }
                if (!added.TryGetProperty("torrent-added", out var torrent)) return;
                var hash = torrent.GetProperty("hashString").GetString();
                if (string.IsNullOrEmpty(hash)) return;

                var labels = await GetLabelsAsync(hash);
                if (labels.Contains(label, StringComparer.OrdinalIgnoreCase)) return;

                labels.Add(label);
                var set = await SendRequestAsync("torrent-set", new Dictionary<string, object> { { "ids", new[] { hash } }, { "labels", labels } });
                _logger.Info($"[Transmission] Set label '{label}' on {hash}: {await set.Content.ReadAsStringAsync()}");
            }
            catch (Exception ex)
            {
                _logger.Warn($"[Transmission] Could not set label '{label}': {ex.Message}");
            }
        }

        private async Task<List<string>> GetLabelsAsync(string hash)
        {
            var response = await SendRequestAsync("torrent-get", new { ids = new[] { hash }, fields = new[] { "labels" } });
            using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
            return doc.RootElement.GetProperty("arguments").GetProperty("torrents").EnumerateArray().SelectMany(ReadLabels).ToList();
        }

        // 3.00 answers unknown fields with 0, so only an array counts
        private static IEnumerable<string> ReadLabels(JsonElement torrent) =>
            torrent.TryGetProperty("labels", out var labels) && labels.ValueKind == JsonValueKind.Array
                ? labels.EnumerateArray().Where(l => l.ValueKind == JsonValueKind.String).Select(l => l.GetString()!)
                : Enumerable.Empty<string>();

        // Download ids are hashes, which Transmission takes as strings; a numeric id still goes out as a number
        private static object[] Ids(string id) =>
            new object[] { int.TryParse(id, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : id };

        public Task<bool> AddNzbAsync(string url, string? category = null)
        {
            throw new NotSupportedException("Transmission does not handle NZB downloads. Configure SABnzbd or NZBGet as a Usenet client.");
        }

        public async Task<bool> RemoveDownloadAsync(string id, bool deleteFiles)
        {
            var args = new Dictionary<string, object>
            {
                { "ids", Ids(id) },
                { "delete-local-data", deleteFiles }
            };

            var response = await SendRequestAsync("torrent-remove", args);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> PauseDownloadAsync(string id)
        {
            var args = new Dictionary<string, object> { { "ids", Ids(id) } };
            var response = await SendRequestAsync("torrent-stop", args);
            return response.IsSuccessStatusCode;
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
            var args = new Dictionary<string, object> { { "ids", Ids(id) } };
            var response = await SendRequestAsync("torrent-start", args);
            return response.IsSuccessStatusCode;
        }

        public async Task<List<DownloadStatus>> GetDownloadsAsync()
        {
            var args = new
            {
                fields = new[] { "hashString", "name", "totalSize", "percentDone", "status", "downloadDir", "error", "errorString", "labels" }
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
                    var labels = ReadLabels(torrent).ToList();
                    statusList.Add(new DownloadStatus
                    {
                        // the numeric id changes with every daemon restart, the hash never does
                        Id = torrent.GetProperty("hashString").GetString() ?? string.Empty,
                        Name = torrent.GetProperty("name").GetString() ?? string.Empty,
                        Size = torrent.GetProperty("totalSize").GetInt64(),
                        Progress = (float)torrent.GetProperty("percentDone").GetDouble() * 100,
                        State = MapState(torrent.GetProperty("status").GetInt32(), torrent.GetProperty("percentDone").GetDouble()),
                        DownloadPath = CombinePath(torrent.GetProperty("downloadDir").GetString(), torrent.GetProperty("name").GetString()),
                        Category = labels.FirstOrDefault(l => l.Equals(_category, StringComparison.OrdinalIgnoreCase)) ?? labels.FirstOrDefault()
                    });
                }
            }

            return statusList;
        }

        // downloadDir is the folder shared by all torrents, the torrent itself lives below it
        private static string? CombinePath(string? dir, string? name) =>
            string.IsNullOrEmpty(dir) || string.IsNullOrEmpty(name) ? dir : System.IO.Path.Combine(dir, name);

        private static DownloadState MapState(int status, double percentDone)
        {
            // stopped or waiting in the seed queue after finishing is still a finished download
            if (percentDone >= 1 && (status == 0 || status == 5)) return DownloadState.Completed;

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
