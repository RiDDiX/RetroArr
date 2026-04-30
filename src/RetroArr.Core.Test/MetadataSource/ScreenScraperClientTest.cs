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

        // regression: ssuser block carries a "quotarefu" counter on every authenticated
        // response. a substring scan for "quota" used to flag those as QuotaExceeded.
        [Test]
        public void Classify_JsonWithQuotarefuField_Ok()
        {
            var body = "{\"header\":{\"APIversion\":\"2.0\",\"success\":\"true\"},"
                     + "\"response\":{\"ssuser\":{\"id\":\"42\",\"quotarefu\":\"0\","
                     + "\"requeststoday\":\"3299\",\"maxrequestsperday\":\"20000\"},"
                     + "\"jeux\":[{\"id\":\"1\",\"nom\":\"Demo\"}]}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.Ok));
        }

        [Test]
        public void Classify_JsonHeaderMissingSuccess_Ok()
        {
            var body = "{\"header\":{\"APIversion\":\"2.0\",\"dateTime\":\"2026-04-30 13:40:39\"},"
                     + "\"response\":{\"jeux\":[]}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.Ok));
        }

        [Test]
        public void Classify_JsonSuccessFalse_QuotaInError_QuotaExceeded()
        {
            var body = "{\"header\":{\"success\":\"false\",\"error\":\"Quota dépassé\"},\"response\":{}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.QuotaExceeded));
        }

        [Test]
        public void Classify_JsonSuccessFalse_AuthInError_AuthFailed()
        {
            var body = "{\"header\":{\"success\":\"false\",\"error\":\"Erreur de login !\"},\"response\":{}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.AuthFailed));
        }

        [Test]
        public void Classify_JsonSuccessFalse_GenericError_NetworkError()
        {
            var body = "{\"header\":{\"success\":\"false\",\"error\":\"Erreur interne\"},\"response\":{}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.NetworkError));
        }

        // synopsis text containing "quota" or french auth keywords used to trip the old
        // substring matcher. with json-first parsing it must be ignored.
        [Test]
        public void Classify_JsonWithQuotaWordInSynopsis_Ok()
        {
            var body = "{\"header\":{\"success\":\"true\"},"
                     + "\"response\":{\"jeu\":{\"id\":\"7\",\"nom\":\"Test\","
                     + "\"synopsis\":[{\"langue\":\"en\",\"text\":\"Reach the quota of stars to win.\"}]}}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.Ok));
        }

        [Test]
        public void Classify_JsonWithFrenchAuthWordsInSynopsis_Ok()
        {
            var body = "{\"header\":{\"success\":\"true\"},"
                     + "\"response\":{\"jeu\":{\"id\":\"7\",\"nom\":\"Test\","
                     + "\"synopsis\":[{\"langue\":\"fr\",\"text\":\"Entrez votre identifiant et mot de passe.\"}]}}}";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.Ok));
        }

        [TestCase(430)]
        [TestCase(431)]
        public void Classify_HttpQuotaCodes_QuotaExceeded(int code)
        {
            var s = ScreenScraperClient.ClassifyResponse((HttpStatusCode)code, "Quota dépassé");
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.QuotaExceeded));
        }

        [Test]
        public void Classify_MalformedJson_FallsThroughToPlaintext_Quota()
        {
            // body looks like json but is broken; plaintext matcher should still catch the wording
            var body = "{ broken json with Quota dépassé inside";
            var s = ScreenScraperClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(ScreenScraperStatus.QuotaExceeded));
        }
    }
}
