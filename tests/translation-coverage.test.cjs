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

const walk = dir => fs.readdirSync(dir, { withFileTypes: true }).flatMap(e => e.isDirectory() ? walk(path.join(dir, e.name)) : [path.join(dir, e.name)]);
const spanishPhrases = ['Cargando...', 'Buscando...', 'Ir a esta ruta', 'Carpeta vacía', 'Seleccionar Actual', 'Ruta Actual', 'Doble clic', 'Clic derecho', 'Protocolo:', 'Indexador:', 'Descargar:'];
for (const file of walk(path.join(__dirname, '..', 'frontend', 'src')).filter(f => /\.(tsx?|css)$/.test(f) && !f.endsWith(path.join('i18n', 'translations.ts')))) {
  const src = fs.readFileSync(file, 'utf8');
  const name = path.relative(path.join(__dirname, '..'), file);
  // A fallback that is the Spanish text of its key, has Spanish accents or reads like Spanish
  for (const [, , key, , text] of src.matchAll(/\bt\(\s*(['"])([^'"]+)\1\s*(?:,[^)]*)?\)\s*(?:\|\||\?\?)\s*(['"`])([^'"`]*)\3/g)) {
    const spanish = (text === translations.es[key] && text !== translations.en[key]) || /[áéíóúñÁÉÍÓÚÑ¿¡]/.test(text)
      || /\b(de|del|la|las|el|los|una?|para|con|por|que|juegos?|carpeta|ruta|buscar|guardar|cancelar|ejecutable|archivo|descargar|eliminar|seleccionar|plataforma|guardando|cargando|cargar|error al|elige|sin|cambios|no hay|todos?|nuevo)\b/i.test(text);
    assert(!spanish, `${name}: Spanish fallback "${text}" for ${key}, use an English text or the key alone`);
  }
  const phrase = spanishPhrases.find(p => src.includes(p));
  assert(!phrase, `${name}: hardcoded Spanish "${phrase}", use t()`);
}

// Every language has every key with a text, and every key a component passes to t() exists
for (const lang of languages) {
  const missing = Object.keys(translations.en).filter(key => typeof translations[lang][key] !== 'string' || translations[lang][key].trim() === '');
  const extra = Object.keys(translations[lang]).filter(key => !(key in translations.en));
  assert(missing.length === 0 && extra.length === 0, `${lang}: missing ${missing.slice(0, 10)} extra ${extra.slice(0, 10)}`);
}
for (const file of walk(path.join(__dirname, '..', 'frontend', 'src')).filter(f => /\.tsx?$/.test(f) && !f.endsWith(path.join('i18n', 'translations.ts')))) {
  for (const [, , key] of fs.readFileSync(file, 'utf8').matchAll(/\bt\(\s*(['"])([A-Za-z0-9_]+)\1/g)) {
    assert(key in translations.en, `${path.basename(file)}: t('${key}') has no translation`);
  }
}

// No longer used by any screen
assert(languages.every(lang => !('mediaSettingsSaved' in translations[lang])), 'mediaSettingsSaved is unused and must be gone');

console.log('translation-coverage: ok');
