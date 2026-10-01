# P-804 — Inventory valuation/report export endpoint (multi-category, service-specific representation)

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-804** of the StoreOS Cloud build ("Inventory valuation/report export endpoint (multi-category, service-specific representation)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-803 complete: branch comparison reporting works.
- No inventory export endpoint exists yet (Requirements Section 26).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the Requirements call this a "spreadsheet" export but this prompt only defined a DTO. Add `GET /api/exports/inventory?categories=&branchIds=&format=csv|json` (default `csv`). CSV rules: UTF-8 WITH BOM (Excel opens it correctly), RFC 4180 quoting, `Content-Disposition: attachment; filename="inventory-<yyyyMMdd>.csv"`, decimal values with `.` and two decimals, dates `yyyy-MM-dd`. Physical categories use columns `Item,Category,Qty,PurchasePrice,SellPrice,GstRate,Hsn,Supplier,ImeiCount`; Repair services are written as a SECOND section-file (`?categories=RepairService` → `Service,Description,ServiceCharge,Taxable,GstRate,Sac`) and never share the physical-inventory columns. Hide PurchasePrice when the caller lacks `inventory:cost:view`. No new NuGet package is needed (write CSV with a small `CsvWriter` helper in Application; unit-test quoting of commas, quotes and newlines). An `.xlsx` variant is an optional later enhancement (would need a package added to P-004 first).

Create `backend/src/StoreOS.Application/Reporting/InventoryExport/GetInventoryExportQuery.cs`:

```csharp
public record GetInventoryExportQuery(string[] Categories, Guid[]? BranchIds) : IRequest<InventoryExportDto>;

public record PhysicalInventoryExportRowDto(string Item, string Category, int Quantity,
    decimal PurchasePrice, decimal SellingPrice, decimal Gst, string? Hsn, string? Supplier,
    string? ImeiSummary);
public record ServiceExportRowDto(string Service, string? Description, decimal ServiceCharge,
    bool Taxable, decimal Gst, string? Sac);

public record InventoryExportDto(IReadOnlyList<PhysicalInventoryExportRowDto> PhysicalItems,
    IReadOnlyList<ServiceExportRowDto> Services);
```

Per Requirements Section 26.1/26.2: the caller selects MULTIPLE categories at once (e.g.
`["NewPhone","UsedPhone","Accessory","RepairService"]` or any subset). Physical categories
(`NewPhone`/`UsedPhone`/`Accessory`) populate `PhysicalItems` with full stock/price/HSN/supplier
columns; `RepairService` populates the SEPARATE `Services` list with its OWN
service-appropriate columns (Requirements Section 26.2: "Repair services should not be forced
into physical-inventory columns... Use a service-specific representation") - implement this as
TWO DISTINCT result lists in the SAME response, never a single unified table row-shape that
tries to cram both concepts together.

`ImeiSummary` for an IMEI-tracked item is a simple aggregate display string (e.g. "12 in stock,
3 IMEIs listed" or a comma-joined list of currently-`InStock` IMEIs if the product line count is
small - choose a REASONABLE, simple aggregate rather than an unbounded list; cap at listing up
to 10 IMEIs inline with a "+N more" suffix beyond that, to keep the export usable).

Add `GET /api/exports/inventory?categories=&branchIds=` to a new `ExportsController.cs`
(Plan Section 11 lists exports under a distinct `/exports/` prefix, separate from
`/reports/`).

## 4. TASK

1. Create `GetInventoryExportQuery.cs`, handler, and DTOs in
   `StoreOS.Application/Reporting/InventoryExport/`, implementing the two-list
   physical-vs-service separation exactly as described.
2. Create `ExportsController.cs` with `GET /api/exports/inventory`.
3. Write `StoreOS.IntegrationTests/Reporting/InventoryExportTests.cs`: (a) requesting all 4
   categories returns physical items in `PhysicalItems` and repair services in `Services`,
   never mixed; (b) requesting only `["RepairService"]` returns an EMPTY `PhysicalItems` list
   and a populated `Services` list; (c) an IMEI-tracked item's `ImeiSummary` correctly
   summarizes/truncates for a product with more than 10 active IMEIs; (d) requesting a subset
   of categories (e.g. just `["Accessory"]`) returns only that category's physical items.

## 5. HARD CONSTRAINTS

- Physical items and services are ALWAYS two separate lists in the response, never
  forced into one unified row shape - this is Requirements Section 26.2's explicit,
  named requirement.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter InventoryExportTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-804 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-804 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-804` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `InventoryExportDto` two-list shape and the
`GET /api/exports/inventory` route.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-804: <one-line summary>"` only after verification is
   fully green.
