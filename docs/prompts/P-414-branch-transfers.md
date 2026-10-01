# P-414 — Branch transfer out/in endpoints

**Phase:** 4 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-414** of the StoreOS Cloud build ("Branch transfer out/in endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-406, P-408 complete: qty and IMEI stock services both exist with
  concurrency safety.
- No branch-to-branch transfer workflow exists yet. Per Plan §10.6, a transfer is a TWO-STEP
  process: `TransferOut` at the source branch (immediately reduces stock / marks IMEIs
  `InTransit`), followed LATER by a SEPARATE `TransferIn` action at the destination branch
  (increases stock there / marks IMEIs `InStock` at the new branch). This is intentionally NOT
  a single atomic cross-branch transaction — the physical goods are in transit for a real-world
  period of time between the two steps.

## 3. CONTEXT BLOCK

Target DDL addition (Plan §8.6 mentions transfers conceptually via
`inventory_movements.movement_type IN ('TransferOut','TransferIn')`, and Plan §10.6 implies a
transfer HEADER record is needed to link the two legs together — add a small
`inventory_transfers` table not explicitly spelled out as DDL in the plan but clearly
implied by the two-step workflow needing a shared identifier):

```sql
CREATE TABLE inventory_transfers (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  from_branch_id uuid NOT NULL REFERENCES branches(id),
  to_branch_id uuid NOT NULL REFERENCES branches(id),
  product_id uuid NOT NULL REFERENCES products(id),
  qty int NOT NULL CHECK (qty > 0),
  status text NOT NULL CHECK (status IN ('Pending','Received')),
  imei_ids uuid[] NULL,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  received_by uuid NULL,
  received_at timestamptz NULL
);
```

Create `backend/src/StoreOS.Domain/Inventory/InventoryTransfer.cs` with `Status` constants
(`Pending`, `Received`), factory `CreatePending(...)`, method `MarkReceived(receivedBy)`
guarding against double-receipt (`if (Status == "Received") throw
DomainValidationException("transfer_already_received", ...)`).

Create the two vertical slices:

`StoreOS.Application/Inventory/Transfers/TransferOut/TransferOutCommand.cs`
(`IHighImpactOperation`? — actually a normal `IRequiresPermission("inventory:manage")` is
sufficient, transfers aren't as sensitive as void/refund/adjustments; use
`IRequiresPermission`): takes `ToBranchId`, `ProductId`, `Qty`, optional `ImeiStrings` list
(if the product is IMEI-tracked). Handler: validates `ToBranchId != CurrentBranchId`
(cannot transfer to yourself); for each IMEI string, resolve its row via
`IInventoryStockService`-style lookup (branch-scoped, must be `InStock` at the SOURCE branch)
and call `imeiRow.MarkInTransit()`; decrement qty at the source branch via
`ReserveAndDecrementStockAsync` with `movementType = MovementType.TransferOut`; create the
`InventoryTransfer` header row with `Status = "Pending"`.

`StoreOS.Application/Inventory/Transfers/ReceiveTransfer/ReceiveTransferCommand.cs`
(`IRequiresPermission("inventory:manage")`): takes `TransferId`. Handler: loads the transfer,
validates `ToBranchId == CurrentBranchId` (only the DESTINATION branch's staff can receive
it) and `Status == "Pending"`; increments stock at the destination via
`IncreaseStockAsync` with `movementType = MovementType.TransferIn`; for each linked IMEI, calls
`imeiRow.CompleteTransferIn(toBranchId)`; marks the transfer `Received`.

Create `backend/src/StoreOS.Api/Controllers/TransfersController.cs`:
`POST /api/inventory/transfers` (out), `POST /api/inventory/transfers/{id}/receive`.

## 4. TASK

1. Create `InventoryTransfer.cs` in `StoreOS.Domain/Inventory/`, EF configuration
   (branch-scoped by `from_branch_id` for RLS purposes — actually a transfer is visible to
   BOTH branches; use a query filter allowing the row if EITHER `from_branch_id` OR
   `to_branch_id` is in the caller's accessible set: `(_scope.AllBranches ||
   _scope.BranchIds.Contains(x.FromBranchId) || _scope.BranchIds.Contains(x.ToBranchId))` —
   this is a THIRD query-filter shape beyond the two documented in P-107/P-110; document it now
   as "Archetype C — dual-branch visibility" in `docs/ARTIFACTS.md` for reuse if any later
   entity needs it), migration `016_Transfers` including RLS (the RLS SQL policy needs the
   equivalent OR-of-two-branch-columns condition too).
2. Create both vertical slices (`TransferOut`, `ReceiveTransfer`) exactly as described.
3. Create `TransfersController.cs` with the two routes.
4. Write `StoreOS.IntegrationTests/Inventory/BranchTransferTests.cs`: (a) transfer-out reduces
   source stock, creates a `Pending` transfer, and (for IMEI-tracked items) marks the IMEI(s)
   `InTransit`; (b) attempting to sell an `InTransit` IMEI at EITHER branch fails (reuse
   `LoadImeiForSaleAsync` from P-408, which requires `InStock`); (c) receiving the transfer at
   the CORRECT destination branch increases destination stock, marks IMEIs `InStock` with
   their `branch_id` updated to the destination, and marks the transfer `Received`; (d)
   attempting to receive at the WRONG branch (not the declared `ToBranchId`) -> 403 or 422;
   (e) attempting to receive an ALREADY-received transfer -> `transfer_already_received`; (f)
   confirm TENANT-WIDE total stock (summed across both branches) is IDENTICAL before and after
   a complete transfer out+in cycle (Plan §10.6: "Never changes tenant-level total stock").

## 5. HARD CONSTRAINTS

- Transfer-out and transfer-in are TWO SEPARATE requests/transactions
  — do not attempt to combine them into one atomic cross-branch operation; the real-world
  transit period between them is intentional.
- An `InTransit` IMEI must be unsellable at BOTH the origin and destination branch until
  `ReceiveTransfer` completes — this is already enforced by `LoadImeiForSaleAsync` requiring
  `InStock` status, verify this explicitly with a test.
- Document the new "Archetype C — dual-branch visibility" query-filter/RLS shape clearly, since
  it's the first entity in the codebase visible to two DIFFERENT branches' users simultaneously.

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
dotnet ef migrations add 016_Transfers --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter BranchTransferTests
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
   `BLOCKED: P-414 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-414 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-414` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `InventoryTransfer` signature, the two-step command shapes, the
`TransfersController` routes, migration name `016_Transfers`, and the new "Archetype C —
dual-branch visibility" RLS/query-filter pattern for reuse by any future dual-branch entity.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-414: <one-line summary>"` only after verification is
   fully green.
