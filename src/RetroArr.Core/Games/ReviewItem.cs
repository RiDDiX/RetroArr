using System;
using System.Collections.Generic;

namespace RetroArr.Core.Games
{
    public class ReviewItem
    {
        public string Id { get; set; } = Guid.NewGuid().ToString();
        public List<string> FilePaths { get; set; } = new();
        public string? DetectedPlatformKey { get; set; }
        public int? DetectedPlatformId { get; set; }
        public string? DetectedTitle { get; set; }
        public string? DiskName { get; set; }
        public string? Region { get; set; }
        public string? Serial { get; set; }
        public ReviewReason Reason { get; set; }
        public string? ReasonDetail { get; set; }
        public ReviewStatus Status { get; set; } = ReviewStatus.Pending;
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        // User overrides (set during manual review)
        public int? AssignedPlatformId { get; set; }
        public int? AssignedGameId { get; set; }
        public string? OverrideTitle { get; set; }
        public string? OverrideDiskName { get; set; }
    }

    public enum ReviewReason
    {
        PlatformAmbiguous,
        TitleAmbiguous,
        MultipleMetadataMatches,
        UnknownExtension,
        LowConfidenceMatch
    }

    public enum ReviewStatus
    {
        Pending,
        Mapped,
        Finalized,
        Ignored,
        Dismissed
    }
}
