const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', 'frontend', 'src', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const js = src => ts.transpileModule(src, { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
// An effect's callback, ready to call
const callable = src => `(${js(src).trim().replace(/;$/, '')})()`;
const pick = (src, re, name) => { const m = src.match(re); assert(m, `${name} not found`); return m[1] || m[0]; };
const run = (blocks, names, sandbox) => vm.runInNewContext(`${blocks.map(js).join('\n')}\n({ ${names} })`, sandbox);
const answer = message => Object.assign(new Error('Request failed with status code 400'), { response: { data: { message } } });
const getErrorMessage = e => (e && e.response && e.response.data && e.response.data.message) || e.message;

(async () => {
  // ---- Library: cards the bulk reply has no row for must not each fetch their own review ----
  const library = read('pages', 'Library.tsx');
  assert(library.includes('reviewData={bulkReviews[dto.id] ?? null}'), 'Library cards must get null, not undefined, for games without a review row');

  // ---- Library: the platform-scan poll is one interval held in a ref and cleared on unmount ----
  {
    const block = pick(library, /const waitForScan = \(\) => \{[\s\S]*?\n {4}\};/, 'waitForScan');
    const live = new Map();
    let next = 1;
    const calls = [];
    const answers = [];
    const sandbox = {
      scanPollRef: { current: null },
      setInterval: fn => { live.set(next, fn); return next++; },
      clearInterval: id => { live.delete(id); },
      mediaApi: { getScanStatus: () => new Promise((resolve, reject) => answers.push({ resolve, reject })) },
      setPlatformScanning: v => calls.push(`scanning=${v}`),
      loadPagedGamesRef: { current: () => calls.push('paged') },
      loadPlatformCountsRef: { current: () => calls.push('counts') },
      loadPlatforms: () => calls.push('platforms'),
      loadPagedGames: () => calls.push('stale paged'), loadPlatformCounts: () => calls.push('stale counts'),
    };
    const { waitForScan } = run([block], 'waitForScan', sandbox);
    const done = { data: { isScanning: false } };
    waitForScan();
    const olderTick = [...live.values()][0]();
    const olderFailing = [...live.values()][0]();
    waitForScan();
    assert(live.size === 1, `a second scan start must replace the poll, not add one (${live.size} running)`);
    const tick = [...live.values()][0]();
    answers[0].resolve(done);
    answers[1].reject(new Error('offline'));
    await olderTick;
    await olderFailing;
    assert(calls.length === 0, `a tick of a replaced poll must neither stop the newer one nor reload, got ${calls.join()}`);
    answers[2].resolve(done);
    await tick;
    assert(live.size === 0 && sandbox.scanPollRef.current === null, 'a finished scan must stop the poll');
    assert(calls.join() === 'scanning=false,paged,counts,platforms', `the finished scan must reload with the current filters, got ${calls.join()}`);

    const unmount = 'if (scanPollRef.current) clearInterval(scanPollRef.current);\n      scanPollRef.current = null;\n    };';
    assert(library.includes(unmount), 'unmount must clear the platform-scan poll and its ref');
    calls.length = 0;
    waitForScan();
    const waiting = [...live.values()][0]();
    sandbox.clearInterval(sandbox.scanPollRef.current);
    sandbox.scanPollRef.current = null;
    answers[3].resolve(done);
    await waiting;
    assert(calls.length === 0, `a tick still waiting at unmount must not reload the page, got ${calls.join()}`);
  }

  // ---- ProgressHubBridge: scanFinished is followed by libraryUpdated, so only that one reloads the pages ----
  {
    const effect = pick(read('components', 'ProgressHubBridge.tsx'), /useEffect\((\(\) => \{[\s\S]*?\n {2}\}), \[\]\);/, 'bridge effect');
    const handlers = {};
    let fired = 0;
    vm.runInNewContext(callable(effect), {
      progressHub: { on: (event, fn) => { handlers[event] = fn; return () => {}; } },
      window: { dispatchEvent: () => { fired++; } }, Event: function (type) { this.type = type; },
    });
    assert(!handlers.scanFinished, 'scanFinished must not reload the pages');
    handlers.libraryUpdated();
    assert(fired === 1, `the end of a scan must reload the pages once, fired ${fired}`);
  }

  // ---- Collections: reloads are debounced, and a card does not open a route the app does not have ----
  {
    const collections = read('pages', 'Collections.tsx');
    const effect = pick(collections, /useEffect\((\(\) => \{\n {4}loadCollections\(\);[\s\S]*?\n {2}\}), \[\]\);/, 'Collections effect');
    let loads = 0;
    let listener = null;
    let pending = null;
    vm.runInNewContext(callable(effect), {
      loadCollections: () => { loads++; },
      window: { addEventListener: (_, fn) => { listener = fn; }, removeEventListener: () => {} },
      setTimeout: fn => { pending = fn; return 1; }, clearTimeout: () => { pending = null; },
    });
    for (let i = 0; i < 20; i++) listener();
    assert(loads === 1, `a burst of library events must not reload per event (${loads - 1} reloads)`);
    pending();
    assert(loads === 2, 'the burst must end in one reload');
    assert(!collections.includes('navigate(`/collections/'), 'a card must not navigate to /collections/{id}: App has no such route');
  }

  // ---- Trash: a reload on a library event must not free the buttons while a restore runs ----
  {
    const trash = read('pages', 'Trash.tsx');
    const load = pick(trash, /const load = useCallback\((async \(\) => \{[\s\S]*?\n {2}\}), \[\]\);/, 'Trash load');
    const restore = pick(trash, /const restore = async [\s\S]*?\n {2}\};/, 'Trash restore');
    assert(trash.includes('const busy = loading || acting;'), 'the buttons must stay disabled while either runs');
    const state = {};
    let finish;
    const t = run([`const load = ${load};`, restore], 'load, restore', {
      apiClient: { get: async () => ({ data: [] }), post: () => new Promise(r => { finish = r; }) },
      setLoading: v => { state.loading = v; }, setActing: v => { state.acting = v; },
      setEntries: () => {}, setError: () => {}, setNotice: () => {}, getErrorMessage, t: () => '', NO_TIMEOUT: {},
    });
    const restoring = t.restore('a');
    await t.load();
    assert(state.acting === true, 'a reload that ends during a restore must leave the restore busy');
    finish({});
    await restoring;
    assert(state.acting === false && state.loading === false, 'the restore frees the buttons when it is done');
  }

  // ---- Status: a refused import is shown, and Map & Import keeps its dialog open with the reason ----
  {
    const status = read('pages', 'Status.tsx');
    const refused = answer('Import blocked: no files found');
    const shown = [];
    let closed = false;
    const refreshed = [];
    const sandbox = {
      mapModal: { title: 'Game', downloadClientId: 1, downloadId: 'abc' }, mapPlatform: 'snes', mapGameId: '', mapFileType: '', mapFileTouched: false,
      apiClient: { post: async () => ({}) }, importDownload: async () => { throw refused; },
      API_BASE: '/api/v3/downloadclient', encodeURIComponent, fetch: async () => ({ ok: false, status: 400, json: async () => ({ message: 'x' }) }),
      getErrorMessage, setImportError: v => { if (v) shown.push(v); }, setFileError: () => {}, showFileError: e => shown.push(getErrorMessage(e)),
      setMapModal: () => { closed = true; }, setMapPlatform: () => {}, setMapGameId: () => {}, setMapFileType: () => {}, setMapGameSearch: () => {},
      fetchUnmapped: () => { refreshed.push('unmapped'); }, fetchCounts: () => { refreshed.push('counts'); },
    };
    const handleImport = pick(status, /const handleImport = async [\s\S]*?\n {2}\};/, 'handleImport');
    const handleMapAndImport = pick(status, /const handleMapAndImport = async [\s\S]*?\n {2}\};/, 'handleMapAndImport');
    const s = run([handleImport, handleMapAndImport], 'handleImport, handleMapAndImport', sandbox);
    await s.handleImport(1, 'abc');
    assert(shown[0] === 'Import blocked: no files found', 'a refused queue import must show the server\'s reason');
    await s.handleMapAndImport();
    assert(!closed && shown[1] === 'Import blocked: no files found', 'a refused Map & Import must keep the dialog open with the reason');
    assert(refreshed.join() === 'unmapped,counts', `the mapping is saved before the import is refused, so the lists must refresh, got ${refreshed.join()}`);
    assert(status.includes("{ timeout: 0 }") && status.includes('{fileError && <div className="modal-value small" style={{ color: \'var(--ctp-red)\' }}>{t(\'error\')}: {fileError}</div>}\n            <div className="modal-actions">'),
      'the import has no timeout and the map dialog shows its error');
  }

  // ---- Explicit long actions get the long timeout ----
  assert(read('pages', 'GameDetails.tsx').includes('/merge-into/${conflict.otherGameId}`, undefined, { timeout: 600000 })'), 'merge-into needs the long timeout');
  assert(read('api', 'client.ts').includes("{ igdbId, score, source: source || 'IGDB', screenScraperData }, { timeout: 600000 })"), 'metadata confirm needs the long timeout');
  assert(read('pages', 'ReviewImport.tsx').includes('/finalize`, undefined, { timeout: 600000 })'), 'review finalize needs the long timeout');

  console.log('page-reloads-and-errors: ok');
})().catch(e => { console.error(e.message); process.exit(1); });
