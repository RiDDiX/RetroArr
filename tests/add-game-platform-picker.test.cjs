const fs = require('fs');
const path = require('path');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

const library = read('frontend', 'src', 'pages', 'Library.tsx');

const matcher = library.match(/const getMatchingPlatforms = [\s\S]*?\n  \};/);
assert(matcher, 'Library must define getMatchingPlatforms');
assert(
  matcher[0].includes('availablePlatformIds.includes(p.id)'),
  'getMatchingPlatforms must match on the platform ids resolved by the backend'
);
assert(
  !/\.includes\(\s*(name|p\.name)/.test(matcher[0]),
  'getMatchingPlatforms must not substring-match platform names (X360 never matches "Xbox 360", "PC" matches "PC Engine")'
);
assert(
  library.includes('getMatchingPlatforms(pendingGameToAdd.availablePlatformIds)'),
  'platform picker must pass availablePlatformIds'
);

const addGame = library.match(/const handleAddGame = [\s\S]*?\n  \};/);
assert(addGame, 'Library must define handleAddGame');
assert(
  !addGame[0].includes('addGameWithPlatform('),
  'handleAddGame must always open the picker instead of silently adding as PC'
);
assert(addGame[0].includes('setShowPlatformPicker(true)'), 'handleAddGame must open the platform picker');
assert(!/\breturn\b/.test(addGame[0]), 'handleAddGame must not skip the picker for any result');

const noMatch = library.match(/getMatchingPlatforms\(pendingGameToAdd\.availablePlatformIds\)\.length === 0 && \([\s\S]*?\n              \)\}/);
assert(noMatch, 'picker must still show a hint when nothing matches');
assert(
  !noMatch[0].includes('<select'),
  'manual platform select must be available even when some platforms matched'
);
assert(library.includes("t('selectManually')"), 'picker must render the manual platform select');
assert(
  (library.match(/availablePlatformIds\)\.length === 0 &&/g) || []).length === 1,
  'only the hint may depend on nothing matching, the manual select is always shown'
);

console.log('add-game-platform-picker: all contract checks passed');
