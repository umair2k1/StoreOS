# P-411 — Suppliers minimal master (needed as FK for purchases) entity+migration

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-411** of the StoreOS Cloud build ("Suppliers minimal master (needed as FK for purchases) entity+migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-410 complete: stock-in works, `purchases.supplier_id` exists WITHOUT a
  foreign key yet.
- No `suppliers` table exists. This prompt creates the master table and retrofits the FK onto
  `purchases`. Full supplier TRANSACTION/ledger functionality (payables, returns) comes later
  in Phase 6 (P-605 onward) — this prompt is deliberately minimal, just the master record.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.7, master row only):

```sql
CREATE TABLE suppliers (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  phone text, address text,
  brand_or_category text, notes text,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

Create `backend/src/StoreOS.Domain/Suppliers/Supplier.cs`:

```csharp
namespace StoreOS.Domain.Suppliers;

public class Supplier
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string? Address { get; private set; }
    public string? BrandOrCategory { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Supplier() { }

    public static Supplier Create(Guid tenantId, string name, string? phone, string? address,
        string? brandOrCategory, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Supplier name is required.", nameof(name));
        return new Supplier
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(), Phone = phone,
            Address = address, BrandOrCategory = brandOrCategory, Notes = notes, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string name, string? phone, string? address, string? brandOrCategory, string? notes)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Supplier name is required.", nameof(name));
        Name = name.Trim(); Phone = phone; Address = address; BrandOrCategory = brandOrCategory; Notes = notes;
    }
}
```

Apply the tenant-only RLS archetype (Suppliers is tenant-wide, not branch-scoped — Requirements
§13.1 lists it as tenant-wide master data, purchased-from at various branches but the master
record itself has no `branch_id`).

Follow-up migration step (SAME migration file, second half of `Up()`): add the FK to
`purchases.supplier_id` now that `suppliers` exists:

```sql
ALTER TABLE purchases ADD CONSTRAINT fk_purchases_supplier
    FOREIGN KEY (supplier_id) REFERENCES suppliers(id);
```

Create basic CRUD following the P-402/P-310 vertical-slice pattern:
`StoreOS.Application/Suppliers/CreateSupplier/`, `GetSuppliers/` (paginated, search by name),
`GetSupplierById/`, `UpdateSupplier/` — all `IRequiresPermission` with `purchases:manage`
(supplier master management is part of the purchasing workflow per Requirements §13, not a
separate high-impact permission). `SuppliersController.cs` with the four standard routes.

## 4. TASK

1. Create `Supplier.cs` exactly as shown in `StoreOS.Domain/Suppliers/`.
2. Create the EF configuration (snake_case columns, tenant-only filter).
3. Add `DbSet<Supplier> Suppliers` to `StoreOsDbContext`, add tenant-only query filter to
   `QueryFilters.ApplyAll`.
4. Generate migration `014_SuppliersMaster` including: the `CREATE TABLE suppliers`, its RLS
   policy, AND the follow-up `ALTER TABLE purchases ADD CONSTRAINT fk_purchases_supplier ...`
   in the same migration's `Up()` (with a matching `DROP CONSTRAINT` at the top of `Down()`,
   before `DROP TABLE suppliers`).
5. Apply the migration; re-run `RlsCoverageTests`.
6. Create the four CRUD vertical slices and `SuppliersController.cs` exactly as described.
7. Write `StoreOS.IntegrationTests/Suppliers/SuppliersCrudTests.cs`: standard create/list/
   search/update coverage, plus a specific test that a `Purchase` referencing a valid
   `supplier_id` now enforces referential integrity (attempting to reference a nonexistent
   supplier id in a NEW stock-in call fails with a foreign-key violation translated to a clean
   422/400, not a raw 500 — add this translation if not already generically handled).

## 5. HARD CONSTRAINTS

- This prompt does NOT build supplier transactions, payables, or
  returns — those are Phase 6 (P-605 onward). Keep scope minimal: master record + FK retrofit
  only.
- The FK retrofit on `purchases.supplier_id` must happen in THIS migration, immediately after
  `suppliers` is created, closing the gap deliberately left open by P-409.

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
dotnet ef migrations add 014_SuppliersMaster --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter SuppliersCrudTests
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
   `BLOCKED: P-411 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-411 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-411` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Supplier` signature, migration name `014_SuppliersMaster`, confirmation
the `purchases.supplier_id` FK is now enforced, and the four `SuppliersController` routes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-411: <one-line summary>"` only after verification is
   fully green.
