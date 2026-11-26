// node scripts/screenshot.mjs <url> <out.png> [selector to wait for]
import { chromium } from '@playwright/test';

const [url, out, selector] = process.argv.slice(2);
const browser = await chromium.launch();
const page = await browser.newPage({ viewport: { width: 1440, height: 1000 } });
await page.goto(url, { waitUntil: 'networkidle' });
if (selector) await page.waitForSelector(selector);
await page.screenshot({ path: out, fullPage: true });
await browser.close();
