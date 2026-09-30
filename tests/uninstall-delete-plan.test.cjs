const fs = require('fs');
const path = require('path');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// The dialog shows what the server will move to the trash instead of guessing folders of its own.
const modal = read('frontend', 'src', 'components', 'UninstallModal.tsx');
assert(modal.includes('apiClient.get<DeletePlan>(`/game/${gameId}/delete-plan`)'),
  'the dialog must ask the server what goes to the trash');
assert(modal.includes('plan.paths.map(p => <div key={p}>{p}</div>)') && modal.includes('<span>{plan.refused}</span>'),
  'the dialog must list the planned paths, or say why nothing can go');
assert(modal.includes('{downloads.length > 0 && (') && modal.includes('downloads.map(p => <div key={p}>{p}</div>)'),
  'the download checkbox must only offer, and list, the downloads the server found');
assert(!/getSmartPaths|useContainerFolder|targetLibraryPath|downloadPath/.test(modal),
  'the dialog must not pick a container or download folder by itself');
assert(modal.includes('onDelete(deleteLibraryFiles && filesMovable, deleteDownloadFiles && downloads.length > 0, plan);'),
  'the dialog must hand the plan it showed to the delete call, and no folders of its own');
// Files the server refused, or couldn't name, can't be ticked; nothing can be confirmed before the plan is there.
assert(modal.includes('checked={deleteLibraryFiles && filesMovable}') && modal.includes('disabled={!filesMovable}'),
  'a refused plan must untick and lock "move files to the trash"');
assert(modal.includes('disabled={!plan}') && modal.includes('if (!plan) return;'),
  'the confirm button must wait for the plan');
assert(!/um-depth-selector|um-radio-group|um-radio-item|um-selector-label/.test(read('frontend', 'src', 'components', 'UninstallModal.css')),
  'the folder depth picker is gone, and so is its CSS');

const details = read('frontend', 'src', 'pages', 'GameDetails.tsx');
assert(!details.includes('targetPath=') && !details.includes('downloadPath='),
  'the delete call must not send folders the server did not plan');
assert(details.includes('gameId={game.id}'), 'the dialog needs the game id to ask for the plan');
assert(details.includes('{ data: { paths: plan.paths ?? [], downloads: plan.downloads } }'),
  'the delete call must send what the dialog showed, so the server can refuse a changed plan');

const controller = read('src', 'RetroArr.Api.V3', 'Games', 'GameController.cs');
assert(controller.includes('[HttpGet("{id}/delete-plan")]') && controller.includes('downloads = await DownloadsOfAsync(game, others) });'),
  'GET game/{id}/delete-plan must answer with the files and downloads the delete moves');
assert(controller.includes('downloads = await DownloadsOfAsync(game, others);'),
  'the delete must find the game\'s downloads itself');
assert(controller.includes('DeleteExpectation? expected = null') && controller.includes('!SameSet(paths, expected.Paths)'),
  'the delete must refuse a plan that changed since the dialog showed it');

// Files go to the trash, so no language may say they are deleted from disk or for good.
const translations = read('frontend', 'src', 'i18n', 'translations.ts');
const languages = (translations.match(/\n    [a-z]{2}: \{/g) || []).length;
// The dialog speaks every language it is offered in.
const dialogKeys = [...new Set([...modal.matchAll(/t\('([a-zA-Z]+)'\)/g)].map(m => m[1]))];
for (const key of dialogKeys) {
  assert((translations.match(new RegExp(`\\n        ${key}: '`, 'g')) || []).length === languages, `${key} must exist in every language block`);
}
assert(!translations.includes('downloadFolder:'), 'downloadFolder is not used anywhere');
for (const key of ['deleteFilesOption', 'deleteDownloadFilesOption', 'deleteDownloadFilesWarning']) {
  const texts = translations.match(new RegExp(`\\n        ${key}: '[^'\\n]*(?:\\\\'[^'\\n]*)*',`, 'g')) || [];
  assert(languages === 7 && texts.length === languages, `${key} must exist in every language block`);
  assert(!texts.some(o => /Force|Forzar|disque|Festplatte|disco|permanent|définitivement|dauerhaft/.test(o)),
    `${key} must not promise a delete from disk`);
}

console.log('uninstall-delete-plan: ok');
