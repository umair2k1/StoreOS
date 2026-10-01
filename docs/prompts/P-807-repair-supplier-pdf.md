# P-807 — QuestPDF repair ticket/invoice + supplier return note templates

**Phase:** 8 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-807** of the StoreOS Cloud build ("QuestPDF repair ticket/invoice + supplier return note templates") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-806 complete: invoice PDF rendering works with the DTO-purity
  pattern established.
- No repair ticket/invoice or supplier return note templates exist yet (Requirements Section
  38's printing requirements list).

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Infrastructure/Printing/RepairPdfDto.cs`
and `RepairPdfRenderer.cs`, following the EXACT same DTO-purity pattern as `InvoicePdfRenderer`
(P-806):

```csharp
public record RepairPartLineDto(string PartName, int Quantity, decimal ChargeAmount);

public record RepairPdfDto(string ShopName, string? ShopAddress, string? ShopPhone,
    string JobNumber, DateTimeOffset ReceivedAt, string CustomerName, string? CustomerPhone,
    string? DeviceBrand, string? DeviceModel, string? SerialNumber, string? Imei1,
    string? ProblemDescription, string Status, decimal? EstimatedCost, decimal? FinalTotal,
    decimal? AdvancePaid, decimal? BalanceDue, DateOnly? WarrantyUntil,
    IReadOnlyList<RepairPartLineDto> Parts, string? InvoiceNumber);
```

`RepairPdfRenderer.Render(RepairPdfDto dto) : byte[]` uses the same QuestPDF fluent pattern as
`InvoicePdfRenderer` - header (shop info + job number + received date), body (device/problem
detail, parts table), footer (commercial totals, warranty date if delivered, linked invoice
number if billed).

Create `backend/src/StoreOS.Infrastructure/Printing/SupplierReturnPdfDto.cs` and
`SupplierReturnPdfRenderer.cs`:

```csharp
public record SupplierReturnLineDto(string ItemName, int Qty, decimal ReturnRate, decimal LineTotal, string[]? ImeiNumbers);

public record SupplierReturnPdfDto(string ShopName, string ReturnNumber, DateOnly ReturnDate,
    string SupplierName, string? SupplierPhone, string? SupplierAddress,
    IReadOnlyList<SupplierReturnLineDto> Lines, decimal TotalAmount, string SettlementType,
    string? Reason);
```

Same rendering pattern: header (shop + return number + date + supplier), table of lines, footer
(total + settlement type).

## 4. TASK

1. Create `RepairPdfDto.cs`/`RepairPdfRenderer.cs` in
   `StoreOS.Infrastructure/Printing/`, following P-806's exact structural pattern (QuestPDF
   fluent document, header/content/footer, DTO-only input).
2. Create `SupplierReturnPdfDto.cs`/`SupplierReturnPdfRenderer.cs` similarly.
3. Register both renderers as singletons.
4. Write `StoreOS.IntegrationTests/Printing/RepairAndSupplierReturnPdfTests.cs`: (a)
   `RepairPdfRenderer` renders successfully with full data and with all-optional-fields-null
   (a job just received, no commercials yet); (b) `SupplierReturnPdfRenderer` renders
   successfully with multiple lines including IMEI-bearing ones.

## 5. HARD CONSTRAINTS

- Both renderers follow the EXACT DTO-purity pattern established in P-806 - no
  database access, no entity types, flat DTOs only.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RepairAndSupplierReturnPdfTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-807 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-807 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-807` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RepairPdfDto`/`SupplierReturnPdfDto` shapes and both renderer
signatures.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-807: <one-line summary>"` only after verification is
   fully green.
