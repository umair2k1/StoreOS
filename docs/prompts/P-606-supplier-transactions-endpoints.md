# P-606 — Supplier transactions endpoints + payable derivation

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-606** of the StoreOS Cloud build ("Supplier transactions endpoints + payable derivation") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-605 complete: supplier transaction/ledger schema exists with documented
  polarity.
- No REST endpoints or a payable-calculation service exist yet.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Suppliers/ISupplierLedgerService.cs`
(mirroring `ICustomerLedgerService`'s pattern):

```csharp
namespace StoreOS.Application.Suppliers;

public interface ISupplierLedgerService
{
    Task RecordBillAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
    Task RecordPaymentAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
    Task RecordOpeningBalanceAsync(Guid supplierId, decimal amount, DateOnly entryDate, CancellationToken ct);
    Task RecordReturnCreditAsync(Guid supplierId, decimal amount, Guid? branchId, DateOnly entryDate, string? refId, CancellationToken ct);
    Task<decimal> GetPayableAsync(Guid supplierId, CancellationToken ct);
}
```

Implementation follows the EXACT polarity documented in P-605: `RecordBillAsync`/
`RecordOpeningBalanceAsync` create CREDIT entries (increase payable); `RecordPaymentAsync`/
`RecordReturnCreditAsync` create DEBIT entries (decrease payable). `GetPayableAsync` computes
`Math.Max(0, SUM(credit) - SUM(debit))` — using `IgnoreQueryFilters()` scoped to `tenant_id`
only for the AGGREGATE (mirroring the P-506/507 tenant-global pattern, since Requirements
Section 13.4 doesn't explicitly restrict supplier payables by branch visibility the way
customer credit is explicitly cross-branch, but for consistency and since suppliers are
tenant-wide master data per Plan Section 8.7, apply the SAME tenant-global aggregation pattern).

Create the vertical slice `StoreOS.Application/Suppliers/RecordTransaction/RecordSupplierTransactionCommand.cs`
(`IRequiresPermission`, `purchases:manage`): takes `SupplierId`, `Type`, `Amount`,
`PaymentMode`, `ReferenceNo`, `TxnDate`, `Notes`. Handler creates the `SupplierTransaction` row
AND posts the corresponding `ISupplierLedgerService` call based on `Type`
(`Bill`->`RecordBillAsync`, `Payment`->`RecordPaymentAsync`,
`OpeningBalance`->`RecordOpeningBalanceAsync`).

Create `GetSupplierPayable/GetSupplierPayableQuery.cs`
(`IRequest<SupplierPayableDto>` with `SupplierId, Balance` fields), calling
`ISupplierLedgerService.GetPayableAsync`.

Create `GetSupplierTransactions/GetSupplierTransactionsQuery.cs` (paginated list, filterable by
date range/type).

Create `SuppliersController.cs` (extend from P-411): `POST /api/suppliers/{id}/transactions`,
`GET /api/suppliers/{id}/ledger`, `GET /api/suppliers/{id}/transactions`.

## 4. TASK

1. Create `ISupplierLedgerService.cs` in `StoreOS.Application/Suppliers/` and its
   implementation in `StoreOS.Infrastructure/Suppliers/`, register scoped.
2. Create `RecordSupplierTransactionCommand`, `GetSupplierPayableQuery`,
   `GetSupplierTransactionsQuery` vertical slices exactly as described.
3. Extend `SuppliersController.cs` with the three new routes.
4. Write `StoreOS.IntegrationTests/Suppliers/SupplierTransactionsTests.cs`: (a) recording an
   `OpeningBalance` of 10000 -> `GetPayableAsync` returns 10000; (b) recording a `Bill` of 5000
   -> payable becomes 15000; (c) recording a `Payment` of 8000 -> payable becomes 7000; (d) the
   formula EXACTLY matches Requirements Section 13.4: `Payable = Opening + Bills - Payments -
   Credits` (compute a multi-transaction scenario and assert the exact final number); (e) a
   caller without `purchases:manage` -> 403; (f) listing transactions with a date-range filter
   works correctly.

## 5. HARD CONSTRAINTS

- `GetPayableAsync` is the ONE sanctioned way to compute a supplier's
  payable anywhere in this codebase — P-608 (supplier returns) and P-905 (reconciliation) both
  must call this SAME method, never re-derive the sum independently.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter SupplierTransactionsTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-606 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-606 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-606` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ISupplierLedgerService` interface (all 5 methods), the three new
vertical-slice shapes, and the `SuppliersController` route additions.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-606: <one-line summary>"` only after verification is
   fully green.
