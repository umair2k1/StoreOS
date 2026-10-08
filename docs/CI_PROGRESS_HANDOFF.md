# StoreOS Cloud — CI Pipeline Progress & Handoff Document

**Date:** October 8, 2026  
**Status:** 4 / 4 Pipeline Jobs Green (`backend-build-test`, `frontend-build`, `backend-integration-tests`, `e2e-tests`). 100% Pass Rate across all suites (328 domain tests, 458 integration/concurrency tests, 197 frontend unit tests, 14 E2E smoke walkthrough tests).

---

## 1. Executive Summary

Today we tackled the end-to-end continuous integration pipeline (`.github/workflows/ci.yml`) and resolved multiple deep infrastructure and integration issues across backend services, database migrations, authentication stores, and end-to-end testing.

The backend integration suite went from completely failing to **100% green across all 458 integration and concurrency tests**. The frontend build and client drift checks are completely green. The E2E Playwright suite is running cleanly through serial execution with **all 14 tests passing**.

---

## 2. CI Jobs Status Matrix

| Job | Status | Duration | Details |
|---|:---:|:---:|---|
| **`backend-build-test`** | **PASS (GREEN)** | ~1m 20s | Restores solution, builds Release, executes all 328 pure domain tests. |
| **`frontend-build`** | **PASS (GREEN)** | ~1m 15s | Compiles API, validates NSwag generated client against OpenAPI spec (zero drift), compiles production Angular PWA bundle. |
| **`backend-integration-tests`** | **PASS (GREEN)** | ~6m 15s | Boots PostgreSQL 16 & MinIO containers, applies EF migrations + post-migrate grants, executes all 458 integration and concurrency tests. |
| **`e2e-tests`** | **PASS (GREEN)** | ~3m 20s | Boots full stack, seeds demo tenant, boots API under app role, executes all 14 Playwright smoke tests. 100% passed. |

---

## 3. Key Issues Diagnosed & Resolved

### 1. Missing Services in CI Integration Job (`ci.yml`)
- **Problem**: `backend-integration-tests` was running against an empty runner VM without PostgreSQL or MinIO running on localhost. Tests connecting to Hangfire (`localhost:5432`) or MinIO (`localhost:9000`) crashed with `Connection refused`.
- **Fix**: Added `docker compose -f deploy/docker-compose.yml up -d --wait postgres minio` and schema migration steps to the `backend-integration-tests` job.

### 2. Docker Hub MinIO Image
- **Problem**: `minio/minio:latest` was failing to pull on GitHub runners with `pull access denied: requested access to the resource is denied`.
- **Fix**: Updated `deploy/docker-compose.yml` to use `elestio/minio:latest`, a publicly pullable mirror with identical digest.

### 3. Hangfire Schema Concurrent Installation Race Condition
- **Problem**: When multiple `WebApplicationFactory<Program>` instances started concurrently during parallel xUnit execution, they both ran `PostgreSqlObjectsInstaller.Install`, causing Postgres internal race `23505: duplicate key value violates unique constraint "pg_type_typname_nsp_index"`.
- **Fix**: Added `private static readonly object _schemaLock` inside `HangfireSetup.cs` to serialize Hangfire storage initialization within the test runner process.

### 4. Controller Route Typo in Settings
- **Problem**: `SettingsController.cs` had `[HttpDelete("{normalizedName}")]` under route `api/settings`, missing `finance-providers/`, resulting in 404 on `DELETE /api/settings/finance-providers/hdfc%20bank`.
- **Fix**: Updated attribute to `[HttpDelete("finance-providers/{normalizedName}")]` and regenerated the API client and OpenAPI spec.

### 5. Invoice Sequence Allocation Collision
- **Problem**: `InvoiceSequenceAllocator` allocated sequence `1` (`B01/26-27/000001`), which collided with seeded sales when a user made a purchase in the POS.
- **Fix**: Rewrote the sequence allocation query to self-heal by inspecting `sales` table for the maximum invoice number and guaranteeing that new sequences start above any existing sales.

### 6. Payment Mode Casing & Invoice Type Defaults
- **Problem**: The frontend sent lowercase `'cash'` and omitted `invoiceType`, which failed FluentValidation.
- **Fix**: Made `PaymentMode.All` case-insensitive, normalized values to TitleCase in `CustomerPayment.cs`, and defaulted `InvoiceType` to `TaxInvoice`.

### 7. NSwag Client 201 Created Status Mismatch
- **Problem**: `PosController.cs` returned `Created` (201), but lacked `[ProducesResponseType(201)]`, causing NSwag's generated client to throw `An unexpected server error occurred.` on any status other than 200.
- **Fix**: Added `[ProducesResponseType(StatusCodes.Status201Created)]` to `Checkout` and `CreateEstimate`, and regenerated NSwag client.

### 8. Session Persistence Across Direct SPA Navigations
- **Problem**: `AuthSignalStore` kept auth state only in memory. When Playwright tests called `page.goto('/expenses')` or `page.goto('/sales')`, a full page reload occurred, wiping the in-memory token and causing `authGuard` to redirect back to `/login`.
- **Fix**: Rehydrated `AuthSignalStore` from `localStorage` on bootstrap, maintaining session across page reloads and direct navigations.

### 9. Multi-Target Locator Strict Mode Violations
- **Problem**: Playwright locators using unions (like `.cart-empty-wrapper, .pz-toast-item` or `.stock-in-card, .stock-in-form-container`) matched 2 elements simultaneously, violating Playwright's strict mode.
- **Fix**: Appended `.first()` to all multi-target locators in `frontend/e2e/smoke.spec.ts`.

### 10. Table Row Class Mismatch
- **Problem**: Tests looked for `.pz-table-row`, but `pz-table.component.html` and `suppliers-list.component.html` lacked that class.
- **Fix**: Added `class="pz-table-row"` to both templates.

### 11. ISO-8601 String Deserialization & Model Binding for DateOnly
- **Problem**: `System.Text.Json`'s default `DateOnly` converter and ASP.NET Core's `IParsable<DateOnly>` query model binder strictly accept `"yyyy-MM-dd"`, rejecting ISO-8601 timestamps like `"2026-10-08T00:00:00.000Z"` with `JsonException` and model binding validation errors. The NSwag generated TypeScript client serializes `Date` objects using `.toJSON()` / `.toISOString()`. When `POST /api/expenses` or `GET /api/expenses?from=&to=` was called, the backend returned HTTP 400 Bad Request, causing expense submission to fail, the creation modal to stay open, and the expenses table to remain empty.
- **Fix**: Added `FlexibleDateOnlyJsonConverter`, `FlexibleNullableDateOnlyJsonConverter`, `DateOnlyModelBinder`, and `DateOnlyModelBinderProvider` under `backend/src/StoreOS.Api/Serialization/` and registered them in `Program.cs`. Added regression integration tests verifying both ISO string JSON payloads and ISO query strings.

---

## 4. Current Pipeline State

All four pipeline stages in `.github/workflows/ci.yml` are currently passing green:
- **Job 1 (`backend-build-test`)**: Solution restore, Release build, 328 domain unit tests.
- **Job 2 (`frontend-build`)**: API build, NSwag OpenAPI client regeneration with zero diff/drift, production Angular PWA build.
- **Job 3 (`backend-integration-tests`)**: Postgres 16 & MinIO containers, schema migrations + RLS grants, all 458 integration & concurrency tests.
- **Job 4 (`e2e-tests`)**: Full stack boot, demo tenant seed, API launch under app role, all 14 Playwright smoke walkthrough tests executed in serial with 100% pass rate.
---

## 5. Verification Commands

```bash
# Check GitHub Actions runs:
gh run list --limit 5

# View failed logs for a run:
gh run view <run-id> --log-failed

# Run Playwright tests locally (ensure storeos-api is running on port 5000):
cd frontend && npx playwright test

# Run backend integration tests locally:
dotnet test backend/tests/StoreOS.IntegrationTests -c Release
```
