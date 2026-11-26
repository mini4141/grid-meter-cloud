import { defineConfig } from '@playwright/test';

// BASE_URL defaults to the local docker-compose web container. Phase 2 points it at CloudFront.
export default defineConfig({
  testDir: './e2e',
  timeout: 180_000,
  retries: 0,
  reporter: [['list'], ['html', { open: 'never', outputFolder: process.env.PW_REPORT ?? 'playwright-report' }], ['junit', { outputFile: process.env.PW_JUNIT ?? 'test-results/junit.xml' }]],
  use: { baseURL: process.env.BASE_URL ?? 'http://localhost:5173', viewport: { width: 1440, height: 1000 } },
});
