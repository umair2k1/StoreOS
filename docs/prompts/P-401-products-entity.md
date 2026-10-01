# P-401 — Products/catalog entity + migration

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-401** of the StoreOS Cloud build ("Products/catalog entity + migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 3 (Identity/Auth/RBAC, P-301–312) fully complete: login, refresh,
  permissions (cached + live-DB), branch context, rate limiting all work.
- No catalog entity exists yet. This is the first Phase 4 (Catalog & Inventory) prompt.
- Recall from P-107/P-110/P-119: every new entity's query filter goes into the SHARED
  `QueryFilters.ApplyAll` method, and every new tenant-scoped table needs an RLS policy
  migration alongside its `CREATE TABLE`.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.4):

```sql
CREATE TABLE products (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  category text NOT NULL CHECK (category IN ('NewPhone','UsedPhone','Accessory','RepairService')),
  sell_price numeric(18,2) NOT NULL,
  purchase_price numeric(18,2) NOT NULL DEFAULT 0,
  gst_rate numeric(5,2) NOT NULL DEFAULT 18,
  is_taxable bool NOT NULL DEFAULT true,
  is_margin_scheme bool NOT NULL DEFAULT false,
  hsn_code text,
  description text,
  estimated_duration_minutes int,
  warranty_days int,
  is_active bool NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_products_tenant_cat  ON products (tenant_id, category);
CREATE INDEX ix_products_tenant_name ON products (tenant_id, name);
```

Create `backend/src/StoreOS.Domain/Catalog/ProductCategory.cs`:

```csharp
namespace StoreOS.Domain.Catalog;

public static class ProductCategory
{
    public const string NewPhone = "NewPhone";
    public const string UsedPhone = "UsedPhone";
    public const string Accessory = "Accessory";
    public const string RepairService = "RepairService";
    public static readonly IReadOnlyList<string> All = new[] { NewPhone, UsedPhone, Accessory, RepairService };
    public static bool IsValid(string category) => All.Contains(category);
    public static bool IsPhysical(string category) => category != RepairService;
}
```

Create `backend/src/StoreOS.Domain/Catalog/Product.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Catalog;

public class Product
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public decimal SellPrice { get; private set; }
    public decimal PurchasePrice { get; private set; }
    public decimal GstRate { get; private set; } = 18m;
    public bool IsTaxable { get; private set; } = true;
    public bool IsMarginScheme { get; private set; }
    public string? HsnCode { get; private set; }
    public string? Description { get; private set; }
    public int? EstimatedDurationMinutes { get; private set; }
    public int? WarrantyDays { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private Product() { }

    public static Product Create(Guid tenantId, string name, string category, decimal sellPrice,
        decimal purchasePrice, decimal gstRate, string? hsnCode)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        if (!ProductCategory.IsValid(category))
            throw new DomainValidationException("invalid_product_category", $"Unknown category '{category}'.");
        if (sellPrice < 0) throw new DomainValidationException("invalid_sell_price", "Sell price cannot be negative.");
        if (purchasePrice < 0) throw new DomainValidationException("invalid_purchase_price", "Purchase price cannot be negative.");
        if (gstRate < 0) throw new DomainValidationException("invalid_gst_rate", "GST rate cannot be negative.");

        var product = new Product
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Category = category,
            SellPrice = sellPrice, PurchasePrice = purchasePrice, GstRate = gstRate, HsnCode = hsnCode,
            CreatedAt = DateTimeOffset.UtcNow
        };
        if (category == ProductCategory.RepairService)
        {
            product.HsnCode ??= "998713";
            product.GstRate = gstRate == 0m ? 18m : gstRate;
            product.EstimatedDurationMinutes = 60;
            product.WarrantyDays = 30;
        }
        return product;
    }

    public void SetMarginScheme(bool enabled)
    {
        if (enabled && Category != ProductCategory.UsedPhone)
            throw new DomainValidationException("margin_scheme_invalid_category",
                "Margin scheme can only be enabled for Used Phone items.");
        IsMarginScheme = enabled;
    }

    public void UpdatePricing(decimal sellPrice, decimal purchasePrice, decimal gstRate)
    {
        if (sellPrice < 0 || purchasePrice < 0 || gstRate < 0)
            throw new DomainValidationException("invalid_pricing", "Prices and GST rate cannot be negative.");
        SellPrice = sellPrice; PurchasePrice = purchasePrice; GstRate = gstRate;
    }

    public void UpdateDescriptionAndServiceFields(string? description, int? estimatedDurationMinutes, int? warrantyDays)
    {
        Description = description;
        if (Category == ProductCategory.RepairService)
        {
            EstimatedDurationMinutes = estimatedDurationMinutes ?? 60;
            WarrantyDays = warrantyDays ?? 30;
        }
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Product name is required.", nameof(name));
        Name = name.Trim();
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
```

Note: `SetMarginScheme(true)` throwing when `Category != UsedPhone` directly enforces
Requirements §7.2 ("Margin scheme is permitted only for Used Phone items") at the DOMAIN
level, not just at checkout time — this is a stronger, earlier guard than the P-202 tax-engine
guard (which only checks the resolved cost value, not the category).

## 4. TASK

1. Create `ProductCategory.cs` and `Product.cs` exactly as shown in
   `StoreOS.Domain/Catalog/`.
2. Create the EF configuration mapping every column snake_case, the CHECK constraint on
   `category` (via raw SQL in the migration, since EF's fluent API doesn't emit CHECK
   constraints portably — add `migrationBuilder.Sql("ALTER TABLE products ADD CONSTRAINT
   ck_products_category CHECK (category IN ('NewPhone','UsedPhone','Accessory','RepairService'));");`),
   and the two named indexes.
3. Add `DbSet<Product> Products` to `StoreOsDbContext`. Add the tenant-only query filter for
   `Product` to the SHARED `QueryFilters.ApplyAll` method (per P-119's convention).
4. Generate migration `009_Products`, including: the table, both indexes, the CHECK
   constraint, and RLS enable+force+policy (tenant-only archetype) for `products`.
5. Apply the migration; re-run `RlsCoverageTests` to confirm `products` is now covered.
6. Write `StoreOS.Domain.Tests/Catalog/ProductTests.cs`: (a) creating a `RepairService`
   product with no HSN/GST/duration/warranty specified gets the documented defaults
   (`998713`, `18`, `60`, `30`); (b) creating a non-service product does NOT get those
   auto-defaults applied; (c) `SetMarginScheme(true)` on an `Accessory` throws
   `margin_scheme_invalid_category`; (d) `SetMarginScheme(true)` on a `UsedPhone` succeeds;
   (e) `Create` with an invalid category string throws `invalid_product_category`; (f)
   negative sell/purchase price or GST rate each throw their respective validation codes.

## 5. HARD CONSTRAINTS

- The CHECK constraint category values (`'NewPhone','UsedPhone',
  'Accessory','RepairService'`) must exactly match `ProductCategory.All`'s C# string
  constants — keep these synchronized character-for-character, including case, forever.
- `SetMarginScheme` is the ONLY way to toggle `IsMarginScheme` — never expose a public setter
  that bypasses the category guard.
- RepairService products intentionally do NOT get `branch_inventory` rows (built in P-403) —
  this prompt does not yet touch that table, but document this rule now in
  `docs/ARTIFACTS.md` under the Product entity's record, since it is Invariant 3's
  enforcement point starting from P-403 onward.

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
dotnet ef migrations add 009_Products --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter ProductTests
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
   `BLOCKED: P-401 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-401 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-401` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Product`/`ProductCategory` signatures, migration name `009_Products`,
and the documented rule that RepairService products never get `branch_inventory` rows
(critical for Invariant 3 enforcement in later prompts).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-401: <one-line summary>"` only after verification is
   fully green.
