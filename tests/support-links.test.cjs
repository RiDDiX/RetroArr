const fs = require('fs');
const path = require('path');
const read = (...p) => fs.readFileSync(path.join(__dirname, '..', ...p), 'utf8');
const assert = (cond, msg) => { if (!cond) throw new Error(msg); };

const PAYPAL = 'https://www.paypal.me/RiDDiX93';
const KOFI = 'https://ko-fi.com/riddix';
const SPONSORS = 'https://github.com/sponsors/RiDDiX';

// GitHub's own Sponsor button on the repo page.
const funding = read('.github', 'FUNDING.yml');
assert(/^github:\s*RiDDiX\s*$/m.test(funding), 'FUNDING.yml must keep the GitHub Sponsors entry');
assert(/^ko_fi:\s*riddix\s*$/m.test(funding), 'FUNDING.yml must expose the Ko-fi account');
assert(funding.includes(PAYPAL), 'FUNDING.yml must expose the PayPal.me link');

// In-app support section (About page).
const about = read('frontend', 'src', 'pages', 'About.tsx');
for (const url of [SPONSORS, PAYPAL, KOFI]) {
  assert(about.includes(url), `About page must link ${url}`);
}
assert(about.includes('rel="noopener noreferrer"') && about.includes('target="_blank"'),
  'external support links must open safely in a new tab');
assert(about.includes('supportTitle'), 'About page must use the localized support heading');

// Localized strings exist (t() falls back to en for other languages).
const i18n = read('frontend', 'src', 'i18n', 'translations.ts');
for (const key of ['supportTitle', 'supportDesc', 'supportGithub', 'supportPaypal', 'supportKofi']) {
  assert(i18n.includes(`${key}:`), `translations must define ${key}`);
}

// README support section.
const readme = read('README.md');
for (const url of [SPONSORS, PAYPAL, KOFI]) {
  assert(readme.includes(url), `README must link ${url}`);
}

console.log('support-links: all contract checks passed');
