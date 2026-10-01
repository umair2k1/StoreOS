# P-707 — Repair billing: two-step FK resolution linking repair_job to sale

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-707** of the StoreOS Cloud build ("Repair billing: two-step FK resolution linking repair_job to sale") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-706 complete: approval/delivery/warranty endpoints all work.
- CheckoutHandler (P-510) and the two-step FK resolution described in Plan Section 8.8's
  "Chicken-and-Egg FK Resolution" note are the two pieces this prompt must connect: a
  repair job needs a Sale row created for it (billing labor+parts+service charge), and that
  Sale needs to point back at the repair job, while the repair job also needs to point
  forward at the resulting Sale - neither can be inserted first with a valid non-null pointer
  to the other, so the two-step sequence documented in the Plan resolves this cleanly.

## 3. CONTEXT BLOCK

Plan Section 8.8's exact two-step resolution:
```text
1. Insert the repair-billing sales row first with repair_job_id set to the existing repair
   job's ID.
2. Update repair_jobs in the same transaction: SET sale_id = @saleId,
   invoice_number = @invoiceNumber WHERE id = @repairJobId.
```

Create `backend/src/StoreOS.Application/Repairs/IRepairBillingService.cs`:

```csharp
namespace StoreOS.Application.Repairs;

public interface IRepairBillingService
{
    Task LinkSaleAsync(Guid repairJobId, Guid saleId, string invoiceNumber, CancellationToken ct);
}
```

Implementation in `StoreOS.Infrastructure/Repairs/RepairBillingService.cs`:

```csharp
public class RepairBillingService : IRepairBillingService
{
    private readonly StoreOsDbContext _db;
    public RepairBillingService(StoreOsDbContext db) => _db = db;

    public async Task LinkSaleAsync(Guid repairJobId, Guid saleId, string invoiceNumber, CancellationToken ct)
    {
        var job = await _db.Set<RepairJob>().FirstOrDefaultAsync(j => j.Id == repairJobId, ct)
            ?? throw new DomainValidationException("repair_job_not_found", "Repair job not found.");
        job.LinkToSale(saleId, invoiceNumber); // method already defined on RepairJob per P-702
    }
}
```

Create `StoreOS.Application/Repairs/BillRepair/BillRepairCommand.cs`
(`IRequiresPermission`, `repairs:manage`):

```csharp
public record BillRepairCommand(Guid RepairJobId, IReadOnlyList<Checkout.CheckoutPayment> Payments)
    : IRequest<BillRepairResult>, IRequiresPermission
{
    public string RequiredPermission => "repairs:manage";
}

public record BillRepairResult(Guid SaleId, string InvoiceNumber, decimal GrandTotal, decimal AmountPaid, decimal Balance);
```

`BillRepairHandler.cs`: loads the `RepairJob` (must have `SetCommercials` already called, i.e.
`FinalTotal` is populated - guard: `if (job.FinalTotal is null) throw
new DomainValidationException("repair_commercials_not_set", ...)`), builds a synthetic single
cart line representing the repair as a whole (a `RepairService`-category product line, OR, more
accurately, since a repair's `FinalTotal` already bundles parts+labor+tax per Requirements
Section 21.6, construct the Sale DIRECTLY without routing through the general
`CheckoutHandler`/`PricingResolver` pipeline - the repair job's own commercial fields (already
tax-computed) become the sale's totals directly). Implement `BillRepairHandler` as its OWN
focused assembly (step 1 of the two-step resolution): allocate an invoice number (reusing
`InvoiceSequenceAllocator` + `InvoiceNumberFormatter`, same as regular checkout), create the
`Sale` via `Sale.CreateTaxInvoice(...)` with `RepairJobId` set at construction time (add a
`RepairJobId` parameter to `Sale.CreateTaxInvoice`'s factory if not already present - check
`docs/ARTIFACTS.md`; if the factory doesn't currently accept it, add an overload or a
`LinkRepairJob(Guid repairJobId)` mutator method instead, called right after creation, to avoid
disturbing every other caller of the existing factory signature), a single `SaleItem`
representing "Repair Service - <job number>" with `PricePerUnit = job.FinalTotal`, compose
payments via `PaymentComposer`, apply them, post customer-ledger entries if a customer is
attached (following the SAME `TaxInvoice`-and-`Finance`-aware rules already established in
P-510/P-515), THEN call `IRepairBillingService.LinkSaleAsync(repairJobId, sale.Id,
invoiceNumber, ct)` (step 2), all within ONE `SaveChangesAsync()` at the end.

Add `POST /api/repair-jobs/{id}/bill` to `RepairJobsController.cs`, requiring
`Idempotency-Key`.

## 4. TASK

1. Create `IRepairBillingService.cs`/`RepairBillingService.cs` exactly as shown,
   register scoped.
2. Reconcile `Sale.CreateTaxInvoice`'s actual signature (from `docs/ARTIFACTS.md`) and add
   `RepairJobId` support (either a factory parameter or a small `LinkRepairJob` mutator) without
   breaking any existing caller.
3. Create `BillRepairCommand.cs`/handler in `StoreOS.Application/Repairs/BillRepair/`,
   implementing the two-step resolution exactly as described, reusing
   `InvoiceSequenceAllocator`, `PaymentComposer`, `ICustomerLedgerService` from prior prompts.
4. Add `POST /api/repair-jobs/{id}/bill` to `RepairJobsController.cs`, mandatory
   `Idempotency-Key`.
5. Write `StoreOS.IntegrationTests/Repairs/BillRepairTests.cs`: (a) billing a repair job with
   commercials set creates a `Sale` with `RepairJobId` correctly populated, AND the
   `RepairJob.SaleId`/`InvoiceNumber` are correctly set (bidirectional link verified both
   directions); (b) billing a job with NO commercials set -> `repair_commercials_not_set`; (c)
   the created sale's `GrandTotal` exactly matches `job.FinalTotal`; (d) a caller without
   `repairs:manage` -> 403; (e) idempotency: replaying the same key does not double-bill;
   (f) run this test at least 10 times in a loop asserting no deadlock or FK-constraint failure
   ever occurs (proving the two-step ordering genuinely avoids the chicken-and-egg problem
   under repeated execution, not just by luck once).

## 5. HARD CONSTRAINTS

- The two-step resolution order is fixed: Sale is inserted FIRST with
  `repair_job_id` already pointing at the existing job (this direction is trivial since the job
  already exists), THEN the job is updated with `sale_id`/`invoice_number` SECOND, all within
  the SAME transaction/`SaveChangesAsync()` call. Never attempt to insert both rows
  simultaneously with mutually-referencing non-null foreign keys, which is impossible in a
  single INSERT statement pair without one of them tolerating a temporarily-null FK.
- This handler does NOT route through the general `CheckoutHandler`/`PricingResolver`
  pipeline - a repair's commercials are already fully computed on the `RepairJob` entity
  itself (Requirements Section 21.6), so `BillRepairHandler` constructs the `Sale`/`SaleItem`
  directly from those pre-computed values.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter BillRepairTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-707 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-707 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-707` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IRepairBillingService` signature, the `BillRepairCommand`/
`BillRepairResult` shapes, the `POST /api/repair-jobs/{id}/bill` route, and confirmation the
two-step FK resolution was tested repeatedly without deadlock.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-707: <one-line summary>"` only after verification is
   fully green.
