using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.Games;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class PlatformIgdbIdTest
    {
        // Ids from IGDB's platform list; the old values pointed at other platforms
        // (152 FM-7, 57 WonderSwan, 95 PDP-1, 161 Windows Mixed Reality, 144 AY-3-8710).
        [TestCase("pokemini", 166)]
        [TestCase("jaguarcd", 410)]
        [TestCase("wonderswancolor", 123)]
        [TestCase("supervision", 415)]
        [TestCase("ngage", 42)]
        [TestCase("coco", 151)]
        public void IgdbPlatformId_MatchesIgdb(string slug, int igdbId)
        {
            Assert.That(PlatformDefinitions.AllPlatforms.Single(p => p.Slug == slug).IgdbPlatformId, Is.EqualTo(igdbId));
        }

        // IGDB has no platform for these; 122 is Nuon, 123 WonderSwan Color, 131 the SNES CD-ROM.
        [TestCase("naomi2")]
        [TestCase("atomiswave")]
        [TestCase("oricatmos")]
        public void NoIgdbPlatform_HasNoId(string slug)
        {
            Assert.That(PlatformDefinitions.AllPlatforms.Single(p => p.Slug == slug).IgdbPlatformId, Is.Null);
        }

        [Test]
        public async Task WonderSwanColorGame_OffersWonderSwanColor_NotAtomiswave()
        {
            var json = "[{\"id\":1,\"name\":\"Some WSC Game\",\"platforms\":[" +
                       "{\"id\":123,\"abbreviation\":\"WSC\",\"name\":\"WonderSwan Color\"}]}]";
            var igdbGame = JsonSerializer.Deserialize<List<IgdbGame>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })![0];

            var game = await new GameMetadataService(new IgdbClient("id", "secret"), new SteamClient()).MapIgdbToGameAsync(igdbGame);

            Assert.That(game.AvailablePlatformIds, Is.EquivalentTo(new[] { 111 }));
        }
    }
}
