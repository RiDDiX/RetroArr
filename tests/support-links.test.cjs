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

// About must be reachable from the UI, not only by typing the URL:
// a permanent sidebar footer link AND the RetroArr wordmark in the header.
const sidebar = read('frontend', 'src', 'components', 'layout', 'Sidebar.tsx');
assert(sidebar.includes('sidebar__footer') && sidebar.includes('sidebar__about'),
  'sidebar needs an always-visible About footer link');
assert(/to="\/about"[\s\S]{0,200}sidebar__logo-btn|sidebar__logo-btn[\s\S]{0,200}RetroArr/.test(sidebar),
  'clicking the RetroArr wordmark must navigate to About');
assert((sidebar.match(/to="\/about"/g) || []).length >= 2,
  'About must be linked from both the brand and the sidebar footer');
// The version/changelog overlay keeps its own trigger (it is not a donation popup).
assert(sidebar.includes('toggleKofi') && sidebar.includes('sidebar__beta'),
  'the version/changelog overlay must keep a trigger of its own');

const app = read('frontend', 'src', 'App.tsx');
assert(app.includes('path="/about"'), '/about route must exist');

// The floating LanguageSwitcher sits fixed at bottom-left over the sidebar, so the
// About footer link has to keep clearance or it gets covered.
const sidebarCss = read('frontend', 'src', 'components', 'layout', 'Sidebar.css');
const footerRule = sidebarCss.match(/\.sidebar__footer\s*\{([^}]*)\}/);
assert(footerRule, '.sidebar__footer rule must exist');
const clearance = footerRule[1].match(/margin-bottom:\s*(\d+)px/);
assert(clearance && Number(clearance[1]) >= 56,
  'sidebar footer needs >=56px bottom clearance for the floating language switcher');

console.log('support-links: all contract checks passed');
