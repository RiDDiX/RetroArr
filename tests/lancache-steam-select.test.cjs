const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Bug fix: prefill must not download everything by default.
const cfg = read('src', 'RetroArr.Core', 'Configuration', 'ConfigurationService.cs');
assert(/public bool PrefillAllOwned \{ get; set; \}\s*($|\n)/.test(cfg) || /PrefillAllOwned \{ get; set; \}(?!\s*=\s*true)/.test(cfg),
  'PrefillAllOwned must default to false (no = true)');
assert(!/PrefillAllOwned \{ get; set; \} = true/.test(cfg), 'PrefillAllOwned must not default to true');

// Service: selection read/write (Steam-only, correct file).
const svc = read('src', 'RetroArr.Core', 'LanCache', 'LanCachePrefillService.cs');
assert(svc.includes('selectedAppsToPrefill.json'), 'service must use the select-apps file');
assert(svc.includes('GetSelectedAppIds') && svc.includes('SetSelectedAppIds'), 'service must read/write the selection');
assert(/SetSelectedAppIds[\s\S]{0,200}"steam"/.test(svc), 'selection write must be steam-guarded');

// Controller endpoints.
const ctrl = read('src', 'RetroArr.Api.V3', 'LanCache', 'LanCacheController.cs');
assert(ctrl.includes('[HttpGet("prefill/steam/apps")]'), 'GET steam apps endpoint');
assert(ctrl.includes('[HttpPost("prefill/steam/apps")]'), 'POST steam apps endpoint');

// Frontend api + UI.
const client = read('frontend', 'src', 'api', 'client.ts');
assert(client.includes("apiClient.get<SteamAppsResponse>('/lancache/prefill/steam/apps'"), 'getSteamApps wrapper');
assert(client.includes("apiClient.post<{ saved: boolean; selectedCount: number }>('/lancache/prefill/steam/apps'"), 'setSteamApps wrapper');
const tab = read('frontend', 'src', 'components', 'settings', 'LanCacheTab.tsx');
assert(tab.includes('prefillAllOwned: false'), 'UI default must be selected-only');
assert(tab.includes('Choose Steam games to prefill'), 'UI must expose the game picker');
assert(tab.includes('Save selection'), 'UI must let the user save the selection');

// Family: show selected-but-not-owned titles WITHOUT touching SteamPrefill's
// session (reusing its token invalidates the login). Names via public appdetails.
assert(!ctrl.includes('GenerateAccessTokenForApp') && !ctrl.includes('IFamilyGroupsService'),
  'controller must NOT reuse SteamPrefill\'s session token (it invalidates the login)');
assert(!svc.includes('GetSteamRefreshToken') && !svc.includes('ProtoBuf.Serializer'),
  'service must not read/exchange the SteamPrefill session token');
const csproj = read('src', 'RetroArr.Core', 'RetroArr.Core.csproj');
assert(!csproj.includes('protobuf-net'), 'protobuf-net no longer needed');
assert(ctrl.includes('store.steampowered.com/api/appdetails'), 'names resolved via the public appdetails endpoint');
assert(ctrl.includes('!ownedIds.Contains'), 'selected-but-not-owned titles surface as family/shared');
assert(tab.includes('Family'), 'UI must tag family/shared titles');

console.log('lancache-steam-select: all contract checks passed');
