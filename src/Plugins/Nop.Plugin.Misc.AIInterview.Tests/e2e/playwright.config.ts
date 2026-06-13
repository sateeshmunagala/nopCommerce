import { defineConfig } from '@playwright/test';

const baseURL = process.env.AIINTERVIEW_BASE_URL || 'https://localhost:54077';

export default defineConfig({
  testDir: './tests',
  timeout: 60_000,
  expect: {
    timeout: 15_000
  },
  fullyParallel: false,
  forbidOnly: !!process.env.CI,
  retries: process.env.CI ? 2 : 0,
  reporter: [['list'], ['html', { open: 'never' }]],
  use: {
    baseURL,
    headless: process.env.AIINTERVIEW_HEADED !== '1',
    ignoreHTTPSErrors: true,
    trace: 'retain-on-failure',
    screenshot: 'only-on-failure',
    video: 'retain-on-failure',
    storageState: process.env.AIINTERVIEW_STORAGE_STATE || undefined
  }
});
