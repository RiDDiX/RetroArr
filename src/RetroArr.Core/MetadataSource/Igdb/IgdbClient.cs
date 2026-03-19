using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.MetadataSource.Igdb
{
    [SuppressMessage("Microsoft.Performance", "CA1822:MarkMembersAsStatic")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Design", "CA1055:UriReturnValuesShouldNotBeStrings")]
    [SuppressMessage("Microsoft.Reliability", "CA2000:Dispose objects before losing scope")]
    [SuppressMessage("Microsoft.Design", "CA1001:TypesThatOwnDisposableFieldsShouldBeDisposable")]
    [SuppressMessage("Microsoft.Usage", "CA2234:PassSystemUriObjectsInsteadOfStrings")]
    [SuppressMessage("Microsoft.Usage", "CA2201:DoNotRaiseReservedExceptionTypes")]
    [SuppressMessage("Microsoft.Globalization", "CA1307:SpecifyStringComparison")]
    [SuppressMessage("Microsoft.Design", "CA1062:ValidateArgumentsOfPublicMethods")]
    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    public class IgdbClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly HttpClient _httpClient;
        private readonly string _clientId;
        private readonly string _clientSecret;
        private string? _accessToken;
        private DateTime _tokenExpiration;

        public IgdbClient(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
            _httpClient = new HttpClient();
        }

        private async Task EnsureTokenAsync()
        {
            if (_accessToken != null && DateTime.UtcNow < _tokenExpiration) return;

            var url = $"https://id.twitch.tv/oauth2/token?client_id={_clientId}&client_secret={_clientSecret}&grant_type=client_credentials";
            var response = await _httpClient.PostAsync(new Uri(url), null);
            
            if (!response.IsSuccessStatusCode)
            {
                throw new Exception($"Failed to authenticate with IGDB: {response.StatusCode}");
            }

            var content = await response.Content.ReadAsStringAsync();
            var result = JsonSerializer.Deserialize<TwitchAuthResponse>(content);

            if (result == null) throw new Exception("Failed to deserialize auth response");
            
            _accessToken = result.AccessToken;
            _tokenExpiration = DateTime.UtcNow.AddSeconds(result.ExpiresIn - 60); // Buffer
        }

        public async Task<List<IgdbGame>> SearchGamesAsync(string query, int? platformId = null, string? lang = null, string? serial = null)
        {
            await EnsureTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.igdb.com/v4/games"));
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            // Basic fields to fetch
            var fields = "name, summary, storyline, cover.image_id, screenshots.image_id, artworks.image_id, first_release_date, total_rating, total_rating_count, genres.name, involved_companies.company.name, involved_companies.developer, involved_companies.publisher, external_games.category, external_games.uid, platforms.name, platforms.abbreviation, alternative_names.name, alternative_names.comment";

            // If lang provided, request localized names (not fully supported by IGDB API in search directly, post-filtering needed)
            // Note: IGDB doesn't have a simple "lang" parameter for search. We fetch data and filter logic in Service.
            
            string body;
            
            if (serial != null)
            {
                // IGDB doesn't index serials; fall back to name search.
            }
            
            if (platformId.HasValue)
            {
                body = $"fields {fields}; search \"{query}\"; where platforms = ({platformId.Value}) & version_parent = null; limit 100;";
            }
            else
            {
                body = $"fields {fields}; search \"{query}\"; where version_parent = null; limit 100;";
            }

            request.Content = new StringContent(body);
            var response = await _httpClient.SendAsync(request);
            
            if (!response.IsSuccessStatusCode)
            {
                var errorContent = await response.Content.ReadAsStringAsync();
                var msg = $"[IgdbClient] Search Failed. Status: {response.StatusCode}. Body: {errorContent}";
                _logger.Info(msg);
                throw new HttpRequestException(msg, null, response.StatusCode);
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<IgdbGame>>(content, options) ?? new List<IgdbGame>();
        }
        
        public async Task<List<IgdbGame>> GetGamesByIdsAsync(IEnumerable<int> ids, string? lang = null)
        {
             if (!ids.Any()) return new List<IgdbGame>();
             
             await EnsureTokenAsync();
             
             var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.igdb.com/v4/games"));
             request.Headers.Add("Client-ID", _clientId);
             request.Headers.Add("Authorization", $"Bearer {_accessToken}");
             
             var fields = "name, summary, storyline, cover.image_id, screenshots.image_id, artworks.image_id, first_release_date, total_rating, total_rating_count, genres.name, involved_companies.company.name, involved_companies.developer, involved_companies.publisher, external_games.category, external_games.uid, platforms.name, platforms.abbreviation, alternative_names.name, alternative_names.comment";
             
             var idString = string.Join(",", ids);
             var body = $"fields {fields}; where id = ({idString}); limit 50;";
             
             request.Content = new StringContent(body);
             var response = await _httpClient.SendAsync(request);
             
             if (!response.IsSuccessStatusCode) 
             {
                 var errorContent = await response.Content.ReadAsStringAsync();
                 var msg = $"[IgdbClient] GetGamesByIds Failed. Status: {response.StatusCode}. Body: {errorContent}";
                 _logger.Info(msg);
                 throw new HttpRequestException(msg, null, response.StatusCode);
             }
             
             var content = await response.Content.ReadAsStringAsync();
             var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
             return JsonSerializer.Deserialize<List<IgdbGame>>(content, options) ?? new List<IgdbGame>();
        }

        public static string GetImageUrl(string imageId, ImageSize size)
        {
            if (string.IsNullOrEmpty(imageId)) return "";
            var sizeStr = size switch
            {
                ImageSize.CoverSmall => "cover_small",
                ImageSize.CoverBig => "cover_big",
                ImageSize.ScreenshotMed => "screenshot_med",
                ImageSize.ScreenshotBig => "screenshot_big",
                ImageSize.ScreenshotHuge => "screenshot_huge",
                ImageSize.LogoMed => "logo_med",
                ImageSize.Thumb => "thumb",
                ImageSize.Micro => "micro",
                ImageSize.HD => "720p",
                ImageSize.FullHD => "1080p",
                _ => "cover_big"
            };
            return $"https://images.igdb.com/igdb/image/upload/t_{sizeStr}/{imageId}.jpg";
        }

        /// <summary>
        /// Get similar games based on IGDB's similar_games field
        /// </summary>
        public async Task<List<IgdbGame>> GetSimilarGamesAsync(int igdbId, int limit = 10)
        {
            await EnsureTokenAsync();

            // First get the game with its similar_games IDs
            var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.igdb.com/v4/games"));
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var body = $"fields similar_games; where id = {igdbId};";
            request.Content = new StringContent(body);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
            {
                _logger.Error($"[IgdbClient] GetSimilarGames failed: {response.StatusCode}");
                return new List<IgdbGame>();
            }

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            var games = JsonSerializer.Deserialize<List<IgdbGameWithSimilar>>(content, options);

            if (games == null || games.Count == 0 || games[0].SimilarGames == null || games[0].SimilarGames.Count == 0)
                return new List<IgdbGame>();

            // Now fetch the actual similar games data
            var similarIds = games[0].SimilarGames.Take(limit);
            return await GetGamesByIdsAsync(similarIds);
        }

        /// <summary>
        /// Get games by genre (for recommendations)
        /// </summary>
        public async Task<List<IgdbGame>> GetGamesByGenreAsync(int genreId, int? excludeGameId = null, int limit = 10)
        {
            await EnsureTokenAsync();

            var request = new HttpRequestMessage(HttpMethod.Post, new Uri("https://api.igdb.com/v4/games"));
            request.Headers.Add("Client-ID", _clientId);
            request.Headers.Add("Authorization", $"Bearer {_accessToken}");

            var fields = "name, summary, cover.image_id, first_release_date, total_rating, genres.name, platforms.abbreviation";
            var where = excludeGameId.HasValue 
                ? $"where genres = ({genreId}) & id != {excludeGameId} & total_rating != null & cover != null"
                : $"where genres = ({genreId}) & total_rating != null & cover != null";

            var body = $"fields {fields}; {where}; sort total_rating desc; limit {limit};";
            request.Content = new StringContent(body);

            var response = await _httpClient.SendAsync(request);
            if (!response.IsSuccessStatusCode)
                return new List<IgdbGame>();

            var content = await response.Content.ReadAsStringAsync();
            var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
            return JsonSerializer.Deserialize<List<IgdbGame>>(content, options) ?? new List<IgdbGame>();
        }

        [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
        [SuppressMessage("Microsoft.Performance", "CA1852:SealInternalTypes")]
        private class TwitchAuthResponse
        {
            [JsonPropertyName("access_token")]
            public string AccessToken { get; set; } = string.Empty;

            [JsonPropertyName("expires_in")]
            public int ExpiresIn { get; set; }

            [JsonPropertyName("token_type")]
            public string TokenType { get; set; } = string.Empty;
        }
    }

    public enum ImageSize
    {
        CoverSmall, CoverBig, ScreenshotMed, ScreenshotBig, ScreenshotHuge, LogoMed, Thumb, Micro, HD, FullHD
    }

    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class IgdbGame
    {
        public int Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public string Summary { get; set; } = string.Empty;
        public string Storyline { get; set; } = string.Empty;
        public IgdbImage? Cover { get; set; }
        public List<IgdbImage> Screenshots { get; set; } = new();
        public List<IgdbImage> Artworks { get; set; } = new();
        public List<IgdbGenre> Genres { get; set; } = new();
        
        [JsonPropertyName("first_release_date")]
        public long? FirstReleaseDate { get; set; }
        
        [JsonPropertyName("total_rating")]
        public double? Rating { get; set; }
        
        [JsonPropertyName("total_rating_count")]
        public int? RatingCount { get; set; }
        
        [JsonPropertyName("involved_companies")]
        public List<IgdbInvolvedCompany> InvolvedCompanies { get; set; } = new();
        
        [JsonPropertyName("external_games")]
        public List<IgdbExternalGame> ExternalGames { get; set; } = new();
        
        [JsonPropertyName("localizations")]
        public List<IgdbLocalization> Localizations { get; set; } = new();

        [JsonPropertyName("platforms")]
        public List<IgdbPlatform> Platforms { get; set; } = new();

        [JsonPropertyName("alternative_names")]
        public List<IgdbAlternativeName> AlternativeNames { get; set; } = new();
    }

    public class IgdbAlternativeName
    {
        public string Name { get; set; } = string.Empty;
        public string? Comment { get; set; }
    }

    public class IgdbPlatform
    {
        public string Name { get; set; } = string.Empty;
        public string Abbreviation { get; set; } = string.Empty;
    }

    public class IgdbImage
    {
        [JsonPropertyName("image_id")]
        public string ImageId { get; set; } = string.Empty;
    }

    public class IgdbGenre
    {
        public string Name { get; set; } = string.Empty;
    }

    public class IgdbInvolvedCompany
    {
        public IgdbCompany Company { get; set; } = new();
        public bool Developer { get; set; }
        public bool Publisher { get; set; }
    }

    public class IgdbCompany
    {
        public string Name { get; set; } = string.Empty;
    }
    
    public class IgdbExternalGame
    {
        public int Category { get; set; } // 1 = steam
        public string Uid { get; set; } = string.Empty;
    }
    
    public class IgdbLocalization 
    {
        public string Name { get; set; } = string.Empty;
        [JsonPropertyName("region")]
        public int Language { get; set; } // ID of the region/language
    }

    public class IgdbGameWithSimilar
    {
        public int Id { get; set; }
        [JsonPropertyName("similar_games")]
        public List<int> SimilarGames { get; set; } = new();
    }
}
