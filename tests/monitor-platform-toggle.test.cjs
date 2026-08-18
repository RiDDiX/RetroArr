const fs = require('fs');
const path = require('path');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// ---- Frontend API contract (client.ts) ----
const client = read('frontend', 'src', 'api', 'client.ts');

assert(
  client.includes('`/platforms/${platformId}/monitored`'),
  'client.ts setPlatformMonitored must PUT /platforms/{id}/monitored'
);
assert(
  client.includes("apiClient.get<Record<string, PlatformMonitorCount>>('/platforms/monitored-counts')"),
  'client.ts getPlatformMonitoredCounts must GET /platforms/monitored-counts'
);
assert(
  /export interface PlatformMonitorCount\s*\{[\s\S]*monitorDefault: boolean \| null;/.test(client),
  'client.ts must export PlatformMonitorCount with monitorDefault'
);
assert(
  /export interface GameListDto\s*\{[\s\S]*monitored: boolean;[\s\S]*\}/.test(client),
  'GameListDto type must carry monitored'
);

// ---- Per-game marker on the card (GameCard.tsx) ----
const card = read('frontend', 'src', 'components', 'GameCard.tsx');

assert(card.includes('faBookmark'), 'GameCard must import a bookmark icon');
assert(card.includes('game-card-monitor-btn'), 'GameCard must render the monitor marker button');
assert(
  card.includes('await monitorApi.setMonitored(game.id, next)'),
  'GameCard marker must toggle via monitorApi.setMonitored'
);
assert(
  card.includes('e.stopPropagation()'),
  'GameCard marker click must not bubble to card navigation'
);
assert(card.includes('onMonitoredChange'), 'GameCard must expose onMonitoredChange');

// ---- Platform toggle: Library filter bar ----
const library = read('frontend', 'src', 'pages', 'Library.tsx');

assert(
  library.includes('handlePlatformMonitorToggle(selectedPlatformData.id)'),
  'Library platform action bar must call handlePlatformMonitorToggle'
);
assert(
  library.includes('monitorApi.setPlatformMonitored('),
  'Library must invoke setPlatformMonitored'
);
assert(
  library.includes('onMonitoredChange={handleCardMonitoredChange}'),
  'Library grid card must wire onMonitoredChange'
);

// ---- Platform toggle: Platforms shelf header ----
const platforms = read('frontend', 'src', 'pages', 'Platforms.tsx');

assert(
  platforms.includes('shelf__monitor'),
  'Platforms shelf header must render the platform monitor toggle'
);
assert(
  platforms.includes('handlePlatformMonitorToggle(shelf.platform.id)'),
  'Platforms shelf toggle must call handlePlatformMonitorToggle'
);
assert(
  platforms.includes('monitored: dto.monitored'),
  'Platforms dtoToGame must pass monitored through so the card marker shows state'
);

// ---- Backend route + field contract (static string pins; runs without dotnet) ----
const controller = read('src', 'RetroArr.Api.V3', 'Monitor', 'MonitorController.cs');

assert(
  controller.includes('[HttpPut("platforms/{platformId:int}/monitored")]'),
  'MonitorController must expose PUT platforms/{platformId}/monitored'
);
assert(
  controller.includes('[HttpGet("platforms/monitored-counts")]'),
  'MonitorController must expose GET platforms/monitored-counts'
);
assert(
  controller.includes('PlatformService.SetMonitorNewItemsDefault(platformId, request.Monitored)'),
  'Platform toggle must persist the per-platform default'
);

const platformService = read('src', 'RetroArr.Core', 'Games', 'PlatformService.cs');
assert(
  platformService.includes('GetMonitorNewItemsDefault(int platformId, bool fallback)'),
  'PlatformService must provide GetMonitorNewItemsDefault with a fallback'
);
assert(
  platformService.includes('SetMonitorNewItemsDefault(int platformId, bool monitored)'),
  'PlatformService must provide SetMonitorNewItemsDefault'
);
assert(
  platformService.includes('platform_monitoring.json'),
  'PlatformService must persist the monitoring default to platform_monitoring.json'
);

const scanner = read('src', 'RetroArr.Core', 'Games', 'MediaScannerService.cs');
assert(
  scanner.includes('finalGame.Monitored = PlatformService.GetMonitorNewItemsDefault(finalGame.PlatformId, false)'),
  'New scanned games must inherit the platform monitoring default'
);

const dto = read('src', 'RetroArr.Core', 'Games', 'GameListDto.cs');
assert(dto.includes('public bool Monitored { get; set; }'), 'GameListDto must carry Monitored');

const repo = read('src', 'RetroArr.Core', 'Games', 'SqliteGameRepository.cs');
assert(repo.includes('Monitored = g.Monitored'), 'Paged projection must include Monitored');

console.log('monitor-platform-toggle: all contract checks passed');
