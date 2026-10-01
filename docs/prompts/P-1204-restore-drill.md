# P-1204 — Backup restore drill automation (monthly CI/staging job)

**Phase:** 12 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1204** of the StoreOS Cloud build ("Backup restore drill automation (monthly CI/staging job)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1203 complete: the full CI pipeline runs green end-to-end including
  E2E smoke tests.
- No automated restore-drill exists yet (Plan Section 14.3's "Monthly Restore Drill in CI/
  Staging" and Plan Section 19's risk mitigation table entry).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the restore drill restores the dump into a scratch database with the OWNER role, runs `deploy/db/post-migrate.sql`, and then runs the reconciliation assertions through `ITenantJobRunner` / the app role so that RLS is exercised exactly as in production.

Create `.github/workflows/restore-drill.yml`:

```yaml
name: Monthly Restore Drill

on:
  schedule:
    - cron: "0 3 1 * *" # 03:00 UTC on the 1st of each month
  workflow_dispatch: {} # allow manual trigger too

jobs:
  restore-drill:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v4
      - uses: actions/setup-dotnet@v4
        with: { dotnet-version: "8.0.x" }
      - run: docker compose -f deploy/docker-compose.yml up -d postgres minio
      - run: dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
      - run: dotnet run --project tools/seed -- --tenant-name "Restore Drill Seed"
      - name: Export backup
        run: dotnet run --project backend/src/StoreOS.Api -- --export-backup-and-exit
          # (a small CLI-mode addition to StoreOS.Api, OR reuse the ExportTenantDataCommand
          # via a lightweight standalone runner script - implement whichever is more practical;
          # document the choice.)
      - name: Wipe database
        run: docker compose -f deploy/docker-compose.yml down -v && docker compose -f deploy/docker-compose.yml up -d postgres
      - run: dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
      - name: Restore from backup
        run: dotnet run --project backend/src/StoreOS.Api -- --import-backup-and-exit
      - name: Reconciliation assertions
        run: dotnet test backend/tests/StoreOS.IntegrationTests --filter RestoreDrillReconciliationTests
      - name: Alert on failure
        if: failure()
        run: echo "RESTORE DRILL FAILED - see job logs" # placeholder for real alerting
          # integration (Slack/email webhook), which requires secrets not available in this
          # environment - documented as a deployment-configuration follow-up.
```

Create `StoreOS.IntegrationTests/Platform/RestoreDrillReconciliationTests.cs`: asserts, after a
real export-then-wipe-then-import cycle against a running Postgres instance, that row counts,
ledger sums, and stock quantities in the restored database EXACTLY match a snapshot taken before
the wipe (reusing/extending P-902's round-trip test logic, but run here against the CLI
export/import paths specifically rather than the HTTP endpoints, to also exercise any CLI-mode
wiring added for this drill).

## 4. TASK

1. Add a minimal `--export-backup-and-exit`/`--import-backup-and-exit` CLI mode
   to `StoreOS.Api`'s `Program.cs` (checked at startup before the web host runs), reusing
   `ExportTenantDataCommand`/`ImportTenantDataCommand`'s handlers directly via a manually
   constructed `IServiceProvider` scope, writing to/reading from a fixed local file path for this
   drill's purposes.
2. Create `.github/workflows/restore-drill.yml` exactly as shown, with the alerting step
   honestly documented as a placeholder pending real notification-webhook secrets (a deployment
   concern outside this manifest's scope).
3. Write `RestoreDrillReconciliationTests.cs` as described.
4. Manually trigger the workflow (`workflow_dispatch`) once to confirm it runs successfully.

## 5. HARD CONSTRAINTS

- The alerting-on-failure step is honestly documented as a placeholder for real
  webhook/secret-based notification, which requires deployment-environment configuration outside
  this manifest's scope - this is not a stub of BUSINESS LOGIC (the actual drill mechanics are
  fully implemented and tested), it is an honestly-scoped limitation.

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
python3 -c "import yaml; yaml.safe_load(open('.github/workflows/restore-drill.yml')); print('YAML OK')"
dotnet test backend/tests/StoreOS.IntegrationTests --filter RestoreDrillReconciliationTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1204 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1204 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1204` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the CLI export/import mode added to `StoreOS.Api`, the
`restore-drill.yml` workflow, and the honestly-scoped alerting-placeholder note.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1204: <one-line summary>"` only after verification is
   fully green.
