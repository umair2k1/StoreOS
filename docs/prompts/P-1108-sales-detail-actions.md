# P-1108 — Sales detail view + void/refund/record-payment actions

**Phase:** 11 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1108** of the StoreOS Cloud build ("Sales detail view + void/refund/record-payment actions") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1107 complete: sales history list works.
- GET /api/sales/{id} (P-522), POST /api/sales/{id}/void (P-524), POST /api/sales/{id}/refund
  (P-525), POST /api/sales/{id}/payments (P-523) all exist.

## 3. CONTEXT BLOCK

Create `frontend/src/app/features/sales-history/detail/sale-detail.component.ts` showing
every field from `SaleDetailDto` (P-522), line items, payment history, and THREE conditionally-
visible action buttons: "Record Payment" (visible when `Balance > 0` and `Status == 'Active'`),
"Void" (`*pzCan="'sales:void'"`, visible when `Status == 'Active'`), "Refund"
(`*pzCan="'sales:refund'"`, visible when `Status == 'Active'`, with a line-selection UI for
partial refunds per P-525's `SaleItemIds` support). Every destructive action (Void, Refund) uses
a confirmation dialog (PzDialogService) requiring the Reason field before submission, per
Requirements Section 35.1's "Confirmation dialogs for destructive actions".

## 4. TASK

1. Create `SaleDetailComponent` with the full detail view and three conditional actions,
   each gated by the correct permission directive and sale-state condition.
2. Implement confirmation dialogs requiring a Reason for Void/Refund.
3. Write `sale-detail.component.spec.ts`: (a) action buttons show/hide correctly based on
   Status/Balance/permissions; (b) Void/Refund cannot be submitted without a Reason (client-side
   validation, mirroring the server's own requirement); (c) a successful action refreshes the
   detail view to reflect the new status/balance.

## 5. HARD CONSTRAINTS

- Void/Refund confirmation dialogs require a non-empty Reason before the submit button is
  enabled, mirroring the server-side validators from P-524/525.

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
ng test --include='**/sale-detail.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1108 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1108 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1108` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `SaleDetailComponent` structure and its three conditional-action visibility rules.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1108: <one-line summary>"` only after verification is
   fully green.
