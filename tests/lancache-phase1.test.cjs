const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Backend: LanCache controller endpoints
const ctrl = read('src', 'RetroArr.Api.V3', 'LanCache', 'LanCacheController.cs');
assert(ctrl.includes('[Route("api/v3/lancache")]'), 'LanCacheController must be routed at api/v3/lancache');
assert(ctrl.includes('[HttpGet("settings")]') && ctrl.includes('[HttpPost("settings")]'), 'settings get/post required');
assert(ctrl.includes('[HttpGet("status")]'), 'status endpoint required');
assert(ctrl.includes('[HttpGet("reconcile")]'), 'reconcile endpoint required');
assert(ctrl.includes('/lancache-heartbeat'), 'status must probe the LanCache heartbeat');
assert(ctrl.includes('GetOwnedGamesAsync'), 'reconcile must list the Steam owned library');

// Backend: config persistence
const cfg = read('src', 'RetroArr.Core', 'Configuration', 'ConfigurationService.cs');
assert(cfg.includes('class LanCacheSettings'), 'LanCacheSettings model required');
assert(cfg.includes('LoadLanCacheSettings') && cfg.includes('SaveLanCacheSettings'), 'LanCache load/save required');
assert(cfg.includes('"lancache.json"'), 'LanCache settings persist to lancache.json');

// Frontend: api wrapper + types
const client = read('frontend', 'src', 'api', 'client.ts');
assert(client.includes("apiClient.get<LanCacheStatus>('/lancache/status')"), 'lancacheApi.getStatus route');
assert(client.includes("apiClient.get<LanCacheReconcile>('/lancache/reconcile'"), 'lancacheApi.reconcile route');
assert(/export interface LanCacheSettings\s*\{/.test(client), 'LanCacheSettings type exported');

// Frontend: settings tab wired
const tabIndex = read('frontend', 'src', 'components', 'settings', 'index.ts');
assert(tabIndex.includes("export { default as LanCacheTab }"), 'LanCacheTab exported');
const settings = read('frontend', 'src', 'pages', 'Settings.tsx');
assert(settings.includes("id: 'lancache'"), 'lancache tab registered');
assert(settings.includes("currentTab === 'lancache'") && settings.includes('<LanCacheTab'), 'lancache tab rendered');

console.log('lancache-phase1: all contract checks passed');
