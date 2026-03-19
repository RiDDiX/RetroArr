using System.Collections.Concurrent;

namespace RetroArr.Core.Download
{
    public class ImportStatusService
    {
        private readonly ConcurrentDictionary<string, bool> _importingDownloads = new ConcurrentDictionary<string, bool>();

        public void MarkImporting(string id)
        {
            _importingDownloads.TryAdd(id, true);
        }

        public void MarkFinished(string id)
        {
            _importingDownloads.TryRemove(id, out _);
        }

        public bool IsImporting(string id)
        {
            return _importingDownloads.ContainsKey(id);
        }
    }
}
