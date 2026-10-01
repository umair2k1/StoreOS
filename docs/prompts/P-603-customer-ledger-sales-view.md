# P-603 — GET /customers/{id}/ledger + /sales

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-603** of the StoreOS Cloud build ("GET /customers/{id}/ledger + /sales") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-602 complete: customer CRUD/directory works.
- No detail-view sub-endpoints exist yet for a customer's full ledger and sales history
  (Requirements Section 17.4).

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Customers/GetCustomerLedger/GetCustomerLedgerQuery.cs`:

```csharp
public record GetCustomerLedgerQuery(Guid CustomerId, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<CustomerLedgerEntryDto>>;

public record CustomerLedgerEntryDto(long Id, string EntryType, decimal Debit, decimal Credit,
    Guid? BranchId, string? BranchName, DateOnly EntryDate, string? RefId);
```

This is a DETAIL VIEW (per-invoice branch attribution, per Plan Section 2 Decision 4), so it
uses the NORMAL branch-filtered query path (NOT the `IgnoreQueryFilters()` tenant-only balance
path from P-507) — a branch-scoped viewer sees only entries stamped with branches they have
access to, consistent with the P-506 "two-query-pattern" design. Join to `branches` for
`BranchName` display (nullable if a `NULL`-branch entry exists, e.g. a legacy opening balance).

Create `GetCustomerSales/GetCustomerSalesQuery.cs`
(`IRequest<PagedResult<SaleSummaryDto>>` — reuse `SaleSummaryDto` from P-521, filtering
`sales.customer_id == request.CustomerId`).

Extend `CustomersController.cs`: `GET /api/customers/{id}/ledger`,
`GET /api/customers/{id}/sales`.

## 4. TASK

1. Create `GetCustomerLedgerQuery.cs`/handler/`CustomerLedgerEntryDto` in
   `StoreOS.Application/Customers/GetCustomerLedger/`.
2. Create `GetCustomerSalesQuery.cs`/handler in `StoreOS.Application/Customers/GetCustomerSales/`
   reusing `SaleSummaryDto` from P-521 (import/reference it, do not redefine).
3. Extend `CustomersController.cs` with both routes.
4. Write `StoreOS.IntegrationTests/Customers/CustomerLedgerAndSalesViewTests.cs`: (a) ledger
   view correctly shows branch attribution per entry; (b) a branch-A-scoped user viewing a
   tenant-wide customer's ledger sees ONLY branch-A-stamped entries (contrast explicitly with
   P-601's balance endpoint, which sums ALL branches regardless of viewer scope — write a
   combined test showing BOTH endpoints against the SAME seeded multi-branch customer,
   demonstrating the deliberate difference in scoping between the two); (c) sales view
   correctly lists only that customer's sales, paginated.

## 5. HARD CONSTRAINTS

- This is the DETAIL view (branch-filtered) — contrast explicitly
  with P-601's balance view (tenant-global, `IgnoreQueryFilters()`). Both are correct for their
  respective purposes; do not accidentally swap the filtering behavior between the two
  endpoints.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CustomerLedgerAndSalesViewTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-603 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-603 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-603` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CustomerLedgerEntryDto` shape and both new
`CustomersController` routes, with the explicit branch-filtered-vs-tenant-global contrast
documented against P-601.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-603: <one-line summary>"` only after verification is
   fully green.
