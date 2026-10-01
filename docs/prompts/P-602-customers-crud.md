# P-602 — Customers CRUD endpoints

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-602** of the StoreOS Cloud build ("Customers CRUD endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-601 complete: credit-balance display endpoint exists.
- `Customer` entity (P-506) exists with `Create`/`UpdateProfile`/`AddToTotalPurchases`/
  `SubtractFromTotalPurchases`/`SetCreditLimit` (P-509) — no directory/search/CRUD REST surface
  exists yet (Requirements Section 17.1-17.3).

## 3. CONTEXT BLOCK

Create vertical slices under `StoreOS.Application/Customers/Crud/`:
`CreateCustomer` (`IRequiresPermission`, `pos:checkout` — front-line staff routinely create new
customer records during billing, so gate it at the same level as billing itself, not a
higher-privilege permission), `GetCustomers` (paginated list, search by name/phone per
Requirements Section 17.3, no special permission), `GetCustomerById`, `UpdateCustomer`
(`IRequiresPermission`, `pos:checkout` for basic profile fields; `credit_limit` changes
specifically require `settings:manage` — split into TWO commands:
`UpdateCustomerProfileCommand` for name/phone/gstin, and a SEPARATE
`SetCustomerCreditLimitCommand` requiring `settings:manage`, since credit-limit changes are a
materially more sensitive operation than editing a phone number).

```csharp
public record GetCustomersQuery(string? Search, int Page = 1, int PageSize = 50)
    : IRequest<PagedResult<CustomerListItemDto>>;

public record CustomerListItemDto(Guid Id, string Name, string? Phone, decimal TotalPurchases,
    decimal CurrentCreditBalance);
```

`GetCustomersHandler` needs `CurrentCreditBalance` per customer — for a PAGINATED list, calling
`GetConsolidatedBalanceAsync` once per row (N+1 queries) is acceptable at typical shop scale
(dozens to low hundreds of customers per page) but note this as a candidate optimization if
performance profiling later shows it matters; for now, correctness over premature optimization,
call the service once per listed customer within the page (never for the FULL unfiltered
table).

Create `CustomersController.cs` (extend from P-601): `GET /api/customers`,
`POST /api/customers`, `GET /api/customers/{id}`, `PATCH /api/customers/{id}`,
`PATCH /api/customers/{id}/credit-limit`.

## 4. TASK

1. Create `CreateCustomer`, `GetCustomers`, `GetCustomerById`,
   `UpdateCustomerProfile`, `SetCustomerCreditLimit` vertical slices under
   `StoreOS.Application/Customers/Crud/` exactly as described.
2. Extend `CustomersController.cs` with the five routes.
3. Write `StoreOS.IntegrationTests/Customers/CustomersCrudTests.cs`: (a) create/list/search/get/
   update profile all work correctly; (b) `PATCH .../credit-limit` requires `settings:manage`
   specifically — a user with only `pos:checkout` (sufficient for profile edits) gets 403 when
   attempting to change the credit limit; (c) search by partial phone number and partial name
   both work; (d) `CurrentCreditBalance` in the list DTO matches
   `GetConsolidatedBalanceAsync`'s value for each listed customer exactly.

## 5. HARD CONSTRAINTS

- Credit-limit changes are SPLIT into their own command requiring
  `settings:manage`, deliberately more restrictive than ordinary profile edits
  (`pos:checkout`) — do not merge these into one command with a single shared permission
  requirement.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CustomersCrudTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-602 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-602 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-602` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: all five Customers use-case shapes and the `CustomersController` routes,
noting the split permission requirement for credit-limit changes.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-602: <one-line summary>"` only after verification is
   fully green.
