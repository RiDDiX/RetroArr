namespace RetroArr.Core.Cache
{
    public static class CacheKeys
    {
        public const string GamesAll = "retroarr:games:all";
        public const string GamesProblems = "retroarr:games:problems";
        public const string DbStats = "retroarr:db:stats";
        public const string DownloadCounts = "retroarr:downloads:counts";

        public static string GameDetail(int id) => $"retroarr:game:{id}";
        public static string GameFiles(int id) => $"retroarr:game:{id}:files";

        public const string GamesPrefix = "retroarr:games:";
        public const string GamePrefix = "retroarr:game:";
        public const string AllPrefix = "retroarr:";
    }
}
