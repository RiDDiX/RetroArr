const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Core service
const svc = read('src', 'RetroArr.Core', 'LanCache', 'SteamPrefillService.cs');
assert(svc.includes('class SteamPrefillService'), 'SteamPrefillService required');
assert(svc.includes('"prefill"') && svc.includes('--no-ansi'), 'must invoke SteamPrefill prefill --no-ansi');
assert(svc.includes('successfullyDownloadedDepots.json'), 'must read SteamPrefill prefill state');
assert(svc.includes('account.config'), 'must detect the Steam login session');
assert(svc.includes('IsAvailable') && svc.includes('IsLoggedIn'), 'defensive availability/login checks');

// DI registration
const program = read('src', 'RetroArr.Host', 'Program.cs');
assert(program.includes('AddSingleton<RetroArr.Core.LanCache.SteamPrefillService>'), 'SteamPrefillService must be registered');

// Controller endpoints
const ctrl = read('src', 'RetroArr.Api.V3', 'LanCache', 'LanCacheController.cs');
assert(ctrl.includes('[HttpGet("prefill/status")]'), 'prefill status endpoint');
assert(ctrl.includes('[HttpPost("prefill/run")]'), 'prefill run endpoint');
assert(ctrl.includes('GetPrefilledAppIds'), 'reconcile must mark prefilled games');

// Dockerfile bundles SteamPrefill (multiarch, non-fatal)
const dockerfile = read('Dockerfile');
assert(dockerfile.includes('AS steamprefill'), 'SteamPrefill download stage required');
assert(/TARGETARCH/.test(dockerfile) && dockerfile.includes('linux-arm64') && dockerfile.includes('linux-x64'), 'multiarch SteamPrefill download');
assert(dockerfile.includes('COPY --from=steamprefill /steamprefill /opt/steamprefill'), 'SteamPrefill copied into runtime image');
assert(dockerfile.includes('--retry-all-errors'), 'SteamPrefill download must be resilient');

// Entrypoint persists the session dir
const entry = read('scripts', 'docker-entrypoint.sh');
assert(entry.includes('/opt/steamprefill/Config') && entry.includes('/app/config/steamprefill'), 'entrypoint must symlink the SteamPrefill Config dir to the config volume');

// Frontend api
const client = read('frontend', 'src', 'api', 'client.ts');
assert(client.includes("apiClient.get<PrefillStatus>('/lancache/prefill/status')"), 'getPrefillStatus wrapper');
assert(client.includes("apiClient.post<{ started: boolean; message: string }>('/lancache/prefill/run')"), 'runPrefill wrapper');

// Frontend UI
const tab = read('frontend', 'src', 'components', 'settings', 'LanCacheTab.tsx');
assert(tab.includes('Run prefill now'), 'prefill run button');
assert(tab.includes('SteamPrefill/select-apps') || tab.includes('/opt/steamprefill/SteamPrefill select-apps'), 'one-time login guidance shown');

console.log('lancache-phase2: all contract checks passed');
