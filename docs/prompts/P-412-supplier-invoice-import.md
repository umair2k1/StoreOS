# P-412 — Supplier invoice import pipeline (deterministic, dedupe on import_batch)

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-412** of the StoreOS Cloud build ("Supplier invoice import pipeline (deterministic, dedupe on import_batch)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-411 complete: suppliers master + FK exist; stock-in works.
- No import pipeline exists. Per Requirements §12.4 and Plan §20 (Out of Scope), this MUST be
  fully deterministic — NO AI/ML product-matching is permitted anywhere in this pipeline.
  Ambiguous mappings are surfaced to the user for MANUAL confirmation, never guessed.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.10, `import_batches`):

```sql
CREATE TABLE import_batches (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NULL,
  source_type text NOT NULL,
  source_ref text NOT NULL,
  status text NOT NULL,
  record_counts jsonb,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

Create `backend/src/StoreOS.Domain/Platform/ImportBatch.cs` with `SourceType` constants
(`"SupplierInvoice"`, `"Excel"`, `"JsonBackup"`) and `Status` constants (`"Pending"`,
`"Imported"`, `"Failed"`), factory `Create(...)`, and methods `MarkImported(recordCountsJson)`
/ `MarkFailed(reasonJson)`.

The import pipeline accepts a STRUCTURED input DTO (parsing raw file formats like PDF/Excel
supplier invoices into this structured shape is a SEPARATE, later concern for the frontend
upload UI in P-1111 — this backend prompt operates on already-structured JSON, matching
Requirements §12.4's numbered steps 1-9):

```csharp
public record SupplierInvoiceImportLine(string ProductNameRaw, string? HsnCode, int Qty,
    decimal Rate, string? Imei);

public record ImportSupplierInvoiceCommand(Guid SupplierId, string SupplierInvoiceNo,
    DateOnly InvoiceDate, string SourceRef, IReadOnlyList<SupplierInvoiceImportLine> Lines)
    : IRequest<ImportResult>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}

public record ImportResult(Guid ImportBatchId, string Status, int ProductsMatched,
    int ProductsAmbiguous, IReadOnlyList<AmbiguousLine> AmbiguousLines, int PurchasesCreated);

public record AmbiguousLine(string ProductNameRaw, IReadOnlyList<Guid> CandidateProductIds);
```

Handler logic (deterministic matching, per Requirements §12.4 steps): (1) check
`import_batches` for an EXISTING row with the SAME `(tenant_id, source_ref)` — if found and
`Status == "Imported"`, return the EXISTING result WITHOUT reprocessing (dedupe on re-import of
the identical file, per Requirements §12.4 step 7); (2) for each line, attempt an EXACT
case-insensitive name match against `products.name` for the tenant — if EXACTLY ONE match,
proceed; if ZERO matches, treat as "no match" (report separately, do not silently skip — surface
as needing manual product creation, add to `AmbiguousLines` with an empty candidate list); if
MULTIPLE matches (e.g. two products with the same name in different categories), add to
`AmbiguousLines` with all candidate IDs and DO NOT auto-pick one; (3) for lines with exactly one
match, call `StockInHandler`'s underlying logic (reuse `IInventoryStockService` +
`Purchase.Create` + `InventoryImei.Create` directly, do not go through the full `StockInCommand`
HTTP-level indirection — factor the core stock-in logic into a shared internal method both
`StockInHandler` and this handler call, OR simply call `IMediator.Send(new StockInCommand(...))`
from within this handler, which is simpler and acceptable since MediatR handlers CAN dispatch
other requests) for each unambiguous line; (4) create the `import_batches` row with
`record_counts` JSON summarizing matched/ambiguous/created counts, `Status = "Imported"` ONLY
if there were zero ambiguous lines, else `Status = "Pending"` (partial import awaiting manual
resolution — Requirements §12.4 step 8: "Require user confirmation for ambiguous product
mappings").

## 4. TASK

1. Create `ImportBatch.cs` in `StoreOS.Domain/Platform/`, its EF configuration
   (tenant-only filter, `branch_id` nullable so treat as the "nullable branch" query-filter
   variant from P-107's documented convention), migration `015_ImportBatches` with RLS.
2. Create the full vertical slice
   `StoreOS.Application/Purchasing/ImportSupplierInvoice/` with the command, validator (Lines
   non-empty, each line Qty>0 and Rate>=0), and handler implementing the deterministic matching
   logic exactly as described, dispatching `StockInCommand` via `IMediator.Send` for each
   unambiguous line.
3. Create `POST /api/imports/supplier-invoice` on `PurchasesController.cs` (or a new
   `ImportsController.cs` — prefer a new dedicated controller since Plan §11's API surface
   table lists this under a general `/imports/` prefix).
4. Write `StoreOS.IntegrationTests/Purchasing/SupplierInvoiceImportTests.cs`: (a) import with
   all lines matching exactly one product each -> `Status="Imported"`, correct
   `PurchasesCreated` count, stock increased correctly for each; (b) import with one line
   matching ZERO products and one line matching TWO products (seed a duplicate-name scenario)
   -> `Status="Pending"`, `AmbiguousLines` correctly lists both problem lines (one with an
   empty candidate list, one with two candidate IDs), and the UNAMBIGUOUS lines in the SAME
   batch were STILL processed (partial success, not all-or-nothing failure) — confirm this
   matches Requirements §12.4's intent of routing only the ambiguous ones to manual mapping,
   not blocking the whole invoice; (c) importing the EXACT SAME `SourceRef` a second time
   returns the cached `ImportResult` without creating duplicate `Purchase`/stock rows (dedupe
   proof, Requirements §12.4 step 7).

## 5. HARD CONSTRAINTS

- NO fuzzy matching, NO Levenshtein distance, NO ML/AI product
  matching of any kind — ONLY exact case-insensitive string equality on `products.name`. This
  is a hard, permanent constraint per Requirements §39/Plan §20's explicit prohibition. If a
  name doesn't match exactly, it goes to the ambiguous/unmatched bucket for a human to resolve
  — never guess.
- Dedup key is `(tenant_id, source_ref)` on `import_batches` — an identical `SourceRef`
  re-submitted must never create duplicate purchases/stock movements.
- A batch with SOME ambiguous lines still processes the UNAMBIGUOUS lines immediately (partial
  success) rather than failing the entire import — this matches "avoid creating duplicate stock
  when the same invoice is imported twice" while also not blocking legitimate lines on account
  of one bad line elsewhere in the same file.

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
dotnet ef migrations add 015_ImportBatches --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter SupplierInvoiceImportTests
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
   `BLOCKED: P-412 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-412 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-412` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ImportSupplierInvoiceCommand`/`ImportResult`/`AmbiguousLine` shapes, the
`POST /api/imports/supplier-invoice` route, migration name `015_ImportBatches`, and the
exact-match-only (no fuzzy/AI matching) rule.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-412: <one-line summary>"` only after verification is
   fully green.
