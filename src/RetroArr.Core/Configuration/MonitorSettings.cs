using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace RetroArr.Core.Configuration
{
    // Knobs for the per-game auto-search pipeline. Defaults match the
    // conservative scoring model: a release must be a strong title match
    // AND clear the auto-download bar before anything lands on disk.
    [SuppressMessage("Microsoft.Usage", "CA2227:CollectionPropertiesShouldBeReadOnly")]
    [SuppressMessage("Microsoft.Design", "CA1002:DoNotExposeGenericLists")]
    public class MonitorSettings
    {
        // Master kill-switch for the background poller.
        public bool Enabled { get; set; } = true;

        // How often the background poller scans monitored games.
        public int PollIntervalHours { get; set; } = 6;

        // Score gates. Anything above AutoDownloadThreshold is sent to the
        // configured download client. Between Review and Auto is "needs human".
        public int AutoDownloadThreshold { get; set; } = 85;
        public int ReviewThreshold { get; set; } = 50;

        // Hard rejects (these short-circuit before scoring).
        public int MinSeedersTorrent { get; set; } = 2;
        public int MinTitleSimilarityPercent { get; set; } = 65;
        public int MaxReleaseAgeDays { get; set; } = 0; // 0 = no age limit

        // Soft scoring bonuses.
        public int RegionMatchBonus { get; set; } = 20;
        public int LanguageMatchBonus { get; set; } = 15;
        public int RevisionMatchBonus { get; set; } = 10;
        public int VerifiedSourceBonus { get; set; } = 30;
        public int SizeInRangeBonus { get; set; } = 10;

        // Soft scoring penalties (positive numbers, subtracted).
        public int UnknownUploaderPenalty { get; set; } = 20;
        public int HackOrPatchPenalty { get; set; } = 40;
        public int SizeOutOfRangePenalty { get; set; } = 50;
        public int WrongRegionPenalty { get; set; } = 15;

        // Trusted source prefixes that earn the verified-source bonus.
        // Case-insensitive substring match against the release title.
        // Initializers stay empty - System.Text.Json defaults to appending
        // to existing collections instead of replacing them, which would
        // duplicate every preset entry on each load round-trip. Real defaults
        // come from CreateDefault() when the config file is missing.
        public List<string> VerifiedSources { get; set; } = new();

        // Trusted scene/p2p groups. Earn a smaller bonus than verified
        // dumps but better than unknown uploaders.
        public List<string> TrustedReleaseGroups { get; set; } = new();

        // Tokens that trigger the hack/patch penalty when the user has
        // not explicitly asked for a hack/patch version of the game.
        public List<string> HackPatchTokens { get; set; } = new();

        // Default preferred region used when the Game has no Region set.
        // Empty string = no preference, any region is fine.
        public string PreferredRegion { get; set; } = string.Empty;

        // If true, anything not from a verified source or trusted group is
        // ineligible for auto-download (still surfaces in the manual view).
        public bool RequireTrustedSourceForAuto { get; set; } = true;

        // Factory used by ConfigurationService when no monitor.json exists
        // yet. Centralises the "sensible starter values" so the on-disk
        // schema can stay empty-by-default.
        public static MonitorSettings CreateDefault()
        {
            return new MonitorSettings
            {
                VerifiedSources = new List<string> { "No-Intro", "Redump", "TOSEC", "GoodSet" },
                TrustedReleaseGroups = new List<string> { "CODEX", "EMPRESS", "FitGirl", "DODI", "RUNE", "P2P", "GOG" },
                HackPatchTokens = new List<string> { "[Hack]", "[Patch]", "(Hack)", "Kaizo", "Translation", "Translated", "FanTranslation" }
            };
        }
    }
}
