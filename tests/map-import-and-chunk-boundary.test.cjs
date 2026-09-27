const fs = require('fs');
const path = require('path');

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
  status.includes('gameId: mapGameId || null'),
  'Import must still send the selected gameId'
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

console.log('map-import-and-chunk-boundary: all contract checks passed');
