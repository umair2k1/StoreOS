# P-502 — sales + sale_items entities + migration

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-502** of the StoreOS Cloud build ("sales + sale_items entities + migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-501 complete: concurrency-safe sequence allocation works.
- No `Sale`/`SaleItem` entities exist yet. This is the CORE transactional table of the entire
  product.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.5, core columns — IRN/e-invoicing columns included per
spec even though Phase 8 e-invoicing itself is deferred; estimate-conversion columns per
Plan §8.5's note are added SEPARATELY in P-519, not here):

```sql
CREATE TABLE sales (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  invoice_number text NOT NULL,
  invoice_type text NOT NULL CHECK (invoice_type IN ('TaxInvoice','Estimate')),
  sale_date timestamptz NOT NULL,
  customer_id uuid NULL,
  customer_name text, customer_gstin text,
  total_taxable numeric(18,2) NOT NULL,
  total_cgst numeric(18,2) NOT NULL,
  total_sgst numeric(18,2) NOT NULL,
  grand_total numeric(18,2) NOT NULL,
  discount numeric(18,2) NOT NULL DEFAULT 0,
  amount_paid numeric(18,2) NOT NULL DEFAULT 0,
  status text NOT NULL DEFAULT 'Active' CHECK (status IN ('Active','Voided','Refunded')),
  payment_mode text CHECK (payment_mode IN ('Cash','UPI','Card','Credit','Finance')),
  finance_provider text, financer_name text,
  down_payment numeric(18,2), emi_details jsonb,
  gift_details jsonb, description text,
  repair_job_id uuid NULL,
  irn text NULL, irn_ack_no text NULL, irn_qr_data text NULL,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, invoice_number)
);
CREATE INDEX ix_sales_tenant_branch_date ON sales (tenant_id, branch_id, sale_date DESC);
CREATE INDEX ix_sales_tenant_status ON sales (tenant_id, status);
CREATE INDEX ix_sales_tenant_irn ON sales (tenant_id, irn) WHERE irn IS NOT NULL;

CREATE TABLE sale_items (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  sale_id uuid NOT NULL REFERENCES sales(id),
  product_id uuid NULL,
  item_name text NOT NULL,
  category text NOT NULL,
  qty int NOT NULL CHECK (qty > 0),
  price_per_unit numeric(18,2) NOT NULL,
  purchase_price_snapshot numeric(18,2),
  hsn_snapshot text,
  is_margin_scheme bool NOT NULL DEFAULT false,
  is_gift bool NOT NULL DEFAULT false,
  taxable numeric(18,2) NOT NULL,
  cgst numeric(18,2) NOT NULL,
  sgst numeric(18,2) NOT NULL,
  imei text
);
CREATE INDEX ix_sale_items_sale ON sale_items (tenant_id, sale_id);
CREATE INDEX ix_sale_items_imei ON sale_items (tenant_id, imei);
```

Create `backend/src/StoreOS.Domain/Sales/InvoiceType.cs`
(`TaxInvoice`/`Estimate` constants + `All`), `SaleStatus.cs`
(`Active`/`Voided`/`Refunded` constants + `All`), `PaymentMode.cs`
(`Cash`/`UPI`/`Card`/`Credit`/`Finance` constants + `All`).

Create `backend/src/StoreOS.Domain/Sales/SaleItem.cs` (created via a factory that takes
already-computed tax values — the ACTUAL tax computation happens in `PricingResolver`/
`TaxEngine`, this entity just stores the resulting snapshot):

```csharp
namespace StoreOS.Domain.Sales;

public class SaleItem
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid SaleId { get; private set; }
    public Guid? ProductId { get; private set; }
    public string ItemName { get; private set; } = default!;
    public string Category { get; private set; } = default!;
    public int Qty { get; private set; }
    public decimal PricePerUnit { get; private set; }
    public decimal? PurchasePriceSnapshot { get; private set; }
    public string? HsnSnapshot { get; private set; }
    public bool IsMarginScheme { get; private set; }
    public bool IsGift { get; private set; }
    public decimal Taxable { get; private set; }
    public decimal Cgst { get; private set; }
    public decimal Sgst { get; private set; }
    public string? Imei { get; private set; }

    private SaleItem() { }

    public static SaleItem Create(Guid tenantId, Guid saleId, Guid? productId, string itemName,
        string category, int qty, decimal pricePerUnit, decimal? purchasePriceSnapshot,
        string? hsnSnapshot, bool isMarginScheme, bool isGift, decimal taxable, decimal cgst,
        decimal sgst, string? imei)
    {
        return new SaleItem
        {
            Id = Guid.NewGuid(), TenantId = tenantId, SaleId = saleId, ProductId = productId,
            ItemName = itemName, Category = category, Qty = qty, PricePerUnit = pricePerUnit,
            PurchasePriceSnapshot = purchasePriceSnapshot, HsnSnapshot = hsnSnapshot,
            IsMarginScheme = isMarginScheme, IsGift = isGift, Taxable = taxable, Cgst = cgst,
            Sgst = sgst, Imei = imei
        };
    }
}
```

Create `backend/src/StoreOS.Domain/Sales/Sale.cs` similarly — a rich aggregate root with a
private `List<SaleItem>` backing field exposed as `IReadOnlyList<SaleItem> Items`, a static
`CreateTaxInvoice(...)`/`CreateEstimate(...)` factory pair (both delegate to a shared private
constructor-helper differing only in `InvoiceType`), an `AddItem(SaleItem item)` method, and
STATUS TRANSITION methods `MarkVoided()` / `MarkRefunded()` guarded so only `Active` sales can
transition (`if (Status != SaleStatus.Active) throw new DomainValidationException(...)`), plus
an `ApplyPayment(decimal amount)` method that increases `AmountPaid` (bounded, never exceeding
`GrandTotal` unless a documented overpayment policy is explicitly enabled — for now, hard-cap
it: `if (AmountPaid + amount > GrandTotal) throw new DomainValidationException
("payment_exceeds_balance", ...)`).

Write out `Sale.cs` completely yourself, following the exact style (private setters, static
factories, guarded mutation methods, one aggregate root protecting its own invariants) shown
throughout every prior entity in this manifest (P-102, P-104, P-401, P-403, P-407, P-409,
P-411), covering every column in the DDL above.

## 4. TASK

1. Create `InvoiceType.cs`, `SaleStatus.cs`, `PaymentMode.cs`, `SaleItem.cs` exactly
   as shown, and write `Sale.cs` yourself in full, in `StoreOS.Domain/Sales/`, following the
   established aggregate-root style and covering every DDL column, with the behavior described
   (status guards, payment application cap, item collection).
2. Create EF configurations for both entities: snake_case columns, CHECK constraints (raw SQL)
   on `invoice_type`, `status`, `payment_mode`, `qty > 0` on sale_items; the three named
   indexes on `sales` (including the PARTIAL index on `irn`); the two named indexes on
   `sale_items`; `UNIQUE(tenant_id, invoice_number)`; FK `sale_items.sale_id -> sales.id`;
   `emi_details`/`gift_details` mapped as `jsonb` (nullable, stored as JSON strings on the
   entity — add `EmiDetailsJson`/`GiftDetailsJson` string properties on `Sale` rather than
   structured objects, since structured finance/gift shapes are defined later in P-514/P-515;
   keep these as raw nullable JSON strings for now).
3. Add both `DbSet<>`s to `StoreOsDbContext`, add BOTH entities' branch-scoped (sales) /
   tenant+implicit-via-sale (sale_items — use a subquery-based filter joining to `sales.tenant_id`/
   `branch_id` similar to P-108's `role_permissions` subquery pattern, OR simpler: give
   `sale_items` its OWN `tenant_id` column directly as the DDL already shows, and filter it the
   SAME way as any other tenant-scoped-but-no-own-branch-id table by joining through `sale_id`
   — actually `sale_items` has `tenant_id` directly per the DDL, but no direct `branch_id`;
   apply a tenant-only filter directly on `sale_items.tenant_id`, and rely on the fact that a
   user can only ever legitimately reach a `sale_item` by first loading its parent `Sale`,
   which IS branch-filtered) query filters to `QueryFilters.ApplyAll`.
4. Generate migration `018_Sales` including RLS on both tables (Archetype B for `sales`,
   tenant-only Archetype A for `sale_items` per the reasoning above), apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Sales/SaleTests.cs`: creating a TaxInvoice vs Estimate produces
   the correct `InvoiceType`; `AddItem` accumulates items; `MarkVoided`/`MarkRefunded` on an
   already-non-Active sale throw; `ApplyPayment` beyond `GrandTotal` throws
   `payment_exceeds_balance`; `ApplyPayment` up to exactly `GrandTotal` succeeds.

## 5. HARD CONSTRAINTS

- `sale_items` snapshot fields (`item_name`, `category`,
  `hsn_snapshot`, `purchase_price_snapshot`) are written ONCE at creation and NEVER updated
  afterward by any later prompt, even if the underlying `Product` changes — this is
  Invariant 4's enforcement point. Do not add any method to `SaleItem` that mutates these
  fields after creation.
- `Sale`'s status transition methods must be the ONLY way `Status` changes — no public setter.
  `MarkVoided`/`MarkRefunded` guard against double-transition from a non-Active state.

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
dotnet ef migrations add 018_Sales --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter SaleTests
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
   `BLOCKED: P-502 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-502 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-502` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete `Sale`/`SaleItem`/`InvoiceType`/`SaleStatus`/`PaymentMode`
signatures (every public member), migration name `018_Sales`, and the snapshot-immutability
rule for `sale_items`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-502: <one-line summary>"` only after verification is
   fully green.
