using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.IO;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.MetadataSource.ScreenScraper
{
    public enum ScreenScraperStatus
    {
        Ok,
        Empty,
        QuotaExceeded,
        AuthFailed,
        NetworkError,
        Unconfigured
    }

    /// <summary>
    /// Client for ScreenScraper.fr API - provides metadata for arcade and retro games
    /// that may not be available on IGDB (CPS-1/2/3, FinalBurn Neo, ScummVM, etc.)
    /// </summary>
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    public class ScreenScraperClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.ScannerMetadata);
        private readonly HttpClient _httpClient;
        private readonly string? _username;
        private readonly string? _password;
        private readonly string _devId;
        private readonly string _devPassword;
        
        private const string BaseUrl = "https://api.screenscraper.fr/api2";
        private const string DefaultDevId = "%%SCREENSCRAPER_DEVID%%";
        private const string DefaultDevPassword = "%%SCREENSCRAPER_DEVPASSWORD%%";
        private const string SoftName = "RetroArr";

        public ScreenScraperClient(HttpClient httpClient, string? username = null, string? password = null, string? devId = null, string? devPassword = null)
        {
            _httpClient = httpClient;
            _username = username;
            _password = password;
            _devId = !string.IsNullOrWhiteSpace(devId) ? devId : DefaultDevId;
            _devPassword = !string.IsNullOrWhiteSpace(devPassword) ? devPassword : DefaultDevPassword;
        }

        // dev creds got swapped in via build sed step, if not the placeholder is still here
        private bool IsConfigured =>
            !string.IsNullOrWhiteSpace(_devId)
            && !_devId.StartsWith("%%")
            && !string.IsNullOrWhiteSpace(_devPassword)
            && !_devPassword.StartsWith("%%");

        // map http code + body to a status. screenscraper often replies HTTP 200
        // with a plain-text body like "Quota dépassé" instead of structured json.
        internal static ScreenScraperStatus ClassifyResponse(HttpStatusCode code, string? body)
        {
            if (code == HttpStatusCode.Locked || code == (HttpStatusCode)429)
                return ScreenScraperStatus.QuotaExceeded;
            if (code == HttpStatusCode.Unauthorized || code == HttpStatusCode.Forbidden)
                return ScreenScraperStatus.AuthFailed;

            var lower = (body ?? string.Empty).ToLowerInvariant();

            // quota wording, both french and english are seen in the wild
            if (lower.Contains("quota")
                || lower.Contains("api totalement fermée")
                || lower.Contains("api fermee")
                || lower.Contains("api closed")
                || lower.Contains("limite atteinte")
                || lower.Contains("scrapes par jour")
                || lower.Contains("scrapes restant"))
                return ScreenScraperStatus.QuotaExceeded;

            // auth wording
            if (lower.Contains("erreur de login")
                || lower.Contains("identifiant")
                || lower.Contains("mot de passe")
                || (lower.Contains("login") && lower.Contains("incorrect")))
                return ScreenScraperStatus.AuthFailed;

            if ((int)code >= 500)
                return ScreenScraperStatus.NetworkError;
            if (!(code == HttpStatusCode.OK))
                return ScreenScraperStatus.NetworkError;

            // body must be json from here on
            var trimmed = (body ?? string.Empty).TrimStart();
            if (trimmed.Length < 2 || (!trimmed.StartsWith("{") && !trimmed.StartsWith("[")))
                return ScreenScraperStatus.NetworkError;

            return ScreenScraperStatus.Ok;
        }

        public async Task<(ScreenScraperStatus Status, ScreenScraperGame? Game)> SearchGameAsync(string fileName, int? systemId = null, string? crc = null, string? md5 = null, string? sha1 = null)
        {
            if (!IsConfigured) return (ScreenScraperStatus.Unconfigured, null);
            try
            {
                var url = $"{BaseUrl}/jeuInfos.php?devid={_devId}&devpassword={_devPassword}&softname={SoftName}&output=json";

                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    url += $"&ssid={Uri.EscapeDataString(_username)}&sspassword={Uri.EscapeDataString(_password)}";
                }

                if (systemId.HasValue)
                    url += $"&systemeid={systemId.Value}";

                if (!string.IsNullOrEmpty(sha1))
                    url += $"&sha1={sha1}";
                else if (!string.IsNullOrEmpty(md5))
                    url += $"&md5={md5}";
                else if (!string.IsNullOrEmpty(crc))
                    url += $"&crc={crc}";

                // Always include filename as fallback
                url += $"&romnom={Uri.EscapeDataString(Path.GetFileName(fileName))}";

                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != ScreenScraperStatus.Ok) return (status, null);

                var result = JsonSerializer.Deserialize<ScreenScraperResponse>(body);
                if (result?.Response?.Jeu == null)
                    return (ScreenScraperStatus.Empty, null);

                return (ScreenScraperStatus.Ok, result.Response.Jeu);
            }
            catch (Exception ex)
            {
                _logger.Error($"[ScreenScraper] jeuInfos exception: {ex.Message}");
                return (ScreenScraperStatus.NetworkError, null);
            }
        }

        /// <summary>
        /// Search games by title using jeuRecherche.php (free-text search).
        /// Use this for manual UI searches. For ROM identification, use SearchGameAsync.
        /// </summary>
        public async Task<(ScreenScraperStatus Status, List<ScreenScraperGame> Games)> SearchGamesByNameAsync(string query, int? systemId = null)
        {
            var results = new List<ScreenScraperGame>();
            if (!IsConfigured) return (ScreenScraperStatus.Unconfigured, results);
            try
            {
                var url = $"{BaseUrl}/jeuRecherche.php?devid={_devId}&devpassword={_devPassword}&softname={SoftName}&output=json";

                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    url += $"&ssid={Uri.EscapeDataString(_username)}&sspassword={Uri.EscapeDataString(_password)}";
                }

                if (systemId.HasValue)
                    url += $"&systemeid={systemId.Value}";

                url += $"&recherche={Uri.EscapeDataString(query)}";

                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                _logger.Info($"[ScreenScraper] jeuRecherche HTTP {(int)response.StatusCode}, body length={body.Length}");

                var status = ClassifyResponse(response.StatusCode, body);
                if (status != ScreenScraperStatus.Ok)
                {
                    _logger.Info($"[ScreenScraper] jeuRecherche status={status}. First 200 chars: {body[..Math.Min(body.Length, 200)]}");
                    return (status, results);
                }

                var result = JsonSerializer.Deserialize<ScreenScraperSearchResponse>(body);
                if (result?.Response?.Jeux != null)
                {
                    _logger.Info($"[ScreenScraper] jeuRecherche parsed {result.Response.Jeux.Count} game(s)");
                    results.AddRange(result.Response.Jeux);
                }
                return (results.Count > 0 ? ScreenScraperStatus.Ok : ScreenScraperStatus.Empty, results);
            }
            catch (Exception ex)
            {
                _logger.Error($"[ScreenScraper] jeuRecherche exception: {ex.Message}");
                return (ScreenScraperStatus.NetworkError, results);
            }
        }

        /// <summary>
        /// Get full game details by ScreenScraper game ID using jeuInfos.php.
        /// Unlike jeuRecherche.php, this returns synopsis, developer, publisher, genres, etc.
        /// </summary>
        public async Task<(ScreenScraperStatus Status, ScreenScraperGame? Game)> GetGameByIdAsync(string gameId)
        {
            if (!IsConfigured) return (ScreenScraperStatus.Unconfigured, null);
            try
            {
                var url = $"{BaseUrl}/jeuInfos.php?devid={_devId}&devpassword={_devPassword}&softname={SoftName}&output=json";

                if (!string.IsNullOrEmpty(_username) && !string.IsNullOrEmpty(_password))
                {
                    url += $"&ssid={Uri.EscapeDataString(_username)}&sspassword={Uri.EscapeDataString(_password)}";
                }

                url += $"&gameid={Uri.EscapeDataString(gameId)}";

                var response = await _httpClient.GetAsync(url);
                var body = await response.Content.ReadAsStringAsync();
                var status = ClassifyResponse(response.StatusCode, body);
                if (status != ScreenScraperStatus.Ok)
                {
                    _logger.Info($"[ScreenScraper] jeuInfos by id={gameId} status={status}");
                    return (status, null);
                }

                var result = JsonSerializer.Deserialize<ScreenScraperResponse>(body);
                if (result?.Response?.Jeu == null) return (ScreenScraperStatus.Empty, null);
                return (ScreenScraperStatus.Ok, result.Response.Jeu);
            }
            catch (Exception ex)
            {
                _logger.Error($"[ScreenScraper] jeuInfos by ID exception: {ex.Message}");
                return (ScreenScraperStatus.NetworkError, null);
            }
        }

        public static string? ComputeSha1(string filePath)
        {
            try
            {
                using var stream = File.OpenRead(filePath);
                using var sha1 = SHA1.Create();
                var hash = sha1.ComputeHash(stream);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
            catch
            {
                return null;
            }
        }

        public static string GetMediaUrl(List<ScreenScraperMedia>? medias, string type, string region = "wor")
        {
            if (medias == null || medias.Count == 0)
                return string.Empty;

            // Try to find media of the specified type in preferred region order
            var regionOrder = new[] { region, "wor", "us", "eu", "jp", "ss" };
            
            foreach (var r in regionOrder)
            {
                var media = medias.FirstOrDefault(m => m.Type == type && m.Region == r);
                if (media != null && !string.IsNullOrEmpty(media.Url))
                    return media.Url;
            }

            // Fallback to any media of the specified type
            var fallback = medias.FirstOrDefault(m => m.Type == type);
            return fallback?.Url ?? string.Empty;
        }
    }

    #region Response Models

    public class ScreenScraperResponse
    {
        [JsonPropertyName("header")]
        public ScreenScraperHeader? Header { get; set; }

        [JsonPropertyName("response")]
        public ScreenScraperResponseBody? Response { get; set; }
    }

    public class ScreenScraperHeader
    {
        [JsonPropertyName("success")]
        public string? Success { get; set; }

        [JsonPropertyName("error")]
        public string? Error { get; set; }
    }

    public class ScreenScraperResponseBody
    {
        [JsonPropertyName("jeu")]
        public ScreenScraperGame? Jeu { get; set; }
    }

    public class ScreenScraperSearchResponse
    {
        [JsonPropertyName("header")]
        public ScreenScraperHeader? Header { get; set; }

        [JsonPropertyName("response")]
        public ScreenScraperSearchResponseBody? Response { get; set; }
    }

    public class ScreenScraperSearchResponseBody
    {
        [JsonPropertyName("jeux")]
        public List<ScreenScraperGame>? Jeux { get; set; }
    }

    public class ScreenScraperGame
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("noms")]
        public List<ScreenScraperLocalized>? Names { get; set; }

        [JsonPropertyName("nom")]
        public string? Nom { get; set; }

        [JsonPropertyName("synopsis")]
        public List<ScreenScraperLocalized>? Synopsis { get; set; }

        [JsonPropertyName("dates")]
        public List<ScreenScraperLocalized>? Dates { get; set; }

        [JsonPropertyName("editeur")]
        public ScreenScraperCompany? Publisher { get; set; }

        [JsonPropertyName("developpeur")]
        public ScreenScraperCompany? Developer { get; set; }

        [JsonPropertyName("note")]
        public ScreenScraperNote? Rating { get; set; }

        [JsonPropertyName("genres")]
        public List<ScreenScraperGenre>? Genres { get; set; }

        [JsonPropertyName("medias")]
        public List<ScreenScraperMedia>? Medias { get; set; }

        [JsonPropertyName("systeme")]
        public ScreenScraperSystem? System { get; set; }

        public string GetName(string lang = "en")
        {
            if (Names != null && Names.Count > 0)
            {
                var name = Names.FirstOrDefault(n => n.Region == "wor") 
                        ?? Names.FirstOrDefault(n => n.Region == "us")
                        ?? Names.FirstOrDefault(n => n.Region == "eu")
                        ?? Names.FirstOrDefault();

                if (!string.IsNullOrEmpty(name?.Text))
                    return name!.Text;
            }

            // Fallback: jeuRecherche.php returns "nom" (string) instead of "noms" (array)
            return Nom ?? string.Empty;
        }

        public string GetSynopsis(string lang = "en")
        {
            if (Synopsis == null || Synopsis.Count == 0)
                return string.Empty;

            var synopsis = Synopsis.FirstOrDefault(s => s.Langue == lang)
                        ?? Synopsis.FirstOrDefault(s => s.Langue == "en")
                        ?? Synopsis.FirstOrDefault();

            return synopsis?.Text ?? string.Empty;
        }

        public int? GetYear()
        {
            if (Dates == null || Dates.Count == 0)
                return null;

            var date = Dates.FirstOrDefault(d => d.Region == "wor")
                    ?? Dates.FirstOrDefault(d => d.Region == "us")
                    ?? Dates.FirstOrDefault(d => d.Region == "eu")
                    ?? Dates.FirstOrDefault();

            if (date?.Text != null && date.Text.Length >= 4 && int.TryParse(date.Text.Substring(0, 4), out var year))
                return year;

            return null;
        }
    }

    public class ScreenScraperLocalized
    {
        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("langue")]
        public string? Langue { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class ScreenScraperCompany
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class ScreenScraperNote
    {
        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    public class ScreenScraperGenre
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("nomcourt")]
        public string? ShortName { get; set; }

        [JsonPropertyName("noms")]
        public List<ScreenScraperLocalized>? Names { get; set; }

        public string GetName(string lang = "en")
        {
            if (Names == null || Names.Count == 0)
                return ShortName ?? string.Empty;

            var name = Names.FirstOrDefault(n => n.Langue == lang)
                    ?? Names.FirstOrDefault(n => n.Langue == "en")
                    ?? Names.FirstOrDefault();

            return name?.Text ?? ShortName ?? string.Empty;
        }
    }

    public class ScreenScraperMedia
    {
        [JsonPropertyName("type")]
        public string? Type { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonPropertyName("region")]
        public string? Region { get; set; }

        [JsonPropertyName("format")]
        public string? Format { get; set; }
    }

    public class ScreenScraperSystem
    {
        [JsonPropertyName("id")]
        public string? Id { get; set; }

        [JsonPropertyName("text")]
        public string? Text { get; set; }
    }

    #endregion
}
