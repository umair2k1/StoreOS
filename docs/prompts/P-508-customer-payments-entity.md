# P-508 — customer_payments entity/migration + payment composition in checkout

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-508** of the StoreOS Cloud build ("customer_payments entity/migration + payment composition in checkout") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-507 complete: `ICustomerLedgerService` handles ledger postings.
- No `customer_payments` table exists yet. This table is the actual immutable per-transaction
  payment RECORD (Requirements §9.3/Invariant 5 — "a subsequent payment is a transaction, not
  an overwrite"); `customer_ledger_entries` (P-506) is the accounting VIEW derived from it (and
  from invoice debits) — the two tables serve different purposes and both are needed.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.5):

```sql
CREATE TABLE customer_payments (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  sale_id uuid NOT NULL REFERENCES sales(id),
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  payment_mode text NOT NULL,
  payment_date date NOT NULL,
  received_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_custpay_sale ON customer_payments (tenant_id, sale_id);
```

Create `backend/src/StoreOS.Domain/Sales/CustomerPayment.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Sales;

public class CustomerPayment
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public decimal Amount { get; private set; }
    public string PaymentMode { get; private set; } = default!;
    public DateOnly PaymentDate { get; private set; }
    public Guid ReceivedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomerPayment() { }

    public static CustomerPayment Create(Guid tenantId, Guid saleId, decimal amount,
        string paymentMode, DateOnly paymentDate, Guid receivedBy)
    {
        if (amount <= 0) throw new DomainValidationException("invalid_payment_amount", "Payment amount must be positive.");
        if (!PaymentMode.All.Contains(paymentMode))
            throw new DomainValidationException("invalid_payment_mode", $"Unknown payment mode '{paymentMode}'.");
        return new CustomerPayment
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SaleId = saleId, Amount = amount,
            PaymentMode = paymentMode, PaymentDate = paymentDate, ReceivedBy = receivedBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

No `Update`/`Delete`-style method exists on this entity AT ALL — it is immutable once created,
by design (Invariant 5). Apply the tenant-only RLS archetype (it has `tenant_id` but no direct
`branch_id`; visibility is implicitly governed by the caller's ability to load the parent
`Sale`, same reasoning as `sale_items` in P-502).

This prompt ALSO updates `PricingResolver`/checkout plumbing minimally: no handler changes yet
(full assembly is P-510) — this prompt is schema-only, but ADD a small
`PaymentComposer` helper that will be consumed by P-510, translating a `CheckoutCommand`'s
`Payments` list into `CustomerPayment` entities plus the resulting `AmountPaid` total, since
this pure-composition logic is naturally scoped to THIS prompt's table:

```csharp
namespace StoreOS.Application.Sales.Checkout;

public static class PaymentComposer
{
    public static (IReadOnlyList<StoreOS.Domain.Sales.CustomerPayment> Payments, decimal TotalPaid) Compose(
        Guid tenantId, Guid saleId, IReadOnlyList<CheckoutPayment> requested, DateOnly paymentDate, Guid receivedBy)
    {
        var payments = requested.Select(p =>
            StoreOS.Domain.Sales.CustomerPayment.Create(tenantId, saleId, p.Amount, p.PaymentMode, paymentDate, receivedBy))
            .ToList();
        var total = payments.Sum(p => p.Amount);
        return (payments, total);
    }
}
```

## 4. TASK

1. Create `CustomerPayment.cs` in `StoreOS.Domain/Sales/` exactly as shown (no
   update/delete method — immutable by construction).
2. Create the EF configuration: snake_case columns, CHECK `amount > 0` (raw SQL), FK to
   `sales`, the named index.
3. Add `DbSet<CustomerPayment> CustomerPayments` to `StoreOsDbContext`, tenant-only query
   filter.
4. Generate migration `020_CustomerPayments` including RLS, apply it.
5. Re-run `RlsCoverageTests`.
6. Create `PaymentComposer.cs` in `StoreOS.Application/Sales/Checkout/` exactly as shown.
7. Write `StoreOS.Domain.Tests/Sales/CustomerPaymentTests.cs`: valid creation succeeds; zero/
   negative amount throws `invalid_payment_amount`; invalid payment mode string throws
   `invalid_payment_mode`; confirm (via reflection or simple code inspection documented in a
   test comment) that `CustomerPayment` exposes NO public mutation method whatsoever — only the
   static factory and read-only properties.
8. Write `StoreOS.Domain.Tests/Sales/PaymentComposerTests.cs`: composing a list of 2 payments
   (e.g. Cash 3000 + UPI 2000) returns 2 `CustomerPayment` entities and `TotalPaid == 5000`; an
   empty payments list returns `TotalPaid == 0` and an empty entity list (a fully unpaid credit
   sale is valid).

## 5. HARD CONSTRAINTS

- `CustomerPayment` is PERMANENTLY IMMUTABLE once created — no
  method may ever be added to this class that changes `Amount`, `PaymentMode`, or any other
  field after construction. Every later "correction" to a payment (e.g. wrong amount entered)
  must be modeled as a NEW payment record (or, if truly needed, a reversal/adjustment pattern
  documented separately) — never an edit to this row. This directly enforces Invariant 5.
- `PaymentComposer` performs NO database access — it is pure composition logic over already
  validated `CheckoutPayment` DTOs, safe to unit test without any DB fixture.

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
dotnet ef migrations add 020_CustomerPayments --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter CustomerPaymentTests
dotnet test backend/tests/StoreOS.Domain.Tests --filter PaymentComposerTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-508 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-508 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-508` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CustomerPayment`/`PaymentComposer` signatures, migration name
`020_CustomerPayments`, and the permanent-immutability rule for this entity.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-508: <one-line summary>"` only after verification is
   fully green.
