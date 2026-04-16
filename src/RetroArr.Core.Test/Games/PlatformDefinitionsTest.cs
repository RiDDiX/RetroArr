using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class PlatformDefinitionsTest
    {
        [Test]
        public void AllPlatforms_IsNotEmpty()
        {
            Assert.That(PlatformDefinitions.AllPlatforms, Is.Not.Empty);
        }

        [Test]
        public void AllPlatforms_IdsAreUnique()
        {
            var ids = PlatformDefinitions.AllPlatforms.Select(p => p.Id).ToList();
            Assert.That(ids, Is.Unique);
        }

        [Test]
        public void AllPlatforms_SlugsAreUnique()
        {
            var slugs = PlatformDefinitions.AllPlatforms.Select(p => p.Slug).ToList();
            Assert.That(slugs, Is.Unique);
        }

        [Test]
        public void AllPlatforms_FolderNamesAreNonEmpty()
        {
            // Every platform must have a non-empty FolderName
            foreach (var p in PlatformDefinitions.AllPlatforms)
            {
                Assert.That(p.FolderName, Is.Not.Null.And.Not.Empty, $"Platform Id={p.Id} Slug={p.Slug} has empty FolderName");
            }
        }

        [Test]
        public void AllPlatforms_HaveNonEmptySlugAndFolderName()
        {
            foreach (var p in PlatformDefinitions.AllPlatforms)
            {
                Assert.That(p.Slug, Is.Not.Null.And.Not.Empty, $"Platform Id={p.Id} has empty Slug");
                Assert.That(p.FolderName, Is.Not.Null.And.Not.Empty, $"Platform Id={p.Id} has empty FolderName");
            }
        }

        [TestCase("gog")]
        [TestCase("pc")]
        [TestCase("switch")]
        [TestCase("ps4")]
        [TestCase("ps5")]
        [TestCase("3do")]
        public void AllPlatforms_ContainsExpectedPlatform(string slug)
        {
            var found = PlatformDefinitions.AllPlatforms.Any(p => p.Slug == slug);
            Assert.That(found, Is.True, $"Missing expected platform: {slug}");
        }

        [Test]
        public void AllPlatforms_GogPlatformHasId126()
        {
            var gog = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "gog");
            Assert.That(gog, Is.Not.Null);
            Assert.That(gog!.Id, Is.EqualTo(126));
        }

        [Test]
        public void ThreeDO_HasCorrectIgdbId()
        {
            var threeDO = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "3do");
            Assert.That(threeDO, Is.Not.Null, "3DO platform missing");
            Assert.That(threeDO!.IgdbPlatformId, Is.EqualTo(50));
            Assert.That(threeDO.FolderName, Is.EqualTo("3do"));
            Assert.That(threeDO.Type, Is.EqualTo(PlatformType.ThreeDO));
        }

        [Test]
        public void SegaCD_HasBatoceraFolderName()
        {
            var segacd = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "segacd");
            Assert.That(segacd, Is.Not.Null, "Sega CD platform missing");
            Assert.That(segacd!.BatoceraFolderName, Is.EqualTo("megacd"));
            Assert.That(segacd.RetroBatFolderName, Is.EqualTo("megacd"));
        }

        [Test]
        public void SegaCD_MatchesFolderName_BothVariants()
        {
            var segacd = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == "segacd");
            Assert.That(segacd, Is.Not.Null);
            Assert.That(segacd!.MatchesFolderName("segacd"), Is.True);
            Assert.That(segacd.MatchesFolderName("megacd"), Is.True);
        }

        [TestCase("nes", ".nes,.unf,.unif,.zip,.7z")]
        [TestCase("snes", ".sfc,.smc,.fig,.gd3,.gd7,.dx2,.bsx,.swc,.zip,.7z")]
        [TestCase("ps1", ".bin,.cue,.chd,.pbp,.iso,.img,.mdf,.toc,.cbn,.m3u,.ccd")]
        [TestCase("3do", ".iso,.chd,.cue")]
        public void AllPlatforms_MatchesFolderName_BySlug(string slug, string _)
        {
            var platform = PlatformDefinitions.AllPlatforms.FirstOrDefault(p => p.Slug == slug);
            Assert.That(platform, Is.Not.Null, $"Platform {slug} not found");
            Assert.That(platform!.MatchesFolderName(platform.FolderName), Is.True);
        }
    }
}
