# P-509 — Credit limit check (tenant-global consolidated balance)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-509** of the StoreOS Cloud build ("Credit limit check (tenant-global consolidated balance)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-507 (`ICustomerLedgerService.GetConsolidatedBalanceAsync`), P-508
  (`CustomerPayment`/`PaymentComposer`) complete.
- `customers.credit_limit` column does NOT exist yet (deferred to P-601 per P-506's note) —
  THIS PROMPT must add it now instead, since checkout (P-510, the very next prompt) needs it.
  Correcting the earlier deferral: add `credit_limit` here.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Migration addition:

```sql
ALTER TABLE customers ADD COLUMN credit_limit numeric(18,2) NULL;
```

Update `backend/src/StoreOS.Domain/Customers/Customer.cs` (extend from P-506): add
`CreditLimit` property and a `SetCreditLimit(decimal? limit)` method
(`if (limit is not null && limit < 0) throw new DomainValidationException("invalid_credit_limit", ...)`).

Create `backend/src/StoreOS.Application/Sales/Checkout/CreditLimitChecker.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Sales.Checkout;

public class CreditLimitChecker
{
    private readonly IAppDbContext _db;
    private readonly ICustomerLedgerService _ledgerService;
    private readonly IAccessScope _scope;

    public CreditLimitChecker(IAppDbContext db, ICustomerLedgerService ledgerService, IAccessScope scope)
    { _db = db; _ledgerService = ledgerService; _scope = scope; }

    public async Task CheckAsync(Guid customerId, decimal newCreditAmount, CancellationToken ct)
    {
        if (newCreditAmount <= 0) return; // fully paid sale never needs a credit check

        var customer = await _db.Set<Customer>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(c => c.TenantId == _scope.TenantId && c.Id == customerId, ct)
            ?? throw new DomainValidationException("customer_not_found", "Customer not found.");

        if (customer.CreditLimit is null) return; // unlimited credit

        var existingBalance = await _ledgerService.GetConsolidatedBalanceAsync(customerId, ct);
        var projectedBalance = existingBalance + newCreditAmount;

        if (projectedBalance > customer.CreditLimit.Value)
        {
            if (_scope.HasPermission("sales:credit:override"))
                return; // manager override permission bypasses the block (still allowed, not silently ignored)
            throw new DomainValidationException("credit_limit_exceeded",
                $"This sale would bring the customer's balance to {projectedBalance:F2}, exceeding their credit limit of {customer.CreditLimit.Value:F2}.");
        }
    }
}
```

Register scoped: `builder.Services.AddScoped<CreditLimitChecker>();`

Add `credit_limit` to `Customer`'s EF configuration and the `CreateCustomer`/`UpdateCustomer`
handlers from Phase 6 will later expose it for editing — for THIS prompt, since those handlers
don't exist yet, just wire the column/entity property; a direct EF-level test can set it.

## 4. TASK

1. Create migration `021_CreditLimit` (extends `customers` — this is a small,
   standalone schema-change migration per the manifest's "every DB schema change is its own
   small migration" rule) adding the `credit_limit` column.
2. Extend `Customer.cs` with `CreditLimit`/`SetCreditLimit` exactly as shown; update its EF
   configuration.
3. Create `CreditLimitChecker.cs` exactly as shown in `StoreOS.Application/Sales/Checkout/`,
   register scoped.
4. Apply the migration.
5. Write `StoreOS.IntegrationTests/Sales/CreditLimitCheckerTests.cs`: (a) `newCreditAmount <= 0`
   -> always passes without even loading the customer (verify via a nonexistent customer id
   passed with `newCreditAmount = 0` -> does NOT throw `customer_not_found`, confirming the
   early-return path); (b) customer with `CreditLimit = null` -> unlimited, always passes
   regardless of balance; (c) customer with `CreditLimit = 10000`, existing balance 8000, new
   credit amount 1500 (projected 9500) -> passes; (d) same but new credit amount 3000
   (projected 11000) -> throws `credit_limit_exceeded` for a caller WITHOUT
   `sales:credit:override`; (e) same over-limit scenario but the caller DOES have
   `sales:credit:override` -> passes without throwing; (f) **cross-branch**: seed the existing
   8000 balance as ledger entries split across TWO different branches, confirm the check still
   correctly sums both (reusing P-507's proven consolidation) regardless of which branch the
   CURRENT checkout is happening in.

## 5. HARD CONSTRAINTS

- The override permission (`sales:credit:override`) BYPASSES the
  block but this bypass is still a LOGGED business event — P-510's checkout handler must audit-
  log when an override was used (add this note now for P-510 to pick up: an
  `IAuditWriter.WriteAsync(..., "sales.credit_limit_overridden", ...)` call is required
  whenever `CreditLimitChecker` would have blocked but didn't due to the override permission;
  since `CreditLimitChecker` itself doesn't have direct access to `IAuditWriter` in this
  design, either inject it here too and call it before the `return`, OR have
  `CreditLimitChecker` return a result object indicating whether an override was used, so
  `CheckoutHandler` can log it. Choose the SIMPLER approach: inject `IAuditWriter` into
  `CreditLimitChecker` directly and log the override event before returning, since it has all
  the context (customer id, projected balance, limit) right there).
- Update `CreditLimitChecker` accordingly to inject and use `IAuditWriter` for the override
  case before finalizing this prompt.

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
dotnet ef migrations add 021_CreditLimit --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter CreditLimitCheckerTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-509 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-509 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-509` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CreditLimitChecker.CheckAsync` signature (including the `IAuditWriter`
injection for override logging), migration name `021_CreditLimit`, and the
`credit_limit_exceeded` error code / `sales:credit:override` permission gate.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-509: <one-line summary>"` only after verification is
   fully green.
