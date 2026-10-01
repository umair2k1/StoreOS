# P-1103 — POS: cart editing (qty, IMEI entry, margin toggle, gift, discount)

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1103** of the StoreOS Cloud build ("POS: cart editing (qty, IMEI entry, margin toggle, gift, discount)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1102 complete: customer selection works.
- CheckoutCommand's CheckoutCartLine shape (Guid ProductId, int Qty, string? Imei, bool
  UseMarginScheme, bool IsGift, decimal? OverridePricePerUnit) is documented in
  docs/ARTIFACTS.md's P-504 record.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/pos/cart/cart.component.ts` and a `CartStateService`
(signal-based, holding the current array of cart lines matching `CheckoutCartLine`'s shape plus
UI-only display fields like product name/price for rendering). Implement every cart rule from
Requirements Section 6.4/6.7: adding an existing (non-IMEI, non-gift) item increases its
quantity rather than duplicating a row; quantity cannot be decreased below 1 (use a remove
action instead) nor increased client-side beyond the search result's reported `StockQty` for
non-service categories (soft client-side guard only - the SAME `insufficient_stock` guard is
authoritative server-side at checkout); an IMEI-bearing line is ALWAYS quantity 1 (selecting a
specific unit inherently means qty=1 for that line - adding a second unit of the same product
with a DIFFERENT IMEI creates a SEPARATE cart line, never merges); the margin-scheme toggle is
only enabled/shown for `UsedPhone` category lines; the gift toggle and per-line
`OverridePricePerUnit` field are visible only when `*pzCan="'sales:discount:apply'"` is
satisfied.

## 4. TASK

1. Create `CartStateService` (signal-based) and `CartComponent` implementing every rule
   above.
2. Wire `+`/`-` and `Del` hotkeys (deferred from P-1101) to the currently-focused/selected cart
   row.
3. Write `cart-state.service.spec.ts`: adding a plain accessory twice merges into one line with
   qty=2; adding two DIFFERENT IMEIs of the same product creates two separate lines; setting
   margin scheme on a non-UsedPhone line is rejected/ignored; removing a line works; the qty
   client-side soft-cap at the reported stock level is enforced (with a toast warning via
   P-1011's ToastService) but does not hard-block if the user proceeds (server is authoritative).
2. Write `cart.component.spec.ts`: gift/override fields are hidden without
   `sales:discount:apply` and shown with it.

## 5. HARD CONSTRAINTS

- IMEI-bearing lines never merge with each other or with non-IMEI lines of the same product
  - each specific physical unit is its own cart line, always qty=1.
- The client-side stock soft-cap is advisory only; the server's conditional-decrement check
  (P-406) is the sole authoritative guard.

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
cd store-os-cloud/frontend
ng test --include='**/cart-state.service.spec.ts' --include='**/cart.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1103 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1103 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1103` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CartStateService`/`CartComponent` structures and the IMEI-never-merges rule.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1103: <one-line summary>"` only after verification is
   fully green.
