const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const js = src => ts.transpileModule(src, { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;

// Pull one `const name = ... };` block (or the function inside `useCallback(fn, [deps])`) out of a component
const load = (src, name, sandbox) => {
  const hook = src.match(new RegExp(`const ${name} = useCallback\\((async \\([^)]*\\) => \\{[\\s\\S]*?\\n  \\}), \\[[^\\]]*\\]\\);`));
  const block = hook ? `const ${name} = ${hook[1]};` : (src.match(new RegExp(`const ${name} = [\\s\\S]*?\\n  \\};`)) || [])[0];
  assert(block, `${name} not found`);
  return vm.runInNewContext(`${js(block)}\n${name}`, sandbox);
};

// POST /media/scan and /discovery/scan answer 409 while a scan runs
const client = read('frontend', 'src', 'api', 'client.ts');
const conflictFn = client.match(/export function isConflict[\s\S]*?\n\}/);
assert(conflictFn, 'client.ts must tell a 409 apart');
const isConflict = vm.runInNewContext(`${js(conflictFn[0].replace('export ', ''))}\nisConflict`, { axios: { isAxiosError: e => !!(e && e.isAxiosError) } });
const answer = (status, message) => Object.assign(new Error(`Request failed with status code ${status}`), { isAxiosError: true, response: { status, data: { message } } });
const running = answer(409, 'A scan is already running.');
const refused = answer(400, 'The folder does not exist.');
const noIgdb = answer(400, 'IGDB credentials are required for scanning.');
noIgdb.response.data.errorCode = 'IGDB_NOT_CONFIGURED';
assert(isConflict(running) && !isConflict(refused) && !isConflict(new Error('offline')), 'isConflict must match a 409 only');

const getErrorMessage = e => (e && e.response && e.response.data && e.response.data.message) || e.message;
const stand = (calls, extra) => ({
  isConflict, getErrorMessage, isAxiosError: e => !!(e && e.isAxiosError), t: k => k, console: { error: () => {} },
  alert: m => { calls.alerts.push(m); },
  setInterval: fn => { calls.polls.push(fn); return calls.polls.length; },
  clearInterval: () => { calls.cleared++; },
  setTimeout: () => 0,
  ...extra,
});
const newCalls = () => ({ alerts: [], polls: [], cleared: 0, spinner: [], busy: true, reloads: 0, info: [], errors: [], posts: [], navigated: [] });

(async () => {
  // Library: a platform scan clicked during a running scan says so, and the spinner follows the running scan
  const library = read('frontend', 'src', 'pages', 'Library.tsx');
  const platformScan = async failure => {
    const calls = newCalls();
    const scan = load(library, 'handlePlatformScan', stand(calls, {
      platformScanning: false,
      setPlatformScanning: v => { calls.spinner.push(v); },
      mediaApi: {
        startScan: async () => { if (failure) throw failure; },
        getScanStatus: async () => ({ data: { isScanning: calls.busy } }),
      },
      loadPagedGames: () => { calls.reloads++; }, loadPlatformCounts: () => {}, loadPlatforms: () => {},
    }));
    await scan('snes');
    return calls;
  };
  const busy = await platformScan(running);
  // The server text is English; the UI names the running scan in its own language
  assert(busy.alerts.length === 1 && busy.alerts[0] === 'scanAlreadyRunning', `the running scan must be named with t('scanAlreadyRunning'), got ${JSON.stringify(busy.alerts)}`);
  assert(!busy.spinner.includes(false) && busy.polls.length === 1, 'the spinner must keep turning while the running scan goes on');
  await busy.polls[0]();
  assert(!busy.spinner.includes(false) && busy.reloads === 0, 'the spinner must stay while the scan still runs');
  busy.busy = false;
  await busy.polls[0]();
  assert(busy.spinner[busy.spinner.length - 1] === false && busy.reloads === 1, 'when the running scan ends the spinner stops and the library reloads');
  const failed = await platformScan(refused);
  assert(failed.alerts[0] === 'The folder does not exist.' && failed.spinner[failed.spinner.length - 1] === false && failed.polls.length === 0,
    'any other failure shows the server message and stops the spinner');
  const igdb = await platformScan(noIgdb);
  assert(igdb.alerts[0] === 'igdbRequired' && igdb.spinner[igdb.spinner.length - 1] === false,
    'missing IGDB credentials use the same translated message as the Media tab');
  const started = await platformScan(null);
  assert(started.alerts.length === 0 && started.polls.length === 1, 'a started scan is followed without a message');

  // Settings, Media tab: both Scan now and the discovery scan leave the spinner to the running scan
  const tab = read('frontend', 'src', 'components', 'settings', 'MediaTab.tsx');
  const mediaCall = async (name, failure, arg) => {
    const calls = newCalls();
    const fn = load(tab, name, stand(calls, {
      apiClient: { post: async (url, body) => { calls.posts.push({ url, body }); if (failure) throw failure; return { data: {} }; } },
      setScanning: v => { calls.spinner.push(v); },
      prefixSave: { current: Promise.resolve(true) },
      navigate: to => { calls.navigated.push(to); },
      folderPath: '/games',
    }));
    await fn(arg);
    return calls;
  };
  for (const external of [false, true]) {
    const scanNow = await mediaCall('handleScanNow', running, external);
    assert(scanNow.alerts.length === 1 && scanNow.alerts[0] === 'scanAlreadyRunning', `Scan now must name the running scan, got ${JSON.stringify(scanNow.alerts)}`);
    assert(!scanNow.spinner.includes(false), 'Scan now must leave the spinner to the running scan');
  }
  const discovery = await mediaCall('triggerDiscoveryScan', running);
  assert(discovery.alerts.length === 1 && discovery.alerts[0] === 'scanAlreadyRunning', 'the discovery scan must name the running scan');
  assert(discovery.spinner.includes(true) && discovery.navigated.length === 0, 'the discovery scan must show the running scan instead of leaving the page');
  const discoveryStarted = await mediaCall('triggerDiscoveryScan', null);
  assert(discoveryStarted.navigated[0] === '/discover' && discoveryStarted.alerts.length === 0, 'a started discovery scan still opens Discover');

  // Discover page: the message stays (loadList clears errors) and the list refreshes until the running scan ends
  const discover = read('frontend', 'src', 'pages', 'Discover.tsx');
  const discoverScan = async failure => {
    const calls = newCalls();
    const fn = load(discover, 'triggerScan', stand(calls, {
      apiClient: { post: async () => { if (failure) throw failure; return { data: {} }; } },
      mediaApi: { getScanStatus: async () => ({ data: { isScanning: calls.busy } }) },
      setScanning: v => { calls.spinner.push(v); },
      setInfo: v => { calls.info.push(v); },
      setError: v => { calls.errors.push(v); },
      loadList: async () => { calls.reloads++; },
    }));
    await fn();
    return calls;
  };
  const page = await discoverScan(running);
  assert(page.info[page.info.length - 1] === 'scanAlreadyRunning' && !page.errors.some(Boolean), 'Discover must name the running scan as info');
  assert(!page.spinner.includes(false) && page.polls.length === 1, 'Discover must keep scanning while the other scan runs');
  await page.polls[0]();
  assert(!page.spinner.includes(false), 'Discover must wait while the scan still runs');
  page.busy = false;
  await page.polls[0]();
  assert(page.spinner[page.spinner.length - 1] === false && page.reloads === 1 && page.cleared === 1, 'Discover must stop and reload once the scan is done');
  const pageFailed = await discoverScan(refused);
  assert(pageFailed.errors.includes('The folder does not exist.') && pageFailed.spinner[pageFailed.spinner.length - 1] === false,
    'any other failure stays an error and stops the spinner');

  console.log('scan-running: ok');
})().catch(e => { console.error(e.message); process.exit(1); });
