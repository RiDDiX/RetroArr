using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using RetroArr.Core.Games;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

namespace RetroArr.Core.MetadataSource.Steam
{
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Globalization", "CA1304:SpecifyCultureInfo")]
    [SuppressMessage("Microsoft.Globalization", "CA1311:SpecifyCultureForToLowerAndToUpper")]
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    [SuppressMessage("Microsoft.Globalization", "CA1303:DoNotPassLiteralsAsLocalizedParameters")]
    public class SteamClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly HttpClient _httpClient;
        private readonly string? _apiKey;

        public SteamClient(string? apiKey = null)
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://store.steampowered.com/")
            };
            // Try to find API key from environment if not provided
            _apiKey = apiKey ?? Environment.GetEnvironmentVariable("Steam__ApiKey");
        }

        public async Task<List<SteamGameResult>> SearchAsync(string query)
        {
            // Steam Store Search is unofficial and messy (often HTML scraping).
            // A better way is using the full app list which is cached, but for a direct search:
            // https://store.steampowered.com/api/storesearch/?term={query}&l=english&cc=US
            
            var url = $"api/storesearch/?term={Uri.EscapeDataString(query)}&l=english&cc=US";
            try 
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var result = JsonSerializer.Deserialize<SteamStoreSearchResponse>(content);
                    return result?.Items ?? new List<SteamGameResult>();
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Steam Search Error: {ex.Message}");
            }
            return new List<SteamGameResult>();
        }

        public async Task<SteamAppDetails?> GetGameDetailsAsync(string appId, string? lang = "english")
        {
            // https://store.steampowered.com/api/appdetails?appids={appId}&l={lang}
            var url = $"api/appdetails?appids={appId}&l={lang ?? "english"}";
            try
            {
                var response = await _httpClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    // Response is a dynamic dictionary: { "APPID": { "success": true, "data": { ... } } }
                    using var doc = JsonDocument.Parse(content);
                    if (doc.RootElement.TryGetProperty(appId, out var appElement))
                    {
                        if (appElement.TryGetProperty("success", out var success) && success.GetBoolean())
                        {
                            if (appElement.TryGetProperty("data", out var dataElement))
                            {
                                return JsonSerializer.Deserialize<SteamAppDetails>(dataElement.GetRawText());
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Steam Details Error: {ex.Message}");
            }
            return null;
        }

        // New method to fetch player's owned games (requires API Key)
        public async Task<List<SteamUserGame>> GetOwnedGamesAsync(string steamId)
        {
            if (string.IsNullOrEmpty(_apiKey))
            {
                _logger.Info("Steam API Key is missing. Cannot fetch owned games.");
                return new List<SteamUserGame>();
            }

            var url = $"https://api.steampowered.com/IPlayerService/GetOwnedGames/v0001/?key={_apiKey}&steamid={steamId}&include_appinfo=1&include_played_free_games=1&format=json";
            
            try
            {
                // Use a separate client or absolute URI for API calls (different base)
                using var apiClient = new HttpClient();
                var response = await apiClient.GetAsync(url);
                
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<SteamOwnedGamesResponse>(content, options);
                    
                    var games = result?.Response?.Games ?? new List<SteamUserGame>();
                    return games;
                }
                else 
                {
                     _logger.Error($"[SteamClient] GetOwnedGames Failed. Status: {response.StatusCode}");
                     var errorContent = await response.Content.ReadAsStringAsync();
                     _logger.Error($"[SteamClient] Error Body: {errorContent}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"Steam GetOwnedGames Exception: {ex.Message}");
            }
            
            return new List<SteamUserGame>();
        }

        public async Task<SteamPlayerProfile?> GetPlayerProfileAsync(string steamId)
        {
            if (string.IsNullOrEmpty(_apiKey)) return null;

            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_apiKey}&steamids={steamId}";
            try
            {
                using var apiClient = new HttpClient();
                var response = await apiClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                     var content = await response.Content.ReadAsStringAsync();
                     var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                     var result = JsonSerializer.Deserialize<SteamPlayerSummariesResponse>(content, options);
                     return result?.Response?.Players?.FirstOrDefault();
                }
            }
             catch (Exception ex)
            {
                _logger.Error($"Steam GetPlayerProfile Error: {ex.Message}");
            }
            return null;
        }
        
        public async Task<List<SteamRecentGame>> GetRecentlyPlayedGamesAsync(string steamId)
        {
             if (string.IsNullOrEmpty(_apiKey)) return new List<SteamRecentGame>();
             
             var url = $"https://api.steampowered.com/IPlayerService/GetRecentlyPlayedGames/v0001/?key={_apiKey}&steamid={steamId}&format=json";
             try
             {
                 using var apiClient = new HttpClient();
                 var response = await apiClient.GetAsync(url);
                 if (response.IsSuccessStatusCode)
                 {
                     var content = await response.Content.ReadAsStringAsync();
                     var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                     var result = JsonSerializer.Deserialize<SteamRecentGamesResponse>(content, options);
                     return result?.Response?.Games ?? new List<SteamRecentGame>();
                 }
             }
             catch
             {
                 // Ignore errors
             }
             return new List<SteamRecentGame>();
        }
        
        public async Task<List<SteamNewsItem>> GetGameNewsAsync(string appId, int count = 3, int maxLength = 300)
        {
            var url = $"https://api.steampowered.com/ISteamNews/GetNewsForApp/v0002/?appid={appId}&count={count}&maxlength={maxLength}&format=json";
            try 
            {
                using var apiClient = new HttpClient();
                apiClient.DefaultRequestHeaders.Add("User-Agent", "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/91.0.4472.124 Safari/537.36");
                var response = await apiClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<SteamNewsResponse>(content, options);
                    var items = result?.AppNews?.NewsItems ?? new List<SteamNewsItem>();
                    _logger.Info($"[SteamClient] GetGameNews for {appId}: Found {items.Count} items.");
                    return items;
                }
                else
                {
                    _logger.Error($"[SteamClient] GetGameNews for {appId} Failed: {response.StatusCode}");
                }
            }
            catch (Exception ex)
            {
                _logger.Error($"[SteamClient] GetGameNews for {appId} Error: {ex.Message}");
            }
            return new List<SteamNewsItem>();
        }

        public async Task<int> GetSteamLevelAsync(string steamId)
        {
            var url = $"https://api.steampowered.com/IPlayerService/GetSteamLevel/v1/?key={_apiKey}&steamid={steamId}";
            try
            {
                using var apiClient = new HttpClient();
                var response = await apiClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<SteamLevelResponse>(content, options);
                    return result?.Response?.PlayerLevel ?? 0;
                }
            }
            catch { }
            return 0;
        }

        public async Task<(int Achieved, int Total)> GetPlayerAchievementsAsync(string steamId, string appId)
        {
            var url = $"https://api.steampowered.com/ISteamUserStats/GetPlayerAchievements/v0001/?appid={appId}&key={_apiKey}&steamid={steamId}";
            try
            {
                using var apiClient = new HttpClient();
                var response = await apiClient.GetAsync(url);
                 if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<SteamUserStatsResponse>(content, options);
                    
                    if (result?.PlayerStats?.Achievements != null)
                    {
                        var total = result.PlayerStats.Achievements.Count;
                        var achieved = result.PlayerStats.Achievements.Count(a => a.Achieved == 1);
                        return (achieved, total);
                    }
                }
            }
            catch { }
            return (0, 0);
        }

        public async Task<List<string>> GetFriendListAsync(string steamId)
        {
            var url = $"https://api.steampowered.com/ISteamUser/GetFriendList/v0001/?key={_apiKey}&steamid={steamId}&relationship=friend";
            try
            {
                using var apiClient = new HttpClient();
                var response = await apiClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<SteamFriendsResponse>(content, options);
                    return result?.Friendslist?.Friends?.Select(f => f.SteamId).ToList() ?? new List<string>();
                }
            }
            catch { }
            return new List<string>();
        }

        public async Task<List<SteamPlayerProfile>> GetPlayerProfilesAsync(IEnumerable<string> steamIds)
        {
            if (!steamIds.Any()) return new List<SteamPlayerProfile>();
            
            // Steam API allows comma separated IDs
            var ids = string.Join(",", steamIds);
            var url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v0002/?key={_apiKey}&steamids={ids}";
            
            try
            {
                using var apiClient = new HttpClient();
                var response = await apiClient.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var content = await response.Content.ReadAsStringAsync();
                    var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                    var result = JsonSerializer.Deserialize<SteamPlayerSummariesResponse>(content, options);
                    return result?.Response?.Players ?? new List<SteamPlayerProfile>();
                }
            }
            catch { }
            return new List<SteamPlayerProfile>();
        }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamStoreSearchResponse
    {
        [JsonPropertyName("items")]
        public List<SteamGameResult> Items { get; set; } = new List<SteamGameResult>();
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamGameResult
    {
        [JsonPropertyName("id")]
        public int Id { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("tiny_image")]
        public string TinyImage { get; set; }
        
        [JsonPropertyName("price")]
        public SteamPriceInfo Price { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamPriceInfo
    {
        [JsonPropertyName("initial")]
        public int Initial { get; set; }
        
        [JsonPropertyName("final")]
        public int Final { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamAppDetails
    {
        [JsonPropertyName("type")]
        public string Type { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("steam_appid")]
        public int SteamAppId { get; set; }
        
        [JsonPropertyName("required_age")]
        public object RequiredAge { get; set; } // Can be int or string
        
        [JsonPropertyName("is_free")]
        public bool IsFree { get; set; }
        
        [JsonPropertyName("detailed_description")]
        public string DetailedDescription { get; set; }
        
        [JsonPropertyName("about_the_game")]
        public string AboutTheGame { get; set; }
        
        [JsonPropertyName("short_description")]
        public string ShortDescription { get; set; }
        
        [JsonPropertyName("header_image")]
        public string HeaderImage { get; set; }
        
        [JsonPropertyName("website")]
        public string Website { get; set; }
        
        [JsonPropertyName("developers")]
        public List<string> Developers { get; set; }
        
        [JsonPropertyName("publishers")]
        public List<string> Publishers { get; set; }
        
        [JsonPropertyName("platforms")]
        public SteamPlatforms Platforms { get; set; }
        
        [JsonPropertyName("categories")]
        public List<SteamCategory> Categories { get; set; }
        
        [JsonPropertyName("genres")]
        public List<SteamGenre> Genres { get; set; }
        
        [JsonPropertyName("screenshots")]
        public List<SteamScreenshot> Screenshots { get; set; }
        
        [JsonPropertyName("movies")]
        public List<SteamMovie> Movies { get; set; }
        
        [JsonPropertyName("release_date")]
        public SteamReleaseDate ReleaseDate { get; set; }
        
        [JsonPropertyName("background")]
        public string Background { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamPlatforms
    {
        [JsonPropertyName("windows")]
        public bool Windows { get; set; }
        [JsonPropertyName("mac")]
        public bool Mac { get; set; }
        [JsonPropertyName("linux")]
        public bool Linux { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamCategory { [JsonPropertyName("description")] public string Description { get; set; } }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamGenre { [JsonPropertyName("description")] public string Description { get; set; } }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamScreenshot { [JsonPropertyName("path_thumbnail")] public string Thumbnail { get; set; } [JsonPropertyName("path_full")] public string Full { get; set; } }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamMovie { [JsonPropertyName("webm")] public SteamMovieUrl Webm { get; set; } [JsonPropertyName("mp4")] public SteamMovieUrl Mp4 { get; set; } [JsonPropertyName("thumbnail")] public string Thumbnail { get; set; } }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class SteamMovieUrl { [JsonPropertyName("480")] public string Url480 { get; set; } [JsonPropertyName("max")] public string UrlMax { get; set; } }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    public class SteamReleaseDate { [JsonPropertyName("coming_soon")] public bool ComingSoon { get; set; } [JsonPropertyName("date")] public string Date { get; set; } }

    // User / Owned Games Classes
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamOwnedGamesResponse
    {
        [JsonPropertyName("response")]
        public SteamOwnedGamesResponseData Response { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamOwnedGamesResponseData
    {
        [JsonPropertyName("game_count")]
        public int GameCount { get; set; }
        
        [JsonPropertyName("games")]
        public List<SteamUserGame> Games { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class SteamUserGame
    {
        [JsonPropertyName("appid")]
        public int AppId { get; set; }
        
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("playtime_forever")]
        public int PlaytimeForever { get; set; } // Minutes
        
        [JsonPropertyName("img_icon_url")]
        public string ImgIconUrl { get; set; }
        
        [JsonPropertyName("playtime_2weeks")]
        public int? Playtime2Weeks { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamPlayerSummariesResponse
    {
        [JsonPropertyName("response")]
        public SteamPlayerSummariesData Response { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamPlayerSummariesData
    {
        [JsonPropertyName("players")]
        public List<SteamPlayerProfile> Players { get; set; }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class SteamPlayerProfile
    {
        [JsonPropertyName("steamid")]
        public string SteamId { get; set; }
        
        [JsonPropertyName("personaname")]
        public string PersonaName { get; set; }
        
        [JsonPropertyName("profileurl")]
        public string ProfileUrl { get; set; } // Suppressed CA1056 here
        
        [JsonPropertyName("avatar")]
        public string Avatar { get; set; }
        
        [JsonPropertyName("avatarmedium")]
        public string AvatarMedium { get; set; }
        
        [JsonPropertyName("avatarfull")]
        public string AvatarFull { get; set; }

        public string AvatarUrl => AvatarFull ?? AvatarMedium ?? Avatar;
        
        [JsonPropertyName("personastate")]
        public int PersonaState { get; set; } // 0 - Offline, 1 - Online, etc

        [JsonPropertyName("realname")]
        public string RealName { get; set; }

        [JsonPropertyName("loccountrycode")]
        public string LocCountryCode { get; set; }

        [JsonPropertyName("timecreated")]
        public long TimeCreated { get; set; }

        [JsonPropertyName("gameextrainfo")]
        public string GameExtraInfo { get; set; }
    }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamRecentGamesResponse
    {
        [JsonPropertyName("response")]
        public SteamRecentGamesData Response { get; set; }
    }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamRecentGamesData
    {
         [JsonPropertyName("total_count")]
         public int TotalCount { get; set; }
         
         [JsonPropertyName("games")]
         public List<SteamRecentGame> Games { get; set; }
    }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class SteamRecentGame
    {
         [JsonPropertyName("appid")]
         public int AppId { get; set; }
         
         [JsonPropertyName("name")]
         public string Name { get; set; }
         
         [JsonPropertyName("playtime_2weeks")]
         public int Playtime2Weeks { get; set; }
         
         [JsonPropertyName("playtime_forever")]
         public int PlaytimeForever { get; set; }
         
         [JsonPropertyName("img_icon_url")]
         public string ImgIconUrl { get; set; }
         
         // Helper to build full icon URL (Steam legacy)
         public string IconUrl => !string.IsNullOrEmpty(ImgIconUrl) 
            ? $"http://media.steampowered.com/steamcommunity/public/images/apps/{AppId}/{ImgIconUrl}.jpg" 
            : "";
    }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamNewsResponse
    {
        [JsonPropertyName("appnews")]
        public SteamAppNews AppNews { get; set; }
    }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class SteamAppNews
    {
        [JsonPropertyName("appid")]
        public int AppId { get; set; }
        
        [JsonPropertyName("newsitems")]
        public List<SteamNewsItem> NewsItems { get; set; }
    }
    
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1056:UriPropertiesShouldNotBeStrings")]
    public class SteamNewsItem
    {
        [JsonPropertyName("gid")]
        public string Gid { get; set; }
        
        [JsonPropertyName("title")]
        public string Title { get; set; }
        
        [JsonPropertyName("url")]
        public string Url { get; set; }
        
        [JsonPropertyName("is_external_url")]
        public bool IsExternalUrl { get; set; }
        
        [JsonPropertyName("author")]
        public string Author { get; set; }
        
        [JsonPropertyName("contents")]
        public string Contents { get; set; }
        
        [JsonPropertyName("feedlabel")]
        public string FeedLabel { get; set; }
        
        [JsonPropertyName("date")]
        public long DateUnix { get; set; }
        
        [JsonPropertyName("feedname")]
        public string FeedName { get; set; }
        
        public DateTime Date => DateTimeOffset.FromUnixTimeSeconds(DateUnix).DateTime;
    }

    public class SteamLevelResponse
    {
        [JsonPropertyName("response")]
        public SteamLevelData Response { get; set; }
    }

    public class SteamLevelData
    {
        [JsonPropertyName("player_level")]
        public int PlayerLevel { get; set; }
    }

    public class SteamUserStatsResponse
    {
        [JsonPropertyName("playerstats")]
        public SteamUserStatsData PlayerStats { get; set; }
    }

    public class SteamUserStatsData
    {
        [JsonPropertyName("achievements")]
        public List<SteamAchievementStatus> Achievements { get; set; }
    }

    public class SteamAchievementStatus
    {
        [JsonPropertyName("apiname")]
        public string ApiName { get; set; }
        
        [JsonPropertyName("achieved")]
        public int Achieved { get; set; }
    }

    public class SteamFriendsResponse
    {
        [JsonPropertyName("friendslist")]
        public SteamFriendsList Friendslist { get; set; }
    }

    public class SteamFriendsList
    {
        [JsonPropertyName("friends")]
        public List<SteamFriend> Friends { get; set; }
    }

    public class SteamFriend
    {
        [JsonPropertyName("steamid")]
        public string SteamId { get; set; }
        
        [JsonPropertyName("relationship")]
        public string Relationship { get; set; }
        
        [JsonPropertyName("friend_since")]
        public long FriendSince { get; set; }
    }
}
