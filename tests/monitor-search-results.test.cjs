const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

const panel = read('frontend', 'src', 'components', 'MonitorPanel.tsx');
const client = read('frontend', 'src', 'api', 'client.ts');

// Hide and Reject rows only appear with "show all"; the counter counts what is shown.
const visible = panel.match(/const visibleResults = ([^\n]+);/);
assert(visible, 'visibleResults not found');
const rows = ['AutoDownload', 'Review', 'Hide', 'Reject'].map(decision => ({ decision }));
const shown = showAll => vm.runInNewContext(visible[1], { results: rows, showAll }).map(r => r.decision).join(',');
assert(shown(false) === 'AutoDownload,Review', 'Hide and Reject rows must be hidden by default');
assert(shown(true) === 'AutoDownload,Review,Hide,Reject', 'show all must list Hide and Reject rows');
assert(panel.includes("t('monitorResultsCount').replace('{count}', String(visibleResults.length))"),
  'the result counter must count the visible rows');

// A rejected row says why next to its badge.
assert(panel.includes("{r.decision === 'Reject' && r.reason && <span className=\"monitor-reject-reason\">{r.reason}</span>}"),
  'Reject rows must show their reason');

// Search diagnostics: queries, rejected count and provider errors.
assert(/export interface MonitorSearchResultDto \{[\s\S]*rejectedCount: number;[\s\S]*queries: string\[\];[\s\S]*providerErrors: string\[\];/.test(client),
  'MonitorSearchResultDto must carry rejectedCount, queries and providerErrors');
for (const needle of ["{searchInfo && searchInfo.queries.length > 0 && (", "t('searchQueriesSent')", "t('monitorRejectedCount')", 'searchInfo.providerErrors.map(']) {
  assert(panel.includes(needle), `monitor panel must render ${needle}`);
}

// Pull one `const name = ... };` block out of the panel and run it with the given stand-ins.
const load = (name, sandbox) => {
  const block = panel.match(new RegExp(`const ${name} = [\\s\\S]*?\\n  \\};`));
  assert(block, `${name} not found`);
  const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  return vm.runInNewContext(`${js}\n${name}`, sandbox);
};

// The search response feeds the diagnostics line.
const response = { gameId: 7, scored: [], queries: ['Halo 3'], providerErrors: ['Jackett search failed: refused'], rejectedCount: 2, error: null };
let searchInfo = 'untouched';
const searchNow = load('searchNow', {
  monitorApi: { searchNow: async () => ({ data: response }) },
  gameId: 7, autoDispatch: false,
  setSearching: () => {}, setError: () => {}, setNotice: () => {}, setAutoQueued: () => {}, setResults: () => {},
  setSearchInfo: v => { searchInfo = v; }, getErrorMessage: () => '', t: k => k,
});

// GameDetails keeps the sent queries and categories and shows them above the results.
const details = read('frontend', 'src', 'pages', 'GameDetails.tsx');
for (const needle of [
  'queries: response.data.queries || []',
  'categories: response.data.categories || []',
  '{!searching && !error && searchDiagnostics?.queries && searchDiagnostics.queries.length > 0 && (',
  "t('searchQueriesSent')",
  "t('searchCategoriesSent')",
]) {
  assert(details.includes(needle), `game details search diagnostics must contain ${needle}`);
}

// A manual grab sends only the game; the backend resolves the platform from it.
let posted;
const queueRelease = load('queueRelease', {
  apiClient: { post: async (_url, body) => { posted = body; return { data: {} }; } },
  gameId: 7, queueingUrl: null, setQueueingUrl: () => {}, setError: () => {}, setNotice: () => {},
  detectImportSubfolder: () => null, getErrorMessage: () => '', t: k => k,
});

(async () => {
  await searchNow();
  assert(searchInfo === response, 'searchNow must hand the response to the diagnostics state');

  await queueRelease({ magnetUrl: 'magnet:?xt=x', protocol: 'torrent', platformFolder: 'wii', title: 'Halo 3' });
  assert(posted && posted.gameId === 7, 'the grab must send the game id');
  assert(!('platformFolder' in posted), 'the grab must not send the release platform folder');
  console.log('monitor-search-results: all contract checks passed');
})().catch(e => { console.error(e.message); process.exit(1); });
