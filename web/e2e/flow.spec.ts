import { expect, test, type Page } from '@playwright/test';
import { mkdirSync } from 'node:fs';
import { join } from 'node:path';

// Set PROOF_DIR to also write the proof screenshots (scripts/capture-proof.sh does this).
const proofDir = process.env.PROOF_DIR;
async function shot(page: Page, name: string) {
  if (!proofDir) return;
  mkdirSync(proofDir, { recursive: true });
  await page.screenshot({ path: join(proofDir, name), fullPage: true });
}

function readingsCsv(meters: string[]) {
  const lines = ['meter_id,timestamp,kwh'];
  const start = Date.UTC(2026, 10, 1);
  for (const m of meters) {
    for (let i = 0; i < 30 * 48; i++) {
      const t = new Date(start + i * 30 * 60_000);
      const hour = t.getUTCHours() + t.getUTCMinutes() / 60;
      const kwh = (0.3 + Math.exp(-((hour - 18) ** 2) / 8) * 0.6 + ((i * 7919) % 13) / 100).toFixed(3);
      lines.push(`${m},${t.toISOString()},${kwh}`);
    }
  }
  return lines.join('\n') + '\n';
}

async function signIn(page: Page, tenant: string) {
  await page.goto('/');
  await page.getByLabel('Tenant').selectOption(tenant);
  await page.getByRole('button', { name: 'Sign in' }).click();
  await expect(page.getByTestId('tenant-tag')).toHaveText(tenant);
}

test('upload a meter file and see consumption, estimate and demand response', async ({ page }) => {
  const run = Date.now().toString(36);
  const siteName = `Melbourne Depot ${run}`;
  const meters = [`MPAN-${run}-1`, `MPAN-${run}-2`];

  // F1: sites + meters
  await signIn(page, 'aurora-power');
  await page.getByRole('button', { name: 'Create site' }).click();
  await page.getByLabel('Name').fill(siteName);
  await page.getByLabel('Region').fill('VIC');
  await page.getByRole('button', { name: 'Create', exact: true }).click();
  await expect(page.getByRole('link', { name: siteName })).toBeVisible();
  await shot(page, '01-sites-carbon-table.png');

  await page.getByRole('link', { name: siteName }).click();
  for (const m of meters) {
    await page.getByLabel('Meter ID (MPAN)').fill(m);
    await page.getByRole('button', { name: 'Add meter' }).click();
    await expect(page.getByText(m)).toBeVisible();
  }

  // F2-F4: upload -> Lambda parse -> SQS -> inbox -> dashboard
  await page.locator('input[type=file]').setInputFiles({ name: 'readings-2026-11.csv', mimeType: 'text/csv', buffer: Buffer.from(readingsCsv(meters)) });
  await expect(page.getByText(/Uploaded|processing/i).first()).toBeVisible();
  await shot(page, '02-upload.png');
  await expect(page.getByTestId('consumption-chart')).toBeVisible({ timeout: 120_000 });
  await expect(page.getByTestId('upload-status')).toHaveText('Readings ingested', { timeout: 120_000 });
  // 2 meters x 30 days x 48 half-hours: every batch must have been ingested
  await expect.poll(async () => parseFloat((await page.getByTestId('total-kwh').textContent()) ?? '0')).toBeGreaterThan(1000);
  await shot(page, '03-consumption-chart.png');

  // F5: feature flag on -> estimate visible
  await page.getByRole('link', { name: 'Admin' }).click();
  const flag = page.locator('#flag-tariff-estimates');
  if ((await flag.getAttribute('aria-checked')) !== 'true') await page.locator('label[for="flag-tariff-estimates"]').click();
  await expect(flag).toHaveAttribute('aria-checked', 'true');
  await page.goBack();
  await expect(page.getByTestId('estimate-panel')).toBeVisible();
  await expect(page.getByTestId('estimate-cost')).toBeVisible({ timeout: 30_000 });
  await shot(page, '04-estimate-flag-on.png');

  // F6: demand response through the outbox
  await page.getByRole('button', { name: 'Issue demand-response event' }).click();
  await expect(page.getByTestId('dr-list').getByText(/published via outbox/)).toBeVisible({ timeout: 30_000 });
  await shot(page, '05-dr-event.png');

  // Multi-tenancy: kita-denki sees nothing of aurora's data
  await page.getByRole('button', { name: 'Switch tenant' }).click();
  await signIn(page, 'kita-denki');
  await expect(page.getByText(siteName)).toHaveCount(0);
  await shot(page, '06-tenant-switch-isolation.png');
});
