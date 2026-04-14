using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.MetadataSource.Steam
{
    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Reliability", "CA2007:DoNotDirectlyAwaitATask")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    public class ProtonDbClient
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private static readonly HttpClient _httpClient = new()
        {
            Timeout = TimeSpan.FromSeconds(10)
        };

        static ProtonDbClient()
        {
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("RetroArr/1.0");
        }

        /// <summary>
        /// Fetches the ProtonDB compatibility tier for a given Steam AppID.
        /// Returns null if no data is available.
        /// </summary>
        public async Task<string?> GetTierAsync(int steamAppId)
        {
            try
            {
                var url = $"https://www.protondb.com/api/v1/reports/summaries/{steamAppId}.json";
                var response = await _httpClient.GetAsync(url);

                if (!response.IsSuccessStatusCode)
                    return null;

                var json = await response.Content.ReadAsStringAsync();
                var summary = JsonSerializer.Deserialize<ProtonDbSummary>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
                return summary?.Tier;
            }
            catch (TaskCanceledException)
            {
                // Timeout — skip silently
                return null;
            }
            catch (Exception ex)
            {
                _logger.Debug($"[ProtonDB] Error fetching tier for AppID {steamAppId}: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// Fetches ProtonDB tiers for multiple Steam AppIDs.
        /// Returns a dictionary mapping AppID → tier string.
        /// </summary>
        public async Task<Dictionary<int, string>> GetTiersBulkAsync(IEnumerable<int> steamAppIds, int delayMs = 100)
        {
            var result = new Dictionary<int, string>();
            foreach (var appId in steamAppIds)
            {
                var tier = await GetTierAsync(appId);
                if (!string.IsNullOrEmpty(tier))
                    result[appId] = tier;

                if (delayMs > 0)
                    await Task.Delay(delayMs);
            }
            return result;
        }
    }

    [SuppressMessage("Microsoft.Performance", "CA1812:AvoidUninstantiatedInternalClasses")]
    public class ProtonDbSummary
    {
        [JsonPropertyName("tier")]
        public string? Tier { get; set; }

        [JsonPropertyName("bestReportedTier")]
        public string? BestReportedTier { get; set; }

        [JsonPropertyName("trendingTier")]
        public string? TrendingTier { get; set; }

        [JsonPropertyName("score")]
        public double? Score { get; set; }

        [JsonPropertyName("confidence")]
        public string? Confidence { get; set; }

        [JsonPropertyName("total")]
        public int? Total { get; set; }
    }
}
