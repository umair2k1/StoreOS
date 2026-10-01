# P-803 — Multi-branch comparison report + consolidated totals

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-803** of the StoreOS Cloud build ("Multi-branch comparison report + consolidated totals") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-802 complete: daily sales report fully works for a single branch
  selection.
- No multi-branch COMPARISON view exists yet (Plan's roadmap Phase 6 item "dashboards
  (per-branch + consolidated + comparison)").

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Reporting/Compare/GetBranchComparisonQuery.cs`:

```csharp
public record GetBranchComparisonQuery(DateOnly Date) : IRequest<BranchComparisonDto>;

public record BranchRowDto(Guid BranchId, string BranchName, decimal Revenue, int InvoiceCount,
    decimal Profit, int ActiveRepairs);

public record BranchComparisonDto(IReadOnlyList<BranchRowDto> Branches, BranchRowDto Consolidated);
```

`GetBranchComparisonHandler` runs the SAME core aggregation logic as P-802's sales-summary
section (Revenue/InvoiceCount/Profit using snapshot costs) and P-801's active-repairs count,
PER BRANCH the caller has access to (respecting `AllBranches`/`BranchIds` from `IAccessScope` -
a single-branch user sees only their own branch's row and it happens to equal the
"consolidated" row trivially; a multi-branch/all-branches user sees every accessible branch
plus a `Consolidated` row summing them all). To avoid duplicating the summary-computation logic
between P-802 and this prompt, factor the CORE per-branch summary computation (revenue, invoice
count, profit, active repairs) into a small shared internal helper class
`StoreOS.Application/Reporting/ReportAggregationHelpers.cs` that BOTH `GetDailySalesReportHandler`
(retrofit it to use this) and `GetBranchComparisonHandler` call, ensuring the two reports can
never numerically disagree due to duplicated-and-drifted logic.

Add `GET /api/reports/compare?date=` to `ReportsController.cs`.

## 4. TASK

1. Extract the shared per-branch summary computation from P-802's handler into
   `StoreOS.Application/Reporting/ReportAggregationHelpers.cs`, and retrofit
   `GetDailySalesReportHandler` to call it (removing the duplicated inline logic).
2. Create `GetBranchComparisonQuery.cs`/handler/DTOs in
   `StoreOS.Application/Reporting/Compare/`, using the SAME shared helper, iterating over every
   branch the caller can access.
3. Add the route to `ReportsController.cs`.
4. Write `StoreOS.IntegrationTests/Reporting/BranchComparisonTests.cs`: (a) seed sales across
   TWO branches on the same date, confirm each branch's row is correct; (b) the `Consolidated`
   row EXACTLY equals the sum of the individual branch rows (Plan's testing-strategy table
   explicitly calls out "Consolidated report == sum of branch reports" as a required
   invariant test); (c) a user scoped to only ONE branch sees only that branch's row (and no
   `Consolidated` row distinct from it, or a `Consolidated` row identical to their one visible
   branch - choose and document one behavior clearly); (d) re-run `DailySalesReportTests`
   (P-802) to confirm the refactor introduced no regression.

## 5. HARD CONSTRAINTS

- The per-branch summary computation MUST be extracted into ONE shared helper
  used by BOTH P-802's daily report and this comparison report - these two numbers can never be
  allowed to drift apart due to independently-maintained duplicate logic.
- The consolidated-equals-sum-of-branches invariant is EXPLICITLY required by Plan Section 16's
  testing table - this test must exist and pass.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter BranchComparisonTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter DailySalesReportTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-803 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-803 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-803` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the shared `ReportAggregationHelpers` signature, the
`BranchComparisonDto` shape, the `GET /api/reports/compare` route, and confirmation the
consolidated-equals-sum invariant is tested and passes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-803: <one-line summary>"` only after verification is
   fully green.
