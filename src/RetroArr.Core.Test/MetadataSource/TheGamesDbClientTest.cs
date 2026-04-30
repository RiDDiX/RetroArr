using System.Net;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.TheGamesDb;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class TheGamesDbClientTest
    {
        [Test]
        public void Classify_Http401_AuthFailed()
        {
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.Unauthorized, "");
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.AuthFailed));
        }

        [Test]
        public void Classify_Http403_QuotaExceeded()
        {
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.Forbidden, "");
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.QuotaExceeded));
        }

        [Test]
        public void Classify_Http500_NetworkError()
        {
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.InternalServerError, "{}");
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.NetworkError));
        }

        [Test]
        public void Classify_JsonCode200_Ok()
        {
            var body = "{\"code\":200,\"status\":\"Success\",\"remaining_monthly_allowance\":100,\"extra_allowance\":0,\"data\":{\"count\":0,\"games\":[]}}";
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.Ok));
        }

        [Test]
        public void Classify_JsonCode401_AuthFailed()
        {
            var body = "{\"code\":401,\"status\":\"This route requires an API key\",\"remaining_monthly_allowance\":0}";
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.AuthFailed));
        }

        [Test]
        public void Classify_JsonCode403_QuotaExceeded()
        {
            var body = "{\"code\":403,\"status\":\"Monthly allowance exceeded\",\"remaining_monthly_allowance\":0}";
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.QuotaExceeded));
        }

        [Test]
        public void Classify_JsonCode404_Empty()
        {
            var body = "{\"code\":404,\"status\":\"Not found\"}";
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.Empty));
        }

        [Test]
        public void Classify_NonJson_NetworkError()
        {
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.OK, "<html>oops</html>");
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.NetworkError));
        }

        [Test]
        public void Classify_Empty_NetworkError()
        {
            var s = TheGamesDbClient.ClassifyResponse(HttpStatusCode.OK, "");
            Assert.That(s, Is.EqualTo(TheGamesDbStatus.NetworkError));
        }

        [Test]
        public void PickBoxartUrl_FrontBoxartLarge_BuildsUrl()
        {
            var images = new System.Collections.Generic.List<TheGamesDbImage>
            {
                new TheGamesDbImage { Type = "fanart", Filename = "fanart/123-1.jpg" },
                new TheGamesDbImage { Type = "boxart", Side = "back", Filename = "boxart/back/123-1.jpg" },
                new TheGamesDbImage { Type = "boxart", Side = "front", Filename = "boxart/front/123-1.jpg" }
            };
            var baseUrls = new TheGamesDbImageBaseUrls
            {
                Large = "https://cdn.thegamesdb.net/images/large/",
                Original = "https://cdn.thegamesdb.net/images/original/"
            };

            var url = TheGamesDbClient.PickBoxartUrl(images, baseUrls);
            Assert.That(url, Is.EqualTo("https://cdn.thegamesdb.net/images/large/boxart/front/123-1.jpg"));
        }

        [Test]
        public void PickBoxartUrl_NoBoxart_ReturnsNull()
        {
            var images = new System.Collections.Generic.List<TheGamesDbImage>
            {
                new TheGamesDbImage { Type = "fanart", Filename = "fanart/123-1.jpg" }
            };
            var baseUrls = new TheGamesDbImageBaseUrls { Large = "https://cdn.thegamesdb.net/images/large/" };

            var url = TheGamesDbClient.PickBoxartUrl(images, baseUrls);
            Assert.That(url, Is.Null);
        }
    }
}
