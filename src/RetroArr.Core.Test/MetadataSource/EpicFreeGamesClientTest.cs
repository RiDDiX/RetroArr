using System;
using System.Collections.Generic;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.Epic;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class EpicFreeGamesClientTest
    {
        [Test]
        public void ExtractStoreSlug_PrefersProductHomeMapping()
        {
            var element = new EpicFreeGameElement
            {
                OfferMappings = new List<EpicOfferMapping>
                {
                    new EpicOfferMapping { PageType = "productHome", PageSlug = "mapped-slug" }
                },
                ProductSlug = "fallback/home",
                UrlSlug = "url-fallback"
            };

            Assert.That(EpicFreeGamesClient.ExtractStoreSlug(element), Is.EqualTo("mapped-slug"));
        }

        [Test]
        public void ExtractStoreSlug_NormalizesHomeSuffixAndSkipsPlaceholder()
        {
            var element = new EpicFreeGameElement
            {
                ProductSlug = "demo-game/home",
                UrlSlug = "demo-fallback"
            };
            Assert.That(EpicFreeGamesClient.ExtractStoreSlug(element), Is.EqualTo("demo-game"));

            element.ProductSlug = "[]";
            Assert.That(EpicFreeGamesClient.ExtractStoreSlug(element), Is.EqualTo("demo-fallback"));
        }

        [Test]
        public void SelectBestPromotion_PrefersActiveFreeOffer()
        {
            var now = DateTimeOffset.Parse("2026-05-31T12:00:00Z");
            var promotions = new EpicPromotions
            {
                PromotionalOffers = new List<EpicPromotionBucket>
                {
                    new EpicPromotionBucket
                    {
                        PromotionalOffers = new List<EpicPromotionOffer>
                        {
                            new EpicPromotionOffer
                            {
                                StartDate = "2026-05-28T15:00:00Z",
                                EndDate = "2026-06-04T15:00:00Z",
                                DiscountSetting = new EpicDiscountSetting { DiscountPercentage = 0 }
                            }
                        }
                    }
                },
                UpcomingPromotionalOffers = new List<EpicPromotionBucket>
                {
                    new EpicPromotionBucket
                    {
                        PromotionalOffers = new List<EpicPromotionOffer>
                        {
                            new EpicPromotionOffer
                            {
                                StartDate = "2026-06-04T15:00:00Z",
                                EndDate = "2026-06-11T15:00:00Z",
                                DiscountSetting = new EpicDiscountSetting { DiscountPercentage = 0 }
                            }
                        }
                    }
                }
            };

            var selected = EpicFreeGamesClient.SelectBestPromotion(promotions, now);
            Assert.That(selected, Is.Not.Null);
            Assert.That(selected!.IsCurrentlyFree, Is.True);
            Assert.That(selected.StartDate, Is.EqualTo("2026-05-28T15:00:00Z"));
        }

        [Test]
        public void MapElements_OnlyIncludesFreeCurrentOrUpcomingOffers()
        {
            var elements = new List<EpicFreeGameElement>
            {
                new EpicFreeGameElement
                {
                    Title = "Paid Game",
                    Promotions = new EpicPromotions
                    {
                        PromotionalOffers = new List<EpicPromotionBucket>
                        {
                            new EpicPromotionBucket
                            {
                                PromotionalOffers = new List<EpicPromotionOffer>
                                {
                                    new EpicPromotionOffer
                                    {
                                        StartDate = "2026-05-28T15:00:00Z",
                                        EndDate = "2026-06-04T15:00:00Z",
                                        DiscountSetting = new EpicDiscountSetting { DiscountPercentage = 20 }
                                    }
                                }
                            }
                        }
                    }
                },
                new EpicFreeGameElement
                {
                    Title = "Free Game",
                    ProductSlug = "free-game/home",
                    Promotions = new EpicPromotions
                    {
                        PromotionalOffers = new List<EpicPromotionBucket>
                        {
                            new EpicPromotionBucket
                            {
                                PromotionalOffers = new List<EpicPromotionOffer>
                                {
                                    new EpicPromotionOffer
                                    {
                                        StartDate = "2026-05-28T15:00:00Z",
                                        EndDate = "2099-06-04T15:00:00Z",
                                        DiscountSetting = new EpicDiscountSetting { DiscountPercentage = 0 }
                                    }
                                }
                            }
                        }
                    }
                }
            };

            var mapped = EpicFreeGamesClient.MapElements(elements, "en-US", DateTimeOffset.Parse("2026-05-31T12:00:00Z"));
            Assert.That(mapped.Count, Is.EqualTo(1));
            Assert.That(mapped[0].Title, Is.EqualTo("Free Game"));
            Assert.That(mapped[0].StoreUrl, Is.EqualTo("https://store.epicgames.com/en-US/p/free-game"));
        }
    }
}
