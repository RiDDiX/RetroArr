using System;

namespace RetroArr.Api.V3.Steam
{
    public class SteamProfileViewModel
    {
        public string SteamId { get; set; } = string.Empty;
        public string PersonaName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int PersonaState { get; set; }
        public string GameExtraInfo { get; set; } = string.Empty;
        
        // New metadata
        public string RealName { get; set; } = string.Empty;
        public string CountryCode { get; set; } = string.Empty;
        public DateTime? AccountCreated { get; set; }
        public int Level { get; set; }
    }

    public class SteamGameViewModel
    {
        public int AppId { get; set; }
        public string Name { get; set; } = string.Empty;
        public int Playtime2Weeks { get; set; }
        public int PlaytimeForever { get; set; }
        public string IconUrl { get; set; } = string.Empty;
        
        // Extended fields for UI compatibility
        public int Achieved { get; set; }
        public int TotalAchievements { get; set; }
        public double CompletionPercent { get; set; }
        public SteamNewsItemViewModel? LatestNews { get; set; }
    }

    public class SteamStatsViewModel
    {
        public int TotalGames { get; set; }
        public int TotalMinutesPlayed { get; set; }
        public double TotalHoursPlayed { get; set; }
    }

    public class SteamNewsItemViewModel
    {
        public string Title { get; set; } = string.Empty;
        public string Url { get; set; } = string.Empty;
        public string FeedLabel { get; set; } = string.Empty;
        public DateTime Date { get; set; }
    }

    public class SteamFriendViewModel
    {
        public string SteamId { get; set; } = string.Empty;
        public string PersonaName { get; set; } = string.Empty;
        public string AvatarUrl { get; set; } = string.Empty;
        public int PersonaState { get; set; } // 0=Offline, 1=Online, etc.
        public string GameExtraInfo { get; set; } = string.Empty; // Currently playing
    }
}
