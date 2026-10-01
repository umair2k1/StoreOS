# P-507 — ICustomerLedgerService (RecordInvoiceDebitAsync etc.)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-507** of the StoreOS Cloud build ("ICustomerLedgerService (RecordInvoiceDebitAsync etc.)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-506 complete: `Customer`/`CustomerLedgerEntry` schema exists.
- No cross-module service exists yet for posting ledger entries — per Plan §5.1, Sales
  checkout must call a typed internal service (`ICustomerLedgerService`), never write to
  `customer_ledger_entries` directly.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Customers/ICustomerLedgerService.cs`:

```csharp
namespace StoreOS.Application.Customers;

public interface ICustomerLedgerService
{
    Task RecordInvoiceDebitAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task RecordPaymentCreditAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task RecordVoidReversalAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task RecordRefundCreditAsync(Guid customerId, Guid saleId, decimal amount, Guid branchId,
        DateOnly entryDate, CancellationToken ct);
    Task<decimal> GetConsolidatedBalanceAsync(Guid customerId, CancellationToken ct);
}
```

Implementation `backend/src/StoreOS.Infrastructure/Customers/CustomerLedgerService.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Domain.Customers;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Customers;

public class CustomerLedgerService : ICustomerLedgerService
{
    private readonly StoreOsDbContext _db;
    private readonly IAccessScope _scope;
    public CustomerLedgerService(StoreOsDbContext db, IAccessScope scope) { _db = db; _scope = scope; }

    public async Task RecordInvoiceDebitAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateDebit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.Invoice, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordPaymentCreditAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateCredit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.Payment, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordVoidReversalAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateCredit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.InvoiceVoid, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task RecordRefundCreditAsync(Guid customerId, Guid saleId, decimal amount,
        Guid branchId, DateOnly entryDate, CancellationToken ct)
    {
        var entry = CustomerLedgerEntry.CreateCredit(_scope.TenantId, customerId, saleId,
            CustomerLedgerEntryType.Refund, amount, branchId, entryDate, saleId.ToString());
        await _db.Set<CustomerLedgerEntry>().AddAsync(entry, ct);
    }

    public async Task<decimal> GetConsolidatedBalanceAsync(Guid customerId, CancellationToken ct)
    {
        // Tenant-global by design (Requirements §17.5) — deliberately bypasses branch query
        // filters, but stays strictly scoped to the caller's OWN tenant via the explicit
        // tenant_id predicate below. See P-506's documented two-query-pattern rule.
        var sums = await _db.Set<CustomerLedgerEntry>().IgnoreQueryFilters()
            .Where(e => e.TenantId == _scope.TenantId && e.CustomerId == customerId)
            .GroupBy(e => 1)
            .Select(g => new { Debit = g.Sum(e => e.Debit), Credit = g.Sum(e => e.Credit) })
            .FirstOrDefaultAsync(ct);
        if (sums is null) return 0m;
        return Math.Max(0m, sums.Debit - sums.Credit);
    }
}
```

Register scoped: `builder.Services.AddScoped<ICustomerLedgerService, CustomerLedgerService>();`

Note none of the `Record*Async` methods call `SaveChangesAsync()` — same staged-write
convention as `IAuditWriter` (P-116) and `IInventoryStockService` (P-406); the calling
handler's single `SaveChangesAsync()` covers everything atomically.

## 4. TASK

1. Create `ICustomerLedgerService.cs` in `StoreOS.Application/Customers/` and
   `CustomerLedgerService.cs` in `StoreOS.Infrastructure/Customers/` exactly as shown, register
   scoped.
2. Write `StoreOS.IntegrationTests/Customers/CustomerLedgerServiceTests.cs`: (a) recording an
   invoice debit followed by `SaveChangesAsync()` produces a `Debit` row with the right amount/
   type; (b) `GetConsolidatedBalanceAsync` after ONE debit of 5000 and ZERO payments returns
   5000; (c) after also recording a payment credit of 2000, balance returns 3000; (d) after a
   void reversal credit equal to the original debit, balance returns to 0, never negative
   (confirm the `Math.Max(0m, ...)` guard); (e) **cross-branch consolidation**: seed debit
   entries stamped with TWO DIFFERENT branch ids for the SAME customer, and confirm
   `GetConsolidatedBalanceAsync` sums BOTH regardless of which branch the CALLING user's
   `IAccessScope` is currently scoped to (proving the tenant-global, cross-branch-visible
   requirement from Requirements §17.5 / Plan §2 Decision 4).

## 5. HARD CONSTRAINTS

- `GetConsolidatedBalanceAsync` is the SOLE sanctioned way to compute
  a customer's credit balance anywhere in the codebase — P-509 (credit limit check), P-601
  (balance display endpoint), and P-905 (reconciliation) all call this SAME method, never
  re-implement the summation independently.
- None of the five methods call `SaveChangesAsync()` — this is a permanent convention shared
  with `IAuditWriter`/`IInventoryStockService`; the calling handler owns the single
  atomic commit.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CustomerLedgerServiceTests
# Expected: all 5 cases pass, especially the cross-branch consolidation case.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-507 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-507 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-507` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the full `ICustomerLedgerService` interface (all 5 methods) and the rule that
`GetConsolidatedBalanceAsync` is the one and only sanctioned balance-computation path.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-507: <one-line summary>"` only after verification is
   fully green.
