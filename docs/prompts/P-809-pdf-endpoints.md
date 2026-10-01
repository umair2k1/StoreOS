# P-809 — GET /sales/{id}/invoice.pdf, /repair-jobs/{id}/ticket.pdf, /supplier-returns/{id}/note.pdf endpoints

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-809** of the StoreOS Cloud build ("GET /sales/{id}/invoice.pdf, /repair-jobs/{id}/ticket.pdf, /supplier-returns/{id}/note.pdf endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-808 complete: all PDF renderers (invoice, repair ticket, supplier
  return note, daily sales report, inventory export) exist as pure DTO-to-bytes functions.
- No HTTP endpoint exposes any of them yet - this prompt wires the mapping-from-entities step
  and the controller actions that tie everything together.

## 3. CONTEXT BLOCK

For EACH of the three primary document types, create a small mapping
function (entity data -> the renderer's flat DTO) living alongside the relevant controller,
NOT inside the renderer itself (preserving P-806's DTO-purity rule):

`SalesController.cs` addition:
```csharp
[HttpGet("{id:guid}/invoice.pdf")]
public async Task<IActionResult> GetInvoicePdf(Guid id, [FromServices] InvoicePdfRenderer renderer,
    [FromServices] IMediator mediator, CancellationToken ct)
{
    var detail = await mediator.Send(new GetSaleByIdQuery(id), ct); // reuses P-522's existing query
    var dto = MapToInvoicePdfDto(detail); // small private mapping method in this controller,
                                           // pulling in shop branding via a settings lookup
    var bytes = renderer.Render(dto);
    return File(bytes, "application/pdf", $"{detail.InvoiceNumber}.pdf");
}
```

Write the FULL `MapToInvoicePdfDto` mapping method yourself, resolving shop branding
(name/address/phone/email/GSTIN) from the branch-level `"shop_profile"` setting (P-103's
reserved key) via a small settings-read helper (query `Setting` where `key = "shop_profile"`
and `branch_id = sale's branch`, deserialize its JSON) - if no shop-profile setting exists yet
for that branch, fall back gracefully to using `branches.name`/`address`/`phone`/`gstin`
directly as a reasonable default (never throw or fail PDF generation just because onboarding
customization hasn't been completed yet).

`RepairJobsController.cs` addition: `GET /api/repair-jobs/{id}/ticket.pdf` similarly mapping
`RepairJob` detail data to `RepairPdfDto`.

`SupplierReturnsController.cs` addition: `GET /api/supplier-returns/{id}/note.pdf` similarly.

Per Requirements Section 40's "Printer failure" rule and Plan's explicit "Printing failure never
rolls back a completed sale" invariant: these GET endpoints are READ-ONLY PDF generation calls
happening entirely AFTER a sale/job/return is already committed - there is no risk of a PDF
generation failure rolling back a business transaction here (they're separate, later requests),
but DO ensure a PDF-rendering exception (e.g. a QuestPDF layout error) is caught and surfaces as
a clean 500 with a correlation id via the EXISTING generic exception handler (P-113), rather
than corrupting the HTTP response stream mid-write.

## 4. TASK

1. Add `GET /api/sales/{id}/invoice.pdf` to `SalesController.cs`, with a private
   `MapToInvoicePdfDto` method resolving shop branding from the `"shop_profile"` setting with a
   graceful fallback to branch fields.
2. Add `GET /api/repair-jobs/{id}/ticket.pdf` to `RepairJobsController.cs` with an equivalent
   mapping method.
3. Add `GET /api/supplier-returns/{id}/note.pdf` to `SupplierReturnsController.cs` with an
   equivalent mapping method.
4. Write `StoreOS.IntegrationTests/Printing/PdfEndpointsTests.cs`: (a) each of the three
   endpoints returns `200` with `Content-Type: application/pdf` and a non-trivial byte length
   for a fully-populated record; (b) the invoice PDF endpoint works correctly even when NO
   `"shop_profile"` setting exists yet, falling back to branch fields without error; (c) a
   nonexistent id on any of the three endpoints returns 404 (reusing each resource's existing
   not-found handling).

## 5. HARD CONSTRAINTS

- The entity-to-DTO mapping lives in each CONTROLLER, never inside the renderer
  classes themselves - this preserves the DTO-purity boundary established in P-806.
- Missing shop-profile settings must NEVER cause PDF generation to fail - always fall back
  gracefully to branch master-data fields.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter PdfEndpointsTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-809 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-809 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-809` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the three new PDF-download routes and confirmation the mapping logic
lives in each controller, not in the shared renderers.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-809: <one-line summary>"` only after verification is
   fully green.
