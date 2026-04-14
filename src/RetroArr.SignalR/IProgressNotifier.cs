using System.Threading.Tasks;

namespace RetroArr.SignalR
{
    public interface IProgressNotifier
    {
        Task ScanStartedAsync();
        Task ScanProgressAsync(object payload);
        Task ScanFinishedAsync(int gamesAdded);
        Task DownloadSnapshotAsync(object payload);
        Task LibraryUpdatedAsync();
    }
}
