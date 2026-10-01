# P-805 — GSTR-1 export grouped by GSTIN

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-805** of the StoreOS Cloud build ("GSTR-1 export grouped by GSTIN") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-804 complete: inventory export works with the physical/service split.
- No GSTR-1 export exists yet (Requirements Section 27).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** add `GET /api/exports/gstr1?from=&to=&format=csv|json` (default `csv`; the JSON shape is the existing DTO). CSV: one file per request with a leading `Gstin` column so multi-GSTIN groups stay in one download; columns `Gstin,InvoiceNumber,Date,CustomerName,CustomerGstin,Hsn,Category,TaxableValue,Cgst,Sgst,TotalValue`, one row per invoice, sorted by GSTIN then invoice number, plus a `TOTAL` row per GSTIN group; same BOM/quoting/Content-Disposition rules as P-804 (`gstr1-<from>-<to>.csv`). Treatment of complimentary (gift) lines and the exact GSTR-1 table mapping (B2B/B2CL/B2CS/HSN summary) are OPEN QUESTIONS for the shop's CA (DECISIONS O-02) — do not invent them; export exactly what is stored.

Create `backend/src/StoreOS.Application/Reporting/Gstr1Export/GetGstr1ExportQuery.cs`:

```csharp
public record GetGstr1ExportQuery(DateOnly From, DateOnly To) : IRequest<Gstr1ExportDto>;

public record Gstr1RowDto(string InvoiceNumber, DateOnly Date, string CustomerName,
    string? CustomerGstin, decimal TaxableValue, decimal Cgst, decimal Sgst, decimal TotalValue,
    string? HsnOrSac, string Category);

public record Gstr1GroupDto(string Gstin, IReadOnlyList<Gstr1RowDto> Rows, decimal TotalTaxable,
    decimal TotalCgst, decimal TotalSgst, decimal GrandTotal);

public record Gstr1ExportDto(IReadOnlyList<Gstr1GroupDto> ByGstin);
```

Implement exactly per Requirements Section 27 and Plan's GSTIN-grouping decision (Plan Section
2 Decision 7: "GSTR-1 export groups by GSTIN"): ONLY Active TaxInvoices within the `[From, To]`
date range are included (explicitly excluding Estimates and Voided/Refunded sales - reuse the
exact same three-way exclusion pattern from P-801's "today's revenue"). Group rows by the
SELLING BRANCH's `gstin` (from `branches.gstin`, Plan Section 8.2 - a branch attribute, NOT the
tenant or the customer's GSTIN) since Requirements Section 27 lists "Customer GSTIN" as a
per-row detail field, but the GROUPING key per Plan Section 2 Decision 7 is about the SELLER's
(the shop's) GSTIN, which can differ per branch. One `Gstr1GroupDto` per distinct branch GSTIN
represented in the date range's active tax invoices; branches sharing the same GSTIN are
grouped TOGETHER into one group (per Plan's explicit "Branches may share one GSTIN" note).

Add `GET /api/exports/gstr1?from=&to=` to `ExportsController.cs`.

## 4. TASK

1. Create `GetGstr1ExportQuery.cs`, handler, and DTOs in
   `StoreOS.Application/Reporting/Gstr1Export/`, implementing the GSTIN-based grouping (by
   SELLING branch's GSTIN, not customer GSTIN) and the three-way exclusion (Estimate/Voided/
   Refunded) exactly as described.
2. Add the route to `ExportsController.cs`.
3. Write `StoreOS.IntegrationTests/Reporting/Gstr1ExportTests.cs`: (a) seed sales across two
   branches with DIFFERENT GSTINs -> two separate groups in the export, each with correct
   subtotals; (b) seed two branches SHARING the same GSTIN -> their sales are combined into ONE
   group; (c) an Estimate, a Voided sale, and a Refunded sale within the date range are all
   correctly EXCLUDED; (d) a sale outside the `[From, To]` range is excluded; (e) each group's
   `TotalTaxable`/`TotalCgst`/`TotalSgst`/`GrandTotal` exactly match the sum of its own rows.

## 5. HARD CONSTRAINTS

- Grouping is by the SELLING BRANCH's GSTIN (`branches.gstin`), not the
  customer's GSTIN - re-read Plan Section 2 Decision 7 and Section 8.2 carefully; this is an
  easy point to get backwards.
- The three-way exclusion (Estimate, Voided, Refunded) is identical in spirit to P-801's
  today's-revenue exclusion and P-802's daily-report exclusion - keep it consistent.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter Gstr1ExportTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-805 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-805 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-805` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Gstr1ExportDto`/`Gstr1GroupDto` shapes, the
`GET /api/exports/gstr1` route, and confirmation grouping is by SELLING branch GSTIN.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-805: <one-line summary>"` only after verification is
   fully green.
