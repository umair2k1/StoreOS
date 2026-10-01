# P-007 — Angular npm package pinning

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-007** of the StoreOS Cloud build ("Angular npm package pinning") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-006 complete: `frontend/` is a working Angular 21 standalone PWA workspace
  named `storeos-web`, building successfully to `dist/storeos-web/browser`.
- P-003's `Dockerfile.web` build was deferred until this prompt exists (per P-003's report).
- P-005's CI `frontend-build` job is currently disabled (`if: false`).

## 3. CONTEXT BLOCK

Pin these exact additional dependencies in `frontend/package.json` (in
addition to whatever `ng new`/`ng add @angular/pwa` already added — do not remove those,
only add the ones below with `npm install --save-exact <pkg>@<version>`):

```text
rxjs@7.8.1                     (likely already present from ng new — confirm exact version)
zone.js@0.14.10                (likely already present — confirm)
```

Dev dependencies to add:

```text
@playwright/test@1.48.2
```

No other runtime dependency (chart libs, UI kits, etc.) is added in this prompt — individual
Phase 11 feature prompts will name exact packages as they are needed (e.g. a charting
library for the 7-day dashboard chart), each pinned explicitly in that prompt's own
CONTEXT BLOCK, and each addition must also be reflected back into this file's dependency
list at that time.

Set `"engines"` in `package.json`:

```json
"engines": {
  "node": ">=24.0.0",
  "npm": ">=10.0.0"
}
```

## 4. TASK

1. Install `@playwright/test@1.48.2` as an exact-pinned devDependency:
   `npm install --save-exact --save-dev @playwright/test@1.48.2` inside `frontend/`.
2. Add the `"engines"` block to `frontend/package.json` exactly as shown.
3. Confirm `rxjs` and `zone.js` versions already present match the CONTEXT BLOCK's stated
   versions; if the CLI installed different exact versions, pin them explicitly with
   `npm install --save-exact rxjs@7.8.1 zone.js@0.14.10` to match.
4. Run `npm ci` to confirm the lockfile is consistent, then `npm run build -- --configuration
   production` to confirm the build still succeeds.
5. Now that `frontend/package.json` exists with a working build, build the web Docker image
   deferred from P-003: `docker build -f deploy/Dockerfile.web -t storeos-web:dev .` from the
   repo root. Fix any path mismatch in `Dockerfile.web` if the actual `dist/` output
   directory differs from what P-003 assumed (it should match `dist/storeos-web/browser`
   exactly per P-006).
6. Flip `.github/workflows/ci.yml`'s `frontend-build` job from `if: false` to `if: true`
   (remove the `if:` line entirely, which defaults to always-run).
7. Initialize Playwright config: `npx playwright install --with-deps chromium` and create a
   minimal `frontend/playwright.config.ts` pointing `testDir` at `frontend/e2e` (directory
   created now, populated later in P-1202).

## 5. HARD CONSTRAINTS

- Every dependency added must use `--save-exact` (no caret/tilde
  ranges) — this repo pins exact versions everywhere, frontend included.
- Do not upgrade any Angular package to a version different from what `ng new`/`ng add`
  installed in P-006.

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
cd store-os-cloud/frontend
npm ci
npm run build -- --configuration production
cd ..
docker build -f deploy/Dockerfile.web -t storeos-web:dev .
# Expected: image builds successfully
python3 -c "import yaml; d=yaml.safe_load(open('.github/workflows/ci.yml')); assert d['jobs']['frontend-build'].get('if') in (None, True, 'true')"
# Expected: no assertion error
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-007 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-007 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-007` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the final pinned versions of `rxjs`, `zone.js`, `@playwright/test`, and
confirmation that `Dockerfile.web` now builds successfully; the exact `playwright.config.ts`
`testDir` path.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-007: <one-line summary>"` only after verification is
   fully green.
