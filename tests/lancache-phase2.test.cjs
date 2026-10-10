const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Generic multi-provider prefill service (Steam / Battle.net / Epic)
const svc = read('src', 'RetroArr.Core', 'LanCache', 'LanCachePrefillService.cs');
assert(svc.includes('class LanCachePrefillService'), 'LanCachePrefillService required');
for (const id of ['steam', 'battlenet', 'epic']) {
  assert(svc.includes(`"${id}"`), `provider ${id} must be registered`);
}
assert(svc.includes('"prefill"') && svc.includes('--no-ansi'), 'prefill --no-ansi invocation');
assert(svc.includes('successfullyDownloadedDepots.json') && svc.includes('successfullyDownloadedApps.json'), 'reads per-tool prefill state files');
assert(svc.includes('account.config') && svc.includes('userAccount.json'), 'detects Steam + Epic login sessions');

// DI + controller
const program = read('src', 'RetroArr.Host', 'Program.cs');
assert(program.includes('AddSingleton<RetroArr.Core.LanCache.LanCachePrefillService>'), 'service registered');
const ctrl = read('src', 'RetroArr.Api.V3', 'LanCache', 'LanCacheController.cs');
assert(ctrl.includes('[HttpGet("prefill/status")]'), 'prefill status endpoint');
assert(ctrl.includes('[HttpPost("prefill/{provider}/run")]'), 'per-provider prefill run endpoint');
assert(ctrl.includes('GetPrefilledAppIds("steam")'), 'reconcile marks steam-prefilled games');

// Dockerfile bundles all three tools (multiarch, non-fatal)
const dockerfile = read('Dockerfile');
for (const s of ['AS steamprefill', 'AS battlenetprefill', 'AS epicprefill']) {
  assert(dockerfile.includes(s), `Dockerfile stage ${s} required`);
}
for (const tool of ['steamprefill', 'battlenetprefill', 'epicprefill']) {
  assert(dockerfile.includes(`COPY --from=${tool} /${tool} /opt/${tool}`), `copy ${tool} into runtime`);
}
assert(dockerfile.includes('linux-arm64') && dockerfile.includes('linux-x64') && /TARGETARCH/.test(dockerfile), 'multiarch downloads');
assert((dockerfile.match(/--retry-all-errors/g) || []).length >= 3, 'all prefill downloads resilient');

// Entrypoint symlinks every tool's Config dir
const entry = read('scripts', 'docker-entrypoint.sh');
for (const t of ['steamprefill', 'battlenetprefill', 'epicprefill']) {
  assert(entry.includes(t), `entrypoint must handle ${t}`);
}
assert(entry.includes('/opt/') && entry.includes('/Config'), 'entrypoint symlinks Config dirs');
// The tools write app.log and get updated inside /opt/<tool>, so those dirs follow PUID/PGID too
const chownDirs = (entry.match(/for dir in ([^;]+); do/) || [])[1] || '';
for (const t of ['steamprefill', 'battlenetprefill', 'epicprefill']) {
  assert(chownDirs.includes(`/opt/${t}`), `entrypoint must hand /opt/${t} to PUID/PGID`);
}
assert(entry.includes('touch "$dir/app.log"'), 'entrypoint pre-creates app.log for root docker exec runs');
// The root phase only chowns, then re-runs the script as PUID, so the cert and the prefill
// links are made by the app user (root-squashed NFS); hard-linked files are never chowned
assert(/exec setpriv [^\n]*"\$0" "\$@"/.test(entry), 'entrypoint re-runs itself as PUID/PGID');
assert(entry.indexOf('exec setpriv') < entry.indexOf('openssl req'), 'privilege drop happens before cert generation');
assert(entry.includes('-links 1'), 'root chown skips hard-linked files');
assert(entry.includes('-execdir chown -h'), 'root chown never re-resolves parent folders');
assert(entry.includes('readlink -f'), 'root chown skips folders reached through a symlink');

// The prefill tools init a temp/manifest cache under $HOME; the app user must have a
// writable home or their config initializer crashes on startup. It is set after the
// drop, not as an image ENV, so a root docker exec never runs the app user's dotfiles.
assert(dockerfile.includes('/home/retroarr') && !dockerfile.includes('ENV HOME='),
  'HOME must not be an image ENV (a root docker exec would read the app user\'s dotfiles)');
assert(entry.includes('export HOME=/home/retroarr'), 'entrypoint gives the app user a writable HOME');

// Frontend
const client = read('frontend', 'src', 'api', 'client.ts');
assert(client.includes("apiClient.get<PrefillProviderStatus[]>('/lancache/prefill/status')"), 'getPrefillStatus wrapper (array)');
assert(client.includes('`/lancache/prefill/${provider}/run`'), 'runPrefill(provider) wrapper');
const tab = read('frontend', 'src', 'components', 'settings', 'LanCacheTab.tsx');
assert(tab.includes('providers.map'), 'tab renders each provider');
assert(tab.includes('Battle.net') || tab.includes('battlenet'), 'battle.net surfaced');
assert(tab.includes('Epic') || tab.includes('epic'), 'epic surfaced');

console.log('lancache-phase2: all contract checks passed');
