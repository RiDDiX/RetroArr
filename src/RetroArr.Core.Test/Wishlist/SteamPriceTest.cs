using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;
using RetroArr.Core.Data;
using RetroArr.Core.Games;
using RetroArr.Core.Notifications;
using RetroArr.Core.Wishlist;

namespace RetroArr.Core.Test.Wishlist
{
    [TestFixture]
    public class SteamPriceTest
    {
        private const string Body = "{\"123\":{\"success\":true,\"data\":[]},\"456\":{\"success\":true,\"data\":{\"price_overview\":{\"final\":1999,\"currency\":\"EUR\"}}}}";

        private sealed class FakeHandler : HttpMessageHandler
        {
            protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct) =>
                Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(Body) });
        }

        private sealed class NoWebhooks : IWebhookService
        {
            public Task TriggerAsync(WebhookEvents eventType, object? data) => Task.CompletedTask;
            public Task SendAsync(Webhook webhook, WebhookEvents eventType, object? data) => Task.CompletedTask;
        }

        // The client builds its own HttpClient, so the test swaps in one with a canned answer
        private static SteamPriceClient Client()
        {
            var client = new SteamPriceClient();
            typeof(SteamPriceClient).GetField("_httpClient", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(client, new HttpClient(new FakeHandler()) { BaseAddress = new Uri("https://store.steampowered.com/") });
            return client;
        }

        [Test]
        public async Task Client_QuotesOnlyTheAppWithAPrice()
        {
            using var client = Client();
            var quotes = await client.GetPricesAsync(new[] { "123", "456" });

            Assert.That(quotes.ContainsKey("123"), Is.False, "no price: left out, not failed");
            Assert.That(quotes["456"]!.Final, Is.EqualTo(19.99m));
            Assert.That(quotes["456"]!.Currency, Is.EqualTo("EUR"));
        }

        [Test]
        public async Task Service_KeepsTheOldPrice_AndDoesNotCountItAsFailed()
        {
            using var connection = new SqliteConnection("DataSource=:memory:");
            connection.Open();
            var options = new DbContextOptionsBuilder<RetroArrDbContext>().UseSqlite(connection).Options;
            using var db = new RetroArrDbContext(options);
            db.Database.EnsureCreated();
            db.Platforms.Add(new Platform { Id = 1, Name = "PC", Slug = "pc", FolderName = "windows" });
            db.Games.AddRange(new Game { Id = 1, Title = "A", PlatformId = 1 }, new Game { Id = 2, Title = "B", PlatformId = 1 });
            db.WishlistPriceWatches.AddRange(
                new WishlistPriceWatch { GameId = 1, ExternalId = "123", CurrentPrice = 5m, Currency = "EUR" },
                new WishlistPriceWatch { GameId = 2, ExternalId = "456" });
            await db.SaveChangesAsync();

            using var client = Client();
            var summary = await new WishlistPriceService(db, client, new NoWebhooks()).RefreshAllAsync();

            Assert.That(summary.Failed, Is.EqualTo(0));
            Assert.That(summary.Updated, Is.EqualTo(1));
            var watches = db.WishlistPriceWatches.AsNoTracking().ToList();
            Assert.That(watches.Single(w => w.ExternalId == "123").CurrentPrice, Is.EqualTo(5m));
            Assert.That(watches.Single(w => w.ExternalId == "456").CurrentPrice, Is.EqualTo(19.99m));
        }
    }
}
