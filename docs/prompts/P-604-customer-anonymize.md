# P-604 — POST /customers/{id}/anonymize (DPDP erasure)

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-604** of the StoreOS Cloud build ("POST /customers/{id}/anonymize (DPDP erasure)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-603 complete: full customer ledger/sales views work.
- No DPDP Act erasure/anonymization workflow exists yet (Plan Section 13.2).

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Customers/Anonymize/AnonymizeCustomerCommand.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Customers.Anonymize;

public record AnonymizeCustomerCommand(Guid CustomerId) : IRequest, IHighImpactOperation
{
    public string RequiredPermission => "settings:manage";
}
```

Add an `Anonymize()` method to `Customer.cs` (P-506):

```csharp
public void Anonymize()
{
    Name = "Anonymized Customer";
    Phone = null;
    Gstin = null;
}
```

`AnonymizeCustomerHandler.cs`: loads the customer, calls `Anonymize()`, and per Plan Section
13.2 ALSO scrubs the customer-name SNAPSHOT stored on `sales.customer_name` for every sale
referencing this customer (while leaving `sales.grand_total`, `sales.total_taxable`,
`sales.total_cgst`, `sales.total_sgst`, `sales.invoice_number`, and every other fiscal field
COMPLETELY untouched, since Section 36 CGST mandates 72-month immutable retention of tax
figures):

```csharp
public class AnonymizeCustomerHandler : IRequestHandler<AnonymizeCustomerCommand>
{
    private readonly StoreOsDbContext _db;
    private readonly IAuditWriter _audit;
    public AnonymizeCustomerHandler(StoreOsDbContext db, IAuditWriter audit) { _db = db; _audit = audit; }

    public async Task Handle(AnonymizeCustomerCommand request, CancellationToken ct)
    {
        var customer = await _db.Set<StoreOS.Domain.Customers.Customer>()
            .FirstOrDefaultAsync(c => c.Id == request.CustomerId, ct)
            ?? throw new StoreOS.Domain.Exceptions.DomainValidationException("customer_not_found", "Customer not found.");

        customer.Anonymize();

        var sales = await _db.Set<StoreOS.Domain.Sales.Sale>()
            .Where(s => s.CustomerId == request.CustomerId).ToListAsync(ct);
        foreach (var sale in sales)
            sale.ScrubCustomerNameSnapshot("Anonymized Customer"); // add this narrow method to Sale.cs

        await _audit.WriteAsync(null, "customer.anonymized", "Customer", customer.Id.ToString(), null, ct);
        await _db.SaveChangesAsync(ct);
    }
}
```

Add the NARROW `ScrubCustomerNameSnapshot(string replacement)` method to `Sale.cs` — this is
DELIBERATELY the ONLY way `sales.customer_name` can ever change after creation (contrast with
every OTHER field on `Sale`, which per Invariant 4 never changes); document this as the single,
explicit, legally-mandated exception to snapshot immutability, scoped ONLY to the customer name
display field, never touching any fiscal figure.

Add `POST /api/customers/{id}/anonymize` to `CustomersController.cs`.

## 4. TASK

1. Add `Anonymize()` to `Customer.cs` and `ScrubCustomerNameSnapshot(string)` to
   `Sale.cs` (the ONE narrow, documented exception to Invariant 4's snapshot immutability —
   note this exception explicitly and prominently in `docs/ARTIFACTS.md`).
2. Create `AnonymizeCustomerCommand.cs`/handler in
   `StoreOS.Application/Customers/Anonymize/` exactly as shown.
3. Add `POST /api/customers/{id}/anonymize` to `CustomersController.cs`.
4. Write `StoreOS.IntegrationTests/Customers/AnonymizeCustomerTests.cs`: (a) after
   anonymization, `customers.name == "Anonymized Customer"`, `phone`/`gstin` are `NULL`; (b)
   EVERY sale referencing this customer has `customer_name` updated to "Anonymized Customer";
   (c) **critically**: `invoice_number`, `grand_total`, `total_taxable`, `total_cgst`,
   `total_sgst`, `sale_date`, and every `sale_items` row are COMPLETELY UNCHANGED — assert
   byte-for-byte equality against a pre-anonymization snapshot of these specific fields; (d)
   `customer_ledger_entries` rows are untouched (the erasure targets PII, not financial
   ledger history); (e) a caller without `settings:manage` -> 403 via live-DB check.

## 5. HARD CONSTRAINTS

- `ScrubCustomerNameSnapshot` is the ONLY sanctioned exception to
  Invariant 4 (historical invoice snapshot immutability) in the ENTIRE codebase, and it touches
  ONLY the display-name string — never any fiscal figure, HSN code, purchase price snapshot,
  tax amount, or invoice number. Document this exception prominently so no future prompt
  mistakes it for a precedent to relax snapshot immutability more broadly.
- This is `IHighImpactOperation` requiring `settings:manage` (a data-erasure action is
  significant enough to warrant the live-DB, zero-cache-lag permission check).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter AnonymizeCustomerTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-604 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-604 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-604` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Customer.Anonymize`/`Sale.ScrubCustomerNameSnapshot` signatures (the
latter flagged prominently as the sole, narrow, legally-mandated exception to Invariant 4), and
the `POST /api/customers/{id}/anonymize` route.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-604: <one-line summary>"` only after verification is
   fully green.
