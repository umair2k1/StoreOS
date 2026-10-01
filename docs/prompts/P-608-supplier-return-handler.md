# P-608 — POST /supplier-returns handler (atomic: header+lines+stock+IMEI+ledger)

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-608** of the StoreOS Cloud build ("POST /supplier-returns handler (atomic: header+lines+stock+IMEI+ledger)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-607 complete: supplier return schema exists.
- All of `IInventoryStockService` (P-406/408), `ISupplierLedgerService` (P-606),
  `InvoiceSequenceAllocator` (P-501, reused for return numbering) are available. This prompt
  assembles the full atomic supplier-return transaction per Plan Section 10.4.

## 3. CONTEXT BLOCK

Create `backend/src/StoreOS.Application/Suppliers/CreateReturn/CreateSupplierReturnCommand.cs`:

```csharp
public record ReturnLineInput(Guid? ProductId, string ItemName, int Qty, decimal PurchasePrice,
    decimal ReturnRate, string[]? ImeiNumbers);

public record CreateSupplierReturnCommand(Guid SupplierId, DateOnly ReturnDate,
    IReadOnlyList<ReturnLineInput> Lines, string SettlementType, string? Reason, string? Notes)
    : IRequest<CreateSupplierReturnResult>, IRequiresPermission
{
    public string RequiredPermission => "purchases:manage";
}

public record CreateSupplierReturnResult(Guid ReturnId, string ReturnNumber, decimal TotalAmount);
```

`CreateSupplierReturnHandler.cs` implements Plan Section 10.4/14.5 atomically:

```csharp
public class CreateSupplierReturnHandler : IRequestHandler<CreateSupplierReturnCommand, CreateSupplierReturnResult>
{
    // constructor injects: StoreOsDbContext, IAccessScope, IBranchContext, IInventoryStockService,
    // ISupplierLedgerService, InvoiceSequenceAllocator, IAuditWriter

    public async Task<CreateSupplierReturnResult> Handle(CreateSupplierReturnCommand request, CancellationToken ct)
    {
        var branchId = _branchContext.CurrentBranchId!.Value;
        var branch = await _db.Branches.FirstAsync(b => b.Id == branchId, ct);
        var supplier = await _db.Set<Supplier>().FirstOrDefaultAsync(s => s.Id == request.SupplierId, ct)
            ?? throw new DomainValidationException("supplier_not_found", "Supplier not found.");

        var seq = await _sequenceAllocator.AllocateNextAsync(_scope.TenantId, branchId,
            $"RETURN-{request.ReturnDate:yyyyMMdd}", ct);
        var returnNumber = SupplierReturnNumberFormatter.Format(branch.Code, request.ReturnDate, seq);

        var supplierReturn = SupplierReturn.Create(_scope.TenantId, branchId, returnNumber,
            request.ReturnDate, request.SupplierId, supplier.Phone, supplier.Address,
            request.SettlementType, request.Reason, request.Notes, _scope.UserId);

        decimal totalAmount = 0; int totalQty = 0;

        foreach (var line in request.Lines)
        {
            // Conditional stock decrement — fails if insufficient at THIS branch (Plan §10.4)
            if (line.ProductId is not null)
                await _stockService.ReserveAndDecrementStockAsync(branchId, line.ProductId.Value, line.Qty,
                    StoreOS.Domain.Inventory.MovementType.SupplierReturn, "SupplierReturn", "", ct);
                // NOTE: refId is set to the return's id AFTER the return itself is added and
                // flushed — reorder as needed so a real, non-empty refId is always used (do not
                // ship an empty-string refId; flush the SupplierReturn header first to get its
                // real Id, matching the P-510 ordering lesson).

            var lineTotal = StoreOS.Domain.Common.Money.Round2(line.Qty * line.ReturnRate);
            var item = SupplierReturnItem.Create(_scope.TenantId, supplierReturn.Id, line.ProductId,
                line.ItemName, line.Qty, line.PurchasePrice, line.ReturnRate, lineTotal, line.ImeiNumbers);
            supplierReturn.AddItem(item);
            totalAmount += lineTotal; totalQty += line.Qty;

            if (line.ImeiNumbers is { Length: > 0 })
            {
                foreach (var imeiStr in line.ImeiNumbers)
                {
                    var imeiRow = await _db.Set<InventoryImei>()
                        .FirstOrDefaultAsync(i => i.TenantId == _scope.TenantId && i.BranchId == branchId && i.Imei == imeiStr, ct)
                        ?? throw new DomainValidationException("imei_not_found", $"IMEI {imeiStr} not found at this branch.");
                    imeiRow.MarkReturnedToSupplier(supplierReturn.Id);
                }
            }
        }

        // finalize totals on the header (add a SetTotals(qty, amount) method to SupplierReturn if not present)
        supplierReturn.SetTotals(totalQty, totalAmount);
        await _db.Set<SupplierReturn>().AddAsync(supplierReturn, ct);

        // Ledger posting by settlement type (Plan §10.4):
        switch (request.SettlementType)
        {
            case "PayableDeduction":
                await _supplierLedger.RecordReturnCreditAsync(request.SupplierId, totalAmount, branchId, request.ReturnDate, supplierReturn.Id.ToString(), ct);
                break;
            case "CashRefund":
                // Cash refund doesn't reduce payable — it's an immediate cash transaction outside the ledger's
                // payable tracking; still worth an audit trail but NOT a ledger entry, since no payable existed
                // to reduce for a pure cash-in-hand refund scenario. Document this explicitly.
                break;
            case "PendingCredit":
                // Recorded but not yet applied — still worth a ledger credit marking the credit as
                // available, using the SAME ReturnCredit entry type; a later manual application
                // against a future bill is a separate transaction (Payment/Bill), not modeled specially here.
                await _supplierLedger.RecordReturnCreditAsync(request.SupplierId, totalAmount, branchId, request.ReturnDate, supplierReturn.Id.ToString(), ct);
                break;
        }

        await _audit.WriteAsync(branchId, "supplier_return.created", "SupplierReturn", supplierReturn.Id.ToString(),
            new { returnNumber, totalAmount, request.SettlementType }, ct);

        await _db.SaveChangesAsync(ct);
        return new CreateSupplierReturnResult(supplierReturn.Id, returnNumber, totalAmount);
    }
}
```

Fix the `refId` ordering issue noted inline: flush the `SupplierReturn` header (a preliminary
`SaveChangesAsync` right after creating it, before the loop) so `supplierReturn.Id` is
committed and can be used as a real `refId` string in the stock-service calls within the loop —
mirroring the exact lesson learned in P-510.

Add `POST /api/supplier-returns` to a new `SupplierReturnsController.cs`.

## 4. TASK

1. Fix the `refId`-ordering issue by flushing the header via a preliminary
   `SaveChangesAsync` before the line loop, exactly as noted.
2. Write the complete `CreateSupplierReturnCommand.cs`/handler in
   `StoreOS.Application/Suppliers/CreateReturn/`, reconciling all signatures against actual
   prior prompts' outputs (add `SupplierReturn.SetTotals(...)` if not already present from
   P-607).
3. Create `SupplierReturnsController.cs` with `POST /api/supplier-returns`.
4. Write `StoreOS.IntegrationTests/Suppliers/CreateSupplierReturnTests.cs`: (a) a return with
   a non-IMEI line correctly decrements stock and creates a `SupplierReturn` movement; (b) a
   return with an IMEI line correctly transitions the IMEI to `ReturnedToSupplier` (and confirms
   it becomes UNAVAILABLE for sale afterward, reusing `LoadImeiForSaleAsync`'s existing guard —
   Invariant 8); (c) `PayableDeduction` settlement correctly reduces the supplier's payable
   (via `GetPayableAsync`); (d) `CashRefund` settlement does NOT affect the payable; (e)
   `PendingCredit` settlement DOES post a return credit (per the documented choice); (f)
   returning MORE than available stock -> `insufficient_stock`, whole transaction rolls back,
   nothing persisted; (g) the `return_number` is unique and correctly formatted.

## 5. HARD CONSTRAINTS

- The whole operation (header, lines, stock, IMEI, ledger, audit) is
  ATOMIC — a single `SaveChangesAsync()` at the end (plus the ONE necessary preliminary flush
  for the header's id, which is a documented, narrow exception mirroring P-510's pattern, not a
  violation of the single-commit principle since both saves are still within the SAME ambient
  transaction and both roll back together on any later failure).
- `CashRefund` settlement deliberately does NOT post a ledger entry — document this choice
  clearly since it might look like a bug to a future reader; it is a deliberate design decision.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CreateSupplierReturnTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-608 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-608 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-608` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CreateSupplierReturnCommand`/handler signatures, the
`POST /api/supplier-returns` route, and the documented settlement-type-to-ledger-posting
behavior (PayableDeduction and PendingCredit post; CashRefund does not).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-608: <one-line summary>"` only after verification is
   fully green.
