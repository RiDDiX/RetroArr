using System.Collections.Generic;
using System.Text.Json;
using System.Threading.Tasks;
using NUnit.Framework;
using RetroArr.Core.MetadataSource;
using RetroArr.Core.MetadataSource.Igdb;
using RetroArr.Core.MetadataSource.Steam;

namespace RetroArr.Core.Test.MetadataSource
{
    [TestFixture]
    public class IgdbPlatformMatchTest
    {
        [Test]
        public async Task SearchResult_OffersXbox360_ForGameAlsoOnPcAndPs3()
        {
            // Red Dead Redemption as IGDB returns it
            var json = "[{\"id\":434,\"name\":\"Red Dead Redemption\",\"platforms\":[" +
                       "{\"id\":6,\"abbreviation\":\"PC\",\"name\":\"PC (Microsoft Windows)\"}," +
                       "{\"id\":9,\"abbreviation\":\"PS3\",\"name\":\"PlayStation 3\"}," +
                       "{\"id\":12,\"abbreviation\":\"X360\",\"name\":\"Xbox 360\"}]}]";
            var igdbGame = JsonSerializer.Deserialize<List<IgdbGame>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })![0];

            var service = new GameMetadataService(new IgdbClient("id", "secret"), new SteamClient());
            var game = await service.MapIgdbToGameAsync(igdbGame);

            // 1 PC (Windows), 22 PS3, 31 Xbox 360. Steam/GOG/Epic share IGDB id 6 but aren't offered,
            // and 90 PC Engine must not show up just because its name contains "PC".
            Assert.That(game.AvailablePlatformIds, Is.EquivalentTo(new[] { 1, 22, 31 }));
        }

        [Test]
        public async Task PlatformsWithoutIds_StillMatchByName()
        {
            var json = "[{\"id\":434,\"name\":\"Red Dead Redemption\",\"platforms\":[" +
                       "{\"abbreviation\":\"PS3\",\"name\":\"PlayStation 3\"}," +
                       "{\"abbreviation\":\"X360\",\"name\":\"Xbox 360\"}]}]";
            var igdbGame = JsonSerializer.Deserialize<List<IgdbGame>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })![0];

            var game = await new GameMetadataService(new IgdbClient("id", "secret"), new SteamClient()).MapIgdbToGameAsync(igdbGame);

            Assert.That(game.AvailablePlatformIds, Is.EquivalentTo(new[] { 22, 31 }));
        }

        [Test]
        public async Task PocketStation_DoesNotOfferSwitch2()
        {
            var json = "[{\"id\":1,\"name\":\"Some PS1 Game\",\"platforms\":[" +
                       "{\"id\":7,\"abbreviation\":\"PS1\",\"name\":\"PlayStation\"}," +
                       "{\"id\":441,\"name\":\"PocketStation\"}]}]";
            var igdbGame = JsonSerializer.Deserialize<List<IgdbGame>>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })![0];

            var game = await new GameMetadataService(new IgdbClient("id", "secret"), new SteamClient()).MapIgdbToGameAsync(igdbGame);

            Assert.That(game.AvailablePlatformIds, Does.Not.Contain(47));
        }
    }
}
