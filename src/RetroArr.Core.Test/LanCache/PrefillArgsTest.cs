using NUnit.Framework;
using RetroArr.Core.Configuration;
using RetroArr.Core.LanCache;

namespace RetroArr.Core.Test.LanCache
{
    // The nightly run must not be a full re-download: --force makes the prefill
    // tools fetch every selected app again (a reseed/benchmark knob), which is what
    // hammered the scheduled 02:05 Steam run. Manual runs keep it on purpose.
    [TestFixture]
    public class PrefillArgsTest
    {
        [Test]
        public void Scheduled_DoesNotForce()
        {
            var args = LanCachePrefillService.BuildPrefillArgs("steam", true, new LanCacheSettings(), true, "scheduled");
            Assert.That(args, Does.Not.Contain("--force"));
            Assert.That(args, Is.EqualTo(new[] { "prefill", "--no-ansi", "--os", "windows" }));
        }

        [Test]
        public void Manual_Forces()
        {
            var args = LanCachePrefillService.BuildPrefillArgs("steam", true, new LanCacheSettings(), true, "manual");
            Assert.That(args, Contains.Item("--force"));
        }

        [Test]
        public void AllOwned_AddsAllOnlyWithoutSelection()
        {
            var settings = new LanCacheSettings { PrefillAllOwned = true };
            Assert.That(LanCachePrefillService.BuildPrefillArgs("steam", true, settings, false, "scheduled"),
                        Contains.Item("--all"));
            Assert.That(LanCachePrefillService.BuildPrefillArgs("steam", true, settings, true, "scheduled"),
                        Does.Not.Contain("--all"));
        }

        [Test]
        public void SteamOptions_Unchanged()
        {
            var settings = new LanCacheSettings { PrefillAllOwned = true, PrefillRecent = true, PrefillOs = "linux" };
            Assert.That(LanCachePrefillService.BuildPrefillArgs("steam", true, settings, false, "manual"),
                        Is.EqualTo(new[] { "prefill", "--no-ansi", "--force", "--all", "--recent", "--os", "linux" }));
        }

        [Test]
        public void MultipleOs_BecomeOneFlagEach()
        {
            // "--os windows,linux" is rejected by the tool: one --os per value.
            var settings = new LanCacheSettings { PrefillOs = "windows,linux" };
            Assert.That(LanCachePrefillService.BuildPrefillArgs("steam", true, settings, true, "scheduled"),
                        Is.EqualTo(new[] { "prefill", "--no-ansi", "--os", "windows", "--os", "linux" }));
        }

        [TestCase("windows,linux,macos", "windows|linux|macos")]
        [TestCase(" Windows , LINUX ", "windows|linux")]
        [TestCase("windows,windows", "windows")]
        [TestCase("plan9", "windows")]     // unknown values fall back to the tool's default
        [TestCase("", "windows")]
        [TestCase(null, "windows")]
        public void ParseOsList_NormalizesTheSetting(string? value, string expected)
        {
            Assert.That(string.Join("|", LanCachePrefillService.ParseOsList(value)), Is.EqualTo(expected));
        }

        [TestCase("manual-retry")]
        [TestCase("scheduled-retry")]
        public void RetryPass_IsNeverForced(string trigger)
        {
            // The retry exists to pick up what a run skipped - forcing it would
            // re-download the whole selection instead.
            var args = LanCachePrefillService.BuildPrefillArgs("steam", true, new LanCacheSettings(), true, trigger);
            Assert.That(args, Does.Not.Contain("--force"));
        }

        [TestCase("[9:43:31 PM] Unexpected download error : Unable to download manifests!  Skipping app...", true)]
        [TestCase("Unexpected download error : name or service not known Skipping app...", true)]
        [TestCase("[9:43:31 PM] Starting Half-Life 2", false)]
        [TestCase("Prefill complete!", false)]
        public void IsSkippedAppLine_SpotsGivenUpApps(string line, bool expected)
        {
            Assert.That(LanCachePrefillService.IsSkippedAppLine(line), Is.EqualTo(expected));
        }

        [Test]
        public void NonSteam_HasNoSteamOnlyFlags()
        {
            var settings = new LanCacheSettings { PrefillRecent = true };
            var args = LanCachePrefillService.BuildPrefillArgs("epic", false, settings, true, "manual");
            Assert.That(args, Is.EqualTo(new[] { "prefill", "--no-ansi", "--force" }));
        }
    }
}
