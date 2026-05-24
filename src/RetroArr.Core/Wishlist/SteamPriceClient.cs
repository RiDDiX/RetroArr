using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;

namespace RetroArr.Core.Wishlist
{
    public sealed class SteamPriceQuote
    {
        public string AppId { get; set; } = string.Empty;
        public string Currency { get; set; } = "USD";
        // Already converted from Steam's integer cents to decimal units.
        public decimal Final { get; set; }
        public decimal Initial { get; set; }
        public int DiscountPercent { get; set; }
        public bool IsFree { get; set; }
    }

    [SuppressMessage("Microsoft.Design", "CA1031:DoNotCatchGeneralExceptionTypes")]
    [SuppressMessage("Microsoft.Performance", "CA1869:CacheAndReuseJsonSerializerOptions")]
    public sealed class SteamPriceClient : IDisposable
    {
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(Logging.AppLoggerService.General);
        private readonly HttpClient _httpClient;
        private bool _disposed;

        // Steam tolerates ~50 ids per call, 25 leaves headroom.
        private const int BatchSize = 25;

        public SteamPriceClient()
        {
            _httpClient = new HttpClient
            {
                BaseAddress = new Uri("https://store.steampowered.com/"),
                Timeout = TimeSpan.FromSeconds(30)
            };
        }

        public async Task<IReadOnlyDictionary<string, SteamPriceQuote?>> GetPricesAsync(
            IEnumerable<string> appIds, string countryCode = "US", CancellationToken ct = default)
        {
            var result = new Dictionary<string, SteamPriceQuote?>(StringComparer.OrdinalIgnoreCase);
            var distinct = appIds.Where(id => !string.IsNullOrWhiteSpace(id)).Distinct().ToList();
            if (distinct.Count == 0) return result;

            foreach (var batch in Chunk(distinct, BatchSize))
            {
                var quotes = await FetchBatchAsync(batch, countryCode, ct).ConfigureAwait(false);
                foreach (var kv in quotes) result[kv.Key] = kv.Value;
            }
            return result;
        }

        private async Task<IReadOnlyDictionary<string, SteamPriceQuote?>> FetchBatchAsync(
            IReadOnlyList<string> batch, string countryCode, CancellationToken ct)
        {
            var ids = string.Join(",", batch);
            var url = $"api/appdetails?appids={Uri.EscapeDataString(ids)}&cc={Uri.EscapeDataString(countryCode)}&filters=price_overview";

            var quotes = new Dictionary<string, SteamPriceQuote?>(StringComparer.OrdinalIgnoreCase);
            // Pre-fill so callers always see every requested id, even if Steam
            // omits some (free games, region locks, dead app ids).
            foreach (var id in batch) quotes[id] = null;

            try
            {
                var response = await _httpClient.GetAsync(url, ct).ConfigureAwait(false);
                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warn($"[SteamPriceClient] Storefront returned {(int)response.StatusCode} for batch of {batch.Count}.");
                    return quotes;
                }

                var content = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
                using var doc = JsonDocument.Parse(content);

                foreach (var appId in batch)
                {
                    if (!doc.RootElement.TryGetProperty(appId, out var node)) continue;
                    if (!node.TryGetProperty("success", out var success) || !success.GetBoolean()) continue;

                    if (!node.TryGetProperty("data", out var data)) continue;

                    // Free games: data is present but price_overview is absent.
                    if (!data.TryGetProperty("price_overview", out var po))
                    {
                        quotes[appId] = new SteamPriceQuote { AppId = appId, IsFree = true };
                        continue;
                    }

                    var quote = ParsePrice(appId, po);
                    if (quote != null) quotes[appId] = quote;
                }
            }
            catch (TaskCanceledException) { throw; }
            catch (Exception ex)
            {
                _logger.Warn($"[SteamPriceClient] batch fetch failed: {ex.Message}");
            }
            return quotes;
        }

        private static SteamPriceQuote? ParsePrice(string appId, JsonElement po)
        {
            try
            {
                var currency = po.TryGetProperty("currency", out var cur) ? cur.GetString() ?? "USD" : "USD";
                var final = po.TryGetProperty("final", out var f) ? f.GetInt32() : 0;
                var initial = po.TryGetProperty("initial", out var i) ? i.GetInt32() : final;
                var discount = po.TryGetProperty("discount_percent", out var d) ? d.GetInt32() : 0;

                return new SteamPriceQuote
                {
                    AppId = appId,
                    Currency = currency,
                    Final = final / 100m,
                    Initial = initial / 100m,
                    DiscountPercent = discount,
                    IsFree = false
                };
            }
            catch (Exception ex)
            {
                _logger.Warn($"[SteamPriceClient] parse failed for app {appId}: {ex.Message}");
                return null;
            }
        }

        private static IEnumerable<IReadOnlyList<T>> Chunk<T>(IReadOnlyList<T> source, int size)
        {
            for (var i = 0; i < source.Count; i += size)
            {
                yield return source.Skip(i).Take(size).ToList();
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _httpClient.Dispose();
            _disposed = true;
        }
    }
}
