using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Mvc;
using RetroArr.Core.Prowlarr;
using RetroArr.Core.Jackett;
using RetroArr.Core.Configuration;
using RetroArr.Core.Indexers;
using RetroArr.Core.Games;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Api.V3.Search
{
    [ApiController]
    [Route("api/v3/[controller]")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Performance", "CA1860:AvoidUsingAnyWhenUseCount")]
    [SuppressMessage("Microsoft.Performance", "CA1849:CallAsyncMethodsWhenInAnAsyncMethod")]
    [SuppressMessage("Microsoft.Reliability", "CA2008:DoNotCreateTasksWithoutPassingATaskScheduler")]
    public class SearchController : ControllerBase
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ReleaseSearch);
        private readonly ConfigurationService _configurationService;
        private readonly System.Net.Http.IHttpClientFactory _httpClientFactory;

        public SearchController(ConfigurationService configurationService, System.Net.Http.IHttpClientFactory httpClientFactory)
        {
            _configurationService = configurationService;
            _httpClientFactory = httpClientFactory;
        }

        [HttpGet]
        public async Task<IActionResult> Search([FromQuery] string query, [FromQuery] string? categories = null)
        {
            if (string.IsNullOrWhiteSpace(query))
            {
                return BadRequest("Query parameter is required");
            }

            // Sanitize query: strip characters that break indexer searches
            // e.g. "Anno 1404: History Edition" → "Anno 1404 History Edition"
            var sanitized = System.Text.RegularExpressions.Regex.Replace(query, @"[:\(\)\[\]\{\}""'™®©\-–—]", " ");
            // Strip common edition/version suffixes that make queries too specific
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, 
                @"\b(Definitive|Complete|Ultimate|Gold|Game of the Year|GOTY|Deluxe|Premium|Enhanced|Remastered|HD|Anniversary|Legacy|Legendary|Standard|Digital|Special)\s*(Edition|Collection|Version|Bundle)?\b", 
                "", System.Text.RegularExpressions.RegexOptions.IgnoreCase);
            sanitized = System.Text.RegularExpressions.Regex.Replace(sanitized, @"\s{2,}", " ").Trim();
            if (!string.IsNullOrEmpty(sanitized))
                query = sanitized;

            // Load fresh settings from config on each request (not from singleton)
            var prowlarrSettings = _configurationService.LoadProwlarrSettings();
            var jackettSettings = _configurationService.LoadJackettSettings();
            var hydraConfigs = _configurationService.LoadHydraIndexers().Where(h => h.Enabled).ToList();

            _logger.Info("Query: {Query}, Prowlarr: Configured={ProwlarrConfigured}/Enabled={ProwlarrEnabled}, Jackett: Configured={JackettConfigured}/Enabled={JackettEnabled}, Hydra: {HydraCount} enabled",
                query, prowlarrSettings.IsConfigured, prowlarrSettings.Enabled, jackettSettings.IsConfigured, jackettSettings.Enabled, hydraConfigs.Count);

            // Track provider diagnostics so the frontend can show WHY there are no results
            var providers = new List<object>();

            if (!prowlarrSettings.IsConfigured && !jackettSettings.IsConfigured && !hydraConfigs.Any())
            {
                return Ok(new
                {
                    results = Array.Empty<object>(),
                    providers = Array.Empty<object>(),
                    diagnostics = new { configured = false, message = "No indexers configured. Add Prowlarr, Jackett, or a Hydra source in Settings." }
                });
            }

            var results = new List<SearchResult>();
            var tasks = new List<(string Name, Task<List<SearchResult>> Task)>();
            
            var sharedClient = _httpClientFactory.CreateClient("");
            sharedClient.Timeout = TimeSpan.FromSeconds(60);  
            
            int[]? categoryIds = null;
            if (!string.IsNullOrEmpty(categories))
            {
                categoryIds = categories.Split(',')
                                       .Select(c => int.TryParse(c, out var id) ? id : (int?)null)
                                       .Where(id => id.HasValue)
                                       .Select(id => id!.Value)
                                       .ToArray();
            }

            // 1. Search Prowlarr
            if (prowlarrSettings.IsConfigured && prowlarrSettings.Enabled)
            {
                var prowlarrClient = new ProwlarrClient(prowlarrSettings.Url, prowlarrSettings.ApiKey);
                tasks.Add(("Prowlarr", prowlarrClient.SearchAsync(query, indexerIds: null, categories: categoryIds)));
            }
            else if (!prowlarrSettings.IsConfigured)
            {
                providers.Add(new { name = "Prowlarr", status = "not_configured", error = "Not configured" });
            }
            else if (!prowlarrSettings.Enabled)
            {
                providers.Add(new { name = "Prowlarr", status = "disabled", error = "Disabled" });
            }

            // 2. Search Jackett
            if (jackettSettings.IsConfigured && jackettSettings.Enabled)
            {
                var jackettClient = new JackettClient(jackettSettings.Url, jackettSettings.ApiKey);
                tasks.Add(("Jackett", jackettClient.SearchAsync(query, categoryIds).ContinueWith(t =>
                {
                    if (t.IsFaulted) throw t.Exception!.InnerException ?? t.Exception;
                    return t.Result.Select(j => new SearchResult
                    {
                        Title = j.Title,
                        Guid = j.Guid,
                        Size = j.Size,
                        IndexerName = j.Tracker,
                        Seeders = j.Seeders,
                        Leechers = j.Leechers,
                        PeersFromIndexer = j.Peers,
                        PublishDate = j.PublishDate,
                        DownloadUrl = j.DownloadUrl,
                        MagnetUrl = j.MagnetUri,
                        InfoUrl = j.Guid,
                        Protocol = j.Protocol,
                        Provider = "Jackett"
                    }).ToList();
                })));
            }

            // 3. Search HydraSources
            foreach (var hydraConfig in hydraConfigs)
            {
                var hydraClient = new HydraIndexer(sharedClient, hydraConfig.Name, hydraConfig.Url);
                tasks.Add((hydraConfig.Name, hydraClient.SearchAsync(query)));
            }

            try
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(60));
                var allTasks = tasks.Select(t => t.Task).ToArray();
                var searchTask = Task.WhenAll(allTasks);
                
                var completedTask = await Task.WhenAny(searchTask, timeoutTask);
                bool timedOut = completedTask == timeoutTask;

                if (timedOut)
                {
                    _logger.Warn("Search timed out after 60 seconds");
                }

                // Collect results and errors from each provider
                for (int i = 0; i < tasks.Count; i++)
                {
                    var (name, task) = tasks[i];
                    if (task.IsCompletedSuccessfully && task.Result != null)
                    {
                        results.AddRange(task.Result);
                        providers.Add(new { name, status = "ok", count = task.Result.Count, error = (string?)null });
                        _logger.Info("{Provider}: {Count} results", name, task.Result.Count);
                    }
                    else if (task.IsFaulted)
                    {
                        var errorMsg = task.Exception?.InnerException?.Message ?? task.Exception?.Message ?? "Unknown error";
                        providers.Add(new { name, status = "error", count = 0, error = errorMsg });
                        _logger.Error("{Provider} FAILED: {Error}", name, errorMsg);
                    }
                    else if (timedOut && !task.IsCompleted)
                    {
                        providers.Add(new { name, status = "timeout", count = 0, error = "Timed out after 60s" });
                        _logger.Warn("{Provider}: timed out", name);
                    }
                }

                _logger.Info("Total results: {Count}", results.Count);

                // De-duplicate by title and size
                var uniqueResults = results
                    .GroupBy(r => new { r.Title, r.Size })
                    .Select(g => g.First())
                    .ToList();

                // Detect platform for each result
                foreach (var result in uniqueResults)
                {
                    PlatformDetector.DetectPlatform(result);
                }

                _logger.Info("Returning {Count} unique results", uniqueResults.Count);

                return Ok(new
                {
                    results = uniqueResults.Select(r => new {
                        r.Title,
                        r.Guid,
                        r.DownloadUrl,
                        r.MagnetUrl,
                        r.InfoUrl,
                        r.IndexerId,
                        r.IndexerName,
                        r.Size,
                        Seeders = r.EffectiveSeeders,
                        Leechers = r.EffectiveLeechers,
                        r.Grabs,
                        r.PublishDate,
                        r.Provider,
                        r.Protocol,
                        r.DetectedPlatform,
                        r.PlatformFolder,
                        r.FormattedSize,
                        r.FormattedAge,
                        // Comma-separated category IDs for frontend platform detection
                        Category = string.Join(",", r.Categories?.SelectMany(c =>
                        {
                            var ids = new List<int> { c.Id };
                            if (c.SubCategories != null)
                                ids.AddRange(c.SubCategories.Select(sc => sc.Id));
                            return ids;
                        }) ?? Enumerable.Empty<int>()),
                        // Human-readable category name for display
                        CategoryName = r.Category,
                        r.Categories
                    }),
                    providers,
                    diagnostics = new { configured = true, message = (string?)null }
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Search error: {Message}", ex.Message);
                return StatusCode(500, new { error = ex.Message });
            }
        }

        [HttpGet("debug/prowlarr")]
        public async Task<IActionResult> DebugProwlarrSearch([FromQuery] string query, [FromQuery] string? categories = null)
        {
            try
            {
                var prowlarrSettings = _configurationService.LoadProwlarrSettings();
                
                if (!prowlarrSettings.IsConfigured)
                {
                    return Ok(new { 
                        success = false, 
                        message = "Prowlarr is not configured",
                        rawResponse = (string?)null
                    });
                }

                // Build search URL
                var categoryQuery = "";
                if (!string.IsNullOrEmpty(categories))
                {
                    var cats = categories.Split(',').Select(c => c.Trim());
                    categoryQuery = "&" + string.Join("&", cats.Select(c => $"categories={c}"));
                }

                var searchUrl = $"/api/v1/search?query={Uri.EscapeDataString(query ?? "test")}{categoryQuery}";
                
                _logger.Debug("Prowlarr debug search URL: {Url}", RetroArr.Core.Logging.LogRedactor.RedactUrl($"{prowlarrSettings.Url}{searchUrl}"));

                using var httpClient = new HttpClient { BaseAddress = new Uri(prowlarrSettings.Url) };
                using var request = new HttpRequestMessage(HttpMethod.Get, searchUrl);
                request.Headers.Add("X-Api-Key", prowlarrSettings.ApiKey);

                var response = await httpClient.SendAsync(request);
                var rawContent = await response.Content.ReadAsStringAsync();

                _logger.Debug("Prowlarr debug response: Status={StatusCode}, Length={Length}", (int)response.StatusCode, rawContent.Length);

                return Ok(new { 
                    success = response.IsSuccessStatusCode,
                    statusCode = (int)response.StatusCode,
                    searchUrl = searchUrl,
                    prowlarrUrl = prowlarrSettings.Url,
                    responseLength = rawContent.Length,
                    rawResponse = rawContent.Length > 5000 ? rawContent.Substring(0, 5000) + "... (truncated)" : rawContent
                });
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Prowlarr debug search error: {Message}", ex.Message);
                return Ok(new { 
                    success = false, 
                    message = $"Error: {ex.Message}",
                    rawResponse = (string?)null
                });
            }
        }

        [HttpGet("test/prowlarr")]
        public async Task<IActionResult> TestProwlarrSavedSettings()
        {
            try
            {
                var prowlarrSettings = _configurationService.LoadProwlarrSettings();
                
                if (!prowlarrSettings.IsConfigured)
                {
                    return Ok(new { 
                        connected = false, 
                        message = "Prowlarr is not configured. Please configure it in Settings -> Indexer.",
                        url = prowlarrSettings.Url,
                        isConfigured = false
                    });
                }

                var prowlarrClient = new ProwlarrClient(prowlarrSettings.Url, prowlarrSettings.ApiKey);
                var isConnected = await prowlarrClient.TestConnectionAsync();
                
                // Try to get indexer count if connected
                int indexerCount = 0;
                if (isConnected)
                {
                    try
                    {
                        indexerCount = await prowlarrClient.GetIndexerCountAsync();
                    }
                    catch { }
                }

                return Ok(new { 
                    connected = isConnected, 
                    message = isConnected 
                        ? $"Connected to Prowlarr at {prowlarrSettings.Url}" 
                        : "Failed to connect. Check URL and API Key.",
                    url = prowlarrSettings.Url,
                    indexerCount = indexerCount,
                    isConfigured = true,
                    enabled = prowlarrSettings.Enabled
                });
            }
            catch (Exception ex)
            {
                return Ok(new { 
                    connected = false, 
                    message = $"Connection error: {ex.Message}"
                });
            }
        }

        [HttpPost("test")]
        public async Task<IActionResult> TestConnection([FromBody] TestConnectionRequest request)
        {
            try
            {
                // Resolve masked API key from saved config
                var apiKey = request.ApiKey;
                if (IsMaskedOrEmpty(apiKey))
                {
                    apiKey = request.Type == "jackett"
                        ? _configurationService.LoadJackettSettings().ApiKey
                        : _configurationService.LoadProwlarrSettings().ApiKey;
                }

                if (request.Type == "jackett")
                {
                    var jackettClient = new JackettClient(request.Url, apiKey);
                    var isConnected = await jackettClient.TestConnectionAsync();
                    return Ok(new { 
                        connected = isConnected, 
                        message = isConnected ? "Connection successful" : "Failed to connect. Check URL and API Key." 
                    });
                }
                else
                {
                    var prowlarrClient = new ProwlarrClient(request.Url, apiKey);
                    var isConnected = await prowlarrClient.TestConnectionAsync();
                    return Ok(new { 
                        connected = isConnected, 
                        message = isConnected ? "Connection successful" : "Failed to connect. Check URL and API Key." 
                    });
                }
            }
            catch (Exception ex)
            {
                return Ok(new { 
                    connected = false, 
                    message = $"Connection error: {ex.Message}" 
                });
            }
        }

        private const string MaskedPlaceholder = "\u2022\u2022\u2022\u2022\u2022\u2022\u2022\u2022";

        private static bool IsMaskedOrEmpty(string? value)
        {
            return string.IsNullOrWhiteSpace(value) || value.Contains(MaskedPlaceholder);
        }

        /// <summary>
        /// Get available platforms for download destination selection
        /// </summary>
        [HttpGet("platforms")]
        public ActionResult GetAvailablePlatforms()
        {
            var platforms = PlatformDefinitions.AllPlatforms
                .Where(p => PlatformService.IsEnabled(p.Id, p.Enabled))
                .Select(p => new { 
                    id = p.Id,
                    name = p.Name, 
                    folder = p.FolderName,
                    slug = p.Slug,
                    category = p.Category
                })
                .OrderBy(p => p.category)
                .ThenBy(p => p.name)
                .ToList();

            return Ok(platforms);
        }
    }

    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class TestConnectionRequest
    {
        public string Url { get; set; } = string.Empty;
        public string ApiKey { get; set; } = string.Empty;
        public string Type { get; set; } = "prowlarr"; // "prowlarr" or "jackett"
    }
}
