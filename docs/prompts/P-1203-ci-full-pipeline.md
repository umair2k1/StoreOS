# P-1203 — CI: wire integration/concurrency/RLS schema tests + Testcontainers into pipeline

**Phase:** 12 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1203** of the StoreOS Cloud build ("CI: wire integration/concurrency/RLS schema tests + Testcontainers into pipeline") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1202 complete: the full E2E smoke suite passes against a live stack.
- The CI pipeline (P-005, extended minimally in P-1001/1007) still only runs backend
  build+domain-tests and a basic frontend build - it does NOT yet run integration tests,
  concurrency tests, the RLS schema-coverage test, or the E2E suite.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the e2e job now migrates as the OWNER role, runs `deploy/db/apply-post-migrate.sh`, seeds with the owner connection, and starts the API with the app role (`storeos_app`) and a readiness wait, otherwise Playwright starts before the API is up and RLS would silently be bypassed. `dotnet-ef` must be a LOCAL tool: add `.config/dotnet-tools.json` (created in P-004; if missing create it with `dotnet new tool-manifest` and `dotnet tool install dotnet-ef --version <same 10.0.x minor as EF Core in P-004>`), so `dotnet tool restore` works on clean runners. The tester users log in on `http://apexci.localhost:4200` (see P-1008/P-1010) or through the API with `Host: apexci.localhost`.

Extend `.github/workflows/ci.yml` with the full pipeline described in
Plan Section 14.2:

```yaml
  backend-integration-tests:
    runs-on: ubuntu-latest
                 # pre-installed and available to the job by default, no explicit service block
                 # strictly required - remove this placeholder service block and rely on the
                 # runner's built-in Docker daemon instead, which is the correct approach.
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - run: dotnet restore StoreOS.sln
      - run: dotnet build StoreOS.sln --no-restore -c Release
      - run: dotnet test backend/tests/StoreOS.IntegrationTests --no-build -c Release --logger "trx;LogFileName=integration-tests.trx"
      - run: dotnet test backend/tests/StoreOS.ConcurrencyTests --no-build -c Release --logger "trx;LogFileName=concurrency-tests.trx"
      - uses: actions/upload-artifact@v4
        if: always()
        with: { name: integration-test-results, path: "**/*-tests.trx" }

  e2e-tests:
    runs-on: ubuntu-latest
    needs: [backend-integration-tests, frontend-build]
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "10.0.x" }
      - uses: actions/setup-node@v4
        with: { node-version: "24" }
      - run: docker compose -f deploy/docker-compose.yml up -d postgres minio otel-collector
      - name: Migrate (owner role) and apply grants
        env:
          STOREOS_MIGRATIONS_CONNECTION: "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password"
          STOREOS_MIGRATIONS_CONNECTION_URI: "postgresql://storeos:storeos_dev_password@localhost:5432/storeos"
        run: |
          dotnet tool restore
          dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
          sh deploy/db/apply-post-migrate.sh
      - name: Seed demo tenant
        env: { STOREOS_MIGRATIONS_CONNECTION: "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password" }
        run: dotnet run --project tools/seed -- --tenant-name "Apex Mobiles Demo CI" --subdomain apexci
      - name: Start API (app role) and wait until ready
        run: |
          nohup dotnet run --project backend/src/StoreOS.Api --urls http://localhost:5000 > api.log 2>&1 &
          for i in $(seq 1 60); do curl -fsS http://localhost:5000/health/ready && break; sleep 2; done
          curl -fsS http://localhost:5000/health/ready
      - run: npm ci --prefix frontend && npx --prefix frontend playwright install --with-deps chromium
      - run: npm --prefix frontend run build -- --configuration production
      - run: npx --prefix frontend playwright test
```

(The GitHub-hosted runner already provides Docker for Testcontainers; there is no `services:` block.)

## 4. TASK

1. Extend `.github/workflows/ci.yml` with the `backend-integration-tests` and
   `e2e-tests` jobs exactly as shown (with the corrected removal of the placeholder Docker
   service block), wiring proper job dependencies (`needs:`) so E2E only runs after integration
   tests and the frontend build both succeed.
2. Confirm `RlsCoverageTests` (P-111) and the various `*ConcurrencyTests` classes are picked up
   automatically by the `dotnet test backend/tests/StoreOS.IntegrationTests`/
   `StoreOS.ConcurrencyTests` calls (they should be, as ordinary test classes in those
   projects - no special wiring needed beyond what's already there).
3. Validate the complete YAML file with a linter (`actionlint` if available, or the
   `python3 -c "import yaml..."` check from P-005) to confirm correct syntax.
4. Document in `docs/BUILD_LOG.md` that the full CI pipeline (build, domain tests, integration
   tests with Testcontainers, concurrency tests, RLS schema coverage, frontend build, E2E smoke)
   is now wired end-to-end.

## 5. HARD CONSTRAINTS

- The `e2e-tests` job must depend on (`needs:`) both `backend-integration-tests`
  and `frontend-build` succeeding first - never run the expensive E2E suite before cheaper
  checks have already passed.
- Every test class written in any earlier prompt (RlsCoverageTests, all *ConcurrencyTests, every
  domain/integration test) must now be genuinely exercised by this CI pipeline - this is the
  point where "does it actually all run together in CI" gets proven, not just asserted.

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
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/ci.yml')); print('YAML OK')"
# Manually trigger or push to verify the full pipeline runs green end-to-end in GitHub Actions
# (or an equivalent local `act`-based dry run if available in this environment).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1203 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1203 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1203` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete final `.github/workflows/ci.yml` job graph (build ->
domain-tests / frontend-build -> backend-integration-tests -> e2e-tests), confirming every test
class from every earlier prompt is now exercised in CI.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1203: <one-line summary>"` only after verification is
   fully green.
