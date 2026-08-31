// The prefill tools are pinned at image build time; the LanCache tab can pull a
// newer upstream release at runtime. Contract: endpoint, client wrapper, button.
const fs = require('fs');
const assert = (cond, msg) => { if (!cond) { console.error('FAIL: ' + msg); process.exit(1); } };

const svc = fs.readFileSync('src/RetroArr.Core/LanCache/LanCachePrefillService.cs', 'utf8');
const ctrl = fs.readFileSync('src/RetroArr.Api.V3/LanCache/LanCacheController.cs', 'utf8');
const client = fs.readFileSync('frontend/src/api/client.ts', 'utf8');
const tab = fs.readFileSync('frontend/src/components/settings/LanCacheTab.tsx', 'utf8');

assert(ctrl.includes('[HttpPost("prefill/{provider}/update")]'), 'per-provider update endpoint required');
assert(ctrl.includes('UserAgent.ParseAdd'), 'api.github.com needs a User-Agent header');

for (const repo of ['tpill90/steam-lancache-prefill', 'tpill90/battlenet-lancache-prefill', 'tpill90/epic-lancache-prefill']) {
  assert(svc.includes(repo), `release source for ${repo} must be configured`);
}
assert(svc.includes('st.Lock.WaitAsync(0'), 'update must take the same gate as a run so the binary is not swapped mid-prefill');
assert(svc.includes('File.Move(fresh, p.BinaryPath, overwrite: true)'),
  'the binary must be replaced by rename (a running process would otherwise fail ETXTBSY)');
assert(svc.includes('SetUnixFileMode'), 'the replaced binary must stay executable, the entrypoint skips it otherwise');
assert(!svc.includes('ExtractToDirectory(zipPath, toolDir'), 'never extract into the tool dir - it would clobber the Config symlink');

assert(client.includes("apiClient.post<PrefillUpdateResult>(`/lancache/prefill/${provider}/update`"), 'updatePrefill wrapper');
assert(/updatePrefill:[^\n]*timeout: \d{6,}/.test(client), 'update needs a timeout well above the 30s default');
assert(svc.includes('st.Running || st.Lock.CurrentCount == 0'),
  'an update must count as busy, otherwise a run is admitted mid-update and dies on the lock');
assert(tab.includes('startingId === p.id || updatingId === p.id'), 'run button must be disabled while updating');
assert(tab.includes('lancacheApi.updatePrefill('), 'tab must call the update endpoint');
assert(tab.includes('Update tool'), 'tab must expose an update button');

console.log('PASS lancache-prefill-update');
