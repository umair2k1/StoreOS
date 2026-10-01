# P-1205 — Final Completeness Audit + Remediation Templates

**Phase:** 12 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1205** of the StoreOS Cloud build ("Final Completeness Audit + Remediation Templates") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- ALL 95 prior prompts (P-001 through P-1204) are complete. Every backend
  module, every frontend screen, CI, E2E, and the restore drill all exist and are individually
  tested and green.
- This is the FINAL prompt in the entire build sequence: a full-repository completeness audit
  proving zero stubs, zero placeholders, full build/test/E2E success, and a walkthrough of
  every flow from the master prompt's Section 2 against the seeded tenant.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the audit must ALSO run `python prompts/tools/verify_prompts.py`-style checks against the finished repository where they apply, and add these rows to the audit table: (1) `SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user` on the API connection is `f | f` (P-108 `RlsRoleGuard` test); (2) no `BeginTransaction` outside `TransactionBehavior`/`TenantJobRunner`; (3) no reference from `StoreOS.Application` to `StoreOS.Infrastructure` (ArchitectureTests, P-114); (4) `deploy/db/post-migrate.sql` applied in every environment; (5) invoice numbers: tax series consecutive per branch and FY, estimates in their own series (D-07); (6) toolchain decision O-03 recorded in `docs/BUILD_LOG.md`. The audit counts 157 prompts, not 155.

Perform, in order, EVERY check below and record a PASS/FAIL table in
`docs/COMPLETENESS_AUDIT.md`:

**1. Forbidden-pattern grep** across the ENTIRE repository (excluding `node_modules/`, `bin/`,
`obj/`, `.git/`, generated files like `generated-client.ts`):
```bash
grep -rn "TODO\|FIXME\|HACK\|XXX" --include="*.cs" --include="*.ts" --include="*.html" backend/ frontend/src tools/ | grep -v generated-client.ts
grep -rn "NotImplementedException\|NotSupportedException" backend/src backend/tests tools/
grep -rn "// similar to above\|// rest omitted\|for brevity" backend/ frontend/src tools/
grep -rn "coming soon" frontend/src
```
Every one of these must return ZERO results (excluding this audit document's own text
discussing the rule, and any legitimate business-domain use of a word like "hack" in an
unrelated context, which should not occur here anyway).

**2. Full backend build + test**:
```bash
dotnet build StoreOS.sln -c Release
dotnet test backend/tests/StoreOS.Domain.Tests -c Release
dotnet test backend/tests/StoreOS.IntegrationTests -c Release
dotnet test backend/tests/StoreOS.ConcurrencyTests -c Release
dotnet test tools/pz-migrate.Tests -c Release
dotnet test tools/seed.Tests -c Release
```
Every command must succeed with 0 failures.

**3. Full frontend build + unit tests**:
```bash
cd frontend && ng build --configuration production && ng test --watch=false
```

**4. docker-compose full stack boot**:
```bash
docker-compose -f deploy/docker-compose.yml --profile full up -d
docker-compose -f deploy/docker-compose.yml ps # all healthy
curl -sf http://localhost:5000/health/ready
curl -sf http://localhost:8081/
```

**5. E2E walkthrough**: re-run the full P-1202 Playwright suite one final time against the
complete, final codebase.

**6. Manifest coverage re-verification**: re-read `MANIFEST.md`'s coverage checklist and
confirm, item by item, that the corresponding prompt(s) were actually completed with matching
artifacts in `docs/ARTIFACTS.md` - flag ANY discrepancy found.

**7. RLS 100% coverage**: re-run `RlsCoverageTests` one final time as the definitive proof.

Produce `docs/COMPLETENESS_AUDIT.md` with a table: `Check | Result | Notes`, one row per item
above (expand item 6 into one row per manifest coverage-checklist section).

## 4. TASK

1. Run every check listed above, in order, against the complete, final
   repository.
2. Produce `docs/COMPLETENESS_AUDIT.md` with the full PASS/FAIL table.
3. For any FAIL discovered, do NOT attempt to silently patch it inline as part of this audit
   prompt - instead, document the exact gap precisely (which file, which behavior, which test
   failed and why) in the audit document, to be closed by a dedicated Remediation Prompt (see
   the 5 templates appended to this manifest) rather than an ad-hoc fix bolted onto an audit
   step.
4. If ALL checks pass, the audit document's final line reads:
   `OVERALL RESULT: PASS - StoreOS Cloud build complete per master prompt Section 2.`
   If ANY check fails, it reads:
   `OVERALL RESULT: FAIL - N gap(s) found, see table above. Use Remediation Prompt Templates
   to close each gap.`

## 5. HARD CONSTRAINTS

- This prompt NEVER patches a discovered gap inline - its sole job is
  DETECTION and DOCUMENTATION. Fixing any gap found here is explicitly the job of a dedicated
  follow-up Remediation Prompt (using the templates below), keeping the audit's own scope
  honest and bounded.
- Every one of the 7 numbered check categories must actually be RUN, not assumed - this is a
  real, executed audit, not a paperwork exercise.

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
cat docs/COMPLETENESS_AUDIT.md
# Expected: a complete PASS/FAIL table with a final OVERALL RESULT line.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1205 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1205 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1205` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete `docs/COMPLETENESS_AUDIT.md` PASS/FAIL table and the final
OVERALL RESULT line.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1205: <one-line summary>"` only after verification is
   fully green.
