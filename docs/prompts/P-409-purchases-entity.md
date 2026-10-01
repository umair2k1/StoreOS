# P-409 — Purchases entity + migration

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-409** of the StoreOS Cloud build ("Purchases entity + migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-403–408 complete: full stock/IMEI infrastructure with concurrency safety.
- No purchase ledger exists yet.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.6, minus supplier FK which is added in P-411's
migration as a follow-up ALTER since Suppliers doesn't exist yet — for THIS prompt, create
`supplier_id` as a bare nullable `uuid` column with NO foreign key constraint yet; P-411 adds
the FK once the `suppliers` table exists):

```sql
CREATE TABLE purchases (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  qty int NOT NULL CHECK (qty > 0),
  purchase_rate numeric(18,2) NOT NULL,
  supplier_id uuid NULL,
  supplier_name text, supplier_phone text, supplier_address text,
  seller_name text, seller_phone text, seller_address text,
  supplier_invoice_no text,
  import_batch_id uuid NULL,
  purchase_date date NOT NULL,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_purchases_tenant_branch_product ON purchases (tenant_id, branch_id, product_id);
CREATE INDEX ix_purchases_tenant_branch_date ON purchases (tenant_id, branch_id, purchase_date);
```

Create `backend/src/StoreOS.Domain/Purchasing/Purchase.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Purchasing;

public class Purchase
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public int Qty { get; private set; }
    public decimal PurchaseRate { get; private set; }
    public Guid? SupplierId { get; private set; }
    public string? SupplierName { get; private set; }
    public string? SupplierPhone { get; private set; }
    public string? SupplierAddress { get; private set; }
    public string? SellerName { get; private set; }
    public string? SellerPhone { get; private set; }
    public string? SellerAddress { get; private set; }
    public string? SupplierInvoiceNo { get; private set; }
    public Guid? ImportBatchId { get; private set; }
    public DateOnly PurchaseDate { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Purchase() { }

    public static Purchase Create(Guid tenantId, Guid branchId, Guid productId, int qty,
        decimal purchaseRate, Guid? supplierId, string? supplierName, string? supplierPhone,
        string? supplierAddress, DateOnly purchaseDate, Guid createdBy,
        string? supplierInvoiceNo = null, Guid? importBatchId = null,
        string? sellerName = null, string? sellerPhone = null, string? sellerAddress = null)
    {
        if (qty <= 0) throw new DomainValidationException("invalid_quantity", "Purchase quantity must be positive.");
        if (purchaseRate < 0) throw new DomainValidationException("invalid_purchase_rate", "Purchase rate cannot be negative.");
        return new Purchase
        {
            Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchId, ProductId = productId,
            Qty = qty, PurchaseRate = purchaseRate, SupplierId = supplierId, SupplierName = supplierName,
            SupplierPhone = supplierPhone, SupplierAddress = supplierAddress, SellerName = sellerName,
            SellerPhone = sellerPhone, SellerAddress = sellerAddress, SupplierInvoiceNo = supplierInvoiceNo,
            ImportBatchId = importBatchId, PurchaseDate = purchaseDate, CreatedBy = createdBy,
            CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

Apply the Archetype B RLS policy to `purchases`.

## 4. TASK

1. Create `Purchase.cs` exactly as shown in `StoreOS.Domain/Purchasing/`.
2. Create the EF configuration: snake_case columns, FK to `branches`/`products` (NOT to
   suppliers yet — leave `supplier_id` as a bare column), CHECK `qty > 0` (raw SQL), and the
   two named indexes.
3. Add `DbSet<Purchase> Purchases` to `StoreOsDbContext`, add its branch-scoped query filter.
4. Generate migration `013_Purchases` including the RLS policy, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Purchasing/PurchaseTests.cs`: valid creation succeeds with all
   optional fields null; zero/negative qty throws `invalid_quantity`; negative rate throws
   `invalid_purchase_rate`.

## 5. HARD CONSTRAINTS

- `supplier_id` has NO foreign-key constraint in this prompt's
  migration — P-411 adds it via a follow-up `ALTER TABLE purchases ADD CONSTRAINT ... FOREIGN
  KEY (supplier_id) REFERENCES suppliers(id);` once the `suppliers` table exists. Do not
  attempt to create the `suppliers` table early just to satisfy this FK now — respect the
  phase ordering.

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
dotnet ef migrations add 013_Purchases --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter PurchaseTests
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
   `BLOCKED: P-409 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-409 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-409` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Purchase` signature, migration name `013_Purchases`, and the explicit
note that `supplier_id`'s FK constraint is deferred to P-411.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-409: <one-line summary>"` only after verification is
   fully green.
