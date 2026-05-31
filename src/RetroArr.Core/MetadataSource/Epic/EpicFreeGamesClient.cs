using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace RetroArr.Core.MetadataSource.Epic
{
    [SuppressMessage("Interoperability", "CA1416:Validate platform compatibility", Justification = "Networking-only client.")]
    public class EpicFreeGamesClient
    {
        private static readonly HttpClient _http = new HttpClient();
        private static readonly NLog.Logger _logger = NLog.LogManager.GetLogger(RetroArr.Core.Logging.AppLoggerService.ScannerMetadata);

        static EpicFreeGamesClient()
        {
            _http.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (RetroArr EpicFreeGamesClient)");
            _http.Timeout = TimeSpan.FromSeconds(30);
        }

        public async Task<List<EpicFreeGame>> GetFreeGamesAsync(string locale = "en-US", string country = "US")
        {
            locale = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
            country = string.IsNullOrWhiteSpace(country) ? "US" : country.Trim().ToUpperInvariant();

            var url = $"https://store-site-backend-static.ak.epicgames.com/freeGamesPromotions?locale={Uri.EscapeDataString(locale)}&country={Uri.EscapeDataString(country)}&allowCountries={Uri.EscapeDataString(country)}";
            using var response = await _http.GetAsync(url);
            var body = await response.Content.ReadAsStringAsync();
            if (!response.IsSuccessStatusCode)
            {
                _logger.Warn($"[Epic] Free games http={(int)response.StatusCode} body={(body.Length > 200 ? body[..200] : body)}");
                return new List<EpicFreeGame>();
            }

            try
            {
                var parsed = JsonSerializer.Deserialize<EpicFreeGamesEnvelope>(body, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return MapElements(parsed?.Data?.Catalog?.SearchStore?.Elements, locale, DateTimeOffset.UtcNow);
            }
            catch (JsonException ex)
            {
                _logger.Warn($"[Epic] Free games invalid json: {ex.Message}");
                return new List<EpicFreeGame>();
            }
        }

        public static List<EpicFreeGame> MapElements(List<EpicFreeGameElement>? elements, string locale, DateTimeOffset? now = null)
        {
            var effectiveNow = now ?? DateTimeOffset.UtcNow;
            var results = new List<EpicFreeGame>();
            foreach (var element in elements ?? new List<EpicFreeGameElement>())
            {
                var window = SelectBestPromotion(element?.Promotions, effectiveNow);
                if (window == null)
                {
                    continue;
                }

                results.Add(new EpicFreeGame
                {
                    Title = element?.Title ?? string.Empty,
                    Description = element?.Description,
                    ImageUrl = PickImage(element),
                    IsCurrentlyFree = window.IsCurrentlyFree,
                    StartDate = window.StartDate,
                    EndDate = window.EndDate,
                    StoreUrl = BuildStoreUrl(locale, element)
                });
            }

            return results
                .OrderByDescending(x => x.IsCurrentlyFree)
                .ThenBy(x => x.EndDate ?? x.StartDate ?? string.Empty, StringComparer.Ordinal)
                .ThenBy(x => x.Title, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public static EpicPromotionWindow? SelectBestPromotion(EpicPromotions? promotions, DateTimeOffset now)
        {
            var active = Flatten(promotions?.PromotionalOffers)
                .Where(IsFreeOffer)
                .Select(p => ToWindow(p, true))
                .Where(w => w != null && w.Start <= now && now <= w.End)
                .OrderBy(w => w!.End)
                .FirstOrDefault();
            if (active != null)
            {
                return active;
            }

            return Flatten(promotions?.UpcomingPromotionalOffers)
                .Where(IsFreeOffer)
                .Select(p => ToWindow(p, false))
                .Where(w => w != null && w.End >= now)
                .OrderBy(w => w!.Start)
                .FirstOrDefault();
        }

        public static string? ExtractStoreSlug(EpicFreeGameElement? element)
        {
            var mappingSlug = element?.OfferMappings?.FirstOrDefault(m => string.Equals(m.PageType, "productHome", StringComparison.OrdinalIgnoreCase))?.PageSlug;
            if (!string.IsNullOrWhiteSpace(mappingSlug))
            {
                return NormalizeSlug(mappingSlug);
            }

            var catalogSlug = element?.CatalogNs?.Mappings?.FirstOrDefault(m => string.Equals(m.PageType, "productHome", StringComparison.OrdinalIgnoreCase))?.PageSlug;
            if (!string.IsNullOrWhiteSpace(catalogSlug))
            {
                return NormalizeSlug(catalogSlug);
            }

            var productSlug = NormalizeSlug(element?.ProductSlug);
            if (!string.IsNullOrWhiteSpace(productSlug))
            {
                return productSlug;
            }

            return NormalizeSlug(element?.UrlSlug);
        }

        public static string? BuildStoreUrl(string locale, EpicFreeGameElement? element)
        {
            var slug = ExtractStoreSlug(element);
            if (string.IsNullOrWhiteSpace(slug))
            {
                return null;
            }

            var safeLocale = string.IsNullOrWhiteSpace(locale) ? "en-US" : locale.Trim();
            return $"https://store.epicgames.com/{safeLocale}/p/{slug}";
        }

        private static string? PickImage(EpicFreeGameElement? element)
        {
            if (element?.KeyImages == null || element.KeyImages.Count == 0)
            {
                return null;
            }

            string? Pick(params string[] preferredTypes)
            {
                foreach (var type in preferredTypes)
                {
                    var hit = element.KeyImages.FirstOrDefault(i => string.Equals(i.Type, type, StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(i.Url));
                    if (!string.IsNullOrWhiteSpace(hit?.Url))
                    {
                        return hit.Url;
                    }
                }

                return null;
            }

            return Pick("OfferImageTall", "DieselStoreFrontTall", "OfferImageWide", "DieselStoreFrontWide", "Thumbnail")
                ?? element.KeyImages.FirstOrDefault(i => !string.IsNullOrWhiteSpace(i.Url))?.Url;
        }

        private static IEnumerable<EpicPromotionOffer> Flatten(List<EpicPromotionBucket>? buckets)
            => buckets?.SelectMany(b => b.PromotionalOffers ?? new List<EpicPromotionOffer>()) ?? Enumerable.Empty<EpicPromotionOffer>();

        private static bool IsFreeOffer(EpicPromotionOffer offer)
            => (offer.DiscountSetting?.DiscountPercentage ?? -1) == 0;

        private static EpicPromotionWindow? ToWindow(EpicPromotionOffer offer, bool current)
        {
            if (!DateTimeOffset.TryParse(offer.StartDate, out var start))
            {
                return null;
            }

            if (!DateTimeOffset.TryParse(offer.EndDate, out var end))
            {
                return null;
            }

            return new EpicPromotionWindow
            {
                IsCurrentlyFree = current,
                Start = start,
                End = end,
                StartDate = offer.StartDate,
                EndDate = offer.EndDate
            };
        }

        private static string? NormalizeSlug(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
            {
                return null;
            }

            var slug = raw.Trim();
            if (slug == "[]")
            {
                return null;
            }

            slug = slug.Trim('/');
            if (slug.EndsWith("/home", StringComparison.OrdinalIgnoreCase))
            {
                slug = slug[..^5].Trim('/');
            }

            return string.IsNullOrWhiteSpace(slug) ? null : slug;
        }
    }

    public class EpicFreeGame
    {
        public string Title { get; set; } = string.Empty;
        public string? Description { get; set; }
        public string? ImageUrl { get; set; }
        public bool IsCurrentlyFree { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
        public string? StoreUrl { get; set; }
    }

    public class EpicPromotionWindow
    {
        public bool IsCurrentlyFree { get; set; }
        public DateTimeOffset Start { get; set; }
        public DateTimeOffset End { get; set; }
        public string? StartDate { get; set; }
        public string? EndDate { get; set; }
    }

    public class EpicFreeGamesEnvelope
    {
        [JsonPropertyName("data")] public EpicFreeGamesData? Data { get; set; }
    }

    public class EpicFreeGamesData
    {
        [JsonPropertyName("Catalog")] public EpicFreeGamesCatalog? Catalog { get; set; }
    }

    public class EpicFreeGamesCatalog
    {
        [JsonPropertyName("searchStore")] public EpicFreeGamesSearchStore? SearchStore { get; set; }
    }

    public class EpicFreeGamesSearchStore
    {
        [JsonPropertyName("elements")] public List<EpicFreeGameElement>? Elements { get; set; }
    }

    public class EpicFreeGameElement
    {
        [JsonPropertyName("title")] public string? Title { get; set; }
        [JsonPropertyName("description")] public string? Description { get; set; }
        [JsonPropertyName("keyImages")] public List<EpicKeyImage>? KeyImages { get; set; }
        [JsonPropertyName("promotions")] public EpicPromotions? Promotions { get; set; }
        [JsonPropertyName("productSlug")] public string? ProductSlug { get; set; }
        [JsonPropertyName("urlSlug")] public string? UrlSlug { get; set; }
        [JsonPropertyName("offerMappings")] public List<EpicOfferMapping>? OfferMappings { get; set; }
        [JsonPropertyName("catalogNs")] public EpicCatalogNamespace? CatalogNs { get; set; }
    }

    public class EpicPromotions
    {
        [JsonPropertyName("promotionalOffers")] public List<EpicPromotionBucket>? PromotionalOffers { get; set; }
        [JsonPropertyName("upcomingPromotionalOffers")] public List<EpicPromotionBucket>? UpcomingPromotionalOffers { get; set; }
    }

    public class EpicPromotionBucket
    {
        [JsonPropertyName("promotionalOffers")] public List<EpicPromotionOffer>? PromotionalOffers { get; set; }
    }

    public class EpicPromotionOffer
    {
        [JsonPropertyName("startDate")] public string? StartDate { get; set; }
        [JsonPropertyName("endDate")] public string? EndDate { get; set; }
        [JsonPropertyName("discountSetting")] public EpicDiscountSetting? DiscountSetting { get; set; }
    }

    public class EpicDiscountSetting
    {
        [JsonPropertyName("discountPercentage")] public int? DiscountPercentage { get; set; }
    }

    public class EpicOfferMapping
    {
        [JsonPropertyName("pageType")] public string? PageType { get; set; }
        [JsonPropertyName("pageSlug")] public string? PageSlug { get; set; }
    }

    public class EpicCatalogNamespace
    {
        [JsonPropertyName("mappings")] public List<EpicOfferMapping>? Mappings { get; set; }
    }
}
