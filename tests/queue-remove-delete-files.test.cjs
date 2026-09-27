const fs = require('fs');
const path = require('path');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Queue delete asks first and lets the user keep the files (seeding torrents).
const status = read('frontend', 'src', 'pages', 'Status.tsx');
assert(
  !status.includes('onClick={() => handleDelete(d.clientId, d.id)}'),
  'The queue delete button must open the confirm dialog instead of deleting straight away'
);
assert(
  status.includes('onChange={e => setRemoveFiles(e.target.checked)}'),
  'The confirm dialog must offer an "also delete files" checkbox'
);
assert(
  status.includes('?deleteFiles=${removeFiles}'),
  'The delete request must send the user\'s deleteFiles choice'
);

const controller = read('src', 'RetroArr.Api.V3', 'DownloadClients', 'DownloadClientController.cs');
assert(
  controller.includes('[FromQuery] bool deleteFiles = true') &&
  controller.includes('client.RemoveDownloadAsync(decodedId, deleteFiles)'),
  'DELETE queue/{clientId}/{downloadId} must pass deleteFiles through to the client'
);

const download = (f) => read('src', 'RetroArr.Core', 'Download', f);
assert(download('QBittorrentClient.cs').includes('"deleteFiles", deleteFiles ? "true" : "false"'),
  'qBittorrent must not hard-code deleteFiles=true');
assert(download('TransmissionClient.cs').includes('{ "delete-local-data", deleteFiles }'),
  'Transmission must not hard-code delete-local-data=true');
assert(download('DelugeClient.cs').includes('"core.remove_torrent", new object[] { id, deleteFiles }'),
  'Deluge must not hard-code remove_data=true');
assert(download('SabnzbdClient.cs').includes('&del_files={delFiles}'),
  'SABnzbd must pass del_files');
assert(!download('NzbgetClient.cs').includes('GroupParkDelete'),
  'NZBGet must not pretend to keep files: HistoryDelete removes a parked job\'s files anyway');
assert(/onClick=\{handleDelete\} disabled=\{removing\}/.test(read('frontend', 'src', 'pages', 'Status.tsx')),
  'the Remove button must be disabled while a remove is in flight');
for (const guard of [
  'if (!target || removing) return;',
  'current === target ? null : current',
  'onClick={() => { if (!removing) setRemoveTarget(null); }}',
  'onClick={() => setRemoveTarget(null)} disabled={removing}>Cancel',
]) {
  assert(read('frontend', 'src', 'pages', 'Status.tsx').includes(guard), `remove modal must keep its guard: ${guard}`);
}
