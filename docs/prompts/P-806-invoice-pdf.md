# P-806 — QuestPDF invoice/estimate template (DTO-only, IRN/QR slots reserved)

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-806** of the StoreOS Cloud build ("QuestPDF invoice/estimate template (DTO-only, IRN/QR slots reserved)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-805 complete: GSTR-1 export works.
- No PDF generation exists anywhere yet. QuestPDF (pinned in P-004) will be used throughout
  Phase 8's remaining prompts. Per Plan Section 3, templates receive DTOs only - never raw
  entities or live database access from within a template.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the shop logo (if any) is read with `IObjectStorageService.GetAsync("storeos-media", <setting branding.logo_key>)` (P-1009). A missing logo or a storage failure must never fail PDF generation: render the header without the image.

Add `QuestPDF.Settings.License = QuestPDF.Infrastructure.LicenseType.Community;`
to `Program.cs` startup (required one-time global configuration for QuestPDF's community
license).

Create `backend/src/StoreOS.Infrastructure/Printing/InvoicePdfDto.cs` - a FLAT, print-ready DTO
containing EVERYTHING the template needs, with NO further data access required once
constructed:

```csharp
public record InvoicePdfLineDto(string ItemName, string? Hsn, int Qty, decimal PricePerUnit,
    decimal Taxable, decimal Cgst, decimal Sgst, string? Imei, bool IsGift);

public record InvoicePdfDto(string ShopName, string? ShopAddress, string? ShopPhone,
    string? ShopEmail, string? ShopGstin, string InvoiceNumber, string InvoiceType,
    DateTimeOffset SaleDate, string CustomerName, string? CustomerGstin,
    IReadOnlyList<InvoicePdfLineDto> Lines, decimal TotalTaxable, decimal TotalCgst,
    decimal TotalSgst, decimal Discount, decimal GrandTotal, decimal AmountPaid, decimal Balance,
    string? PaymentMode, string? FinanceProvider, string? FinancerName, string? Description,
    string? IrnDisplay, string? IrnQrDataUrl // both null until Phase 8 (roadmap) e-invoicing activates; slots reserved now
);
```

Create `backend/src/StoreOS.Infrastructure/Printing/InvoicePdfRenderer.cs` using QuestPDF's
fluent document API:

```csharp
using QuestPDF.Fluent;
using QuestPDF.Helpers;
using QuestPDF.Infrastructure;

namespace StoreOS.Infrastructure.Printing;

public class InvoicePdfRenderer
{
    public byte[] Render(InvoicePdfDto dto)
    {
        var document = Document.Create(container =>
        {
            container.Page(page =>
            {
                page.Size(PageSizes.A4);
                page.Margin(30);
                page.Header().Column(col =>
                {
                    col.Item().Text(dto.ShopName).FontSize(18).Bold();
                    if (dto.ShopAddress is not null) col.Item().Text(dto.ShopAddress);
                    if (dto.ShopPhone is not null) col.Item().Text($"Phone: {dto.ShopPhone}");
                    if (dto.ShopGstin is not null) col.Item().Text($"GSTIN: {dto.ShopGstin}");
                    col.Item().PaddingTop(10).Text($"{dto.InvoiceType} #{dto.InvoiceNumber}").FontSize(14).Bold();
                    col.Item().Text($"Date: {dto.SaleDate:dd-MMM-yyyy HH:mm}");
                    col.Item().Text($"Customer: {dto.CustomerName}" + (dto.CustomerGstin is not null ? $" (GSTIN: {dto.CustomerGstin})" : ""));
                });
                page.Content().Table(table =>
                {
                    table.ColumnsDefinition(c => { c.RelativeColumn(3); c.RelativeColumn(1); c.RelativeColumn(1);
                        c.RelativeColumn(1); c.RelativeColumn(1); c.RelativeColumn(1); });
                    table.Header(h =>
                    {
                        h.Cell().Text("Item").Bold(); h.Cell().Text("Qty").Bold(); h.Cell().Text("Price").Bold();
                        h.Cell().Text("Taxable").Bold(); h.Cell().Text("CGST").Bold(); h.Cell().Text("SGST").Bold();
                    });
                    foreach (var line in dto.Lines)
                    {
                        var label = line.ItemName + (line.IsGift ? " (Gift)" : "") + (line.Imei is not null ? $" [IMEI: {line.Imei}]" : "");
                        table.Cell().Text(label);
                        table.Cell().Text(line.Qty.ToString());
                        table.Cell().Text(line.PricePerUnit.ToString("F2"));
                        table.Cell().Text(line.Taxable.ToString("F2"));
                        table.Cell().Text(line.Cgst.ToString("F2"));
                        table.Cell().Text(line.Sgst.ToString("F2"));
                    }
                });
                page.Footer().Column(col =>
                {
                    col.Item().Text($"Taxable: {dto.TotalTaxable:F2}  CGST: {dto.TotalCgst:F2}  SGST: {dto.TotalSgst:F2}");
                    col.Item().Text($"Discount: {dto.Discount:F2}  Grand Total: {dto.GrandTotal:F2}").Bold();
                    col.Item().Text($"Paid: {dto.AmountPaid:F2}  Balance: {dto.Balance:F2}");
                    if (dto.FinanceProvider is not null) col.Item().Text($"Financed via: {dto.FinanceProvider} ({dto.FinancerName})");
                    if (dto.IrnDisplay is not null) col.Item().Text($"IRN: {dto.IrnDisplay}"); // reserved slot, Phase 8 roadmap
                });
            });
        });
        return document.GeneratePdf();
    }
}
```

Register scoped/singleton (stateless, safe as singleton):
`builder.Services.AddSingleton<InvoicePdfRenderer>();`

The MAPPING from a real `Sale`/`SaleItem`/`Branch` entity set to `InvoicePdfDto` happens in a
SEPARATE mapping step (inside the controller action or a small dedicated mapper class in
`StoreOS.Application`), NEVER inside the renderer itself - the renderer is pure
DTO-to-bytes, with zero database awareness, per Plan Section 3's template purity rule.

## 4. TASK

1. Add the QuestPDF community license line to `Program.cs`.
2. Create `InvoicePdfDto.cs` and `InvoicePdfRenderer.cs` exactly as shown in
   `StoreOS.Infrastructure/Printing/`, register as singleton.
3. Write `StoreOS.IntegrationTests/Printing/InvoicePdfRendererTests.cs` (or a unit test in
   `StoreOS.Domain.Tests` if you prefer, though `InvoicePdfRenderer` lives in Infrastructure so
   `StoreOS.IntegrationTests` is the correct home): (a) rendering a well-formed
   `InvoicePdfDto` returns a non-empty byte array starting with the PDF magic bytes (`%PDF`);
   (b) rendering succeeds with EVERY optional field null (minimal walk-in cash sale, no GSTIN,
   no finance, no IRN); (c) rendering with a gift line and an IMEI line both display their
   respective annotations correctly (cannot easily assert PDF text content without a PDF-parsing
   library - at minimum assert the render does not throw and produces a plausible non-trivial
   byte length for a multi-line invoice vs. a single-line one, as a smoke-level content check).

## 5. HARD CONSTRAINTS

- `InvoicePdfRenderer.Render` takes ONLY the flat DTO - it must never accept or
  internally query a `Sale`/`Product`/`Customer` entity, a `DbContext`, or any other
  database-aware type. This purity is what makes the renderer trivially unit-testable and
  reusable.
- The `IrnDisplay`/`IrnQrDataUrl` fields are reserved now (per Plan's Phase 8 e-invoicing
  roadmap item) but remain null/unused until that future work begins - do not build any real
  IRN-generation logic in this prompt.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter InvoicePdfRendererTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-806 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-806 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-806` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `InvoicePdfDto`/`InvoicePdfRenderer` signatures and the DTO-purity
rule every future PDF renderer in this codebase must also follow.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-806: <one-line summary>"` only after verification is
   fully green.
