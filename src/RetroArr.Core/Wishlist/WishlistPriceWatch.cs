using System;
using RetroArr.Core.Games;

namespace RetroArr.Core.Wishlist
{
    public class WishlistPriceWatch
    {
        public int Id { get; set; }

        public int GameId { get; set; }
        public Game? Game { get; set; }

        // steam | gog (gog reserved for phase 2)
        public string Provider { get; set; } = "steam";

        // Steam app id as string, GOG product id, etc.
        public string ExternalId { get; set; } = string.Empty;

        // ISO 4217, e.g. "EUR", "USD". Driven by store response.
        public string? Currency { get; set; }

        public decimal? CurrentPrice { get; set; }
        public decimal? PreviousPrice { get; set; }

        // alert when CurrentPrice <= TargetPrice (null = no target, only "any drop")
        public decimal? TargetPrice { get; set; }

        public bool NotifyOnAnyDrop { get; set; } = true;

        public bool IsOnSale { get; set; }
        public int? DiscountPercent { get; set; }

        public DateTime? LastCheckedAt { get; set; }
        public DateTime? LastChangedAt { get; set; }

        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    }
}
