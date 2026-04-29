using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroArr.Core.Games
{
    // Projection of the Games row. Keeps EF navigations off the test path.
    public class DuplicateProbe
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int PlatformId { get; set; }
        public string? Path { get; set; }
        public int? IgdbId { get; set; }
        public string? Serial { get; set; }
        public string? Region { get; set; }
    }

    public enum DuplicateReason
    {
        // Same dir + stem (e.g. Foo.cue and Foo.bin).
        PathStem,
        // Same title on the same platform.
        TitleAndPlatform,
        // Same IGDB id (typically multi-disc matched twice).
        IgdbId,
        // Same disc/cart serial on the same platform.
        SerialAndPlatform
    }

    public class DuplicateMember
    {
        public int GameId { get; set; }
        public string Title { get; set; } = string.Empty;
        public string? Path { get; set; }
        public int? IgdbId { get; set; }
        public string? Serial { get; set; }
    }

    public class DuplicateCluster
    {
        public DuplicateReason Reason { get; set; }
        public string Key { get; set; } = string.Empty;
        public int? PlatformId { get; set; }
        public string? PlatformName { get; set; }
        public List<DuplicateMember> Games { get; set; } = new();
    }

    public static class DuplicateGameDetector
    {
        public static List<DuplicateCluster> Detect(IEnumerable<DuplicateProbe> games)
        {
            var clusters = new List<DuplicateCluster>();
            var list = games.ToList();

            // 1) Same dir + stem + platform.
            var stemGroups = list
                .Where(g => !string.IsNullOrWhiteSpace(g.Path))
                .Select(g => new
                {
                    Game = g,
                    Dir = SafeDirectoryName(g.Path!),
                    Stem = SafeFileNameWithoutExtension(g.Path!)
                })
                .Where(x => !string.IsNullOrEmpty(x.Stem))
                .GroupBy(x => (Dir: x.Dir.ToLowerInvariant(), Stem: x.Stem.ToLowerInvariant(), x.Game.PlatformId))
                .Where(g => g.Count() > 1);

            foreach (var group in stemGroups)
            {
                clusters.Add(new DuplicateCluster
                {
                    Reason = DuplicateReason.PathStem,
                    Key = $"{group.Key.Dir}/{group.Key.Stem}",
                    PlatformId = group.Key.PlatformId,
                    Games = group.Select(x => ToMember(x.Game)).ToList()
                });
            }

            // 2) Same title + platform + region. Different regional dumps of
            // the same game (e.g. Player Manager 2000 GE vs EU) stay separate.
            var titleGroups = list
                .Where(g => !string.IsNullOrWhiteSpace(g.Title))
                .GroupBy(g => (
                    Title: g.Title.Trim().ToLowerInvariant(),
                    g.PlatformId,
                    Region: NormalizeRegion(g.Region)))
                .Where(g => g.Count() > 1);

            foreach (var group in titleGroups)
            {
                clusters.Add(new DuplicateCluster
                {
                    Reason = DuplicateReason.TitleAndPlatform,
                    Key = group.Key.Title,
                    PlatformId = group.Key.PlatformId,
                    Games = group.Select(ToMember).ToList()
                });
            }

            // 3) Same IGDB id on the same platform. Multi-platform releases
            // share one IGDB id and are NOT duplicates across platforms.
            var igdbGroups = list
                .Where(g => g.IgdbId.HasValue && g.IgdbId.Value > 0)
                .GroupBy(g => (IgdbId: g.IgdbId!.Value, g.PlatformId))
                .Where(g => g.Count() > 1);

            foreach (var group in igdbGroups)
            {
                clusters.Add(new DuplicateCluster
                {
                    Reason = DuplicateReason.IgdbId,
                    Key = group.Key.IgdbId.ToString(),
                    PlatformId = group.Key.PlatformId,
                    Games = group.Select(ToMember).ToList()
                });
            }

            // 4) Serial + platform.
            var serialGroups = list
                .Where(g => !string.IsNullOrWhiteSpace(g.Serial))
                .GroupBy(g => (Serial: g.Serial!.Trim().ToLowerInvariant(), g.PlatformId))
                .Where(g => g.Count() > 1);

            foreach (var group in serialGroups)
            {
                clusters.Add(new DuplicateCluster
                {
                    Reason = DuplicateReason.SerialAndPlatform,
                    Key = group.Key.Serial,
                    PlatformId = group.Key.PlatformId,
                    Games = group.Select(ToMember).ToList()
                });
            }

            return clusters;
        }

        public static HashSet<int> CollectAffectedGameIds(IEnumerable<DuplicateCluster> clusters)
        {
            var ids = new HashSet<int>();
            foreach (var c in clusters)
                foreach (var m in c.Games)
                    ids.Add(m.GameId);
            return ids;
        }

        // Winner order: IGDB-matched > .m3u/.cue/.gdi primary > path on disk > lowest id.
        public static DuplicateMember PickWinner(DuplicateCluster cluster)
        {
            return cluster.Games
                .OrderByDescending(g => g.IgdbId.HasValue && g.IgdbId.Value > 0 ? 1 : 0)
                .ThenByDescending(g => IsMultiDiscPrimary(g.Path) ? 1 : 0)
                .ThenByDescending(g => PathExists(g.Path) ? 1 : 0)
                .ThenBy(g => g.GameId)
                .First();
        }

        private static DuplicateMember ToMember(DuplicateProbe g) => new()
        {
            GameId = g.Id,
            Title = g.Title,
            Path = g.Path,
            IgdbId = g.IgdbId,
            Serial = g.Serial
        };

        private static string SafeDirectoryName(string path)
        {
            try { return Path.GetDirectoryName(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static string SafeFileNameWithoutExtension(string path)
        {
            try { return Path.GetFileNameWithoutExtension(path) ?? string.Empty; }
            catch { return string.Empty; }
        }

        private static bool IsMultiDiscPrimary(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            var ext = Path.GetExtension(path).ToLowerInvariant();
            return ext == ".m3u" || ext == ".cue" || ext == ".gdi";
        }

        private static bool PathExists(string? path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            try { return File.Exists(path) || Directory.Exists(path); }
            catch { return false; }
        }

        // null and empty count as the same region bucket
        private static string NormalizeRegion(string? region) =>
            string.IsNullOrWhiteSpace(region) ? string.Empty : region.Trim().ToLowerInvariant();
    }
}
