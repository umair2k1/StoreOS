# P-1007 — Onboarding wizard (shop name + branding prompt on first run)

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1007** of the StoreOS Cloud build ("Onboarding wizard (shop name + branding prompt on first run)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1006 complete: app shell with dynamic branding/branch-switcher
  works.
- No onboarding flow exists yet. Per Requirements Section 3.5/Plan Section 12.1, a brand-new
  tenant must be immediately prompted for its shop name on first login, before seeing any other
  screen.

## 3. CONTEXT BLOCK

Backend note: no dedicated "is this tenant onboarded yet" endpoint
exists in any prior prompt - infer onboarding status client-side from whether
`Tenant.Name` still equals a recognizable NOT-YET-SET sentinel. Since `Tenant.Create(name)`
(P-102) REQUIRES a non-empty name at creation time already (there's no "unnamed tenant" state
in the backend's current design), reconsider: the onboarding wizard's real job, given the
EXISTING backend, is less about detecting "no name set" and more about capturing the FULL set
of onboarding fields (logo, GSTIN, address, phone, receipt footer) that the tenant/branch
creation flow doesn't yet collect. Treat "onboarding" as: shown once per SESSION for a NEWLY
created tenant with no branches yet (`GET /api/branches` returns an empty list) - this is a
CLIENT-SIDE heuristic requiring no new backend endpoint, using data already available.

Create `frontend/src/app/features/onboarding/onboarding-wizard.component.ts`: a
multi-step form (shop name -> branch code/name -> optional logo/GSTIN/address/phone) that, on
submit, calls: `POST /api/branches` (P-310, already exists) to create the tenant's first
branch, and `PATCH /api/settings` (P-103's settings key `"shop_profile"`, needs a REST
endpoint - check `docs/ARTIFACTS.md`; if no generic settings PATCH endpoint exists yet, this
prompt must ALSO add one: `PATCH /api/settings/{key}` on `SettingsController.cs`,
`IRequiresPermission`, `settings:manage`, upserting a tenant-or-branch-scoped setting value)
to persist the shop-profile JSON (name mirror, address, phone, GSTIN, receipt footer).

Create a route guard `frontend/src/app/core/guards/onboarding.guard.ts` redirecting to
`/onboarding` when `GET /api/branches` returns zero branches for an authenticated user with
`branches:manage` permission (only an Owner/Admin should ever see the onboarding wizard; a
Cashier logging into an unfinished tenant setup sees a simple "ask your admin to finish setup"
message instead, not the wizard itself).

## 4. TASK

1. Add the missing generic `PATCH /api/settings/{key}` endpoint to the BACKEND's
   `SettingsController.cs` (a small backend addition within this frontend-focused prompt,
   since the onboarding flow cannot function without it) - `IRequiresPermission`,
   `settings:manage`, accepting a raw JSON body as the new setting value, upserting via
   `Setting.Create`/`UpdateValue` (P-103).
2. Write a backend integration test `StoreOS.IntegrationTests/Settings/PatchSettingTests.cs`
   confirming the upsert behavior (create-if-absent, update-if-present).
3. Create `OnboardingWizardComponent` (multi-step form) in
   `frontend/src/app/features/onboarding/`, calling `POST /api/branches` and
   `PATCH /api/settings/shop_profile` via the generated API client.
4. Create `onboardingGuard` exactly as described, and a simple
   `AwaitingSetupComponent` shown to non-admin users of an unfinished tenant.
5. Wire both into `app.routes.ts`.
6. Write `onboarding-wizard.component.spec.ts`: submitting the wizard form calls both expected
   API methods with correctly-shaped payloads (using HttpTestingController to intercept and
   assert, never hitting a real backend in this unit test). Write `onboarding.guard.spec.ts`:
   redirects appropriately based on branch count and permission.

## 5. HARD CONSTRAINTS

- The onboarding heuristic (zero branches = unonboarded) is a pragmatic
  CLIENT-SIDE inference given the existing backend schema, not a first-class backend concept -
  document this clearly, since a future enhancement might want a real
  `tenants.onboarding_completed` flag instead; this prompt does not add one, since it is
  out of the current manifest's explicit scope.
- Only a user with `branches:manage` sees the actual wizard; anyone else sees a simple
  "ask your admin" message, never a broken/confusing partial UI.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter PatchSettingTests
cd frontend
ng test --include='**/onboarding-wizard.component.spec.ts' --include='**/onboarding.guard.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1007 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1007 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1007` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the new `PATCH /api/settings/{key}` backend endpoint, the
`OnboardingWizardComponent`/`onboardingGuard`, and the documented client-side onboarding
heuristic.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1007: <one-line summary>"` only after verification is
   fully green.
