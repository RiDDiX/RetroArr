using NUnit.Framework;
using RetroArr.Core.LanCache;

namespace RetroArr.Core.Test.LanCache
{
    // The run history records which games a prefill touched. That list is parsed
    // from the tools' streaming "Starting <name>" progress lines, so the parser has
    // to survive the real-world output shapes seen in SteamPrefill/EpicPrefill logs.
    [TestFixture]
    public class PrefillGameParsingTest
    {
        [TestCase("[9:43:31 PM] Starting  Build & Battle", "Build & Battle")]
        [TestCase("[9:43:35 PM] Starting \"LIFE\" not found;", "\"LIFE\" not found;")]
        [TestCase("[9:43:39 PM] Starting (square)", "(square)")]
        [TestCase("[9:43:43 PM] Starting *NEW* EPIC SCUFFED BHOP SIMULATOR 2023 (POG CHAMP)",
                  "*NEW* EPIC SCUFFED BHOP SIMULATOR 2023 (POG CHAMP)")]
        [TestCase("Starting Half-Life 2", "Half-Life 2")]
        public void ExtractStartingGame_ParsesRealProgressLines(string line, string expected)
        {
            Assert.That(LanCachePrefillService.ExtractStartingGame(line), Is.EqualTo(expected));
        }

        [Test]
        public void ExtractStartingGame_IgnoresLoginBanner()
        {
            // "Starting login!" is the tool's own banner, not a game.
            Assert.That(LanCachePrefillService.ExtractStartingGame("[10:38:07 PM] Starting login!"), Is.Null);
        }

        [TestCase("[9:43:33 PM] Finished downloading 28.74 MiB in 00.3806 - 633.46 Mbit/s")]
        [TestCase("Fetching depot manifests...")]
        [TestCase("Downloading..: 61%")]
        [TestCase("Connecting to Steam...")]
        [TestCase("")]
        [TestCase("[9:43:31 PM] Starting")]
        [TestCase("[9:43:31 PM] Starting   ")]
        [TestCase("[9:43:31 PM] StartingWithoutSeparator")]
        public void ExtractStartingGame_IgnoresNonGameLines(string line)
        {
            Assert.That(LanCachePrefillService.ExtractStartingGame(line), Is.Null);
        }

        [Test]
        public void ExtractStartingGame_DoesNotMatchMidSentence()
        {
            // Only a line that *begins* with the marker (after the timestamp) is a game.
            Assert.That(LanCachePrefillService.ExtractStartingGame("[1:00 PM] Re-starting Foo"), Is.Null);
            Assert.That(LanCachePrefillService.ExtractStartingGame("Now Starting Foo"), Is.Null);
        }
    }
}
