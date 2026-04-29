using System.Net;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.ScreenScraper;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class ScreenScraperClientTest
    {
        // http-status driven cases

        [Test]
        public void Classify_HttpLocked_QuotaExceeded()
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.Locked, "{}");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.QuotaExceeded));
        }

        [Test]
        public void Classify_Http429_QuotaExceeded()
        {
            var s = ScreenScraperClient.ClassifyResponse((HttpStatusCode)429, "");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.QuotaExceeded));
        }

        [Test]
        public void Classify_HttpUnauthorized_AuthFailed()
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.Unauthorized, "");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.AuthFailed));
        }

        [Test]
        public void Classify_Http500_NetworkError()
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.InternalServerError, "{}");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.NetworkError));
        }

        // body-text driven cases (HTTP 200 but the api returned plain text)

        [TestCase("Votre quota de scrapes est dépassé.")]
        [TestCase("Quota dépassé")]
        [TestCase("API totalement fermée")]
        [TestCase("API closed for non-registered members")]
        [TestCase("Limite atteinte")]
        public void Classify_QuotaWording_QuotaExceeded(string body)
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.QuotaExceeded));
        }

        [TestCase("Erreur de login !")]
        [TestCase("Identifiant ou mot de passe incorrect")]
        public void Classify_AuthWording_AuthFailed(string body)
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.AuthFailed));
        }

        [Test]
        public void Classify_NonJsonGarbage_NetworkError()
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, "<html>oops</html>");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.NetworkError));
        }

        [Test]
        public void Classify_EmptyBody_NetworkError()
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, "");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.NetworkError));
        }

        [Test]
        public void Classify_ValidJson_Ok()
        {
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, "{\"header\":{\"success\":\"true\"},\"response\":{\"jeux\":[]}}");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.Ok));
        }
    }
}
