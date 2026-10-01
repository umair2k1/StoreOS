# P-408 — IInventoryStockService: SellImeisAsync / status transitions

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-408** of the StoreOS Cloud build ("IInventoryStockService: SellImeisAsync / status transitions") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-406 complete: qty-based conditional stock decrement works.
- P-407 complete: `InventoryImei` entity/table/uniqueness constraint exist.
- `IInventoryStockService` (P-406) does not yet have IMEI-specific methods. This prompt
  extends it.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `SellImeisAsync` runs inside the caller's transaction (P-109). It must never open one. The `SELECT ... FOR UPDATE` on the IMEI row remains valid; it is the row lock that makes a double sale of one IMEI impossible.

Extend `backend/src/StoreOS.Application/Inventory/IInventoryStockService.cs`:

```csharp
Task SellImeiAsync(Guid imeiRowId, Guid saleId, Guid saleItemId, CancellationToken ct);
Task RestoreImeiToStockAsync(Guid imeiRowId, CancellationToken ct);
Task<InventoryImeiSnapshot> LoadImeiForSaleAsync(Guid branchId, string imei, CancellationToken ct);
```

Add `backend/src/StoreOS.Application/Inventory/InventoryImeiSnapshot.cs`:

```csharp
namespace StoreOS.Application.Inventory;

public record InventoryImeiSnapshot(Guid Id, Guid ProductId, Guid BranchId, decimal? PurchasePrice, string Status);
```

Extend `InventoryStockService`:

```csharp
public async Task<InventoryImeiSnapshot> LoadImeiForSaleAsync(Guid branchId, string imei, CancellationToken ct)
{
    var row = await _db.Set<InventoryImei>()
        .Where(i => i.TenantId == _scope.TenantId && i.BranchId == branchId && i.Imei == imei)
        .OrderByDescending(i => i.CreatedAt) // most recent row for this IMEI string at this branch
        .FirstOrDefaultAsync(ct);
    if (row is null)
        throw new DomainValidationException("imei_not_found", $"IMEI {imei} was not found at this branch.");
    if (row.Status != ImeiStatus.InStock)
        throw new DomainValidationException("imei_not_available", $"IMEI {imei} is not available (status: {row.Status}).");
    return new InventoryImeiSnapshot(row.Id, row.ProductId, row.BranchId, row.PurchasePrice, row.Status);
}

public async Task SellImeiAsync(Guid imeiRowId, Guid saleId, Guid saleItemId, CancellationToken ct)
{
    // Use a locking read (FOR UPDATE) to prevent two concurrent sales from both reading
    // this row as "InStock" before either commits its status change.
    var row = await _db.Set<InventoryImei>().FromSqlInterpolated($@"
        SELECT * FROM inventory_imeis WHERE id = {imeiRowId} AND tenant_id = {_scope.TenantId} FOR UPDATE")
        .FirstOrDefaultAsync(ct);
    if (row is null) throw new DomainValidationException("imei_not_found", "IMEI record not found.");
    row.MarkSold(saleId, saleItemId); // throws imei_not_available if status changed since LoadImeiForSaleAsync
}

public async Task RestoreImeiToStockAsync(Guid imeiRowId, CancellationToken ct)
{
    var row = await _db.Set<InventoryImei>().FromSqlInterpolated($@"
        SELECT * FROM inventory_imeis WHERE id = {imeiRowId} AND tenant_id = {_scope.TenantId} FOR UPDATE")
        .FirstOrDefaultAsync(ct);
    if (row is null) throw new DomainValidationException("imei_not_found", "IMEI record not found.");
    row.RestoreToStock();
}
```

The `LoadImeiForSaleAsync` -> `SellImeiAsync` two-step is intentional: the FIRST call (no
locking) is used during PRICING/VALIDATION (P-505's cost cascade needs `PurchasePrice` before
committing to the sale), and the SECOND call (with `FOR UPDATE` row locking) is the actual
COMMIT step performed later in the same transaction, immediately before insert of the sale.
Because both run inside the SAME ambient transaction (`RepeatableRead` per P-109 by default, `Serializable` for
`CheckoutCommand` because it is an `ISerializableRequest`, P-511), the row lock acquired by `SellImeiAsync`'s
`FOR UPDATE` blocks a concurrent transaction attempting the same IMEI until the first commits
or rolls back — this, combined with the tenant-wide partial unique index from P-407, gives
double protection against double-selling.

## 4. TASK

1. Extend `IInventoryStockService.cs` with the three new method signatures.
2. Add `InventoryImeiSnapshot.cs` in `StoreOS.Application/Inventory/`.
3. Extend `InventoryStockService.cs` with the three implementations exactly as shown.
4. Write `StoreOS.IntegrationTests/Inventory/SellImeiServiceTests.cs`: (a) full
   Load-then-Sell happy path leaves the IMEI `Sold` with `sale_id`/`sale_item_id` set; (b)
   calling `SellImeiAsync` on an already-`Sold` IMEI throws `imei_not_available`; (c)
   `RestoreImeiToStockAsync` on a `Sold` IMEI returns it to `InStock`; on a non-`Sold` IMEI
   throws `imei_not_sold`; (d) `LoadImeiForSaleAsync` for a nonexistent IMEI string throws
   `imei_not_found`; for one at a DIFFERENT branch than requested also throws `imei_not_found`
   (branch-scoped lookup, not tenant-wide, since a phone must be sold from the branch that
   physically holds it).
5. Write `StoreOS.ConcurrencyTests/ImeiDoubleSaleConcurrencyTests.cs`: seed one `InStock` IMEI.
   Launch two concurrent tasks in separate scopes, each independently calling
   `LoadImeiForSaleAsync` then `SellImeiAsync` then `SaveChangesAsync()` for the SAME IMEI row.
   Assert EXACTLY ONE succeeds; the other fails (either via the `FOR UPDATE` lock forcing it to
   see the already-`Sold` status inside `MarkSold`'s guard, or via the P-407 tenant-wide unique
   index / serialization failure at commit time — assert on WHICHEVER failure mode actually
   occurs empirically, documenting which one it was).

## 5. HARD CONSTRAINTS

- `SellImeiAsync` MUST use `FOR UPDATE` row locking — do not simplify
  this to a plain `FirstOrDefaultAsync` without locking, which would reopen the double-sale
  race this prompt exists to close.
- `LoadImeiForSaleAsync` is branch-scoped (an IMEI must be sold from the branch that currently
  holds it) — do not make this tenant-wide; a phone physically at Branch A cannot be sold as if
  it were at Branch B (that would require a transfer first, per P-414).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter SellImeiServiceTests
dotnet test backend/tests/StoreOS.ConcurrencyTests --filter ImeiDoubleSaleConcurrencyTests
# Expected: concurrency test proves exactly one winner for the same physical IMEI.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-408 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-408 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-408` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the three new `IInventoryStockService` method signatures, the
`InventoryImeiSnapshot` shape, and which failure mode (FOR UPDATE lock vs. unique index vs.
serialization error) the concurrency test empirically observed for the losing transaction.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-408: <one-line summary>"` only after verification is
   fully green.
