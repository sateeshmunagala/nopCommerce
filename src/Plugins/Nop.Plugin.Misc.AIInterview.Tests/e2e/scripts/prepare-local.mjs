import fs from 'node:fs';
import path from 'node:path';
import { fileURLToPath } from 'node:url';
import { chromium } from '@playwright/test';
import sql from 'mssql';

const rootDir = path.resolve(path.dirname(fileURLToPath(import.meta.url)), '..');
const envPath = path.join(rootDir, '.env.local');
const authDir = path.join(rootDir, '.auth');
const storageStatePath = path.join(authDir, 'applicant.storage.json');
const appSettingsPath = path.resolve(rootDir, '../../../../src/Presentation/Nop.Web/App_Data/appsettings.json');

function isPrivateHost(hostname) {
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

function parseConnectionStringValue(connectionString, keys) {
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

function getConfig() {
  const baseUrl = process.env.AIINTERVIEW_BASE_URL || 'https://localhost:54077';
  const productUrl = process.env.AIINTERVIEW_PRODUCT_URL;
  const email = process.env.AIINTERVIEW_LOGIN_EMAIL || 'aiinterview.e2e@example.local';
  const password = process.env.AIINTERVIEW_LOGIN_PASSWORD || 'E2eLocalOnly!234';
  const minimumCredits = Number(process.env.AIINTERVIEW_MIN_CREDITS || '5');

  if (!productUrl)
    throw new Error('AIINTERVIEW_PRODUCT_URL is required. Point it at a dedicated local AI Interview job product.');

  const parsedBaseUrl = new URL(baseUrl);
  const parsedProductUrl = new URL(productUrl, parsedBaseUrl);
  if (!isPrivateHost(parsedBaseUrl.hostname))
    throw new Error(`Refusing to prepare E2E against non-local base URL host: ${parsedBaseUrl.hostname}`);
  if (parsedBaseUrl.origin !== parsedProductUrl.origin)
    throw new Error('AIINTERVIEW_PRODUCT_URL must use the same origin as AIINTERVIEW_BASE_URL.');

  let connectionString = process.env.AIINTERVIEW_E2E_DB_CONNECTION_STRING?.trim();
  if (!connectionString && fs.existsSync(appSettingsPath)) {
    const raw = fs.readFileSync(appSettingsPath, 'utf8').replace(/^\uFEFF/, '');
    const json = JSON.parse(raw);
    connectionString = json?.ConnectionStrings?.ConnectionString || '';
  }
  if (!connectionString)
    throw new Error('AIINTERVIEW_E2E_DB_CONNECTION_STRING is required when no local App_Data/appsettings.json connection string is available.');

  const dataSource = parseConnectionStringValue(connectionString, ['data source', 'server', 'addr', 'address', 'network address']);
  const initialCatalog = parseConnectionStringValue(connectionString, ['initial catalog', 'database']);
  const normalizedDataSource = dataSource.replace(/^tcp:/i, '').split(',')[0].replace(/^\.\\/, 'localhost');
  if (!dataSource || !isPrivateHost(normalizedDataSource))
    throw new Error(`Refusing to prepare E2E against non-local database host: ${dataSource || '(missing)'}`);
  if (initialCatalog && !/(local|test|e2e|sandbox|dev)/i.test(initialCatalog))
    throw new Error(`Refusing to prepare E2E against a database that does not look disposable: ${initialCatalog}`);

  return {
    baseUrl: parsedBaseUrl,
    productUrl: parsedProductUrl,
    email,
    password,
    minimumCredits,
    connectionString
  };
}

async function ensureAccountAndStorageState(config) {
  fs.mkdirSync(authDir, { recursive: true });

  const browser = await chromium.launch({ headless: true });
  const context = await browser.newContext({ ignoreHTTPSErrors: true });
  const page = await context.newPage();

  const tryLogin = async () => {
    await page.goto(new URL('/login', config.baseUrl).toString());
    await page.locator('input[name="Email"], #Email').first().fill(config.email);
    await page.locator('input[name="Password"], #Password').first().fill(config.password);
    await Promise.all([
      page.waitForLoadState('networkidle'),
      page.locator('button[type="submit"], input[type="submit"]').first().click()
    ]);
    return (await page.locator('.account, .my-account, a[href*="/customer/info"], a[href*="/logout"]').count()) > 0;
  };

  if (!(await tryLogin())) {
    await page.goto(new URL('/register', config.baseUrl).toString());
    await page.locator('input[name="FirstName"], #FirstName').first().fill('AIInterview');
    await page.locator('input[name="LastName"], #LastName').first().fill('E2E');
    await page.locator('input[name="Email"], #Email').first().fill(config.email);
    await page.locator('input[name="Password"], #Password').first().fill(config.password);
    await page.locator('input[name="ConfirmPassword"], #ConfirmPassword').first().fill(config.password);
    await Promise.all([
      page.waitForLoadState('networkidle'),
      page.locator('button[type="submit"], input[type="submit"]').first().click()
    ]);

    const emailExists = await page.getByText(/The specified email already exists/i).count();
    if (emailExists > 0 && !(await tryLogin()))
      throw new Error(`Unable to login with existing applicant account ${config.email}.`);
  }

  await context.storageState({ path: storageStatePath });
  await browser.close();
}

async function prepareDatabase(config) {
  const slug = config.productUrl.pathname.split('/').filter(Boolean).pop();
  if (!slug)
    throw new Error(`Unable to derive a slug from ${config.productUrl.href}`);

  const pool = new sql.ConnectionPool(config.connectionString);
  await pool.connect();
  try {
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
    customerRequest.input('email', sql.NVarChar(400), config.email);
    const customerResult = await customerRequest.query(`
SELECT TOP (1) CAST([Id] AS int) AS CustomerId
FROM [Customer]
WHERE [Email] = @email AND ISNULL([Deleted], 0) = 0
ORDER BY [Id] DESC;`);
    const customerId = customerResult.recordset[0]?.CustomerId;
    if (!customerId)
      throw new Error(`Unable to resolve CustomerId for '${config.email}' after registration.`);

    await pool.request().query(`
IF EXISTS (SELECT 1 FROM [Setting] WHERE [Name] = 'mockaiinterviewsettings.usemockresponses' AND [StoreId] = 0)
    UPDATE [Setting] SET [Value] = 'True' WHERE [Name] = 'mockaiinterviewsettings.usemockresponses' AND [StoreId] = 0;
ELSE
    INSERT INTO [Setting] ([Name], [Value], [StoreId]) VALUES ('mockaiinterviewsettings.usemockresponses', 'True', 0);`);

    const walletRequest = pool.request();
    walletRequest.input('customerId', sql.Int, customerId);
    walletRequest.input('minimumBalance', sql.Decimal(18, 4), config.minimumCredits);
    await walletRequest.query(`
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
END;

UPDATE [InterviewSession]
SET [IsActive] = 0,
    [CompletedOnUtc] = COALESCE([CompletedOnUtc], SYSUTCDATETIME()),
    [TokenExpiryUtc] = COALESCE([TokenExpiryUtc], SYSUTCDATETIME())
WHERE [CustomerId] = @customerId
  AND [ProductId] = ${productId}
  AND [IsActive] = 1
  AND [CompletedOnUtc] IS NULL;`);
  } finally {
    await pool.close();
  }
}

function writeEnvFile(config) {
  const content = [
    `AIINTERVIEW_BASE_URL=${config.baseUrl.origin}`,
    `AIINTERVIEW_PRODUCT_URL=${config.productUrl.href}`,
    `AIINTERVIEW_EXPIRED_PRODUCT_URL=${config.productUrl.origin}${config.productUrl.pathname}?interviewError=expired`,
    `AIINTERVIEW_LOGIN_EMAIL=${config.email}`,
    `AIINTERVIEW_LOGIN_PASSWORD=${config.password}`,
    `AIINTERVIEW_STORAGE_STATE=${storageStatePath.replace(/\\/g, '/')}`,
    `AIINTERVIEW_E2E_DB_CONNECTION_STRING=${config.connectionString}`,
    `AIINTERVIEW_MIN_CREDITS=${config.minimumCredits}`,
    'AIINTERVIEW_EXPECT_RECORDING=0'
  ].join('\n') + '\n';

  fs.writeFileSync(envPath, content, 'utf8');
}

const config = getConfig();
await ensureAccountAndStorageState(config);
await prepareDatabase(config);
writeEnvFile(config);

console.log(`AIInterview E2E local preparation complete.
Env file: ${envPath}
Storage state: ${storageStatePath}
Applicant: ${config.email}
Product URL: ${config.productUrl.href}`);
