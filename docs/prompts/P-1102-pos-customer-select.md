# P-1102 — POS: customer selection + credit balance display + limit warning dialog

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1102** of the StoreOS Cloud build ("POS: customer selection + credit balance display + limit warning dialog") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1101 complete: POS search/scan shell with hotkeys works.
- Backend GET /api/customers, GET /api/customers/{id}/credit-balance (P-601/602) exist.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/pos/customer-select/customer-select.component.ts`:
typeahead search-by-name/phone (debounced, reusing the P-1101 debounce pattern), a "Walk-in
Customer" default option always available at the top of the list, and once a customer is
selected, fetch and display their live consolidated credit balance via
`GET /api/customers/{id}/credit-balance`. When the cart's current total-plus-existing-balance
would exceed the customer's credit limit (computed client-side using the SAME numbers the
balance endpoint returns, purely for a responsive warning - the AUTHORITATIVE check is
server-side at checkout per P-509), show a warning dialog (using P-1011's `PzDialogService`)
with an "Override" button visible only when `*pzCan="'sales:credit:override'"` (P-1005's
directive) is satisfied.

## 4. TASK

1. Create `CustomerSelectComponent` with typeahead search, walk-in default, live balance
   fetch-on-select, and the client-side warning dialog gated by the permission directive.
2. Wire it into `PosComponent`'s template.
3. Write `customer-select.component.spec.ts`: (a) typeahead returns matching customers; (b)
   selecting a customer fetches and displays their balance; (c) a cart total that would exceed
   the limit shows the warning dialog; (d) the Override button is hidden for a user lacking
   `sales:credit:override` and shown for one who has it (mock `AuthSignalStore.hasPermission`).

## 5. HARD CONSTRAINTS

- This client-side warning is UX ONLY - the real, authoritative credit-limit enforcement
  happens server-side (P-509) at actual checkout time; never let the frontend's warning
  dialog's absence or an override click bypass the server's own check.

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
ng test --include='**/customer-select.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1102 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1102 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1102` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CustomerSelectComponent` structure and the client-side-warning-is-UX-only
disclaimer.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1102: <one-line summary>"` only after verification is
   fully green.
