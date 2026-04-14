using Microsoft.AspNetCore.SignalR;

namespace RetroArr.SignalR
{
    public class ProgressHub : Hub
    {
        public string Ping() => "pong";
    }

    public static class ProgressHubEvents
    {
        public const string ScanStarted = "scanStarted";
        public const string ScanProgress = "scanProgress";
        public const string ScanFinished = "scanFinished";
        public const string DownloadSnapshot = "downloadSnapshot";
        public const string LibraryUpdated = "libraryUpdated";
    }
}
