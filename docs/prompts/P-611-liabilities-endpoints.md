# P-611 — Liabilities CRUD + repayment endpoint (balance = principal - sum repayments)

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-611** of the StoreOS Cloud build ("Liabilities CRUD + repayment endpoint (balance = principal - sum repayments)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-610 complete: `Liability`/`LiabilityPayment` schema exists.
- No REST endpoints exist yet. This completes Phase 6.

## 3. CONTEXT BLOCK

Create vertical slices under `StoreOS.Application/FinanceOps/Liabilities/`:
`CreateLiability` (`IRequiresPermission`, `liabilities:manage`), `GetLiabilities` (paginated
list, `IRequiresPermission`, `liabilities:manage` — this is sensitive financial data per Plan's
permission table listing `liabilities:manage` as Owner-only), `GetLiabilityById` (same
permission; the response DTO computes `CurrentBalance` live), `UpdateLiability`
(`IRequiresPermission`, `liabilities:manage`; calls `UpdateDescriptiveData` only — never
touches principal), `RecordRepayment` (`IRequiresPermission`, `liabilities:manage`; creates a
NEW `LiabilityPayment` row, never mutates an existing one or the liability's principal).

```csharp
public record LiabilityDto(Guid Id, string Title, string Type, string? LenderName,
    decimal TotalPrincipal, decimal CurrentBalance, DateOnly? DueDate, string? Status, string? Notes);
```

`CurrentBalance` is computed as `Math.Max(0, TotalPrincipal - SUM(LiabilityPayments.Amount))`
per Requirements Section 19.5's formula — implement this as a small
`ILiabilityBalanceService`-style helper OR simply inline the sum query in each handler that
needs it (`GetLiabilityById`, `GetLiabilities`); for consistency with the
`ICustomerLedgerService`/`ISupplierLedgerService` pattern established twice already, create a
THIRD small service:

```csharp
namespace StoreOS.Application.FinanceOps;

public interface ILiabilityBalanceService
{
    Task<decimal> GetCurrentBalanceAsync(Guid liabilityId, CancellationToken ct);
}
```

Implementation queries `LiabilityPayments` summed against the liability's `TotalPrincipal`,
scoped by `tenant_id` via the normal (non-`IgnoreQueryFilters`) tenant-only filter, since
Liabilities have no cross-branch visibility concern to work around (they're tenant-level with
no branch dimension at all).

`RecordRepaymentCommand`: takes `LiabilityId`, `Amount`, `PaymentMode`, `PaymentDate`, `Notes`.
Validates `Amount` does not cause `CurrentBalance` to go negative (optional overpayment
guard — Requirements doesn't explicitly forbid overpayment here the way it does for sales
payments, but for consistency and correctness, reject a repayment exceeding the current
balance with `DomainValidationException("repayment_exceeds_balance", ...)`).

Create `LiabilitiesController.cs`: `POST/GET /api/liabilities`, `GET/PATCH
/api/liabilities/{id}`, `POST /api/liabilities/{id}/repayments`.

## 4. TASK

1. Create `ILiabilityBalanceService.cs` in `StoreOS.Application/FinanceOps/` and its
   implementation in `StoreOS.Infrastructure/FinanceOps/`, register scoped.
2. Create all five vertical slices under `StoreOS.Application/FinanceOps/Liabilities/` exactly
   as described.
3. Create `LiabilitiesController.cs` with the five routes.
4. Write `StoreOS.IntegrationTests/FinanceOps/LiabilitiesTests.cs`: (a) create a liability with
   `TotalPrincipal=100000` -> `CurrentBalance=100000` initially; (b) record a repayment of
   20000 -> `CurrentBalance=80000`; (c) record a SECOND repayment of 30000 -> `CurrentBalance=
   50000`, and confirm there are TWO SEPARATE `liability_payments` rows (never one merged/
   updated row — Invariant-adjacent test mirroring the customer-payment immutability pattern);
   (d) `UpdateLiability` changing the title/lender does NOT affect `TotalPrincipal` or
   `CurrentBalance`; (e) a repayment exceeding the current balance ->
   `repayment_exceeds_balance`; (f) a caller without `liabilities:manage` -> 403 on every
   route.

## 5. HARD CONSTRAINTS

- `CurrentBalance` is ALWAYS derived live from
  `TotalPrincipal - SUM(payments)` via `ILiabilityBalanceService` — never stored as a mutable
  column that could drift from the underlying transaction history (Requirements Section 19.5's
  explicit preference, matching the "if a manually maintained balance exists... it must
  reconcile with transaction history" caveat, which this design sidesteps entirely by never
  storing a separate mutable balance at all).
- Every one of the five endpoints requires `liabilities:manage` — this is intentionally a
  single, Owner-level permission gate for the entire module per Plan's permission table.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter LiabilitiesTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-611 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-611 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-611` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ILiabilityBalanceService` signature, all five vertical-slice shapes, the
`LiabilitiesController` routes, and confirmation `CurrentBalance` is always live-derived, never
a stored mutable column.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-611: <one-line summary>"` only after verification is
   fully green.
