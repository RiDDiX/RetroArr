const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Pull one `const name = ... };` block out of a component and run it with the given stand-ins.
const load = (src, name, sandbox) => {
  const block = src.match(new RegExp(`const ${name} = [\\s\\S]*?\\n  \\};`));
  assert(block, `${name} not found`);
  const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  return vm.runInNewContext(`${js}\n${name}`, sandbox);
};

// GET /game/paged sends the status by the names of the backend enum, the full Game sends the number
const game = read('src', 'RetroArr.Core', 'Games', 'Game.cs').match(/public enum GameStatus\s*\{([^}]*)\}/);
assert(game, 'GameStatus enum not found');
const backend = game[1].replace(/\/\/.*$/gm, '').split(',').map(s => s.trim()).filter(Boolean);
const client = read('frontend', 'src', 'api', 'client.ts');
const union = client.match(/export type GameStatus = ([^;]+);/);
assert(union, 'client.ts must name the GameStatus values');
const names = [...union[1].matchAll(/'([A-Za-z]+)'/g)].map(m => m[1]);
assert(JSON.stringify(names) === JSON.stringify(backend), `GameStatus names ${names} differ from the backend enum ${backend}`);
assert(/export interface GameListDto \{[^}]*\n  status: GameStatus;/.test(client), 'GameListDto.status must be a GameStatus name');
assert(/export interface Game \{[^}]*\n  status: number;/.test(client), 'Game.status is the enum number');

// The card is available when a scan or an import found the game's content, a path alone is not enough
const card = read('frontend', 'src', 'components', 'GameCard.tsx');
const available = card.match(/const isAvailable = ([^;]+);/);
assert(available, 'GameCard isAvailable not found');
const isAvailable = g => vm.runInNewContext(available[1], { game: g });
assert(!isAvailable({ status: 'Missing', path: '/games/windows/Doom' }), 'a wanted game with an empty folder is not available');
assert(!isAvailable({ status: 'Released', path: '/games/windows/Doom' }), 'a game that was never downloaded is not available');
assert(!isAvailable({ status: 'Missing', path: '/games/windows/Doom', missingSince: '2026-09-01T00:00:00Z' }), 'a game whose files are gone is not available');
assert(isAvailable({ status: 'Downloaded', path: '/games/windows/Doom' }), 'a downloaded game is available');
assert(isAvailable({ status: 'InstallerDetected', path: '/games/windows/Doom' }), 'a game with its installer on disk is available');
assert(card.includes('{game.missingSince && ('), 'the Missing badge follows missingSince');
// Its tooltip puts the date the files went missing where the language wants it
const badgeTitle = card.match(/title=\{(\(t\('missingBadgeTitle'\)[^\n]*)\}\n/);
assert(badgeTitle, 'the Missing badge tooltip must fill {date} into missingBadgeTitle');
const since = '2026-09-01T00:00:00Z';
assert(vm.runInNewContext(badgeTitle[1], { t: () => '{date} since', game: { missingSince: since }, Date }) === `${new Date(since).toLocaleDateString()} since`,
  'the Missing badge tooltip must show the missing-since date in place of {date}');

// The Platforms shelves hand the card the same fields as the library, so gone files show the badge there too
const dtoToGame = vm.runInNewContext(`${ts.transpileModule(read('frontend', 'src', 'pages', 'Platforms.tsx')
  .match(/function dtoToGame\([\s\S]*?\n\}/)[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText}\ndtoToGame`, {});
assert(dtoToGame({ id: 1, status: 'Missing', missingSince: since }).missingSince === since, 'Platforms must pass missingSince to the card');

// The list badge and the card name a missing game the same way in every language
const translationsJs = ts.transpileModule(read('frontend', 'src', 'i18n', 'translations.ts'),
  { compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS } }).outputText;
const mod = { exports: {} };
vm.runInNewContext(translationsJs, { module: mod, exports: mod.exports, require: () => ({}) });
for (const [lang, tr] of Object.entries(mod.exports.translations)) {
  assert(tr.statusMissing === tr.missingBadge && tr.missingBadge === tr.notAvailable,
    `${lang}: the list says ${tr.statusMissing}, the card ${tr.missingBadge} and ${tr.notAvailable}`);
}

// The list view badge names the status it gets
const library = read('frontend', 'src', 'pages', 'Library.tsx');
const translateStatus = load(library, 'translateStatus', { t: k => k });
const getStatusColor = load(library, 'getStatusColor', {});
assert(translateStatus('Downloaded') === 'statusDownloaded' && translateStatus('Downloading') === 'statusDownloading'
  && translateStatus('Missing') === 'statusMissing', 'the list badge must name the status');
assert(translateStatus('InstallerDetected') === 'statusDownloaded' && getStatusColor('InstallerDetected') === getStatusColor('Downloaded'),
  'a game with its installer on disk shows as downloaded, like on the card');

console.log('library-status: ok');
