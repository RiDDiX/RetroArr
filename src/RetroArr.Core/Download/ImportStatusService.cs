using System.Collections.Concurrent;

namespace RetroArr.Core.Download
{
    public class ImportStatusService
    {
        private readonly ConcurrentDictionary<(int, string), bool> _importingDownloads = new ConcurrentDictionary<(int, string), bool>();

        public void MarkImporting(int clientId, string id)
        {
            _importingDownloads.TryAdd((clientId, id), true);
        }

        public void MarkFinished(int clientId, string id)
        {
            _importingDownloads.TryRemove((clientId, id), out _);
        }

        public bool IsImporting(int clientId, string id)
        {
            return _importingDownloads.ContainsKey((clientId, id));
        }
    }
}
