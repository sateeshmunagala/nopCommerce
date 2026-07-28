# AIInterview Browser E2E

This harness is **local/test only**. It must not point at production URLs, production databases, production credits, or production Azure resources.

The Playwright config and setup script now fail fast when:

- `AIINTERVIEW_BASE_URL` is not a localhost/private-origin URL
- the SQL Server host is not local/private
- the database name does not look disposable (`local`, `test`, `e2e`, `sandbox`, or `dev`)

## What the harness covers

- Product page expired-link recovery message
- Start Interview navigation into `/mockaiinterview/runtime`
- First question appearing in the runtime
- Empty-answer reminder visibility
- Non-empty answer auto-submit flow
- Stop Interview navigation to `/mockaiinterview/report/{sessionId}`
- Report page display of saved turns, score, feedback, report data, and optional recording link
- Fixed runtime-token usage using a seeded active unfinished session with future `TokenExpiryUtc`

## Clean-checkout local workflow

### 1. Start a disposable local nopCommerce site

Use a local or disposable database only.

Recommended:

- local site URL: `https://localhost:54077`
- local/private SQL Server host
- disposable database name containing `local`, `test`, `e2e`, `sandbox`, or `dev`

### 2. Prepare one dedicated AI Interview job product

The setup script does **not** create products. Use an existing local-only AI Interview job product page that:

- renders the AI Interview widget
- shows the Start Interview button
- points to the same origin as `AIINTERVIEW_BASE_URL`

Example:

```powershell
$env:AIINTERVIEW_PRODUCT_URL = "https://localhost:54077/jobs/aiinterview-e2e-job"
```

### 3. Install E2E dependencies from a clean checkout

```powershell
cd src\Plugins\Nop.Plugin.Misc.AIInterview.Tests\e2e
npm install
npm run install:browsers
```

This creates the reproducible `package-lock.json` used by future runs.

### 4. Provide local-only environment inputs

Minimum required inputs:

```powershell
$env:AIINTERVIEW_BASE_URL = "https://localhost:54077"
$env:AIINTERVIEW_PRODUCT_URL = "https://localhost:54077/jobs/aiinterview-e2e-job"
```

Optional overrides:

```powershell
$env:AIINTERVIEW_LOGIN_EMAIL = "aiinterview.e2e@example.local"
$env:AIINTERVIEW_LOGIN_PASSWORD = "E2eLocalOnly!234"
$env:AIINTERVIEW_MIN_CREDITS = "5"
$env:AIINTERVIEW_E2E_DB_CONNECTION_STRING = "Data Source=localhost;Initial Catalog=nop-aiinterview-e2e;Integrated Security=True;Trust Server Certificate=True"
```

Notes:

- If `AIINTERVIEW_E2E_DB_CONNECTION_STRING` is omitted, the prep script falls back to `src/Presentation/Nop.Web/App_Data/appsettings.json`.
- If that connection string points to a non-local or non-disposable database, the script refuses to run.

### 5. Run the scripted local preparation

```powershell
npm run prepare:local
```

What `prepare:local` does:

- validates the base URL and DB connection are local/test-safe
- creates or logs into a dedicated applicant account
- saves Playwright storage state to `.auth/applicant.storage.json`
- forces `mockaiinterviewsettings.usemockresponses = True` for deterministic E2E
- resolves the product ID from the provided product URL slug
- tops up the applicant wallet in plugin credit tables to a safe minimum balance
- closes unfinished interview sessions for that applicant/product so Start Interview begins from a clean state
- writes `.env.local` for repeatable `npm test` runs

### 6. Run Playwright E2E

```powershell
npm test
```

Optional:

```powershell
npm run test:headed
npm run test:debug
```

## What the Playwright run seeds automatically

No manual DB surgery is required during test execution.

At runtime, the spec now:

- reloads the local-only fixture state from `.env.local`
- re-enables mock mode if needed
- ensures the applicant wallet has enough test credits
- deactivates unfinished sessions before the product-page candidate flow
- creates a dedicated active unfinished fixed-token session with future `TokenExpiryUtc` before the fixed-token test

That means the fixed-token scenario no longer depends on a hand-built runtime URL.

## Environment files produced locally

- `.env.local`
- `.auth/applicant.storage.json`

These are ignored by the local `.gitignore` in this folder and should not be committed.

## Production-safety guardrails

The harness refuses to run when:

- `AIINTERVIEW_BASE_URL` is not localhost/private
- the SQL host is remote/public
- the database name does not look disposable

This is intentional. If your current site configuration points at a shared or remote database, fix that first instead of bypassing the guard.

## Optional Azure Speech / recording notes

The E2E harness does not require production Azure resources.

- Question/reminder UI flow is validated under mock mode.
- Recording link assertions remain optional and are enabled only when `AIINTERVIEW_EXPECT_RECORDING=1`.
- Do not point Blob or Speech settings at production resources for E2E.

## Manual acceptance checklist

1. Product page shows the expired-link message and Start Interview is enabled.
2. Start Interview lands on `/mockaiinterview/runtime?token=...`.
3. First AI question appears.
4. Empty answer shows the reminder after about 5 seconds.
5. Non-empty answer triggers `Auto submitting...` and advances to the next question.
6. Stop Interview routes to `/mockaiinterview/report/{sessionId}`.
7. Report page shows saved questions, answers, scores, feedback, report data, and the recording link when present.
8. The fixed-token scenario does not call `/mockaiinterview/refresh-token` and does not show `Invalid or expired session token.` during an active unfinished interview.
