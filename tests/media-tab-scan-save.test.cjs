const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Pull one `const name = ... };` block out of a component and run it with the given stand-ins.
const load = (src, name, sandbox) => {
  const block = src.match(new RegExp(`const ${name} = [\\s\\S]*?\\n  \\};`));
  assert(block, `${name} not found`);
  const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  return vm.runInNewContext(`${js}\n${name}`, sandbox);
};

const tab = read('frontend', 'src', 'components', 'settings', 'MediaTab.tsx');
const refused = data => Object.assign(new Error('Request failed with status code 400'), { isAxiosError: true, response: { status: 400, data } });
const settle = () => new Promise(r => setImmediate(r));
const setters = ['setFolderPath', 'setDownloadPath', 'setDestinationPath', 'setDestinationPathPattern', 'setUseDestinationPattern',
  'setFolderNamingMode', 'setWinePrefixPath', 'setBiosPath', 'setTrashPath', 'setTrashRetentionDays', 'setMissingRetentionDays',
  'setBiosFiles', 'setPostDownloadSettings', 'setScanning'];

// Runs a MediaTab handler against a server that answers with `answer`
const run = (name, answer, extra = {}) => {
  const calls = { posts: [], gets: [], alerts: [], set: {}, mediaReloads: 0, fullReloads: 0 };
  const sandbox = {
    apiClient: {
      post: async (url, body) => { calls.posts.push({ url, body }); return answer(url); },
      get: async url => { calls.gets.push(url); return answer(url); },
    },
    isAxiosError: e => !!(e && e.isAxiosError),
    isConflict: e => !!(e && e.isAxiosError && e.response.status === 409),
    getErrorMessage: e => (e && e.response && e.response.data && e.response.data.message) || 'Request failed',
    t: k => k,
    alert: m => { calls.alerts.push(m); },
    console: { error: () => {} },
    loadMediaSettings: async () => { calls.mediaReloads++; },
    loadSettings: async () => { calls.fullReloads++; },
    prefixSave: { current: Promise.resolve(true) },
    folderPath: '/games', downloadPath: '/downloads', destinationPath: '/games', destinationPathPattern: '{Platform}/{Title}',
    useDestinationPattern: true, folderNamingMode: 'native', winePrefixPath: '/wine', biosPath: '/bios',
    trashPath: '/trash', trashRetentionDays: 14, missingRetentionDays: 14,
    ...extra,
  };
  for (const s of setters) sandbox[s] = v => { calls.set[s] = v; };
  return { fn: load(tab, name, sandbox), calls };
};
const ok = async () => ({ data: {} });
const scanCall = from => (tab.slice(tab.indexOf(from)).match(/handleScanNow\(([^)]*)\)/) || [])[1];

(async () => {
  // The Wine prefix's Scan now asks for the external scan, the library's for a scan of the library folder
  assert(scanCall('id="wine-path"') === 'true', 'the Wine prefix Scan now must ask for the external scan');
  assert(scanCall('id="folder-path"') === '', 'the library Scan now scans the library folder');

  const wine = run('handleScanNow', ok);
  await wine.fn(true);
  assert(wine.calls.posts.length === 1 && wine.calls.posts[0].url === '/media/scan', 'the external scan must post /media/scan');
  assert(wine.calls.posts[0].body.external === true && !('folderPath' in wine.calls.posts[0].body),
    'the external scan must not send the prefix as a library folder');
  const library = run('handleScanNow', ok);
  await library.fn();
  assert(library.calls.posts[0].body.folderPath === '/games' && !library.calls.posts[0].body.external,
    'the library scan still sends the library folder');

  // The server scans the stored prefix: the scan waits for a prefix save still on its way, and a refused one stops it
  assert(tab.includes('onBlur={() => { prefixSave.current = saveMediaConfig({ winePrefixPath }); }}'),
    'the Wine prefix field must keep its save so Scan now can wait for it');
  assert(tab.includes("else if (activeFolderField === 'wine') { setWinePrefixPath(path); prefixSave.current = saveMediaConfig({ winePrefixPath: path }); }"),
    'a prefix picked in the folder browser must be waited for as well');
  let finish;
  const pending = run('handleScanNow', ok, { prefixSave: { current: new Promise(r => { finish = r; }) } });
  const scanning = pending.fn(true);
  await settle();
  assert(pending.calls.posts.length === 0, 'the external scan must wait for the prefix save');
  finish(true);
  await scanning;
  assert(pending.calls.posts.length === 1 && pending.calls.posts[0].body.external === true, 'the external scan must start once the prefix is saved');
  const unsaved = run('handleScanNow', ok, { prefixSave: { current: Promise.resolve(false) } });
  await unsaved.fn(true);
  assert(unsaved.calls.posts.length === 0 && unsaved.calls.set.setScanning === false, 'a refused prefix save must not start a scan');

  // A refused scan says why; only the IGDB error code means missing credentials
  const noPrefix = run('handleScanNow', async () => { throw refused({ message: "The Wine prefix '/wine' does not exist." }); });
  await noPrefix.fn(true);
  assert(noPrefix.calls.alerts.length === 1 && noPrefix.calls.alerts[0].includes("The Wine prefix '/wine' does not exist."),
    `a refused scan must show the reason, got ${JSON.stringify(noPrefix.calls.alerts)}`);
  const noIgdb = run('handleScanNow', async () => { throw refused({ errorCode: 'IGDB_NOT_CONFIGURED', message: 'IGDB credentials are required' }); });
  await noIgdb.fn(true);
  assert(noIgdb.calls.alerts.length === 1 && noIgdb.calls.alerts[0] === 'igdbRequired', 'missing IGDB credentials keep their own message');

  // A refused save shows the reason and puts the stored media settings back, unsaved post-download edits stay
  const reason = "The trash folder '/games/trash' can't be the library folder '/games', lie inside it or hold it.";
  const save = run('saveMediaConfig', async () => { throw refused({ message: reason }); });
  assert(await save.fn({ trashPath: '/games/trash' }) === false, 'a refused save must report that nothing was saved');
  assert(save.calls.posts[0].body.TrashPath === '/games/trash', 'the save must send the new trash folder');
  assert(save.calls.alerts.length === 1 && save.calls.alerts[0].includes(reason), 'a refused save must show the reason');
  assert(save.calls.mediaReloads === 1 && save.calls.fullReloads === 0, 'a refused save must reload the media settings only');
  const saved = run('saveMediaConfig', ok);
  assert(await saved.fn({ trashPath: '/trash2' }) === true, 'a save that went through must say so');
  assert(saved.calls.mediaReloads === 0 && saved.calls.alerts.length === 0, 'a save that went through leaves the fields alone');

  const stored = async url => ({ data: url === '/media' ? { folderPath: '/games', trashPath: '/trash' } : url === '/postdownload' ? { enableAutoMove: false } : {} });
  const media = run('loadMediaSettings', stored);
  await media.fn();
  assert(media.calls.set.setTrashPath === '/trash' && media.calls.set.setFolderPath === '/games', 'the media fields must show the stored settings');
  assert(!media.calls.gets.includes('/postdownload') && !('setPostDownloadSettings' in media.calls.set),
    'reloading the media settings must leave the post-download form alone');
  const both = { media: false, postDownload: null };
  const loadAll = load(tab, 'loadSettings', {
    apiClient: { get: stored }, console,
    loadMediaSettings: async () => { both.media = true; },
    setPostDownloadSettings: v => { both.postDownload = v; },
  });
  await loadAll();
  assert(both.media && both.postDownload && both.postDownload.enableAutoMove === false, 'opening the tab loads both forms');

  console.log('media-tab-scan-save: ok');
})().catch(e => { console.error(e.message); process.exit(1); });
