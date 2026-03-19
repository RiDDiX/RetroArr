import { chromium } from 'playwright';
import { mkdirSync } from 'fs';

const BASE = 'http://localhost:2727';
const OUT = './docs/screenshots';

mkdirSync(OUT, { recursive: true });

const pages = [
  { name: 'dashboard', path: '/', wait: 3000 },
  { name: 'library', path: '/library', wait: 3000 },
  { name: 'game-details', path: '/game/25', wait: 4000 },
  { name: 'statistics', path: '/statistics', wait: 2000 },
  { name: 'collections', path: '/collections', wait: 2000 },
  { name: 'settings-general', path: '/settings', wait: 2000 },
  { name: 'library-resort', path: '/library-resort', wait: 2000 },
  { name: 'status', path: '/status', wait: 2000 },
  { name: 'about', path: '/about', wait: 2000 },
];

(async () => {
  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
    deviceScaleFactor: 1,
    storageState: {
      cookies: [],
      origins: [{
        origin: BASE,
        localStorage: [
          { name: 'RetroArr_language', value: 'en' },
          { name: 'retroarr-theme', value: 'retroarr' },
        ],
      }],
    },
  });

  for (const pg of pages) {
    const page = await context.newPage();
    try {
      console.log(`Capturing ${pg.name} ...`);
      await page.goto(`${BASE}${pg.path}`, { waitUntil: 'networkidle', timeout: 15000 });
      await page.waitForTimeout(pg.wait);
      await page.screenshot({ path: `${OUT}/${pg.name}.png`, fullPage: false });
      console.log(`  -> ${OUT}/${pg.name}.png`);
    } catch (e) {
      console.error(`  !! ${pg.name} failed: ${e.message}`);
    } finally {
      await page.close();
    }
  }

  await browser.close();
  console.log('Done.');
})();
