const fs = require('fs');
const path = require('path');
const vm = require('vm');
const ts = require('typescript');

const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

// The translation table as the app loads it
const js = ts.transpileModule(read('frontend', 'src', 'i18n', 'translations.ts'), {
  compilerOptions: { target: ts.ScriptTarget.ES2020, module: ts.ModuleKind.CommonJS },
}).outputText;
const mod = { exports: {} };
vm.runInNewContext(js, { module: mod, exports: mod.exports, require: () => ({}) });
const { translations } = mod.exports;
const languages = ['es', 'en', 'fr', 'de', 'ru', 'zh', 'ja'];
assert(JSON.stringify(Object.keys(translations)) === JSON.stringify(languages), `expected the language blocks ${languages}`);

// The Settings page with every tab it renders (taken from its imports), the library screens and Discover
const settings = read('frontend', 'src', 'pages', 'Settings.tsx');
const tabIndex = read('frontend', 'src', 'components', 'settings', 'index.ts');
const tabs = [...settings.matchAll(/import \{([^}]+)\} from '\.\.\/components\/settings';/g)]
  .flatMap(m => m[1].split(',').map(s => s.trim()).filter(Boolean))
  .map(name => {
    const file = tabIndex.match(new RegExp(`default as ${name} \\} from '\\./(\\w+)'`));
    assert(file, `${name} is not exported by components/settings`);
    return ['frontend', 'src', 'components', 'settings', `${file[1]}.tsx`];
  });
assert(tabs.length >= 13, `only ${tabs.length} settings tabs found`);
// Components the page and its tabs render (the folder browser of the Media tab, ...)
const parts = [
  ...[...settings.matchAll(/import (\w+) from '\.\.\/components\/(\w+)';/g)].map(m => m[2]),
  ...tabs.flatMap(tab => [...read(...tab).matchAll(/import (\w+) from '\.\.\/(\w+)';/g)].map(m => m[2])),
].filter((name, i, all) => all.indexOf(name) === i && fs.existsSync(path.join(__dirname, '..', 'frontend', 'src', 'components', `${name}.tsx`)))
  .map(name => ['frontend', 'src', 'components', `${name}.tsx`]);
assert(parts.some(p => p[3] === 'FolderExplorerModal.tsx'), 'the folder browser of the Media tab must be checked');
// The sidebar labels go through t() too, only product names stay as they are
const plainLabels = [...settings.matchAll(/label: '([^']+)'/g)].map(m => m[1]).filter(l => !['LanCache', 'EmulatorJS'].includes(l));
assert(plainLabels.length === 0, `untranslated Settings labels: ${plainLabels}`);
const screens = [
  ['frontend', 'src', 'pages', 'Settings.tsx'],
  ...tabs,
  ['frontend', 'src', 'components', 'GameCard.tsx'],
  ['frontend', 'src', 'pages', 'Library.tsx'],
  ['frontend', 'src', 'pages', 'Discover.tsx'],
  ['frontend', 'src', 'pages', 'Status.tsx'],
  ['frontend', 'src', 'components', 'MonitorPanel.tsx'],
];

// Every key these screens pass to t() has a text in every language. A key missing in a language shows English,
// one missing in English or left empty shows the raw key.
for (const [file, needsKeys] of [...screens.map(f => [f, true]), ...parts.map(f => [f, false])]) {
  const name = file[file.length - 1];
  const src = read(...file);
  const keys = new Set([...src.matchAll(/\bt\(\s*(['"])([^'"]+)\1/g)].map(m => m[2]));
  assert(keys.size > 0 || !needsKeys, `no t() keys found in ${name}`);
  for (const key of keys) {
    for (const lang of languages) {
      const text = translations[lang][key];
      assert(typeof text === 'string' && text.trim() !== '', `${name}: ${key} has no ${lang} text`);
    }
  }
  // Every placeholder the screen fills in (t('k').replace('{x}', ...), chained over lines too) must be in every language
  for (const m of src.matchAll(/\bt\('(\w+)'\)(?:\s*\|\|\s*'[^']*')?\)?((?:\s*\.replace\('\{\w+\}'[^\n]*)+)/g)) {
    for (const [, placeholder] of m[2].matchAll(/\.replace\('\{(\w+)\}'/g)) {
      for (const lang of languages) {
        assert(translations[lang][m[1]].includes(`{${placeholder}}`), `${name}: the ${lang} ${m[1]} has no {${placeholder}}`);
      }
    }
  }
}

// No longer used by any screen
assert(languages.every(lang => !('mediaSettingsSaved' in translations[lang])), 'mediaSettingsSaved is unused and must be gone');

console.log('translation-coverage: ok');
