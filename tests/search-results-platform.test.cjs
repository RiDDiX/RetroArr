const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const assert = (cond, msg) => { if (!cond) throw new Error(msg); };
const details = fs.readFileSync(path.join(__dirname, '..', 'frontend', 'src', 'pages', 'GameDetails.tsx'), 'utf8');

// The manual search table shows what the backend detector found, no second guess from category ids or keywords.
assert(!details.includes('GetPlatformInfo'), 'the category id table must stay gone, the backend detector decides the platform');
assert(!/analysis\.(detectedPlatform|confidence)/.test(details), 'the row must not use the title keyword guess');

const block = details.match(/const getResultPlatform = [\s\S]*?\n {2}\};/);
assert(block, 'getResultPlatform not found');
assert(block[0].includes('result.platformFolder') && block[0].includes('result.detectedPlatform'),
  'the platform column must read platformFolder/detectedPlatform from the search result');
const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
const availablePlatforms = [
  { id: 1, name: 'PC (Windows)', folder: 'windows' },
  { id: 32, name: 'Nintendo DS', folder: 'nds' },
  { id: 9, name: 'Wii', folder: 'wii' },
  { id: 126, name: 'GOG Galaxy', folder: 'gog' },
];
const platformOf = (result, platformId) => vm.runInNewContext(`${js}\ngetResultPlatform`, {
  availablePlatforms,
  game: platformId === undefined ? null : { platformId },
  t: k => `t:${k}`,
})(result);

const nds = platformOf({ title: 'Pokemon Platinum Version [NDS] (EUR)', category: '1000', detectedPlatform: 'Nintendo DS', platformFolder: 'nds' }, 32);
assert(nds.name === 'Nintendo DS' && nds.confidence === 'match' && nds.badge === 'platform-nintendo',
  `a release detected as the game's platform must show it and match, got ${JSON.stringify(nds)}`);

const wii = platformOf({ title: 'Wii Sports', category: '1030', detectedPlatform: 'Wii', platformFolder: 'wii' }, 32);
assert(wii.name === 'Wii' && wii.confidence === 'mismatch', 'a release for another platform must be a mismatch');

for (const folder of ['unknown', undefined, '']) {
  const unknown = platformOf({ title: 'Something', detectedPlatform: 'Unknown', platformFolder: folder }, 32);
  assert(unknown.name === 't:unknown' && unknown.confidence === 'unknown' && unknown.badge === 'platform-unknown',
    `platform folder ${JSON.stringify(folder)} must show as unknown`);
}

assert(platformOf({ detectedPlatform: 'Nintendo DS', platformFolder: 'nds' }, undefined).confidence === 'unknown',
  'without a loaded game there is nothing to compare against');
assert(platformOf({ detectedPlatform: 'Nintendo DS', platformFolder: 'nds' }, 999).confidence === 'unknown',
  'a game platform that is not in the platform list gives no verdict');

// The row color must agree with the dialog: a folder the dialog cannot offer falls back to the game's folder there.
for (const folder of ['linux', 'mobile']) {
  const offOnly = platformOf({ title: 'Some.Game.Linux-GRP', detectedPlatform: folder, platformFolder: folder }, 1);
  assert(offOnly.name === folder && offOnly.confidence === 'unknown',
    `a detected ${folder} release that the dialog cannot offer must stay neutral, got ${JSON.stringify(offOnly)}`);
}

const badges = { windows: 'platform-pc', gog: 'platform-pc', macintosh: 'platform-mac', psx: 'platform-playstation',
  ps4: 'platform-playstation', vita: 'platform-playstation', xbox360: 'platform-xbox', switch: 'platform-nintendo',
  gba: 'platform-nintendo', megadrive: 'platform-console' };
for (const [folder, badge] of Object.entries(badges)) {
  assert(platformOf({ detectedPlatform: folder, platformFolder: folder }, 1).badge === badge, `${folder} must get ${badge}`);
}

const table = details.slice(details.indexOf('{sortedResults.map((result, index) => {'));
assert(table.includes('const platform = getResultPlatform(result);'), 'the results table must use getResultPlatform');
assert(table.includes('className={`results-row ${platform.confidence}`}'), 'the row color must come from the backend platform');
assert(table.includes('platform-tag ${platform.badge}') && table.includes('{platform.name}'),
  'the platform column must show the backend platform');

// Owned GOG downloads carry the game's own platform (folder gog), a hardcoded windows would flag every row.
const gogRows = details.slice(details.indexOf('gogDownloads = response.data.downloads.map'), details.indexOf("console.log('[Release Search] GOG downloads found:'"));
assert(details.includes('const gamePlatform = availablePlatforms.find(p => p.id === game.platformId);'),
  'owned GOG rows must look up the game platform like the download dialog does');
assert(gogRows.includes('platformFolder: gamePlatform?.folder') && gogRows.includes('detectedPlatform: gamePlatform?.name'),
  'owned GOG rows must carry the game platform folder and name');
assert(!/platformFolder: 'windows'/.test(gogRows), 'owned GOG rows must not be pinned to windows');
const gog = platformOf({ detectedPlatform: 'GOG Galaxy', platformFolder: 'gog' }, 126);
assert(gog.confidence === 'match', 'an owned GOG row on a GOG game must match');

console.log('search-results-platform: all contract checks passed');
