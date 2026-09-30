const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Pull one `const name = ... };` block out of a page and run it with the given stand-ins.
const load = (src, name, sandbox) => {
  const block = src.match(new RegExp(`const ${name} = [\\s\\S]*?\\n  \\};`));
  assert(block, `${name} not found`);
  const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  return vm.runInNewContext(`${js}\n${name}`, sandbox);
};

// GameDetails manual download: the preselected platform must be one the select can show.
const details = read('frontend', 'src', 'pages', 'GameDetails.tsx');
const availablePlatforms = [
  { id: 1, name: 'PC (Windows)', folder: 'windows' },
  { id: 20, name: 'Nintendo Switch', folder: 'switch' },
  { id: 31, name: 'Xbox 360', folder: 'xbox360' },
];
const preselect = (platformFolder, platformId) => {
  let selected = 'untouched';
  const open = load(details, 'handleDownloadWithPlatform', {
    availablePlatforms,
    game: platformId === undefined ? null : { platformId },
    setPendingDownload: () => {},
    setSelectedPlatform: v => { selected = v; },
    setShowPlatformModal: () => {},
  });
  open('magnet:?xt=x', 'torrent', 'Detected', platformFolder, 'Some.Release');
  return selected;
};
assert(preselect('switch', 31) === 'switch', 'a detected platform that is an option must be preselected');
for (const folder of ['unknown', 'mobile', 'psx', '', undefined]) {
  assert(preselect(folder, 31) === 'xbox360',
    `release folder ${JSON.stringify(folder)} is not an option, the game's own platform must be preselected`);
  assert(preselect(folder, 999) === '',
    `release folder ${JSON.stringify(folder)} with a game platform that is not an option must preselect nothing`);
  assert(preselect(folder, undefined) === '', 'no game loaded must preselect nothing');
}

const modal = details.slice(details.indexOf('{/* Platform Selection Modal */}'));
assert(modal.includes('<option value="" disabled>'),
  'the download platform select needs an empty placeholder, else it shows the first platform for an empty value');
assert(modal.includes('onClick={confirmDownload} disabled={!selectedPlatform}'),
  'Start Download must stay disabled until a platform is chosen');
assert(/const confirmDownload = async \(\) => \{\n\s+if \(!pendingDownload \|\| !selectedPlatform \|\|/.test(details),
  'confirmDownload must refuse to post without a platform');

// The grab sends the release title, the backend picks the subfolder (update, DLC) from it
const grabBody = async (releaseTitle) => {
  let posted;
  const confirm = load(details, 'confirmDownload', {
    pendingDownload: { url: 'magnet:?xt=x', protocol: 'torrent', releaseTitle }, selectedPlatform: 'switch', downloadingUrl: null,
    game: { id: 7 }, apiClient: { post: async (_url, body) => { posted = body; return { data: {} }; } },
    setShowPlatformModal: () => {}, setDownloadingUrl: () => {}, setNotification: () => {}, setPendingDownload: () => {},
    getErrorMessage: () => '', console, t: k => k,
  });
  await confirm();
  return posted;
};

// Choosing a platform other than the game's puts the download under another game entry; the modal must say so.
const hint = folder => load(details, 'otherPlatformHint', { availablePlatforms, game: { platformId: 31 }, t: k => `${k}:{platform}` })(folder);
assert(hint('xbox360') === null, 'no hint when the game platform is selected');
assert(hint('') === null, 'no hint when nothing is selected');
assert(hint('switch') === 'downloadOtherPlatformHint:Nintendo Switch', 'a different platform must name the target in the hint');
assert(load(details, 'otherPlatformHint', { availablePlatforms, game: { platformId: 999 }, t: k => `${k}:{platform}` })('switch') === 'downloadOtherPlatformHint:Nintendo Switch',
  'the hint must also show when the game platform is not an option, every choice then files under another entry');
assert(modal.includes('{otherPlatformHint(selectedPlatform) && ('), 'the platform hint must be rendered in the download modal');

// Library search results: lookup results always carry id 0, only igdbId identifies an IGDB game.
const library = read('frontend', 'src', 'pages', 'Library.tsx');
const addBody = async (result) => {
  let posted;
  const add = load(library, 'addGameWithPlatform', {
    apiClient: { post: async (_url, body) => { posted = JSON.parse(JSON.stringify(body)); } },
    loadPagedGames: async () => {}, loadPlatformCounts: async () => {},
    window: { dispatchEvent: () => {} }, Event: function Event() {},
    setShowSearchResults: () => {}, setSearchQuery: () => {}, setSearchResults: () => {},
    setShowPlatformPicker: () => {}, setPendingGameToAdd: () => {},
    console, alert: () => {}, t: k => k,
  });
  await add(result, 20);
  return posted;
};

const inLibraryCheck = library.match(/localResults\.find\(g =>[\s\S]*?\) \?\? null/);
assert(inLibraryCheck, 'Library must match search results against the library');
const findExisting = (localResults, result) => vm.runInNewContext(inLibraryCheck[0], { localResults, result, titleLower: result.title.toLowerCase() });

(async () => {
  const grab = await grabBody('Patch Quest v1.0.3');
  assert(grab && grab.gameId === 7 && grab.platformFolder === 'switch', 'the grab must send the game and the chosen platform');
  assert(grab.releaseTitle === 'Patch Quest v1.0.3', 'the grab must send the release title');
  assert(!('importSubfolder' in grab), 'the grab must leave the subfolder to the backend');

  const igdb = await addBody({ id: 0, igdbId: 434, title: 'Red Dead Redemption', images: {} });
  assert(igdb.igdbId === 434, 'an IGDB result must be added with its igdbId');
  for (const source of ['ScreenScraper', 'TheGamesDB', 'Epic']) {
    const body = await addBody({ id: 0, title: `${source} game`, images: {} });
    assert(!('igdbId' in body),
      `a ${source} result has no igdbId and must not be stored as 0 (unique IgdbId+PlatformId index)`);
    assert(body.platformId === 20, 'the chosen platform must still be sent');
  }

  const library434 = [{ id: 7, title: 'Red Dead Redemption: Game of the Year Edition', igdbId: 434 }];
  assert(findExisting(library434, { id: 0, igdbId: 434, title: 'Red Dead Redemption' })?.id === 7,
    'an IGDB result must be recognised as in the library by igdbId');
  assert(findExisting([{ id: 8, title: 'Other', igdbId: 5 }], { id: 0, title: 'Bonk' }) === null,
    'a result without igdbId must not match by id');
  assert(findExisting([{ id: 9, title: 'bonk', igdbId: 5 }], { id: 0, title: 'Bonk' })?.id === 9,
    'the title match must keep working');

  console.log('search-pickers: all contract checks passed');
})().catch(e => { console.error(e.message); process.exit(1); });
