using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace RetroArr.Core.Games
{
    // Lightweight DTO so the detector doesn't pull in the full Game entity (which
    // would drag EF navigation properties through every test). Health-check and
    // repair build these from a projection on the Games table.
    public class DuplicateProbe
    {
        public int Id { get; set; }
        public string Title { get; set; } = string.Empty;
        public int PlatformId { get; set; }
        public string? Path { get; set; }
        public int? IgdbId { get; set; }
        public string? Serial { get; set; }
    }

    public enum DuplicateReason
    {
        // Two rows whose Path sits in the same directory and shares the exact
        // file-name stem. The classic "Foo.cue" vs "Foo.bin" mis-split.
        PathStem,
        // Same normalised title on the same platform. Catches scanner re-runs
        // that ingested a renamed/cleaned title.
        TitleAndPlatform,
        // Two rows pointing at the same IGDB entry — usually the result of a
        // multi-disc game being matched twice (once per disc) before m3u
        // grouping kicked in.
        IgdbId,
        // Same official disc/cart serial on the same platform (PSX SLES/SCES,
        // Switch title id, etc.). Strongest signal we have for "same game".
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

            // 1) Path-stem inside the same directory + platform.
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

            // 2) Title + platform (case-insensitive, trimmed).
            var titleGroups = list
                .Where(g => !string.IsNullOrWhiteSpace(g.Title))
                .GroupBy(g => (Title: g.Title.Trim().ToLowerInvariant(), g.PlatformId))
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

            // 3) IGDB id (cross-platform on purpose — the same entry shouldn't
            // exist twice). Exclude null/0 since those mean "no metadata yet".
            var igdbGroups = list
                .Where(g => g.IgdbId.HasValue && g.IgdbId.Value > 0)
                .GroupBy(g => g.IgdbId!.Value)
                .Where(g => g.Count() > 1);

            foreach (var group in igdbGroups)
            {
                clusters.Add(new DuplicateCluster
                {
                    Reason = DuplicateReason.IgdbId,
                    Key = group.Key.ToString(),
                    PlatformId = null,
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

        // Pick which row should "win" inside a cluster. Order:
        //   1) Has IGDB id     — already matched, don't lose the metadata.
        //   2) Path is a multi-disc primary (.m3u, .cue, .gdi) — semantically
        //      the canonical pointer for the disc.
        //   3) Path actually exists on disk.
        //   4) Lowest GameId — oldest row, least likely to be the stale one.
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
    }
}
