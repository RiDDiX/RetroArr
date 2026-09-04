// A dropped Steam session takes the whole tail of a long run with it. Those apps
// are not marked as downloaded, so one plain pass right after the run picks them
// up. Contract: the setting exists on both sides and the retry is never forced.
const fs = require('fs');
const assert = (cond, msg) => { if (!cond) { console.error('FAIL: ' + msg); process.exit(1); } };

const cfg = fs.readFileSync('src/RetroArr.Core/Configuration/ConfigurationService.cs', 'utf8');
const svc = fs.readFileSync('src/RetroArr.Core/LanCache/LanCachePrefillService.cs', 'utf8');
const client = fs.readFileSync('frontend/src/api/client.ts', 'utf8');
const tab = fs.readFileSync('frontend/src/components/settings/LanCacheTab.tsx', 'utf8');

assert(cfg.includes('public bool PrefillRetryFailed'), 'setting must exist on the C# side');
assert(client.includes('prefillRetryFailed: boolean;'), 'setting must be mirrored in the TS interface');
assert(tab.includes('prefillRetryFailed: true,'), 'tab defaults must carry the setting');
assert(tab.includes('settings.prefillRetryFailed'), 'tab must expose a toggle for it');

assert(svc.includes('settings.PrefillRetryFailed && pass.Skipped > 0'), 'retry only after a run that skipped apps');
assert(svc.includes('trigger + "-retry"'), 'the retry pass must be recorded under its own trigger');
// "-retry" is not "manual", so BuildPrefillArgs cannot add --force to it.
assert(/if \(string\.Equals\(trigger, "manual", StringComparison\.OrdinalIgnoreCase\)\) args\.Add\("--force"\);/.test(svc),
  'only a plain manual run may be forced');
assert((svc.match(/RunPassAsync\(/g) || []).length >= 3, 'both passes must go through the same runner');

console.log('PASS lancache-prefill-retry');
