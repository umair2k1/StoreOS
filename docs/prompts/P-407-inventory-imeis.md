# P-407 — inventory_imeis entity + migration + tenant-wide active-unique index

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-407** of the StoreOS Cloud build ("inventory_imeis entity + migration + tenant-wide active-unique index") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-406 complete: `IInventoryStockService` handles qty-based stock safely.
- No IMEI tracking exists yet. This prompt implements Requirements §11.6/§11.7 and Plan
  §8.6's tenant-WIDE (not branch-wide) active-uniqueness guarantee — a physical device exists
  once per TENANT, regardless of which branch currently holds it.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.6):

```sql
CREATE TABLE inventory_imeis (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  purchase_id uuid NULL,
  imei text NOT NULL,
  status text NOT NULL CHECK (status IN ('InStock','Sold','ReturnedToSupplier','InTransit')),
  sale_id uuid NULL, sale_item_id uuid NULL,
  return_id uuid NULL,
  purchase_date date, purchase_price numeric(18,2),
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE UNIQUE INDEX ux_imei_active ON inventory_imeis (tenant_id, imei)
  WHERE status IN ('InStock','Sold','InTransit');
CREATE INDEX ix_imei_lookup ON inventory_imeis (tenant_id, imei);
```

Note the partial unique index: a `ReturnedToSupplier` IMEI is NOT counted toward uniqueness,
so the SAME IMEI string CAN be re-registered as a new inventory row later (e.g. a customer
returns a phone under warranty, shop re-stocks it through a fresh purchase) — but at most ONE
row for that `(tenant_id, imei)` may EVER be in an active state (`InStock`/`Sold`/`InTransit`)
simultaneously. This is Invariant 1's actual enforcement mechanism, at the database level.

Create `backend/src/StoreOS.Domain/Inventory/ImeiStatus.cs`:

```csharp
namespace StoreOS.Domain.Inventory;

public static class ImeiStatus
{
    public const string InStock = "InStock";
    public const string Sold = "Sold";
    public const string ReturnedToSupplier = "ReturnedToSupplier";
    public const string InTransit = "InTransit";
    public static readonly IReadOnlyList<string> All = new[] { InStock, Sold, ReturnedToSupplier, InTransit };
}
```

Create `backend/src/StoreOS.Domain/Inventory/InventoryImei.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Inventory;

public class InventoryImei
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public Guid ProductId { get; private set; }
    public Guid? PurchaseId { get; private set; }
    public string Imei { get; private set; } = default!;
    public string Status { get; private set; } = default!;
    public Guid? SaleId { get; private set; }
    public Guid? SaleItemId { get; private set; }
    public Guid? ReturnId { get; private set; }
    public DateOnly? PurchaseDate { get; private set; }
    public decimal? PurchasePrice { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private InventoryImei() { }

    public static InventoryImei Create(Guid tenantId, Guid branchId, Guid productId, Guid? purchaseId,
        string imei, DateOnly purchaseDate, decimal purchasePrice)
    {
        if (string.IsNullOrWhiteSpace(imei)) throw new DomainValidationException("imei_required", "IMEI is required.");
        if (purchasePrice <= 0) throw new DomainValidationException("imei_invalid_purchase_price",
            "IMEI purchase price must be a positive value at stock-in time.");
        return new InventoryImei
        {
            Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchId, ProductId = productId,
            PurchaseId = purchaseId, Imei = imei.Trim(), Status = ImeiStatus.InStock,
            PurchaseDate = purchaseDate, PurchasePrice = purchasePrice, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void MarkSold(Guid saleId, Guid saleItemId)
    {
        if (Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available", $"IMEI {Imei} is not available for sale (status: {Status}).");
        Status = ImeiStatus.Sold; SaleId = saleId; SaleItemId = saleItemId;
    }

    public void RestoreToStock()
    {
        if (Status != ImeiStatus.Sold)
            throw new DomainValidationException("imei_not_sold", $"IMEI {Imei} is not currently sold (status: {Status}).");
        Status = ImeiStatus.InStock; SaleId = null; SaleItemId = null;
    }

    public void MarkReturnedToSupplier(Guid returnId)
    {
        if (Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available",
                $"IMEI {Imei} cannot be returned to supplier from status '{Status}'.");
        Status = ImeiStatus.ReturnedToSupplier; ReturnId = returnId;
    }

    public void MarkInTransit()
    {
        if (Status != ImeiStatus.InStock)
            throw new DomainValidationException("imei_not_available", $"IMEI {Imei} cannot be transferred from status '{Status}'.");
        Status = ImeiStatus.InTransit;
    }

    public void CompleteTransferIn(Guid newBranchId)
    {
        if (Status != ImeiStatus.InTransit)
            throw new DomainValidationException("imei_not_in_transit", $"IMEI {Imei} is not in transit.");
        Status = ImeiStatus.InStock; BranchId = newBranchId;
    }
}
```

Apply the Archetype B RLS policy to `inventory_imeis` (it IS branch-scoped for RLS visibility
purposes even though uniqueness is tenant-wide — these are separate concerns: RLS governs WHO
CAN SEE a row based on their branch access; the unique index governs data integrity
tenant-wide regardless of branch).

## 4. TASK

1. Create `ImeiStatus.cs` and `InventoryImei.cs` exactly as shown in
   `StoreOS.Domain/Inventory/`.
2. Create the EF configuration: snake_case columns, the CHECK constraint on `status` (raw
   SQL), the partial unique index `ux_imei_active` EXACTLY as shown (raw SQL, since EF's fluent
   `HasIndex(...).HasFilter(...)` CAN express partial indexes — use
   `.HasFilter("status IN ('InStock','Sold','InTransit')")` if supported cleanly by the Npgsql
   provider, otherwise fall back to raw SQL in the migration; confirm which approach actually
   works by testing it, and use whichever produces the exact DDL shown), and the plain lookup
   index `ix_imei_lookup`.
3. Add `DbSet<InventoryImei> InventoryImeis` to `StoreOsDbContext`, add its branch-scoped query
   filter to `QueryFilters.ApplyAll`.
4. Generate migration `012_InventoryImeis` including the RLS policy, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Inventory/InventoryImeiTests.cs`: full lifecycle coverage —
   `Create` with a non-positive purchase price throws `imei_invalid_purchase_price`;
   `MarkSold` from `InStock` succeeds, from any other status throws `imei_not_available`;
   `RestoreToStock` from `Sold` succeeds, from other statuses throws `imei_not_sold`;
   `MarkReturnedToSupplier` from `InStock` succeeds, else throws; the transfer pair
   (`MarkInTransit`/`CompleteTransferIn`) round-trips correctly and rejects out-of-order calls.
7. Write `StoreOS.IntegrationTests/Inventory/ImeiUniquenessTests.cs`: (a) inserting the SAME
   IMEI string twice while both rows are `InStock` -> the SECOND insert's `SaveChangesAsync()`
   throws a Postgres unique-violation (wrap and assert via `DbUpdateException`); (b) inserting
   the same IMEI where the FIRST row's status is `ReturnedToSupplier` -> the second insert
   SUCCEEDS (partial index correctly excludes returned units from the uniqueness constraint).

## 5. HARD CONSTRAINTS

- The partial unique index's `WHERE` clause
  (`status IN ('InStock','Sold','InTransit')`) is the EXACT enforcement mechanism for
  Invariant 1 (no double-selling a physical unit) — do not weaken it to a plain
  non-partial unique index (which would wrongly prevent ever reusing an IMEI string even after
  a legitimate supplier return) and do not remove the `'InTransit'` inclusion (an in-transit
  phone must also be protected from being sold or re-registered elsewhere mid-transfer).
- `Create`'s `purchasePrice <= 0` guard directly enforces Requirements §7.2's "Mandatory Cost
  Basis Rule for Margin Scheme" at the point of stock-in — an IMEI can never even be CREATED
  with an invalid cost basis, which is a stronger, earlier guarantee than P-202's checkout-time
  guard (belt-and-suspenders).

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
dotnet ef migrations add 012_InventoryImeis --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter InventoryImeiTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter ImeiUniquenessTests
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
   `BLOCKED: P-407 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-407 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-407` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ImeiStatus`/`InventoryImei` signatures, migration name
`012_InventoryImeis`, and the exact partial unique index definition (critical reference for
P-408/P-512's concurrency tests).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-407: <one-line summary>"` only after verification is
   fully green.
