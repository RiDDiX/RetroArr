#!/usr/bin/env node
/**
 * Captures the documentation screenshots used under docs/screenshots/.
 *
 * Usage:
 *   npx playwright install chromium
 *   node screenshots/capture.js <baseUrl> --apiKey <key> [--lang en]
 *
 * Examples:
 *   node screenshots/capture.js http://localhost:2727 --apiKey abc123
 *   node screenshots/capture.js http://192.168.178.8:2727 --apiKey abc123 --lang en
 *
 * The script writes directly to docs/screenshots/ so the docs and the
 * website pick the new shots up on the next build. Pass an instance that
 * already has a populated library.
 */
const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

function parseArgs() {
  const args = process.argv.slice(2);
  const opts = { baseUrl: 'http://localhost:2727', apiKey: '', lang: 'en' };
  for (let i = 0; i < args.length; i++) {
    const a = args[i];
    if (a === '--apiKey') opts.apiKey = args[++i] ?? '';
    else if (a === '--lang') opts.lang = args[++i] ?? 'en';
    else if (!a.startsWith('--')) opts.baseUrl = a;
  }
  opts.baseUrl = opts.baseUrl.replace(/\/$/, '');
  return opts;
}

const OPTS = parseArgs();
const BASE_URL = OPTS.baseUrl;
const OUTPUT_DIR = path.join(__dirname, '..', 'docs', 'screenshots');

const VIEWPORT = { width: 1600, height: 1000 };

const PAGES = [
  { file: 'dashboard.png',       path: '/dashboard',       wait: 2500 },
  { file: 'library.png',         path: '/library',         wait: 3500 },
  { file: 'collections.png',     path: '/collections',     wait: 2000 },
  { file: 'statistics.png',      path: '/statistics',      wait: 2000 },
  { file: 'status.png',          path: '/status',          wait: 2000 },
  { file: 'library-resort.png',  path: '/library-resort',  wait: 2000 },
  { file: 'settings-general.png', path: '/settings',       wait: 2000 },
  { file: 'about.png',           path: '/about',           wait: 1500 },
];

function withKey(url) {
  if (!OPTS.apiKey) return url;
  const sep = url.includes('?') ? '&' : '?';
  return `${url}${sep}apiKey=${encodeURIComponent(OPTS.apiKey)}`;
}

async function seedStorage(page) {
  // Drop the API key + language into localStorage so the SPA boots
  // authenticated on the next navigation.
  await page.addInitScript((vals) => {
    try {
      if (vals.apiKey) localStorage.setItem('RetroArr_api_key', vals.apiKey);
      if (vals.lang) localStorage.setItem('RetroArr_language', vals.lang);
    } catch (_) {}
  }, { apiKey: OPTS.apiKey, lang: OPTS.lang });
}

async function captureGameDetails(page) {
  await page.goto(`${BASE_URL}/library`, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(2500);

  const id = await page.evaluate(async (args) => {
    try {
      const url = `${args.base}/api/v3/game/paged?page=1&pageSize=50&sortOrder=asc&apiKey=${encodeURIComponent(args.key || '')}`;
      const r = await fetch(url, { credentials: 'include' });
      if (!r.ok) return null;
      const data = await r.json();
      const items = data?.items ?? [];
      const withCover = items.find((g) => g?.images?.coverUrl);
      return (withCover ?? items[0])?.id ?? null;
    } catch { return null; }
  }, { base: BASE_URL, key: OPTS.apiKey });

  if (!id) {
    console.warn('  [SKIP] game-details.png — no games found.');
    return;
  }

  await page.goto(`${BASE_URL}/game/${id}`, { waitUntil: 'networkidle', timeout: 30000 });
  await page.waitForTimeout(3000);
  const filePath = path.join(OUTPUT_DIR, 'game-details.png');
  await page.screenshot({ path: filePath, fullPage: false });
  console.log(`  [OK] game-details.png (game #${id}) -> ${filePath}`);
}

(async () => {
  if (!fs.existsSync(OUTPUT_DIR)) fs.mkdirSync(OUTPUT_DIR, { recursive: true });

  console.log(`Capturing screenshots from ${BASE_URL}`);
  console.log(`Writing to ${OUTPUT_DIR}`);

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: VIEWPORT,
    colorScheme: 'dark',
    deviceScaleFactor: 1,
    ignoreHTTPSErrors: true,
  });
  const page = await context.newPage();
  await seedStorage(page);

  if (!OPTS.apiKey) {
    console.warn('[warn] No --apiKey passed. Remote instances will reject API calls and the screenshots will be empty.');
  }

  for (const entry of PAGES) {
    const url = withKey(`${BASE_URL}${entry.path}`);
    try {
      await page.goto(url, { waitUntil: 'networkidle', timeout: 30000 });
      await page.waitForTimeout(entry.wait);
      const filePath = path.join(OUTPUT_DIR, entry.file);
      await page.screenshot({ path: filePath, fullPage: false });
      console.log(`  [OK] ${entry.file} -> ${filePath}`);
    } catch (err) {
      console.error(`  [FAIL] ${entry.file}: ${err.message}`);
    }
  }

  try { await captureGameDetails(page); }
  catch (err) { console.error(`  [FAIL] game-details.png: ${err.message}`); }

  await browser.close();
  console.log('Done.');
})();
