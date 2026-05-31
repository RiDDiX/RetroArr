using System.Collections.Generic;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.Epic;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class EpicClientTest
    {
        [Test]
        public void LooksLikeGame_NullCategories_True()
        {
            var item = new EpicCatalogItem { Categories = null };
            Assert.That(item.LooksLikeGame(), Is.True);
        }

        [Test]
        public void LooksLikeGame_EmptyCategories_True()
        {
            var item = new EpicCatalogItem { Categories = new List<EpicCategory>() };
            Assert.That(item.LooksLikeGame(), Is.True);
        }

        [Test]
        public void LooksLikeGame_GameCategory_True()
        {
            var item = new EpicCatalogItem
            {
                Categories = new List<EpicCategory> { new EpicCategory { Path = "games/edition/base" } }
            };

            Assert.That(item.LooksLikeGame(), Is.True);
        }

        [TestCase("addons/dlc")]
        [TestCase("digitalextras/soundtrack")]
        [TestCase("soundtrack")]
        [TestCase("software/edu")]
        public void LooksLikeGame_NonGameCategory_False(string path)
        {
            var item = new EpicCatalogItem
            {
                Categories = new List<EpicCategory> { new EpicCategory { Path = path } }
            };

            Assert.That(item.LooksLikeGame(), Is.False);
        }

        [Test]
        public void PickImage_PrefersTypeOrder()
        {
            var item = new EpicCatalogItem
            {
                KeyImages = new List<EpicKeyImage>
                {
                    new EpicKeyImage { Type = "Thumbnail", Url = "thumb.jpg" },
                    new EpicKeyImage { Type = "OfferImageWide", Url = "wide.jpg" },
                    new EpicKeyImage { Type = "OfferImageTall", Url = "tall.jpg" }
                }
            };

            Assert.That(item.PickImage("OfferImageTall", "Thumbnail"), Is.EqualTo("tall.jpg"));
            Assert.That(item.PickImage("MissingType", "Thumbnail"), Is.EqualTo("thumb.jpg"));
        }

        [Test]
        public void PickImage_FallsBackToFirstAvailableUrl()
        {
            var item = new EpicCatalogItem
            {
                KeyImages = new List<EpicKeyImage>
                {
                    new EpicKeyImage { Type = "OfferImageWide", Url = null },
                    new EpicKeyImage { Type = "Thumbnail", Url = "thumb.jpg" }
                }
            };

            Assert.That(item.PickImage("MissingType"), Is.EqualTo("thumb.jpg"));
        }

        [Test]
        public void PickImage_NullOrEmpty_ReturnsNull()
        {
            Assert.That(new EpicCatalogItem { KeyImages = null }.PickImage("Thumbnail"), Is.Null);
            Assert.That(new EpicCatalogItem { KeyImages = new List<EpicKeyImage>() }.PickImage("Thumbnail"), Is.Null);
        }

        [Test]
        public void GetReleaseYear_PrefersReleaseInfoOverCreationDate()
        {
            var item = new EpicCatalogItem
            {
                CreationDate = "2022-04-01T00:00:00.000Z",
                ReleaseInfo = new List<EpicReleaseInfo>
                {
                    new EpicReleaseInfo { DateAdded = "2019-08-12T00:00:00.000Z" }
                }
            };

            Assert.That(item.GetReleaseYear(), Is.EqualTo(2019));
        }

        [Test]
        public void GetReleaseYear_FallsBackToCreationDate()
        {
            var item = new EpicCatalogItem { CreationDate = "2021-08-12T00:00:00.000Z" };
            Assert.That(item.GetReleaseYear(), Is.EqualTo(2021));
        }

        [Test]
        public void GetReleaseYear_MissingOrShortDates_ReturnsNull()
        {
            Assert.That(new EpicCatalogItem { CreationDate = null }.GetReleaseYear(), Is.Null);
            Assert.That(new EpicCatalogItem { CreationDate = string.Empty }.GetReleaseYear(), Is.Null);
            Assert.That(new EpicCatalogItem { CreationDate = "202" }.GetReleaseYear(), Is.Null);
        }
    }
}
