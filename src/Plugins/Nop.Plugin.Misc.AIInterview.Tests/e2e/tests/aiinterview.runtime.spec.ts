import { expect, test, type Page } from '@playwright/test';

const expiredMessage = 'Your previous interview link expired. Start the interview again from this page.';
const unavailableMessage = 'AI service unavailable. Please try again later.';

const productUrl = process.env.AIINTERVIEW_PRODUCT_URL;
const expiredProductUrl = process.env.AIINTERVIEW_EXPIRED_PRODUCT_URL;
const runtimeRenewUrl = process.env.AIINTERVIEW_RUNTIME_RENEW_URL;
const loginEmail = process.env.AIINTERVIEW_LOGIN_EMAIL;
const loginPassword = process.env.AIINTERVIEW_LOGIN_PASSWORD;
const expectRecording = process.env.AIINTERVIEW_EXPECT_RECORDING === '1';

function requireEnv(...names: string[]) {
  const missing = names.filter((name) => !process.env[name]);
  test.skip(missing.length > 0, `Missing environment variables: ${missing.join(', ')}`);
}

async function ensureSignedIn(page: Page) {
  if (process.env.AIINTERVIEW_STORAGE_STATE)
    return;

  requireEnv('AIINTERVIEW_LOGIN_EMAIL', 'AIINTERVIEW_LOGIN_PASSWORD');

  await page.goto('/login');
  await page.locator('input[name="Email"], #Email').first().fill(loginEmail!);
  await page.locator('input[name="Password"], #Password').first().fill(loginPassword!);
  await Promise.all([
    page.waitForLoadState('networkidle'),
    page.locator('button[type="submit"], input[type="submit"]').first().click()
  ]);
}

async function startInterviewFromProduct(page: Page, url: string) {
  await ensureSignedIn(page);
  await page.goto(url);

  const startButton = page.locator('#start-job-interview, [data-start-interview-button="true"]').first();
  await expect(startButton).toBeVisible();
  await expect(startButton).toBeEnabled();

  await Promise.all([
    page.waitForURL(/\/mockaiinterview\/runtime\?token=/),
    startButton.click()
  ]);

  await expect(page.locator('#runtime-question')).toBeVisible();
}

async function waitForActiveQuestion(page: Page) {
  const question = page.locator('#runtime-question');
  await expect.poll(async () => (await question.textContent())?.trim() || '', {
    timeout: 20_000,
    message: 'Expected runtime question to move off placeholder or unavailable text.'
  }).not.toMatch(/^$|^Welcome! Click Start Interview to begin\.$|^AI service unavailable\./);
}

test.describe('AIInterview candidate runtime', () => {
  test('product page shows expired-link recovery message', async ({ page }) => {
    requireEnv('AIINTERVIEW_EXPIRED_PRODUCT_URL');
    await ensureSignedIn(page);
    await page.goto(expiredProductUrl!);

    await expect(page.getByText(expiredMessage)).toBeVisible();
    await expect(page.locator('#start-job-interview, [data-start-interview-button="true"]').first()).toBeEnabled();
  });

  test('start interview loads runtime, reminder, answer submit, stop, and report', async ({ page }) => {
    requireEnv('AIINTERVIEW_PRODUCT_URL');

    await startInterviewFromProduct(page, productUrl!);
    await waitForActiveQuestion(page);

    const questionBefore = ((await page.locator('#runtime-question').textContent()) || '').trim();
    await expect(page.locator('#submit-answer')).toBeEnabled();

    await expect(page.locator('#runtime-status')).toHaveText(/Please answer the question\./, { timeout: 8_500 });

    const answerBox = page.locator('#runtime-answer');
    const submitButton = page.locator('#submit-answer');

    await answerBox.fill('This is an automated local-only E2E answer with context and impact.');
    await expect.poll(async () => await submitButton.textContent(), {
      timeout: 8_500,
      message: 'Expected auto-submit button state after answer silence.'
    }).toBe('Auto submitting...');

    await expect.poll(async () => ((await page.locator('#runtime-question').textContent()) || '').trim(), {
      timeout: 20_000,
      message: 'Expected next question after answer submission.'
    }).not.toBe(questionBefore);

    await expect(page.locator('#conversation')).toContainText('Score:', { timeout: 10_000 });
    await expect(page.locator('#conversation')).not.toContainText(unavailableMessage);

    await Promise.all([
      page.waitForURL(/\/mockaiinterview\/report\//, { timeout: 15_000 }),
      page.locator('#stop-interview').click()
    ]);

    await expect(page).toHaveURL(/\/mockaiinterview\/report\//);
    await expect(page.locator('.mock-report, .ai-interview-report-page')).toBeVisible();
    await expect(page.getByText('Interview Turns')).toBeVisible();
    await expect(page.getByText('Answer:')).toBeVisible();
    await expect(page.getByText('Score:')).toBeVisible();
    await expect(page.getByText('Feedback:')).toBeVisible();
    await expect(page.locator('.report-data')).not.toBeEmpty();

    if (expectRecording)
      await expect(page.getByRole('link', { name: /Open recording/i })).toBeVisible();
  });

  test('runtime renews active token and continues safely', async ({ page }) => {
    requireEnv('AIINTERVIEW_RUNTIME_RENEW_URL');

    await ensureSignedIn(page);

    const refreshResponsePromise = page.waitForResponse((response) =>
      response.url().includes('/mockaiinterview/refresh-token') &&
      response.request().method() === 'POST', { timeout: 15_000 }).catch(() => null);

    await page.goto(runtimeRenewUrl!);
    await waitForActiveQuestion(page);

    const refreshResponse = await refreshResponsePromise;
    if (refreshResponse) {
      const json = await refreshResponse.json();
      expect(json.newToken || json.NewToken).toBeTruthy();
    }

    await page.locator('#runtime-answer').fill('Token renewal verification answer.');
    await page.locator('#submit-answer').click();

    await expect(page.locator('#runtime-status')).not.toContainText(/Invalid or expired session token/i);
    await expect(page.locator('#runtime-question')).not.toContainText(unavailableMessage);
  });
});
