using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;

namespace RetroArr.SignalR
{
    public class ProgressNotifier : IProgressNotifier
    {
        private readonly IHubContext<ProgressHub> _hub;

        public ProgressNotifier(IHubContext<ProgressHub> hub)
        {
            _hub = hub;
        }

        public Task ScanStartedAsync() =>
            _hub.Clients.All.SendAsync(ProgressHubEvents.ScanStarted);

        public Task ScanProgressAsync(object payload) =>
            _hub.Clients.All.SendAsync(ProgressHubEvents.ScanProgress, payload);

        public Task ScanFinishedAsync(int gamesAdded) =>
            _hub.Clients.All.SendAsync(ProgressHubEvents.ScanFinished, new { gamesAdded });

        public Task DownloadSnapshotAsync(object payload) =>
            _hub.Clients.All.SendAsync(ProgressHubEvents.DownloadSnapshot, payload);

        public Task LibraryUpdatedAsync() =>
            _hub.Clients.All.SendAsync(ProgressHubEvents.LibraryUpdated);
    }
}
