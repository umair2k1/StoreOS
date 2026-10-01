# P-1119 — Reports screen: dashboard widgets, daily audit, comparison, exports

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1119** of the StoreOS Cloud build ("Reports screen: dashboard widgets, daily audit, comparison, exports") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1118 complete: full repairs UI works.
- GET /api/reports/dashboard, /daily-sales, /compare, /api/exports/inventory, /gstr1 (P-801-805)
  all exist. GET .../invoice.pdf-style report PDFs (P-808/809 equivalents for reports) exist for
  printing.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/reports/dashboard-widgets.component.ts` (rendered on the
app's landing/home route): today's revenue/invoice-count cards, a 7-day revenue line chart
(using a charting library - add `chart.js` v4.4.6 exact-pinned, per the allowlist note in P-007
that Phase-11 prompts may add exactly-pinned charting dependencies as needed, updating
`package.json` accordingly), low-stock alert list, top-5-sellers list, active-repairs/
ready-for-pickup counts (hidden entirely if the repairs feature is disabled). Create
`frontend/src/app/features/reports/daily-audit.component.ts` (date picker + full report display
matching every DailySalesReportDto section, with a "Print" button fetching the report PDF).
Create `frontend/src/app/features/reports/branch-comparison.component.ts` (visible only for
multi-branch/all-branches users, per Plan Section 12.3). Create
`frontend/src/app/features/reports/exports.component.ts` (category-multiselect inventory export
download, GSTR-1 date-range export download).

## 4. TASK

1. Add `chart.js@4.4.6` (exact) to `frontend/package.json`.
2. Create all four report components exactly as described.
3. Write `.spec.ts` for each: dashboard widgets correctly hide repair-related sections when the
   feature flag is disabled; daily-audit renders every report section; branch-comparison is
   hidden for a single-branch user (checking `AuthSignalStore.allBranches()`/`branchIds()
   .length`); exports trigger the correct download endpoints with selected parameters.

## 5. HARD CONSTRAINTS

- Dashboard repair widgets are ENTIRELY hidden (not just zeroed-out) when the repairs
  feature is disabled, for a cleaner UI.
- Branch comparison is hidden, not just empty, for single-branch users.

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
ng test --include='**/dashboard-widgets.component.spec.ts' --include='**/daily-audit.component.spec.ts' --include='**/branch-comparison.component.spec.ts' --include='**/exports.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1119 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1119 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1119` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: all four report component structures and the `chart.js` version pin.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1119: <one-line summary>"` only after verification is
   fully green.
