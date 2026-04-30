using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.Epic;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class EpicMetadataClientTest
    {
        [Test]
        public void Classify_Http500_NetworkError()
        {
            var s = EpicMetadataClient.ClassifyResponse(HttpStatusCode.InternalServerError, "{}");
            Assert.That(s, Is.EqualTo(EpicMetadataStatus.NetworkError));
        }

        [Test]
        public void Classify_Http403_NetworkError()
        {
            var s = EpicMetadataClient.ClassifyResponse(HttpStatusCode.Forbidden, "");
            Assert.That(s, Is.EqualTo(EpicMetadataStatus.NetworkError));
        }

        [Test]
        public void Classify_NonJson_NetworkError()
        {
            var s = EpicMetadataClient.ClassifyResponse(HttpStatusCode.OK, "<html>oops</html>");
            Assert.That(s, Is.EqualTo(EpicMetadataStatus.NetworkError));
        }

        [Test]
        public void Classify_ValidJson_Ok()
        {
            var body = "{\"data\":{\"Catalog\":{\"searchStore\":{\"elements\":[{\"title\":\"Demo\",\"id\":\"abc\",\"namespace\":\"ns\"}]}}}}";
            var s = EpicMetadataClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(EpicMetadataStatus.Ok));
        }

        [Test]
        public void Classify_GraphqlErrors_NetworkError()
        {
            var body = "{\"data\":null,\"errors\":[{\"message\":\"Some error\"}]}";
            var s = EpicMetadataClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(EpicMetadataStatus.NetworkError));
        }

        [Test]
        public void LooksLikeGame_AddonCategory_False()
        {
            var e = new EpicStoreElement
            {
                Title = "Test DLC",
                Categories = new List<EpicCategory> { new EpicCategory { Path = "addons/dlc" } }
            };
            Assert.That(e.LooksLikeGame(), Is.False);
        }

        [Test]
        public void LooksLikeGame_DigitalExtras_False()
        {
            var e = new EpicStoreElement
            {
                Title = "Soundtrack",
                Categories = new List<EpicCategory> { new EpicCategory { Path = "digitalextras/soundtrack" } }
            };
            Assert.That(e.LooksLikeGame(), Is.False);
        }

        [Test]
        public void LooksLikeGame_GameCategory_True()
        {
            var e = new EpicStoreElement
            {
                Title = "Some Game",
                Categories = new List<EpicCategory> { new EpicCategory { Path = "games/edition/base" } }
            };
            Assert.That(e.LooksLikeGame(), Is.True);
        }

        [Test]
        public void PickImage_PrefersTypeOrder()
        {
            var e = new EpicStoreElement
            {
                KeyImages = new List<EpicKeyImage>
                {
                    new EpicKeyImage { Type = "Thumbnail", Url = "thumb.jpg" },
                    new EpicKeyImage { Type = "OfferImageWide", Url = "wide.jpg" },
                    new EpicKeyImage { Type = "OfferImageTall", Url = "tall.jpg" }
                }
            };
            Assert.That(e.PickImage("OfferImageTall", "Thumbnail"), Is.EqualTo("tall.jpg"));
            Assert.That(e.PickImage("Thumbnail"), Is.EqualTo("thumb.jpg"));
        }

        [Test]
        public void GetReleaseYear_ParsesIsoDate()
        {
            var e = new EpicStoreElement { EffectiveDate = "2021-08-12T00:00:00.000Z" };
            Assert.That(e.GetReleaseYear(), Is.EqualTo(2021));
        }

        [Test]
        public void GetReleaseYear_EmptyOrShort_Null()
        {
            Assert.That(new EpicStoreElement { EffectiveDate = null }.GetReleaseYear(), Is.Null);
            Assert.That(new EpicStoreElement { EffectiveDate = "" }.GetReleaseYear(), Is.Null);
        }
    }
}
