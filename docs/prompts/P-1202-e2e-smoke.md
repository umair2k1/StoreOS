# P-1202 — E2E smoke test suite (Playwright): full Section 2 walkthrough

**Phase:** 12 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1202** of the StoreOS Cloud build ("E2E smoke test suite (Playwright): full Section 2 walkthrough") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1201 complete: the demo tenant seed script works and populates a
  realistic, fully-featured dataset.
- Playwright is installed (P-007) but `frontend/e2e/` has no real spec files yet - only the
  directory and a minimal `playwright.config.ts` exist.

## 3. CONTEXT BLOCK

Create `frontend/e2e/smoke.spec.ts` walking through EVERY flow listed
in the master prompt's Section 2 "Definition of a Complete Product," item 4, against the SEEDED
demo tenant from P-1201, using Playwright's real browser automation against a FULLY RUNNING
stack (API + Postgres + frontend dev server, or the docker-compose `full` profile):

```typescript
import { test, expect } from '@playwright/test';

test.describe('StoreOS full smoke walkthrough', () => {
  test('login -> checkout new phone with IMEI', async ({ page }) => {
    await page.goto('/login');
    await page.fill('[formcontrolname="email"]', 'owner@apexmobiles.demo');
    await page.fill('[formcontrolname="password"]', 'Demo@12345');
    await page.fill('[formcontrolname="tenantId"]', process.env.SEED_TENANT_ID!);
    await page.click('button[type="submit"]');
    await expect(page).toHaveURL(/\/pos/);
    // ... search for a seeded NewPhone, select its IMEI, checkout with cash, assert invoice
    // number appears.
  });

  test('sell used phone under margin scheme', async ({ page }) => { /* ... */ });
  test('credit sale + record subsequent payment', async ({ page }) => { /* ... */ });
  test('void a sale', async ({ page }) => { /* ... */ });
  test('refund a sale', async ({ page }) => { /* ... */ });
  test('purchase / stock-in', async ({ page }) => { /* ... */ });
  test('supplier return', async ({ page }) => { /* ... */ });
  test('record an expense', async ({ page }) => { /* ... */ });
  test('liability + repayment', async ({ page }) => { /* ... */ });
  test('full repair job lifecycle + billing', async ({ page }) => { /* ... */ });
  test('dashboard numbers reconcile with seeded data', async ({ page }) => { /* ... */ });
  test('daily audit report generates correctly', async ({ page }) => { /* ... */ });
  test('GSTR-1 export downloads', async ({ page }) => { /* ... */ });
  test('JSON backup export then import round-trips', async ({ page }) => { /* ... */ });
});
```

Write out EVERY test body completely and specifically against the ACTUAL component
structure/selectors from Phase 11's prompts (form control names, button labels, route paths) -
do not leave any test body as a comment placeholder; every one must be a real, complete
Playwright script exercising the real UI end-to-end.

## 4. TASK

1. Write the COMPLETE `frontend/e2e/smoke.spec.ts` with every one of the 14
   listed test bodies fully implemented (not commented placeholders), using real selectors
   matching the actual Phase 11 component templates.
2. Create a `frontend/e2e/global-setup.ts` that runs the P-1201 seed script (or calls a seed
   API endpoint if one is more practical to invoke from Node) before the test suite runs,
   capturing the seeded tenant id into an environment variable the specs read.
3. Update `playwright.config.ts` to reference the global setup and point at the correct
   `baseURL` (the locally-running frontend dev server or docker-compose `full` profile's web
   service).
4. Run the FULL suite locally against a real running stack.

## 5. HARD CONSTRAINTS

- Every one of the 14 test bodies must be a complete, real Playwright script -
  no commented-out placeholders, no `test.skip`, no empty test bodies. This is the definitive
  end-to-end proof the whole product works together.

Global invariants (apply to all backend code in this repo):

```
- Money: C# decimal ONLY. Never double/float. numeric(18,2) in Postgres.
  Round half-up: MidpointRounding.AwayFromZero, 2 dp, via Money.Round2.
- EMI uses pure decimal exponentiation (DecimalPow loop) — Math.Pow is forbidden.
- Timestamps: timestamptz, stored UTC. Business date computed in tenant timezone
  (Asia/Kolkata default).
- Every tenant table has tenant_id; indexes lead with tenant_id (then branch_id).
- All DB access happens inside transactions with SET LOCAL app.tenant_id / app.branch_ids /
  app.all_branches (RLS session scope). No query outside this scope.
- Validation failures -> 400 with field problems; domain violations -> DomainException -> 422
  with code+message. Never a 500 for business rule violations.
- Authorization runs in MediatR pipeline behaviors, never controllers, never frontend.
- Server recomputes all prices/taxes/totals from DB master data; client-submitted amounts
  are ignored unless an override permission exists.
- Historical snapshots on sale lines are immutable; ledgers and audit_log are append-only.
- tenant_id-scoped RLS must be active on every table containing tenant_id.
- Namespace convention: StoreOS.* everywhere (StoreOS.Domain, StoreOS.Application,
  StoreOS.Infrastructure, StoreOS.Api, StoreOS.Modules). Never PhoneZone.*.
- Layering: StoreOS.Application NEVER references StoreOS.Infrastructure. Application code reaches
  the database only through IAppDbContext and reaches infrastructure services only through
  interfaces declared in StoreOS.Application/Abstractions (see _shared/CONTRACTS.md).
- Transactions: exactly ONE transaction per MediatR request, opened by TransactionBehavior
  (Infrastructure). Handlers never call BeginTransaction/Commit/Rollback. Serialization-failure
  retry wraps the WHOLE transaction (SerializationRetryBehavior is the outermost behavior).
- RLS: policies read session variables ONLY via NULLIF(current_setting('app.x', true), '') and
  never contain an "IS NULL" bypass. Queries implement IReadOnlyRequest, report queries
  IReportingRequest, Serializable commands ISerializableRequest (Application/Abstractions).
```

## 6. FORBIDDEN PATTERNS

```
FORBIDDEN in all code you produce:
- TODO / FIXME / HACK / XXX comments
- NotImplementedException / NotSupportedException / "not implemented" exceptions
- Empty method bodies; methods returning null/0/"" as placeholders for real logic
- "// similar to above", "// rest omitted", "for brevity", ellipses (...) inside code
- Mock/fake/in-memory service implementations in production code (test projects ONLY)
- Hardcoded demo data in production code paths
- Angular: "coming soon" templates, buttons with empty (click) handlers, services
  returning of(mockData) instead of real HTTP calls
If you cannot fully implement something: STOP and write "BLOCKED: <reason>" as the first line below the `---`
separator in docs/BUILD_LOG.md. Never silently stub. Before finishing, grep your created/modified files
for the forbidden patterns above and fix every hit before running verification.
```

## 7. VERIFICATION

```bash
cd store-os-cloud
docker-compose -f deploy/docker-compose.yml --profile full up -d
dotnet run --project tools/seed -- --tenant-name "Apex Mobiles Demo"
cd frontend && npx playwright test
# Expected: all 14 smoke tests pass against the fully running stack.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1202 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1202 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1202` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: confirmation all 14 E2E smoke tests are fully implemented (not
placeholders) and pass against the live seeded stack, plus the `global-setup.ts` seeding
mechanism.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1202: <one-line summary>"` only after verification is
   fully green.
