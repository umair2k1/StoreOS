# P-609 — expenses entity/migration + CRUD endpoints

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-609** of the StoreOS Cloud build ("expenses entity/migration + CRUD endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-608 complete: supplier returns fully work atomically.
- No expense tracker exists yet (Requirements Section 18).

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.8):

```sql
CREATE TABLE expenses (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NULL REFERENCES branches(id),
  description text NOT NULL,
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  category text NOT NULL,
  expense_date date NOT NULL,
  notes text,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_expenses_tenant_branch_date ON expenses (tenant_id, branch_id, expense_date);
```

Create `backend/src/StoreOS.Domain/FinanceOps/ExpenseCategory.cs` with a starter set of
constants PLUS explicit extensibility (Requirements Section 18.3: "The design should permit
future categories") — do NOT enforce a DB CHECK constraint restricting `category` to a fixed
enum (unlike `products.category`, which IS a closed set); instead treat `category` as free text
at the database level, with the starter constants (`Rent`, `Utilities`, `Salaries`,
`Miscellaneous`) offered as UI/API convenience suggestions only, validated ONLY for
non-emptiness, not against a closed list:

```csharp
namespace StoreOS.Domain.FinanceOps;

public static class ExpenseCategory
{
    public const string Rent = "Rent";
    public const string Utilities = "Utilities";
    public const string Salaries = "Salaries";
    public const string Miscellaneous = "Miscellaneous";
    public static readonly IReadOnlyList<string> Suggested = new[] { Rent, Utilities, Salaries, Miscellaneous };
    // NOTE: this is a SUGGESTION list only, not a closed enum — any non-empty string is a
    // valid category, per Requirements §18.3's extensibility requirement.
}
```

Create `backend/src/StoreOS.Domain/FinanceOps/Expense.cs` (nullable `BranchId`, validating
Requirements Section 18.4: description required, amount > 0, date required):

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.FinanceOps;

public class Expense
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid? BranchId { get; private set; }
    public string Description { get; private set; } = default!;
    public decimal Amount { get; private set; }
    public string Category { get; private set; } = default!;
    public DateOnly ExpenseDate { get; private set; }
    public string? Notes { get; private set; }
    public Guid CreatedBy { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Expense() { }

    public static Expense Create(Guid tenantId, Guid? branchId, string description, decimal amount,
        string category, DateOnly expenseDate, string? notes, Guid createdBy)
    {
        if (string.IsNullOrWhiteSpace(description)) throw new DomainValidationException("expense_description_required", "Description is required.");
        if (amount <= 0) throw new DomainValidationException("expense_invalid_amount", "Amount must be greater than zero.");
        if (string.IsNullOrWhiteSpace(category)) throw new DomainValidationException("expense_category_required", "Category is required.");
        return new Expense
        {
            Id = Guid.NewGuid(), TenantId = tenantId, BranchId = branchId, Description = description.Trim(),
            Amount = amount, Category = category.Trim(), ExpenseDate = expenseDate, Notes = notes,
            CreatedBy = createdBy, CreatedAt = DateTimeOffset.UtcNow
        };
    }
}
```

Apply the nullable-branch RLS/query-filter variant (same as `expenses`'s Plan Section 8.8
"NULL = head office" note). Create the vertical slices `CreateExpense` (`IRequiresPermission`,
`expenses:manage`), `GetExpenses` (paginated, date-range filterable, defaulting per
Requirements Section 18.5 to "first day of current month" through "current date" when no range
is specified — implement this default explicitly in the handler when both `From`/`To` are
omitted), `GetMonthlyTotal` (Requirements Section 18.6). Create `ExpensesController.cs`.

## 4. TASK

1. Create `ExpenseCategory.cs` (suggestion list, not enforced) and `Expense.cs` exactly
   as shown in `StoreOS.Domain/FinanceOps/`.
2. Create EF configuration: CHECK `amount > 0` (raw SQL, NO check on `category` — it's free
   text), nullable-branch query filter/RLS, the named index.
3. Add `DbSet<Expense> Expenses` to `StoreOsDbContext`/`QueryFilters.ApplyAll`.
4. Generate migration `025_Expenses` including RLS, apply it.
5. Re-run `RlsCoverageTests`.
6. Create `CreateExpense`, `GetExpenses` (with the documented current-month default),
   `GetMonthlyTotal` vertical slices under `StoreOS.Application/FinanceOps/Expenses/`.
7. Create `ExpensesController.cs`: `POST /api/expenses`, `GET /api/expenses?from=&to=`,
   `GET /api/expenses/monthly-total`.
8. Write `StoreOS.IntegrationTests/FinanceOps/ExpensesTests.cs`: (a) creating with an ARBITRARY
   novel category string (not in the suggested list) succeeds (proving extensibility); (b)
   empty description/zero amount/missing date each throw their specific validation codes; (c)
   `GET /api/expenses` with NO date range defaults to first-of-month through today (seed
   expenses both inside and outside this range, confirm only in-range ones are returned); (d)
   monthly total correctly sums only the current month's expenses.

## 5. HARD CONSTRAINTS

- `category` has NO database CHECK constraint and NO closed C# enum
  validation — any non-empty string is valid, per Requirements Section 18.3's explicit
  extensibility requirement. Do not add a restrictive enum here even though `products.category`
  and other fields elsewhere in the codebase DO use closed enums — this is a deliberately
  different design choice for this one field.

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
dotnet ef migrations add 025_Expenses --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.IntegrationTests --filter ExpensesTests
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
   `BLOCKED: P-609 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-609 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-609` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Expense`/`ExpenseCategory` signatures (noting category is unrestricted
free text), migration name `025_Expenses`, and the `ExpensesController` routes with the
documented current-month default date range.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-609: <one-line summary>"` only after verification is
   fully green.
