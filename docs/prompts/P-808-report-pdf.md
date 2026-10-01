# P-808 — QuestPDF daily sales / inventory report templates

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-808** of the StoreOS Cloud build ("QuestPDF daily sales / inventory report templates") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-807 complete: repair/supplier-return PDF templates work.
- No printable daily-sales/inventory REPORT templates exist yet (Requirements Section 38's
  "Sales reports. Inventory reports.").

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Infrastructure/Printing/ReportPdfRenderer.cs`
with TWO render methods, reusing the DTOs ALREADY DEFINED in P-802 (`DailySalesReportDto`) and
P-804 (`InventoryExportDto`) directly - no new DTOs needed, since those are already flat and
print-ready:

```csharp
namespace StoreOS.Infrastructure.Printing;

public class ReportPdfRenderer
{
    public byte[] RenderDailySales(StoreOS.Application.Reporting.DailySales.DailySalesReportDto dto)
    {
        // QuestPDF document: header (date, shop context passed in separately if needed - add a
        // shopName parameter to this method signature), sales summary section, inventory
        // summary section, payment breakdown table, category breakdown table, itemized log table.
    }

    public byte[] RenderInventoryExport(StoreOS.Application.Reporting.InventoryExport.InventoryExportDto dto)
    {
        // QuestPDF document: physical items table, then a separate services table.
    }
}
```

Write out the FULL QuestPDF fluent implementation for both methods yourself, following the
exact structural conventions established in P-806/P-807 (header/content/footer sections,
`.Table(...)` for tabular data, bold section headers).

## 4. TASK

1. Create `ReportPdfRenderer.cs` in `StoreOS.Infrastructure/Printing/`,
   implementing both `RenderDailySales` and `RenderInventoryExport` fully, reusing the EXISTING
   `DailySalesReportDto`/`InventoryExportDto` types from P-802/P-804 directly (add a `shopName`
   parameter to each render method for header display, since those DTOs don't carry shop
   branding themselves).
2. Register as singleton.
3. Write `StoreOS.IntegrationTests/Printing/ReportPdfRendererTests.cs`: (a) rendering a daily
   sales report with full seeded data succeeds and produces valid PDF bytes; (b) rendering an
   inventory export with both physical and service rows succeeds; (c) rendering with EMPTY
   itemized logs / empty inventory lists (a quiet day/empty catalog) still succeeds without
   throwing.

## 5. HARD CONSTRAINTS

- Reuse `DailySalesReportDto` and `InventoryExportDto` directly - do not create
  parallel "PDF-specific" versions of these DTOs; they are already flat and suitable.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ReportPdfRendererTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-808 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-808 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-808` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ReportPdfRenderer` signature (both methods) and confirmation it
reuses existing report DTOs without duplication.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-808: <one-line summary>"` only after verification is
   fully green.
