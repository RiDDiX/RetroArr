using System.Collections.Generic;
using System.Net;
using NUnit.Framework;
using RetroArr.Core.MetadataSource.SteamGridDb;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class SteamGridDbClientTest
    {
        [Test]
        public void Classify_Http401_AuthFailed()
        {
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.Unauthorized, "");
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.AuthFailed));
        }

        [Test]
        public void Classify_Http403_AuthFailed()
        {
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.Forbidden, "");
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.AuthFailed));
        }

        [Test]
        public void Classify_Http404_Empty()
        {
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.NotFound, "");
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.Empty));
        }

        [Test]
        public void Classify_Http500_NetworkError()
        {
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.InternalServerError, "");
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.NetworkError));
        }

        [Test]
        public void Classify_JsonSuccessTrue_Ok()
        {
            var body = "{\"success\":true,\"data\":[{\"id\":1,\"score\":100,\"url\":\"https://x.png\"}]}";
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.Ok));
        }

        [Test]
        public void Classify_JsonSuccessFalse_AuthFailed()
        {
            var body = "{\"success\":false,\"errors\":[\"Invalid API key\"]}";
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.OK, body);
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.AuthFailed));
        }

        [Test]
        public void Classify_NonJson_NetworkError()
        {
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.OK, "<html>oops</html>");
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.NetworkError));
        }

        [Test]
        public void Classify_Empty_NetworkError()
        {
            var s = SteamGridDbClient.ClassifyResponse(HttpStatusCode.OK, "");
            Assert.That(s, Is.EqualTo(SteamGridDbStatus.NetworkError));
        }

        [Test]
        public void PickBest_PrefersSafeOverNsfw()
        {
            var assets = new List<SteamGridDbAsset>
            {
                new SteamGridDbAsset { Id = 1, Score = 99, Nsfw = true, Url = "nsfw.png" },
                new SteamGridDbAsset { Id = 2, Score = 50, Nsfw = false, Url = "safe.png" }
            };
            var best = SteamGridDbClient.PickBest(assets);
            Assert.That(best, Is.Not.Null);
            Assert.That(best!.Id, Is.EqualTo(2));
        }

        [Test]
        public void PickBest_AllUnsafe_PicksHighestScore()
        {
            var assets = new List<SteamGridDbAsset>
            {
                new SteamGridDbAsset { Id = 1, Score = 30, Humor = true },
                new SteamGridDbAsset { Id = 2, Score = 80, Nsfw = true }
            };
            var best = SteamGridDbClient.PickBest(assets);
            Assert.That(best, Is.Not.Null);
            Assert.That(best!.Id, Is.EqualTo(2));
        }

        [Test]
        public void PickBest_EmptyOrNull_Null()
        {
            Assert.That(SteamGridDbClient.PickBest(new List<SteamGridDbAsset>()), Is.Null);
            Assert.That(SteamGridDbClient.PickBest(null!), Is.Null);
        }
    }
}
