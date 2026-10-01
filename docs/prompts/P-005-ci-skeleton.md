# P-005 — GitHub Actions CI skeleton (build+test only)

**Phase:** 0 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-005** of the StoreOS Cloud build ("GitHub Actions CI skeleton (build+test only)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-001–004 complete: solution builds, packages pinned centrally, docker dev
  stack works.
- No CI pipeline exists yet. This prompt creates the skeleton; P-1203 later extends it with
  integration/concurrency/RLS/E2E stages once those tests exist.

## 3. CONTEXT BLOCK

Create `.github/workflows/ci.yml`:

```yaml
name: CI

on:
  push:
    branches: ["**"]
  pull_request:
    branches: ["**"]

jobs:
  backend-build-test:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with:
          dotnet-version: "10.0.x"
      - name: Restore
        run: dotnet restore StoreOS.sln
      - name: Build
        run: dotnet build StoreOS.sln --no-restore -c Release
      - name: Unit tests (Domain)
        run: dotnet test backend/tests/StoreOS.Domain.Tests --no-build -c Release --logger "trx;LogFileName=domain-tests.trx"
      - name: Upload test results
        if: always()
        uses: actions/upload-artifact@v4
        with:
          name: domain-test-results
          path: "**/domain-tests.trx"

  frontend-build:
    runs-on: ubuntu-latest
    if: false  # flipped to true in P-1203 once frontend/package.json exists
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-node@v4
        with:
          node-version: "24"
      - run: npm ci --prefix frontend
      - run: npm run build --prefix frontend -- --configuration production
```

Note: `frontend-build` is deliberately gated `if: false` because `frontend/` has no
`package.json` yet (created in P-006/P-007). P-007 flips this to `if: true`.

## 4. TASK

1. Create `.github/workflows/ci.yml` exactly as shown, with the solution path adjusted
   to match wherever P-001 actually placed `StoreOS.sln` (repo root, per P-001's commands).
2. Validate the YAML is syntactically correct (no tabs, correct indentation) by running a
   YAML linter if available (`python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml'))"`
   must not raise).
3. Do not push to GitHub or attempt to trigger Actions in this prompt — verification below
   is local only.

## 5. HARD CONSTRAINTS

- Do not enable the `frontend-build` job yet (`if: false` must stay
  until P-007 explicitly flips it) — Angular workspace does not exist yet, so enabling it now
  would always fail once real CI runs.
- Do not add integration/concurrency test jobs yet — those test projects need Testcontainers
  and are wired into CI in P-1203, not here.

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
python3 -c "import yaml,sys; yaml.safe_load(open('.github/workflows/ci.yml')); print('YAML OK')"
# Expected: YAML OK
dotnet restore StoreOS.sln && dotnet build StoreOS.sln -c Release
dotnet test backend/tests/StoreOS.Domain.Tests -c Release
# Expected: both succeed locally, mirroring what CI will run (0 tests yet is fine — the
# xunit template's default test class counts as a pass).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-005 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-005 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-005` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact CI job names (`backend-build-test`, `frontend-build`) and note that
`frontend-build` remains disabled until P-007.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-005: <one-line summary>"` only after verification is
   fully green.
