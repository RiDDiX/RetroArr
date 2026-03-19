namespace RetroArr.Core.Download
{
    public class PostDownloadResult
    {
        public bool Success { get; set; }
        public string? Reason { get; set; }
        public string? DestinationPath { get; set; }

        public static PostDownloadResult Ok(string? destinationPath = null)
            => new PostDownloadResult { Success = true, DestinationPath = destinationPath };

        public static PostDownloadResult Fail(string reason)
            => new PostDownloadResult { Success = false, Reason = reason };
    }
}
