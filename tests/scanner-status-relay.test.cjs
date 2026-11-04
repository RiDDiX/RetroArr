const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const src = fs.readFileSync(path.join(__dirname, '..', 'frontend', 'src', 'components', 'ScannerStatus.tsx'), 'utf8');
const block = src.match(/const fetchStatus = async [\s\S]*?\n {4}\};/);
assert(block, 'fetchStatus not found');

(async () => {
  // fetchStatus is the closure of the first render: what it compares against must not be that render's status
  const fired = [];
  const answers = [];
  const sandbox = {
    status: { isScanning: false, gamesAddedCount: 0, filesScannedCount: 0 },
    prevIsScanning: { current: false },
    prevAdded: { current: 0 },
    mediaApi: { getScanStatus: async () => ({ data: answers.shift() }) },
    window: { dispatchEvent: e => { fired.push(e.type); } },
    Event: function (type) { this.type = type; },
    setStatus: () => {}, setShowFinished: () => {}, setTimeout: () => 0, console: { log: () => {}, error: () => {} },
  };
  const fetchStatus = vm.runInNewContext(`${ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText}\nfetchStatus`, sandbox);
  const scan = n => ({ isScanning: true, gamesAddedCount: n, filesScannedCount: n });

  for (const n of [5, 5, 5]) { answers.push(scan(n)); await fetchStatus(); }
  assert(fired.length === 1, `a scan whose count stays put must announce its batch once, fired ${fired.length}`);
  answers.push(scan(8));
  await fetchStatus(false);
  assert(fired.length === 1, 'a batch ProgressHubBridge announced already must not be announced again');
  answers.push(scan(8));
  await fetchStatus();
  assert(fired.length === 1, 'the batch the hub announced counts as seen');
  answers.push(scan(12));
  await fetchStatus();
  assert(fired.length === 2 && fired.every(t => t === 'LIBRARY_UPDATED_EVENT'), 'new games since the last check are announced');
  // A finished scan keeps its count until the next one starts, and that one counts from 0 again
  answers.push({ isScanning: false, gamesAddedCount: 500, filesScannedCount: 500 });
  await fetchStatus();
  answers.push(scan(120));
  await fetchStatus();
  assert(fired.length === 3, 'the next scan must announce its games even when it found fewer than the last one so far');

  console.log('scanner-status-relay: ok');
})().catch(e => { console.error(e.message); process.exit(1); });
