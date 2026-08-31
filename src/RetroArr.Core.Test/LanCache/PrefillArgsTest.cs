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
        public void NonSteam_HasNoSteamOnlyFlags()
        {
            var settings = new LanCacheSettings { PrefillRecent = true };
            var args = LanCachePrefillService.BuildPrefillArgs("epic", false, settings, true, "manual");
            Assert.That(args, Is.EqualTo(new[] { "prefill", "--no-ansi", "--force" }));
        }
    }
}
