using System;
using Microsoft.AspNetCore.Http;
using NUnit.Framework;
using RetroArr.Api.V3.Auth;

namespace RetroArr.Core.Test.Security
{
    [TestFixture]
    public class SignedUrlTest
    {
        private const string Key = "test-api-key";
        private static readonly DateTimeOffset Now = new(2026, 9, 28, 12, 0, 0, TimeSpan.Zero);

        private static HttpRequest RequestFor(string url)
        {
            var context = new DefaultHttpContext();
            var q = url.IndexOf('?');
            context.Request.Path = q < 0 ? url : url[..q];
            context.Request.QueryString = new QueryString(q < 0 ? "" : url[q..]);
            return context.Request;
        }

        [TestCase("/api/v3/emulator/5/rom")]
        [TestCase("/api/v3/game/5/local-media/file?path=Chrono%20Trigger%20%28USA%29-image.png&folder=images")]
        [TestCase("/api/v3/game/5/files/download?path=sub%2FGame%20%5B%21%5D.zip")]
        public void SignedLink_IsValidForOneToTwoLifetimes(string url)
        {
            var signed = SignedUrl.Sign(url, Key, Now);
            Assert.That(signed, Does.StartWith(url));
            Assert.That(SignedUrl.IsValid(RequestFor(signed), Key, Now), Is.True);
            Assert.That(SignedUrl.IsValid(RequestFor(signed), Key, Now + SignedUrl.DefaultLifetime), Is.True);
            Assert.That(SignedUrl.IsValid(RequestFor(signed), Key, Now + 2 * SignedUrl.DefaultLifetime + TimeSpan.FromSeconds(1)), Is.False);
        }

        [Test]
        public void Link_StaysTheSameWithinAWindow_SoTheEmulatorCacheHits()
        {
            var day = TimeSpan.FromDays(1);
            var start = new DateTimeOffset(2026, 9, 28, 0, 0, 1, TimeSpan.Zero);
            var morning = SignedUrl.Sign("/api/v3/emulator/5/rom", Key, start, day);
            Assert.That(SignedUrl.Sign("/api/v3/emulator/5/rom", Key, start.AddHours(23), day), Is.EqualTo(morning));
            Assert.That(SignedUrl.Sign("/api/v3/emulator/5/rom", Key, start.AddHours(24), day), Is.Not.EqualTo(morning));
        }

        [Test]
        public void RotatedKey_RevokesTheLink()
        {
            var signed = SignedUrl.Sign("/api/v3/emulator/5/rom", Key, Now);
            Assert.That(SignedUrl.IsValid(RequestFor(signed), "rotated", Now), Is.False);
            Assert.That(SignedUrl.IsValid(RequestFor(signed), "", Now), Is.False);
        }

        [Test]
        public void ChangedPathQueryOrExpiry_BreaksTheSignature()
        {
            var signed = SignedUrl.Sign("/api/v3/game/5/files/download?path=a.zip", Key, Now);
            var sig = signed[signed.IndexOf("&sig=", StringComparison.Ordinal)..];
            var exp = signed[(signed.IndexOf("exp=", StringComparison.Ordinal) + 4)..signed.IndexOf("&sig=", StringComparison.Ordinal)];
            var later = (long.Parse(exp) + 86400).ToString();

            Assert.That(SignedUrl.IsValid(RequestFor(signed.Replace("/game/5/", "/game/6/")), Key, Now), Is.False);
            Assert.That(SignedUrl.IsValid(RequestFor(signed.Replace("path=a.zip", "path=b.zip")), Key, Now), Is.False);
            Assert.That(SignedUrl.IsValid(RequestFor(signed.Replace("exp=" + exp, "exp=" + later)), Key, Now), Is.False);
            // a second exp can't be slipped in front of the signed one
            Assert.That(SignedUrl.IsValid(RequestFor(signed.Replace("?path=", "?exp=" + later + "&path=")), Key, Now), Is.False);
            // parameters after the signature are refused
            Assert.That(SignedUrl.IsValid(RequestFor(signed + "&path=b.zip"), Key, Now), Is.False);
            Assert.That(SignedUrl.IsValid(RequestFor("/api/v3/game/5/files/download?path=a.zip" + sig), Key, Now), Is.False);
            Assert.That(SignedUrl.IsValid(RequestFor("/api/v3/game/5/files/download?path=a.zip"), Key, Now), Is.False);
        }
    }
}
