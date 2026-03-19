#!/usr/bin/env node
/**
 * Automated screenshot capture for RetroArr documentation.
 * Usage: npx playwright install chromium && node screenshots/capture.js [baseUrl]
 * Default baseUrl: http://localhost:5000
 */
const { chromium } = require('playwright');
const path = require('path');
const fs = require('fs');

const BASE_URL = process.argv[2] || 'http://localhost:5000';
const OUTPUT_DIR = path.join(__dirname, 'output');

const PAGES = [
  { name: '01-dashboard',       path: '/dashboard',        wait: 2000 },
  { name: '02-library-all',     path: '/library',           wait: 3000 },
  { name: '03-settings',        path: '/settings',          wait: 1500 },
  { name: '04-status',          path: '/status',            wait: 1500 },
  { name: '05-problems',        path: '/problems',          wait: 1500 },
  { name: '06-metadata-review', path: '/metadata-review',   wait: 2000 },
];

(async () => {
  if (!fs.existsSync(OUTPUT_DIR)) fs.mkdirSync(OUTPUT_DIR, { recursive: true });

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({
    viewport: { width: 1920, height: 1080 },
    colorScheme: 'dark',
  });
  const page = await context.newPage();

  console.log(`Capturing screenshots from ${BASE_URL} ...`);

  for (const entry of PAGES) {
    const url = `${BASE_URL}${entry.path}`;
    try {
      await page.goto(url, { waitUntil: 'networkidle', timeout: 15000 });
      await page.waitForTimeout(entry.wait);
      const filePath = path.join(OUTPUT_DIR, `${entry.name}.png`);
      await page.screenshot({ path: filePath, fullPage: false });
      console.log(`  [OK] ${entry.name} -> ${filePath}`);
    } catch (err) {
      console.error(`  [FAIL] ${entry.name}: ${err.message}`);
    }
  }

  // Capture a specific platform view in the library (if platforms exist)
  try {
    await page.goto(`${BASE_URL}/library`, { waitUntil: 'networkidle', timeout: 15000 });
    await page.waitForTimeout(2000);
    const firstPlatformBtn = await page.$('.sidebar-platform-item:not(.active)');
    if (firstPlatformBtn) {
      await firstPlatformBtn.click();
      await page.waitForTimeout(1500);
      const filePath = path.join(OUTPUT_DIR, '07-library-platform-selected.png');
      await page.screenshot({ path: filePath, fullPage: false });
      console.log(`  [OK] 07-library-platform-selected -> ${filePath}`);
    }
  } catch (err) {
    console.error(`  [FAIL] platform-selected: ${err.message}`);
  }

  await browser.close();
  console.log(`\nDone. Screenshots saved to ${OUTPUT_DIR}/`);
})();
