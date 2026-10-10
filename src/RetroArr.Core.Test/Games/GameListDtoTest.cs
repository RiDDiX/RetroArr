using System.Linq;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    // The library list (GET /game/paged) compares the status by name
    [TestFixture]
    public class GameListDtoTest
    {
        [Test]
        public void PagedList_SendsTheStatusByName()
        {
            var page = new PagedResult<GameListDto>
            {
                Items =
                {
                    new GameListDto { Id = 1, Status = GameStatus.Downloaded },
                    new GameListDto { Id = 2, Status = GameStatus.Missing },
                    new GameListDto { Id = 3, Status = GameStatus.InstallerDetected }
                }
            };

            // what AddNewtonsoftJson writes
            var json = JObject.Parse(JsonConvert.SerializeObject(page, new MvcNewtonsoftJsonOptions().SerializerSettings));

            Assert.That(json["items"]!.Select(i => i["status"]!.Type), Is.All.EqualTo(JTokenType.String));
            Assert.That(json["items"]!.Select(i => (string?)i["status"]), Is.EqualTo(new[] { "Downloaded", "Missing", "InstallerDetected" }));
        }
    }
}
