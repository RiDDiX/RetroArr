const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// ---- Map & Import: game picker available for ALL file types (incl. Main) ----
const status = read('frontend', 'src', 'pages', 'Status.tsx');

assert(
  !/mapPlatform && \(mapFileType === 'Patches' \|\| mapFileType === 'DLC'\) &&/.test(status),
  'The game picker must no longer be gated to Patches/DLC only'
);
assert(
  status.includes("size={Math.min(8, Math.max(2, matches.length + 1))}"),
  'The picker should render as a visible list (select with size), not a collapsed dropdown'
);
assert(
  status.includes('Search games by name...'),
  'The picker must offer a name search input'
);
assert(
  status.includes("Map to existing game (search by name)"),
  'Main-game mapping must expose the map-to-existing-game label'
);
assert(
  status.includes('gameId: mapGameId || 0,'),
  'Import must still send the selected gameId, and 0 for "new game from download title"'
);

// ---- Mapping goes to the one download: ids alone aren't unique across clients, names aren't unique at all ----
assert(
  status.includes('clientId: mapModal.downloadClientId,') && status.includes('downloadId: mapModal.downloadId,'),
  'Map & Import must send the download\'s clientId and downloadId to map-platform'
);
assert(
  status.includes('body: JSON.stringify({ downloadName: d.name, clientId: d.clientId, downloadId: d.id, platformFolder })'),
  'The queue platform editor must send the download\'s clientId and downloadId to map-platform'
);
assert(
  status.includes('<tr key={`${u.downloadClientId}-${u.downloadId}`}>') && !status.includes('key={u.downloadId}'),
  'Unmapped rows must be keyed by client and download id'
);
assert(
  (status.match(/editingPlatform === `\$\{d\.clientId\}-\$\{d\.id\}`|setEditingPlatform\(`\$\{d\.clientId\}-\$\{d\.id\}`\)/g) || []).length === 2,
  'The queue platform editor must open for one client\'s download only'
);
const mapController = read('src', 'RetroArr.Api.V3', 'DownloadClients', 'DownloadClientController.cs');
assert(
  /public int\? ClientId \{ get; set; \}\s*public string\? DownloadId \{ get; set; \}\s*\}/.test(mapController),
  'MapPlatformRequest must take optional ClientId and DownloadId'
);
assert(
  mapController.includes('_trackedDownloadService.Find(request.ClientId!.Value, request.DownloadId!)') &&
  mapController.includes('tracked.PlatformFolder = request.PlatformFolder;'),
  'map-platform must set the mapping on the tracked download its ids name'
);
assert(
  /nameShared = byId && _trackedDownloadService\.GetTrackedDownloads\(\)\s*\.Any\(t => t != tracked && string\.Equals\(t\.Title, request\.DownloadName, StringComparison\.OrdinalIgnoreCase\)\);\s*if \(!nameShared\)\s*_platformTracker\.SetPlatformForDownload\(/.test(mapController),
  'map-platform with ids must not store a name mapping another tracked download would pick up'
);

// ---- Map & Import: the file type comes from the import's own rules, "Main" takes back a Patches/DLC subfolder ----
assert(!status.includes('detectFileType'), 'The map dialog must not guess the file type with keyword rules of its own');
assert(
  status.includes('fetch(`${API_BASE}/queue/import-subfolder?${params}`)') && status.includes('}, [mapModal, mapGameId, mapPlatform, mapFileTouched]);'),
  'The map dialog must ask the backend for the file type, again when the game or platform changes'
);
assert(/onChange=\{e => \{ setMapFileType\([^}]*\); setMapFileTouched\(true\); \}\}/.test(status),
  'Picking a file type by hand must stop the suggestion from overwriting it');
// Runs the suggestion effect once: what it fetches and what it sets
const suggest = async ({ touched, answer }) => {
  const body = status.match(/useEffect\(\(\) => \{\n    if \(!mapModal \|\| mapFileTouched\) return;[\s\S]*?\n  \}, \[mapModal, mapGameId, mapPlatform, mapFileTouched\]\);/);
  assert(body, 'the file type suggestion effect not found');
  const effect = body[0].replace(/^useEffect\(/, '(').replace(/, \[mapModal, mapGameId, mapPlatform, mapFileTouched\]\);$/, ')');
  const js = ts.transpileModule(`const effect = ${effect};`, { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  const seen = { fetched: 0, set: [] };
  const run = vm.runInNewContext(`${js}\neffect`, {
    mapModal: { title: 'Patch Quest v1.0.3' }, mapGameId: 7, mapPlatform: 'windows', mapFileTouched: touched, API_BASE: '/api/v3/downloadclient',
    URLSearchParams, String,
    fetch: async () => { seen.fetched++; if (answer instanceof Error) throw answer; return { json: async () => answer }; },
    setMapFileType: v => seen.set.push(v),
  });
  run();
  await new Promise(resolve => setTimeout(resolve, 0));
  return seen;
};
assert(
  mapController.includes('[HttpGet("queue/import-subfolder")]') &&
  mapController.includes('PostDownloadProcessor.ImportSubfolderFor(title, game?.Title, picked) ?? "Main"'),
  'queue/import-subfolder must answer with the import\'s classifier'
);
const mapAndImportBody = async (mapFileType, mapGameId = 7, mapFileTouched = true) => {
  const block = status.match(/const handleMapAndImport = [\s\S]*?\n  \};/);
  assert(block, 'handleMapAndImport not found');
  const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  const calls = [];
  const run = vm.runInNewContext(`${js}\nhandleMapAndImport`, {
    mapModal: { title: 'Patch Quest v1.0.3', downloadClientId: 1, downloadId: 'abc' }, mapPlatform: 'windows', mapGameId, mapFileType, mapFileTouched,
    API_BASE: '/api/v3/downloadclient', encodeURIComponent,
    fetch: async (url, init) => { calls.push({ url, body: init && init.body ? JSON.parse(init.body) : null }); return {}; },
    setMapModal: () => {}, setMapPlatform: () => {}, setMapGameId: () => {}, setMapFileType: () => {}, setMapGameSearch: () => {},
    fetchUnmapped: () => {}, fetchCounts: () => {},
  });
  await run();
  return calls.find(c => c.url.endsWith('/queue/map-platform')).body;
};

// ---- Hard-refresh fix: ChunkErrorBoundary wraps the lazy routes ----
const app = read('frontend', 'src', 'App.tsx');
assert(
  app.includes("import ChunkErrorBoundary from './components/ChunkErrorBoundary'"),
  'App must import ChunkErrorBoundary'
);
assert(
  /<ChunkErrorBoundary>[\s\S]*<Suspense[\s\S]*<\/Suspense>[\s\S]*<\/ChunkErrorBoundary>/.test(app),
  'ChunkErrorBoundary must wrap the Suspense/Routes block'
);

const boundary = read('frontend', 'src', 'components', 'ChunkErrorBoundary.tsx');
assert(
  boundary.includes('getDerivedStateFromError'),
  'ChunkErrorBoundary must implement getDerivedStateFromError'
);
assert(
  boundary.includes('window.location.reload()'),
  'ChunkErrorBoundary must reload to fetch a fresh index.html on chunk failure'
);
assert(
  /ChunkLoadError|dynamically imported module|loading chunk/i.test(boundary),
  'ChunkErrorBoundary must detect chunk-load errors'
);
assert(
  boundary.includes('RELOAD_GUARD_KEY'),
  'ChunkErrorBoundary must guard against reload loops'
);

// ---- Backend: SPA shell served no-cache so index.html is never stale ----
const program = read('src', 'RetroArr.Host', 'Program.cs');
assert(
  program.includes('"Cache-Control"] = "no-cache, no-store, must-revalidate"'),
  'The index.html fallback must be served with no-cache to avoid stale chunk references'
);

// ---- GameDetails fast load: GetById must not block on a live IGDB fetch ----
const gameController = read('src', 'RetroArr.Api.V3', 'Games', 'GameController.cs');
assert(
  !gameController.includes('GetGameMetadataAsync(game.IgdbId.Value, lang)'),
  'GetById must not re-fetch IGDB metadata synchronously (it blocked the detail page for seconds)'
);

// ---- EmulatorJS CDN uses the 'latest' channel, not deprecated 'stable' ----
const emu = read('src', 'RetroArr.Api.V3', 'Emulator', 'EmulatorController.cs');
assert(!emu.includes('emulatorjs.org/stable'), 'EmulatorJS CDN must not use the deprecated stable channel');
assert(emu.includes('emulatorjs.org/latest/data'), 'EmulatorJS CDN base must point at latest/data');
const df = read('Dockerfile');
assert(!df.includes('emulatorjs.org/stable'), 'Dockerfile must not pull EmulatorJS from stable');

// ---- Cross-platform filename sanitizer used at every name-building site ----
const sanitizer = read('src', 'RetroArr.Core', 'IO', 'FileNameSanitizer.cs');
assert(sanitizer.includes('class FileNameSanitizer') && sanitizer.includes('?*') && sanitizer.includes('\\x00-\\x1F'),
  'FileNameSanitizer must strip the Windows-reserved set plus control chars');
for (const f of [
  ['src', 'RetroArr.Core', 'Configuration', 'MediaSettings.cs'],
  ['src', 'RetroArr.Core', 'Download', 'PostDownloadProcessor.cs'],
  ['src', 'RetroArr.Core', 'Rename', 'TemplateRenderer.cs'],
]) {
  assert(read(...f).includes('FileNameSanitizer.Sanitize'),
    `${f[f.length - 1]} must route names through FileNameSanitizer`);
}

// ---- Sanitizer uses a readable dash separator; rename can fix mangled folders ----
assert(sanitizer.includes('" - "'), 'FileNameSanitizer must replace illegal chars with a " - " separator');
const resort = read('src', 'RetroArr.Api.V3', 'Games', 'ResortController.cs');
assert(!/RenameGameFolder &&\s*\n\s*i\.ProposedAction != OperationType\.MoveGameFolder/.test(resort),
  'per-game rename must no longer exclude RenameGameFolder (so it can fix mangled folder names)');
assert(resort.includes('OperationType.MoveGameFolder'), 'per-game rename still excludes platform relocations');

(async () => {
  for (const [type, sent] of [['Main', 'Main'], ['Patches', 'Patches'], ['DLC', 'DLC'], ['', null]]) {
    assert((await mapAndImportBody(type)).importSubfolder === sent,
      `a file type picked as ${JSON.stringify(type)} must be sent as ${sent}: a picked Main takes back a Patches/DLC subfolder`);
  }
  for (const type of ['Main', 'Patches', 'DLC', '']) {
    assert((await mapAndImportBody(type, 7, false)).importSubfolder === null,
      `a suggested ${JSON.stringify(type)} only shows what the import does: sent, it would override the grab's subfolder`);
  }

  const untouched = await suggest({ touched: false, answer: { importSubfolder: 'Patches' } });
  assert(untouched.fetched === 1 && untouched.set.join() === 'Patches', 'an untouched file type must take the suggestion');
  const touched = await suggest({ touched: true, answer: { importSubfolder: 'Main' } });
  assert(touched.fetched === 0 && touched.set.length === 0, 'a file type picked by hand must not be overwritten by the suggestion');
  const failed = await suggest({ touched: false, answer: new Error('offline') });
  assert(failed.set.length === 0, 'a failed suggestion must leave the type to the import, not set one');

  // Mapping or creating a game from a file shows why the server refused it and keeps the dialog open
  const fileAction = async (name, outcome) => {
    const blocks = ['showFileError', name].map(n => {
      const block = status.match(new RegExp(`const ${n} = [\\s\\S]*?\\n  \\};`));
      assert(block, `${n} not found`);
      return ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
    });
    const seen = { error: null, closed: false };
    const run = vm.runInNewContext(`${blocks.join('\n')}\n${name}`, {
      mapFileModal: { fullPath: '/library/gba/x.gba', platformFolder: 'gba' }, mapFileGameId: 7,
      createGameModal: { fullPath: '/library/gba/x.gba', platformFolder: 'gba' }, createGameTitle: 'X',
      apiClient: { post: async () => { if (outcome) throw outcome; return { data: {} }; } },
      getErrorMessage: e => e.response.data.message, t: k => k,
      setFileError: v => { seen.error = v; }, setMapFileModal: () => { seen.closed = true; }, setMapFileGameId: () => {},
      setCreateGameModal: () => { seen.closed = true; }, setCreateGameTitle: () => {}, fetchUnmappedFiles: () => {},
    });
    await run();
    return seen;
  };
  for (const name of ['handleMapFileToGame', 'handleCreateGameFromFile']) {
    const refused = await fileAction(name, { response: { status: 409, data: { message: 'Another game has this file' } } });
    assert(refused.error === 'Another game has this file' && !refused.closed, `${name} must show the server's reason and keep the dialog open`);
    const done = await fileAction(name, null);
    assert(done.error === null && done.closed, `${name} must close the dialog when it worked`);
  }
  assert((await mapAndImportBody('Main', 7)).gameId === 7, 'the game picked in the dialog must be sent');
  assert((await mapAndImportBody('Main', '')).gameId === 0,
    '"New game from download title" must be sent as game 0, nothing sent keeps a wrong game the download was linked to');

  console.log('map-import-and-chunk-boundary: all contract checks passed');
})().catch(e => { console.error(e.message); process.exit(1); });
