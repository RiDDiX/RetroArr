const fs = require('fs');
const path = require('path');

const source = fs.readFileSync(
  path.join(__dirname, '..', 'frontend', 'src', 'i18n', 'translations.ts'),
  'utf8'
);

if (!source.includes('useCallback')) {
  throw new Error('useTranslation must memoize the translate function');
}

const returnBlock = source.match(/return\s*\{([\s\S]*?)\};\s*$/);
if (!returnBlock) {
  throw new Error('useTranslation return block was not found');
}

if (!/t:\s*\w+/.test(returnBlock[1])) {
  throw new Error('useTranslation must return a stable translate reference');
}

console.log('translation hook stays stable');
