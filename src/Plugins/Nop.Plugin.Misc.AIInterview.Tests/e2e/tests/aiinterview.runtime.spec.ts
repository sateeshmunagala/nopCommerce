import fs from 'node:fs';
import path from 'node:path';
import sql from 'mssql';
import { expect, test, type Page } from '@playwright/test';

const expiredMessage = 'Your previous interview link expired. Start the interview again from this page.';
const unavailableMessage = 'AI service unavailable. Please try again later.';
const mockModeWarning = 'Development mock mode is enabled. Azure OpenAI is bypassed.';

const productUrl = process.env.AIINTERVIEW_PRODUCT_URL;
const loginEmail = process.env.AIINTERVIEW_LOGIN_EMAIL;
const loginPassword = process.env.AIINTERVIEW_LOGIN_PASSWORD;
const expectRecording = process.env.AIINTERVIEW_EXPECT_RECORDING === '1';
const targetCredits = Number(process.env.AIINTERVIEW_MIN_CREDITS || '5');
const targetDifficulty = process.env.AIINTERVIEW_RENEW_DIFFICULTY || 'Medium';

type FixtureState = {
  baseUrl: URL;
  productUrl: URL;
  productId: number;
  customerId: number;
  expiredProductUrl: string;
  dbConnectionString: string;
};

let fixtureState: FixtureState | null = null;

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

function parseConnectionStringValue(connectionString: string, keys: string[]): string {
  const parts = connectionString.split(';').map((part) => part.trim()).filter(Boolean);
  for (const part of parts) {
    const separatorIndex = part.indexOf('=');
    if (separatorIndex <= 0)
      continue;

    const key = part.slice(0, separatorIndex).trim().toLowerCase();
    if (!keys.includes(key))
      continue;

    return part.slice(separatorIndex + 1).trim();
  }

  return '';
}

function getSafeDbConnectionString(): string {
  const explicit = process.env.AIINTERVIEW_E2E_DB_CONNECTION_STRING?.trim();
  const appSettingsPath = path.resolve(__dirname, '../../../../Presentation/Nop.Web/App_Data/appsettings.json');

  let candidate = explicit;
  if (!candidate && fs.existsSync(appSettingsPath)) {
    const json = JSON.parse(fs.readFileSync(appSettingsPath, 'utf8'));
    candidate = json?.ConnectionStrings?.ConnectionString || '';
  }

  if (!candidate)
    throw new Error('AIInterview E2E requires AIINTERVIEW_E2E_DB_CONNECTION_STRING or a local App_Data/appsettings.json connection string.');

  const dataSource = parseConnectionStringValue(candidate, ['data source', 'server', 'addr', 'address', 'network address']);
  const initialCatalog = parseConnectionStringValue(candidate, ['initial catalog', 'database']);

  if (!dataSource || !isPrivateHost(dataSource.replace(/^tcp:/i, '').split(',')[0].replace(/^\.\\/, 'localhost')))
    throw new Error(`AIInterview E2E refuses to use a non-local database host: ${dataSource || '(missing)'}`);

  if (initialCatalog && !/(local|test|e2e|sandbox|dev)/i.test(initialCatalog))
    throw new Error(`AIInterview E2E refuses to use a database that does not look disposable: ${initialCatalog}`);

  return candidate;
}

function requireEnv(...names: string[]) {
  const missing = names.filter((name) => !process.env[name]);
  if (missing.length)
    throw new Error(`Missing environment variables: ${missing.join(', ')}`);
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

async function withPool<T>(connectionString: string, work: (pool: sql.ConnectionPool) => Promise<T>): Promise<T> {
  const pool = new sql.ConnectionPool(connectionString);
  await pool.connect();
  try {
    return await work(pool);
  } finally {
    await pool.close();
  }
}

async function resolveFixtureState(): Promise<FixtureState> {
  if (fixtureState)
    return fixtureState;

  requireEnv('AIINTERVIEW_PRODUCT_URL');

  const base = new URL(process.env.AIINTERVIEW_BASE_URL || 'https://localhost:54077');
  const product = new URL(productUrl!, base);

  if (!isPrivateHost(base.hostname))
    throw new Error(`AIInterview E2E refuses to run against non-local base URL: ${base.hostname}`);

  if (base.origin !== product.origin)
    throw new Error('AIINTERVIEW_PRODUCT_URL must use the same origin as AIINTERVIEW_BASE_URL.');

  const connectionString = getSafeDbConnectionString();
  const slug = product.pathname.split('/').filter(Boolean).pop();
  if (!slug)
    throw new Error(`Unable to derive a product slug from ${product.href}`);

  requireEnv('AIINTERVIEW_LOGIN_EMAIL');

  const resolved = await withPool(connectionString, async (pool) => {
    const productRequest = pool.request();
    productRequest.input('slug', sql.NVarChar(400), slug);
    const productResult = await productRequest.query(`
SELECT TOP (1) CAST([EntityId] AS int) AS ProductId
FROM [UrlRecord]
WHERE [EntityName] = 'Product' AND [Slug] = @slug AND [IsActive] = 1
ORDER BY [LanguageId] DESC, [Id] DESC;`);

    const productId = productResult.recordset[0]?.ProductId;
    if (!productId)
      throw new Error(`Unable to resolve ProductId for slug '${slug}'.`);

    const customerRequest = pool.request();
    customerRequest.input('email', sql.NVarChar(400), loginEmail!);
    const customerResult = await customerRequest.query(`
SELECT TOP (1) CAST([Id] AS int) AS CustomerId
FROM [Customer]
WHERE [Email] = @email AND ISNULL([Deleted], 0) = 0
ORDER BY [Id] DESC;`);

    const customerId = customerResult.recordset[0]?.CustomerId;
    if (!customerId)
      throw new Error(`Unable to resolve CustomerId for '${loginEmail}'. Run npm run prepare:local first.`);

    return {
      baseUrl: base,
      productUrl: product,
      productId,
      customerId,
      expiredProductUrl: `${product.origin}${product.pathname}?interviewError=expired`,
      dbConnectionString: connectionString
    };
  });

  fixtureState = resolved;
  return resolved;
}

async function setMockModeEnabled(state: FixtureState) {
  await withPool(state.dbConnectionString, async (pool) => {
    const request = pool.request();
    await request.query(`
IF EXISTS (SELECT 1 FROM [Setting] WHERE [Name] = 'mockaiinterviewsettings.usemockresponses' AND [StoreId] = 0)
    UPDATE [Setting] SET [Value] = 'True' WHERE [Name] = 'mockaiinterviewsettings.usemockresponses' AND [StoreId] = 0;
ELSE
    INSERT INTO [Setting] ([Name], [Value], [StoreId]) VALUES ('mockaiinterviewsettings.usemockresponses', 'True', 0);`);
  });
}

async function ensureWalletBalance(state: FixtureState, minimumBalance: number) {
  await withPool(state.dbConnectionString, async (pool) => {
    const request = pool.request();
    request.input('customerId', sql.Int, state.customerId);
    request.input('minimumBalance', sql.Decimal(18, 4), minimumBalance);
    await request.query(`
DECLARE @walletId int;
SELECT TOP (1) @walletId = [Id] FROM [CreditWallet] WHERE [CustomerId] = @customerId ORDER BY [Id] DESC;

IF @walletId IS NULL
BEGIN
    INSERT INTO [CreditWallet] ([CustomerId], [Balance]) VALUES (@customerId, 0);
    SET @walletId = SCOPE_IDENTITY();
END;

DECLARE @currentBalance decimal(18,4);
SELECT @currentBalance = [Balance] FROM [CreditWallet] WHERE [Id] = @walletId;

IF @currentBalance < @minimumBalance
BEGIN
    DECLARE @delta decimal(18,4) = @minimumBalance - @currentBalance;
    UPDATE [CreditWallet] SET [Balance] = @minimumBalance WHERE [Id] = @walletId;
    INSERT INTO [CreditLedgerEntry] ([CreditWalletId], [Amount], [TransactionType], [Remarks], [CreatedOnUtc])
    VALUES (@walletId, @delta, 'credit', 'Local AIInterview E2E top-up', SYSUTCDATETIME());
END;`);
  });
}

async function deactivateUnfinishedSessions(state: FixtureState) {
  await withPool(state.dbConnectionString, async (pool) => {
    const request = pool.request();
    request.input('customerId', sql.Int, state.customerId);
    request.input('productId', sql.Int, state.productId);
    await request.query(`
UPDATE [InterviewSession]
SET [IsActive] = 0,
    [CompletedOnUtc] = COALESCE([CompletedOnUtc], SYSUTCDATETIME()),
    [TokenExpiryUtc] = COALESCE([TokenExpiryUtc], SYSUTCDATETIME())
WHERE [CustomerId] = @customerId
  AND [ProductId] = @productId
  AND [IsActive] = 1
  AND [CompletedOnUtc] IS NULL;`);
  });
}

async function createFixedTokenSession(state: FixtureState): Promise<string> {
  return withPool(state.dbConnectionString, async (pool) => {
    const token = `e2e-fixed-${Date.now()}`;
    const sessionKey = `e2e-fixed-${Date.now()}`;
    const request = pool.request();
    request.input('customerId', sql.Int, state.customerId);
    request.input('productId', sql.Int, state.productId);
    request.input('sessionKey', sql.NVarChar(100), sessionKey);
    request.input('difficulty', sql.NVarChar(100), targetDifficulty);
    request.input('token', sql.NVarChar(100), token);
    await request.query(`
INSERT INTO [InterviewSession]
(
    [CustomerId], [JobApplicationId], [SessionKey], [ProductId], [Difficulty], [Token], [TokenExpiryUtc], [IsActive],
    [RecordingUrl], [ReportData], [QuestionScores], [Score], [SponsorInviteId], [CreatedOnUtc], [StartedOnUtc], [CompletedOnUtc]
)
VALUES
(
    @customerId, 0, @sessionKey, @productId, @difficulty, @token, DATEADD(MINUTE, 120, SYSUTCDATETIME()), 1,
    NULL, NULL, NULL, 0, 0, SYSUTCDATETIME(), SYSUTCDATETIME(), NULL
);`);

    return `${state.baseUrl.origin}/mockaiinterview/runtime?token=${token}`;
  });
}

test.beforeAll(async () => {
  const state = await resolveFixtureState();
  await setMockModeEnabled(state);
  await ensureWalletBalance(state, targetCredits);
});

test.describe('AIInterview candidate runtime', () => {
  test('product page shows expired-link recovery message', async ({ page }) => {
    const state = await resolveFixtureState();
    await ensureSignedIn(page);
    await page.goto(state.expiredProductUrl);

    await expect(page.getByText(expiredMessage)).toBeVisible();
    await expect(page.locator('#start-job-interview, [data-start-interview-button="true"]').first()).toBeEnabled();
  });

  test('start interview loads runtime, reminder, answer submit, stop, and report', async ({ page }) => {
    const state = await resolveFixtureState();
    await deactivateUnfinishedSessions(state);
    await ensureWalletBalance(state, targetCredits);

    await startInterviewFromProduct(page, state.productUrl.href);
    await waitForActiveQuestion(page);

    await expect(page.getByText(mockModeWarning)).toBeVisible();

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

  test('runtime keeps fixed token and continues without refresh', async ({ page }) => {
    const state = await resolveFixtureState();
    await deactivateUnfinishedSessions(state);
    const runtimeUrl = await createFixedTokenSession(state);
    let refreshRequested = false;
    page.on('request', (request) => {
      if (request.url().includes('/mockaiinterview/refresh-token') && request.method() === 'POST')
        refreshRequested = true;
    });

    await ensureSignedIn(page);

    await page.goto(runtimeUrl);
    await waitForActiveQuestion(page);
    expect(refreshRequested).toBe(false);

    await page.locator('#runtime-answer').fill('Fixed token verification answer.');
    await page.locator('#submit-answer').click();

    await expect(page.locator('#runtime-status')).not.toContainText(/Invalid or expired session token/i);
    await expect(page.locator('#runtime-question')).not.toContainText(unavailableMessage);
    expect(refreshRequested).toBe(false);
  });
});
