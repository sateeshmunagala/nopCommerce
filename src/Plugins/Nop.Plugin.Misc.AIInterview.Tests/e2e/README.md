# AIInterview Browser E2E

This Playwright harness is for **local/test environments only**. Do not point it at production.

## What it covers

- Product page expired-link recovery message
- Start Interview navigation into the mock runtime
- First question appearing in the runtime
- Empty-answer reminder visibility
- Non-empty answer auto-submit flow
- Submit/stop report navigation
- Report page display of saved turns, score, feedback, report data, and optional recording link
- Runtime token renewal using a seeded near-expiry or expired active session

## Preconditions

Use a local or disposable test database copy. Do not run this against production data.

Prepare a dedicated applicant test account and a dedicated AIInterview job product.

Recommended local setup:

1. Start the local nopCommerce site, for example `https://localhost:54077`.
2. Enable `UseMockResponses` in AI Interview admin settings for deterministic runtime behavior, or point Azure OpenAI to a test deployment.
3. Give the dedicated applicant account local test credits using the AI Interview admin top-up page, or use a local sponsor invite for the test product.
4. Use a job product URL that shows the AI Interview widget and Start Interview button.
5. For the renewal scenario, create an active unfinished session in the local DB, capture its runtime URL, then set `InterviewSession.TokenExpiryUtc` to a past or near-future value while keeping `IsActive = 1` and `CompletedOnUtc = NULL`.

## Environment variables

- `AIINTERVIEW_BASE_URL`
  Example: `https://localhost:54077`
- `AIINTERVIEW_PRODUCT_URL`
  Product/job details URL used for the main candidate flow
- `AIINTERVIEW_EXPIRED_PRODUCT_URL`
  Same product URL with `?interviewError=expired`
- `AIINTERVIEW_RUNTIME_RENEW_URL`
  Runtime URL for an active unfinished session whose token should renew during the test
- `AIINTERVIEW_LOGIN_EMAIL`
  Required unless `AIINTERVIEW_STORAGE_STATE` is provided
- `AIINTERVIEW_LOGIN_PASSWORD`
  Required unless `AIINTERVIEW_STORAGE_STATE` is provided
- `AIINTERVIEW_STORAGE_STATE`
  Optional Playwright storage state file for an already-authenticated applicant
- `AIINTERVIEW_EXPECT_RECORDING`
  Set to `1` only if the seeded report session has a recording link
- `AIINTERVIEW_HEADED`
  Set to `1` to run headed

## Install

```powershell
cd src\Plugins\Nop.Plugin.Misc.AIInterview.Tests\e2e
npm install
npm run install:browsers
```

## Run

```powershell
$env:AIINTERVIEW_BASE_URL = "https://localhost:54077"
$env:AIINTERVIEW_PRODUCT_URL = "https://localhost:54077/jobs/sample-job"
$env:AIINTERVIEW_EXPIRED_PRODUCT_URL = "https://localhost:54077/jobs/sample-job?interviewError=expired"
$env:AIINTERVIEW_RUNTIME_RENEW_URL = "https://localhost:54077/mockaiinterview/runtime?token=seeded-near-expiry-token"
$env:AIINTERVIEW_LOGIN_EMAIL = "aiinterview.e2e@example.com"
$env:AIINTERVIEW_LOGIN_PASSWORD = "YourLocalOnlyPassword"
npm test
```

## Non-destructive guidance

- Use a dedicated local applicant account only.
- Use a cloned or disposable local database.
- Use local admin credit top-up or a local sponsor invite only.
- If you need recording assertions, seed `InterviewSession.RecordingUrl` in the local DB for the test session instead of pointing at production storage.

## Manual acceptance checklist

1. Product page shows the expired-link message and Start Interview is enabled.
2. Start Interview lands on `/mockaiinterview/runtime?token=...`.
3. First AI question appears and, when Azure Speech is configured, can be spoken.
4. Empty answer shows the reminder after about 5 seconds.
5. Non-empty answer triggers `Auto submitting...` and advances to the next question with feedback/score saved.
6. Stop Interview routes to `/mockaiinterview/report/{sessionId}`.
7. Report page shows saved questions, answers, scores, feedback, report data, and the recording link when present.
8. The renewal scenario does not show `Invalid or expired session token.` during an active unfinished interview.
