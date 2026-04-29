using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RetroArr.Core.Games;

namespace RetroArr.Core.Test.Games
{
    [TestFixture]
    public class DuplicateGameDetectorTest
    {
        [Test]
        public void Detect_CueBinPairOnSamePlatform_FlaggedAsPathStemDuplicate()
        {
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "SCES 002 55 Tekken 2", PlatformId = 20,
                    Path = Path.Combine("/lib", "psx", "SCES_002.55.Tekken 2.bin") },
                new() { Id = 2, Title = "Tekken 2", PlatformId = 20,
                    Path = Path.Combine("/lib", "psx", "SCES_002.55.Tekken 2.cue") }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Count, Is.EqualTo(1));
            var cluster = clusters[0];
            Assert.That(cluster.Reason, Is.EqualTo(DuplicateReason.PathStem));
            Assert.That(cluster.PlatformId, Is.EqualTo(20));
            Assert.That(cluster.Games.Select(g => g.GameId), Is.EquivalentTo(new[] { 1, 2 }));
        }

        [Test]
        public void Detect_StemMatchAcrossPlatforms_NotFlagged()
        {
            // Same stem but different platforms — legitimate (e.g. PS1 release and
            // a Saturn release both called "Tekken 2.cue" wouldn't actually share
            // a directory, but the test pins the rule that platform must match).
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "A", PlatformId = 20, Path = "/lib/psx/Game.cue" },
                new() { Id = 2, Title = "A", PlatformId = 66, Path = "/lib/saturn/Game.cue" }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            // Stem differs by directory, so no stem cluster. Title+platform also
            // differs by platform. Nothing flagged.
            Assert.That(clusters, Is.Empty);
        }

        [Test]
        public void Detect_SameTitleSamePlatform_FlaggedAsTitleAndPlatform()
        {
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Tekken 2", PlatformId = 20, Path = "/a/Tekken 2.cue" },
                new() { Id = 2, Title = "tekken 2", PlatformId = 20, Path = "/b/Tekken 2.iso" }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.TitleAndPlatform), Is.True);
            var cluster = clusters.First(c => c.Reason == DuplicateReason.TitleAndPlatform);
            Assert.That(cluster.Games.Count, Is.EqualTo(2));
        }

        [Test]
        public void Detect_SameTitleSamePlatformDifferentRegion_NotFlagged()
        {
            // Player Manager 2000 GE and EU are separate releases
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Player Manager 2000", PlatformId = 20, Region = "Germany", Path = "/psx/Player Manager 2000 (GE).cue" },
                new() { Id = 2, Title = "Player Manager 2000", PlatformId = 20, Region = "Europe", Path = "/psx/Player Manager 2000 (EU).cue" }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.TitleAndPlatform), Is.False);
        }

        [Test]
        public void Detect_SameTitlePlatformAndRegion_StillFlagged()
        {
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Tekken 2", PlatformId = 20, Region = "Europe", Path = "/a/Tekken 2.cue" },
                new() { Id = 2, Title = "Tekken 2", PlatformId = 20, Region = "Europe", Path = "/b/Tekken 2.iso" }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.TitleAndPlatform), Is.True);
        }

        [Test]
        public void Detect_SameTitleSamePlatformBothRegionsNull_StillFlagged()
        {
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Tekken 2", PlatformId = 20, Region = null, Path = "/a/Tekken 2.cue" },
                new() { Id = 2, Title = "Tekken 2", PlatformId = 20, Region = null, Path = "/b/Tekken 2.iso" }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.TitleAndPlatform), Is.True);
        }

        [Test]
        public void Detect_SharedIgdbId_FlaggedAsIgdbDuplicate()
        {
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Game A", PlatformId = 20, IgdbId = 12345 },
                new() { Id = 2, Title = "Game B", PlatformId = 20, IgdbId = 12345 }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.IgdbId), Is.True);
        }

        [Test]
        public void Detect_SameIgdbIdDifferentPlatforms_NotClustered()
        {
            // Defcon 5 / Star Wars Demolition: one IGDB id, owned on multiple
            // platforms. Each platform copy is its own legitimate game.
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Defcon 5", PlatformId = 14, Path = "/3do/d5.cue", IgdbId = 2505 },
                new() { Id = 2, Title = "Defcon 5", PlatformId = 20, Path = "/psx/d5.cue", IgdbId = 2505 }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.IgdbId), Is.False);
        }

        [Test]
        public void Detect_SameIgdbIdSamePlatform_IsClustered()
        {
            // Two rows for the same release on the same platform stay flagged.
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Tekken 2", PlatformId = 20, Path = "/psx/a.cue", IgdbId = 1042 },
                new() { Id = 2, Title = "Tekken 2", PlatformId = 20, Path = "/psx/b.cue", IgdbId = 1042 }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.IgdbId), Is.True);
        }

        [Test]
        public void Detect_NullIgdbId_DoesNotCluster()
        {
            // Two unmatched rows shouldn't cluster on igdb id 0/null.
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "A", PlatformId = 20, IgdbId = null },
                new() { Id = 2, Title = "B", PlatformId = 20, IgdbId = 0 }
            };

            var clusters = DuplicateGameDetector.Detect(games);

            Assert.That(clusters.Any(c => c.Reason == DuplicateReason.IgdbId), Is.False);
        }

        [Test]
        public void PickWinner_PrefersIgdbMatchedRow()
        {
            var cluster = new DuplicateCluster
            {
                Reason = DuplicateReason.PathStem,
                Games =
                {
                    new DuplicateMember { GameId = 1, Title = "raw", Path = "/x/Game.bin", IgdbId = null },
                    new DuplicateMember { GameId = 2, Title = "Tekken 2", Path = "/x/Game.cue", IgdbId = 9999 }
                }
            };

            var winner = DuplicateGameDetector.PickWinner(cluster);

            Assert.That(winner.GameId, Is.EqualTo(2));
        }

        [Test]
        public void PickWinner_FallsBackToCuePrimaryWhenNeitherHasIgdb()
        {
            var cluster = new DuplicateCluster
            {
                Reason = DuplicateReason.PathStem,
                Games =
                {
                    new DuplicateMember { GameId = 5, Title = "raw", Path = "/x/Game.bin" },
                    new DuplicateMember { GameId = 6, Title = "Game", Path = "/x/Game.cue" }
                }
            };

            var winner = DuplicateGameDetector.PickWinner(cluster);

            Assert.That(winner.GameId, Is.EqualTo(6));
        }

        [Test]
        public void Detect_NoDuplicates_ReturnsEmpty()
        {
            var games = new List<DuplicateProbe>
            {
                new() { Id = 1, Title = "Mario", PlatformId = 41, Path = "/lib/snes/Mario.sfc" },
                new() { Id = 2, Title = "Zelda", PlatformId = 41, Path = "/lib/snes/Zelda.sfc" }
            };

            Assert.That(DuplicateGameDetector.Detect(games), Is.Empty);
        }
    }
}
