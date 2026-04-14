using System;
using System.IO;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Diagnostics;

namespace RetroArr.Core.Download
{
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    public class DelugeClient : IDownloadClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.DownloadClient);
        private readonly HttpClient _httpClient;
        private readonly string _baseUrl;
        private readonly string _password; 
        private int _requestId = 0;
        // CookieContainer handles cookies automatically
        // private readonly System.Net.CookieContainer _cookieContainer; 
        private string? _cookie;

        public DelugeClient(string host, int port, string password, bool useSsl = false)
        {
            var scheme = useSsl ? "https" : "http";
            _baseUrl = $"{scheme}://{host}:{port}/json";
            _password = password;

            var handler = new HttpClientHandler 
            { 
                UseCookies = true,
                ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true // Accept self-signed
            };

            _httpClient = new HttpClient(handler);
            _httpClient.Timeout = TimeSpan.FromSeconds(5); // Fail fast to avoid 504 Gateway Timeout
        }

        private async Task<T> CallJsonRpcAsync<T>(string method, object[] parameters)
        {
            _requestId++;
            var requestObj = new
            {
                method = method,
                @params = parameters,
                id = _requestId
            };

            var jsonParams = JsonSerializer.Serialize(requestObj);
            _logger.Info($"[Deluge] Sending {method} to {_baseUrl}...");
            
            // Deluge is strict about Content-Type being exactly application/json
            // We create the content and explicitly clear/set headers to be safe
            var content = new StringContent(jsonParams, Encoding.UTF8, "application/json"); 
            content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var requestMessage = new HttpRequestMessage(HttpMethod.Post, _baseUrl);
            requestMessage.Content = content;
            requestMessage.Headers.Accept.Add(new System.Net.Http.Headers.MediaTypeWithQualityHeaderValue("application/json"));

            // CookieContainer handles cookies automatically, no need to manually add header if using HttpClientHandler
            // But we must ensure the handler is actually sharing the container. 
            // The constructor set up _httpClient with the handler, so it should work.

            HttpResponseMessage response;
            try 
            {
                response = await _httpClient.SendAsync(requestMessage);
            }
            catch (TaskCanceledException)
            {
                throw new Exception($"Connection to Deluge timed out after 5 seconds. Check Host/Port/Firewall.");
            }
            catch (HttpRequestException ex)
            {
                 throw new Exception($"Network error connecting to Deluge: {ex.Message}");
            }
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                _logger.Error($"[Deluge] HTTP Error {response.StatusCode} for method {method}: {errorContent}");
                response.EnsureSuccessStatusCode();
            }

            // We don't need to manually capture Set-Cookie either, CookieContainer does it.
            
            var resContent = await response.Content.ReadAsStringAsync();
            _logger.Info($"[Deluge] Response received for {method}. Length: {resContent.Length}");

            var rpcResponse = JsonSerializer.Deserialize<DelugeRpcResponse<T>>(resContent, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (rpcResponse == null)
                throw new Exception($"Empty response from Deluge for method {method}");

            if (rpcResponse.Error != null)
            {
                _logger.Error($"[Deluge] RPC Error for {method}: {rpcResponse.Error.Message} (Code: {rpcResponse.Error.Code})");
                throw new Exception($"Deluge RPC Error [{method}]: {rpcResponse.Error.Message} (Code: {rpcResponse.Error.Code})");
            }

            return rpcResponse.Result;
        }

        private async Task EnsureAuthenticatedAsync()
        {
            // First check if we have a valid session with a connected daemon
            bool isConnected = false;
            try 
            {
                isConnected = await CallJsonRpcAsync<bool>("web.connected", Array.Empty<object>());
            }
            catch 
            {
                isConnected = false;
            }

            if (!isConnected)
            {
                _logger.Info("[Deluge] Not connected to daemon. Attempting login...");
                await LoginAndConnectAsync();
            }
        }

        private async Task LoginAndConnectAsync()
        {
            // 1. Auth Login
            _logger.Info("[Deluge] Performing auth.login...");
            bool authSuccess = await CallJsonRpcAsync<bool>("auth.login", new object[] { _password });
            if (!authSuccess)
            {
                throw new Exception("Deluge authentication failed (Invalid Password).");
            }
            _logger.Info("[Deluge] Auth successful.");

            // 2. Check connection again, just in case default daemon was auto-selected
            bool isConnected = await CallJsonRpcAsync<bool>("web.connected", Array.Empty<object>());
            if (isConnected) return;

            // 3. Get Hosts
            _logger.Info("[Deluge] Getting available hosts...");
            // web.get_hosts returns a list of [id, host, port, status] tuples 
            // Returns [["host_id", "hostname", port, "status"], ...]
            var hostsRaw = await CallJsonRpcAsync<List<object>>("web.get_hosts", Array.Empty<object>());
            
            if (hostsRaw == null || hostsRaw.Count == 0)
            {
                throw new Exception("Deluge WebUI has no daemons configured.");
            }

            // 4. Find first online host
            string? targetHostId = null;
            foreach (var hostEntry in hostsRaw)
            {
                // Deluge 1.x/2.x format: [id, host, port, status]
                if (hostEntry is JsonElement hostElem && hostElem.ValueKind == JsonValueKind.Array)
                {
                    string id = hostElem[0].GetString() ?? "";
                    string status = hostElem[3].GetString() ?? "";
                    
                    _logger.Info($"[Deluge] Found host: {id} ({status})");
                    
                    if (status.Equals("Online", StringComparison.OrdinalIgnoreCase) || 
                        status.Equals("Connected", StringComparison.OrdinalIgnoreCase)) 
                    {
                        targetHostId = id;
                        break;
                    }
                    if (targetHostId == null) targetHostId = id;
                }
            }

            if (string.IsNullOrEmpty(targetHostId))
            {
                throw new Exception("Could not identify a valid Deluge daemon ID.");
            }

            // 5. Connect
            _logger.Info($"[Deluge] Connecting to daemon {targetHostId}...");
            await CallJsonRpcAsync<object>("web.connect", new object[] { targetHostId });
            
            // 6. Verify
            isConnected = await CallJsonRpcAsync<bool>("web.connected", Array.Empty<object>());
            if (!isConnected)
            {
                throw new Exception("Failed to connect WebUI to Deluge Daemon.");
            }
            _logger.Info("[Deluge] Successfully connected to daemon.");
        }

        public async Task<bool> TestConnectionAsync()
        {
            await EnsureAuthenticatedAsync();
            var connected = await CallJsonRpcAsync<bool>("web.connected", Array.Empty<object>());
            return connected;
        }

        public async Task<string> GetVersionAsync()
        {
            try 
            {
                await EnsureAuthenticatedAsync();
                return await CallJsonRpcAsync<string>("daemon.get_version", Array.Empty<object>()) ?? "Unknown";
            }
            catch (Exception ex)
            {
                _logger.Error($"[Deluge] Failed to get version: {ex.Message}");
                return "Deluge (Version Unknown)";
            }
        }

        public async Task<bool> AddTorrentAsync(string url, string? category = null)
        {
            await EnsureAuthenticatedAsync();

            // Aggressive Cleanup
            var sb = new StringBuilder();
            foreach (char c in url)
            {
                if (!char.IsControl(c) && c != '\u200B' && c != '\uFEFF' && c != '\u00A0')
                {
                    sb.Append(c);
                }
            }
            url = sb.ToString().Trim();
            
            _logger.Info($"[Deluge] Sanitized URL: '{url}'");

            var options = new Dictionary<string, object>();
            if (!string.IsNullOrEmpty(category))
            {
                options["label"] = category;
            }
            options["add_paused"] = false; 

            // 1. Explicit Magnet -> DIRECT
            if (url.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
            {
               return await AddMagnetAsync(url, options);
            }
            // 2. Local File -> DIRECT
            if (File.Exists(url)) 
            {
               return await AddFileAsync(url, options);
            }

            // 3. HTTP/Web URL -> Try to RESOLVE/DOWNLOAD (Handle Magnet Redirects)
            _logger.Info("[Deluge] resolving URL content internally...");
            
            byte[]? torrentBytes = null;
            string? resolvedMagnet = null;

            // Attempt 1: Smart HttpClient with Redirect Logic
            try 
            {
                var handler = new HttpClientHandler() 
                { 
                    AllowAutoRedirect = false, // WE HANDLE REDIRECTS MANUALLY
                    ServerCertificateCustomValidationCallback = (sender, cert, chain, sslPolicyErrors) => true
                };
                
                using var tempClient = new HttpClient(handler);
                tempClient.Timeout = TimeSpan.FromSeconds(30);
                tempClient.DefaultRequestHeaders.Add("User-Agent", "RetroArr/1.0");

                var currentUrl = url;
                int maxRedirects = 5;

                for(int i=0; i < maxRedirects; i++)
                {
                   using var response = await tempClient.GetAsync(currentUrl);
                   
                   // Check for Redirect to Magnet
                   if (response.StatusCode == System.Net.HttpStatusCode.Moved || 
                       response.StatusCode == System.Net.HttpStatusCode.Found ||
                       response.StatusCode == System.Net.HttpStatusCode.SeeOther ||
                       response.StatusCode == System.Net.HttpStatusCode.TemporaryRedirect || 
                       (int)response.StatusCode == 308)
                   {
                       var location = response.Headers.Location;
                       if (location != null)
                       {
                           var locStr = location.OriginalString;
                           _logger.Info($"[Deluge] Followed Redirect {i+1} -> {locStr}");

                           if (locStr.StartsWith("magnet:", StringComparison.OrdinalIgnoreCase))
                           {
                               _logger.Info("[Deluge] FOUND MAGNET LINK via redirect!");
                               resolvedMagnet = locStr;
                               break;
                           }
                           
                           // Logic for relative URLs if necessary, but usually Prowlarr gives absolute
                           if (!locStr.StartsWith("http")) 
                           {
                               // Basic relative handling
                               var baseUri = new Uri(currentUrl);
                               var nextUri = new Uri(baseUri, location);
                               currentUrl = nextUri.ToString();
                           }
                           else
                           {
                               currentUrl = locStr;
                           }
                           continue;
                       }
                   }

                   if (response.IsSuccessStatusCode)
                   {
                         var mediaType = response.Content.Headers.ContentType?.MediaType ?? "";
                         _logger.Info($"[Deluge] HttpClient resolved. Type: {mediaType}");
                         
                         if (mediaType.Contains("bittorrent") || mediaType.Contains("octet-stream") || currentUrl.EndsWith(".torrent"))
                         {
                             var bytes = await response.Content.ReadAsByteArrayAsync();
                             if (bytes.Length > 0) torrentBytes = bytes;
                         }
                         break;
                   }
                   
                   // If we are here, it's a non-success 200 and non-redirect (e.g. 404, 500)
                   _logger.Error($"[Deluge] Request failed with {response.StatusCode} at {currentUrl}");
                   break;
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[Deluge] HttpClient resolution failed: {ex.Message}.");
            }

            // 3.1 Did we find a magnet?
            if (!string.IsNullOrEmpty(resolvedMagnet))
            {
                return await AddMagnetAsync(resolvedMagnet, options);
            }

            // 3.2 Did we find a file?
            if (torrentBytes != null && torrentBytes.Length > 0)
            {
                 _logger.Info("[Deluge] Adding downloaded content via add_torrent_file.");
                 var base64 = Convert.ToBase64String(torrentBytes);
                 return await ExecuteDelugeAddAsync("core.add_torrent_file", new object[] { "upload.torrent", base64, options });
            }
            
            // 3.3 Try Curl (as last ditch for FILES, not magnets, as curl fails on magnets too)
            // But if the URL *redirects* to a magnet, curl will fail. 
            // We could parse curl output for "Location: magnet:..." but HttpClient logic above is better.
            
            // Fallback: Let Deluge try URL
            _logger.Error($"[Deluge] All internal resolutions failed. Falling back to native add_torrent_url.");
            if (!url.StartsWith("http", StringComparison.OrdinalIgnoreCase)) url = "https://" + url;
            
            return await ExecuteDelugeAddAsync("core.add_torrent_url", new object[] { url, options });
        }

        private async Task<byte[]?> DownloadWithCurlAsync(string url)
        {
            var tempFile = Path.GetTempFileName();
            try
            {
                var psi = new ProcessStartInfo
                {
                    FileName = "curl",
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                };
                psi.ArgumentList.Add("-L"); 
                psi.ArgumentList.Add("-k"); 
                psi.ArgumentList.Add("--max-time"); psi.ArgumentList.Add("30");
                psi.ArgumentList.Add("-o"); psi.ArgumentList.Add(tempFile);
                psi.ArgumentList.Add(url); 

                using var p = Process.Start(psi);
                if (p != null)
                {
                    await p.WaitForExitAsync();
                    if (p.ExitCode == 0 && File.Exists(tempFile))
                    {
                        var bytes = await File.ReadAllBytesAsync(tempFile);
                        if (bytes.Length > 0) return bytes;
                    }
                }
                return null;
            }
            catch (Exception ex)
            {
                 _logger.Error($"[Deluge] Curl failed: {ex.Message}");
                 return null;
            }
            finally
            {
                if (File.Exists(tempFile)) File.Delete(tempFile);
            }
        }

        private async Task<bool> AddMagnetAsync(string magnet, Dictionary<string, object> options)
        {
            _logger.Info($"[Deluge] Adding via Magnet.");
            return await ExecuteDelugeAddAsync("core.add_torrent_magnet", new object[] { magnet, options });
        }

        private async Task<bool> AddFileAsync(string path, Dictionary<string, object> options)
        {
             _logger.Info($"[Deluge] Adding via Local File.");
             var bytes = await File.ReadAllBytesAsync(path);
             var base64 = Convert.ToBase64String(bytes);
             var name = Path.GetFileName(path);
             return await ExecuteDelugeAddAsync("core.add_torrent_file", new object[] { name, base64, options });
        }

        private async Task<bool> ExecuteDelugeAddAsync(string method, object[] parameters)
        {
             try 
            {
                string? result = await CallJsonRpcAsync<string?>(method, parameters);
                
                if (string.IsNullOrEmpty(result))
                {
                    _logger.Warn($"[Deluge] Warning: {method} returned empty result. Assuming success.");
                }
                else
                {
                    _logger.Info($"[Deluge] Successfully added torrent. Hash: {result}");
                }
                
                return true;
            }
            catch (Exception ex)
            {
                if (ex.Message.Contains("Torrent already in session", StringComparison.OrdinalIgnoreCase))
                {
                    _logger.Info("[Deluge] Torrent already exists in session. Treating as success.");
                    return true;
                }

                _logger.Error($"[Deluge] {method} failed: {ex.Message}");
                throw; 
            }
        }

        public Task<bool> AddNzbAsync(string url, string? category = null)
        {
            throw new NotSupportedException("Deluge does not handle NZB downloads. Configure SABnzbd or NZBGet as a Usenet client.");
        }

        public async Task<bool> RemoveDownloadAsync(string id)
        {
            await EnsureAuthenticatedAsync();
            bool result = await CallJsonRpcAsync<bool>("core.remove_torrent", new object[] { id, true });
            return result;
        }

        public async Task<bool> PauseDownloadAsync(string id)
        {
            await EnsureAuthenticatedAsync();
            await CallJsonRpcAsync<object>("core.pause_torrent", new object[] { new[] { id } });
            return true;
        }

        public async Task<bool> ResumeDownloadAsync(string id)
        {
            await EnsureAuthenticatedAsync();
            await CallJsonRpcAsync<object>("core.resume_torrent", new object[] { new[] { id } });
            return true;
        }

        public async Task<List<DownloadStatus>> GetDownloadsAsync()
        {
            await EnsureAuthenticatedAsync();
            var keys = new[] { "name", "total_size", "state", "progress", "save_path", "hash", "label" };
            var dictResult = await CallJsonRpcAsync<Dictionary<string, DelugeTorrentStatus>>("core.get_torrents_status", new object[] { new { }, keys });
            
            var list = new List<DownloadStatus>();
            if (dictResult == null) return list;

            foreach (var kvp in dictResult)
            {
                var d = kvp.Value;
                list.Add(new DownloadStatus
                {
                    Id = kvp.Key,
                    Name = d.Name,
                    Size = d.TotalSize,
                    Progress = d.Progress,
                    State = MapState(d.State),
                    Category = d.Label,
                    DownloadPath = d.SavePath
                });
            }

            return list;
        }

        private DownloadState MapState(string state)
        {
            return state.ToLowerInvariant() switch
            {
                "downloading" => DownloadState.Downloading,
                "seeding" => DownloadState.Completed,
                "paused" => DownloadState.Paused,
                "checking" => DownloadState.Checking,
                "queuing" => DownloadState.Queued,
                "error" => DownloadState.Error,
                "active" => DownloadState.Downloading,
                "allocating" => DownloadState.Downloading,
                "moving" => DownloadState.Completed,
                _ => DownloadState.Unknown
            };
        }
    }
    
    public class DelugeRpcResponse<T>
    {
        public T Result { get; set; }
        public DelugeRpcError Error { get; set; }
        public int Id { get; set; }
    }

    public class DelugeRpcError
    {
        public string Message { get; set; }
        public int Code { get; set; }
    }

    public class DelugeTorrentStatus
    {
        [JsonPropertyName("name")]
        public string Name { get; set; } = "";
        
        [JsonPropertyName("total_size")]
        public long TotalSize { get; set; }
        
        [JsonPropertyName("state")]
        public string State { get; set; } = "";
        
        [JsonPropertyName("progress")]
        public float Progress { get; set; }
        
        [JsonPropertyName("save_path")]
        public string SavePath { get; set; } = "";
        
        [JsonPropertyName("label")]
        public string? Label { get; set; }
    }
}
