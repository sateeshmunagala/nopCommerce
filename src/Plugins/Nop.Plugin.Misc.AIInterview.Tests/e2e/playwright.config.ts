import fs from 'node:fs';
import path from 'node:path';
import { defineConfig } from '@playwright/test';
import dotenv from 'dotenv';

const envPath = path.resolve(__dirname, '.env.local');
if (fs.existsSync(envPath))
  dotenv.config({ path: envPath, override: false });

const baseURL = process.env.AIINTERVIEW_BASE_URL || 'https://localhost:54077';

function isPrivateHost(hostname: string): boolean {
  const lower = hostname.toLowerCase();
  if (lower === 'localhost' || lower === '127.0.0.1' || lower === '::1' || lower.endsWith('.local') || lower === 'host.docker.internal')
    return true;

  if (/^10\.\d+\.\d+\.\d+$/.test(lower))
    return true;

  if (/^192\.168\.\d+\.\d+$/.test(lower))
    return true;

  const match172 = lower.match(/^172\.(\d+)\.\d+\.\d+$/);
  if (match172) {
    const segment = Number(match172[1]);
    return segment >= 16 && segment <= 31;
  }

  return false;
}

const parsedBaseUrl = new URL(baseURL);
if (!isPrivateHost(parsedBaseUrl.hostname))
  throw new Error(`AIInterview E2E refuses to run against non-local host: ${parsedBaseUrl.hostname}`);

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
