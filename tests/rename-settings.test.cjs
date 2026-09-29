const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// Pull one top-level `const name = ... };` block out of a file and run it with the given stand-ins.
const load = (src, name, sandbox) => {
  const block = src.match(new RegExp(`const ${name} = [\\s\\S]*?\\n\\};`));
  assert(block, `${name} not found`);
  const js = ts.transpileModule(block[0], { compilerOptions: { target: ts.ScriptTarget.ES2020 } }).outputText;
  return vm.runInNewContext(`${js}\n${name}`, sandbox);
};

const tab = read('frontend', 'src', 'components', 'settings', 'RenameSettingsTab.tsx');
const SAMPLE = load(tab, 'SAMPLE', {});
const renderPreview = load(tab, 'renderPreview', {});
const buildPreview = load(tab, 'buildPreview', { SAMPLE, renderPreview });
const normalizeConflict = load(tab, 'normalizeConflict', {});

// Empty tokens leave no empty brackets, like the backend renderer
assert(renderPreview('{Title} ({Region}) [{Languages}]', { Title: 'X', Region: '', Languages: '' }) === 'X', 'empty () or [] left in the preview');
assert(renderPreview('{Title} - DLC - {ContentName}', { Title: 'X', ContentName: '' }) === 'X - DLC', 'dangling separator left in the preview');

// The group suffix is shown once, on every template
const preview = buildPreview({ includeReleaseGroupInFilename: true, releaseGroupSuffix: '[{ReleaseGroup}]', mainFileTemplate: '{Title}' });
assert(preview.main === 'Chrono Trigger [FitGirl].zip', `main preview is ${preview.main}`);
assert(preview.update.endsWith(' [FitGirl].zip'), `update preview is ${preview.update}`);
assert(preview.dlc.endsWith(' [FitGirl].zip'), `dlc preview is ${preview.dlc}`);
assert(!/\.replace\('\.zip'/.test(tab), 'preview still glues a second name onto the first');

// Overwrite is gone; a stored one shows as Skip
assert(normalizeConflict('Overwrite') === 'Skip', 'Overwrite not mapped to Skip');
assert(normalizeConflict('Suffix') === 'Suffix', 'Suffix lost');
assert(normalizeConflict(undefined) === 'Skip', 'missing value not mapped to Skip');
assert(!tab.includes('value="Overwrite"'), 'Overwrite is still offered');
assert(tab.includes("{'{Disc}'}") && tab.includes('renameTokenDisc'), '{Disc} token not documented');

const translations = read('frontend', 'src', 'i18n', 'translations.ts');
const count = key => (translations.match(new RegExp(`\\b${key}:`, 'g')) || []).length;
assert(count('renameTokenDisc') === 7, 'renameTokenDisc is not in all 7 languages');
assert(count('missingRetentionDesc') === 7, 'missingRetentionDesc is not in all 7 languages');
const en = translations.slice(translations.indexOf('\n    en: {'), translations.indexOf('\n    fr: {'));
assert(/missingRetentionDesc: .*Monitored games are kept/.test(en), 'en retention text does not say monitored games are kept');
assert(!translations.includes('renameConflictOverwrite'), 'renameConflictOverwrite still translated');

console.log('rename-settings: ok');
