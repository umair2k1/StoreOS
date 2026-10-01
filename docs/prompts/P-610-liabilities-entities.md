# P-610 — liabilities + liability_payments entities/migration

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-610** of the StoreOS Cloud build ("liabilities + liability_payments entities/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-609 complete: expense tracker fully works.
- No liability schema exists yet (Requirements Section 19).

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.8):

```sql
CREATE TABLE liabilities (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  title text NOT NULL,
  type text NOT NULL CHECK (type IN ('Loan','CreditCard','Financing','Investment')),
  lender_name text,
  total_principal numeric(18,2) NOT NULL,
  due_date date, status text, notes text,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE liability_payments (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  liability_id uuid NOT NULL REFERENCES liabilities(id),
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  payment_mode text NOT NULL,
  payment_date date NOT NULL,
  notes text,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

Create `backend/src/StoreOS.Domain/FinanceOps/LiabilityType.cs`
(`Loan`/`CreditCard`/`Financing`/`Investment` + `All`).

Create `backend/src/StoreOS.Domain/FinanceOps/Liability.cs` (tenant-level, NO `branch_id` at
all per Plan Section 8.8's "Liabilities tenant-level" decision):

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.FinanceOps;

public class Liability
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Title { get; private set; } = default!;
    public string Type { get; private set; } = default!;
    public string? LenderName { get; private set; }
    public decimal TotalPrincipal { get; private set; }
    public DateOnly? DueDate { get; private set; }
    public string? Status { get; private set; }
    public string? Notes { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Liability() { }

    public static Liability Create(Guid tenantId, string title, string type, string? lenderName,
        decimal totalPrincipal, DateOnly? dueDate, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainValidationException("liability_title_required", "Title is required.");
        if (!LiabilityType.All.Contains(type)) throw new DomainValidationException("invalid_liability_type", $"Unknown liability type '{type}'.");
        if (totalPrincipal <= 0) throw new DomainValidationException("invalid_principal", "Total principal must be greater than zero.");
        return new Liability
        {
            Id = Guid.NewGuid(), TenantId = tenantId, Title = title.Trim(), Type = type,
            LenderName = lenderName, TotalPrincipal = totalPrincipal, DueDate = dueDate,
            Status = "Active", Notes = notes, CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateDescriptiveData(string title, string? lenderName, DateOnly? dueDate, string? status, string? notes)
    {
        if (string.IsNullOrWhiteSpace(title)) throw new DomainValidationException("liability_title_required", "Title is required.");
        Title = title.Trim(); LenderName = lenderName; DueDate = dueDate; Status = status; Notes = notes;
        // NOTE: this method deliberately does NOT accept TotalPrincipal — the original
        // principal is immutable after creation (Requirements §19.6: "Editing must not
        // rewrite repayment history" — and by extension, must not silently change the
        // principal baseline repayments are measured against).
    }
}
```

Create `backend/src/StoreOS.Domain/FinanceOps/LiabilityPayment.cs` mirroring
`CustomerPayment`'s immutable pattern exactly (no update method, ever):

```csharp
public static LiabilityPayment Create(Guid tenantId, Guid liabilityId, decimal amount,
    string paymentMode, DateOnly paymentDate, string? notes)
{
    if (amount <= 0) throw new DomainValidationException("invalid_payment_amount", "Payment amount must be positive.");
    // ... construct
}
```

Apply tenant-only RLS to both tables (Liabilities is tenant-level, no branch scoping at all).

## 4. TASK

1. Create `LiabilityType.cs`, `Liability.cs`, `LiabilityPayment.cs` exactly as shown/
   described in `StoreOS.Domain/FinanceOps/`.
2. Create EF configurations: CHECK on `type` (raw SQL) and `amount > 0` on payments (raw SQL),
   tenant-only RLS for both.
3. Add both `DbSet<>`s to `StoreOsDbContext`/`QueryFilters.ApplyAll`.
4. Generate migration `026_Liabilities` including RLS, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/FinanceOps/LiabilityTests.cs`: valid creation of each type;
   invalid type throws; `UpdateDescriptiveData` never changes `TotalPrincipal` (verify this
   explicitly — call it and confirm `TotalPrincipal` is unchanged before/after); zero/negative
   principal throws. `LiabilityPaymentTests.cs`: valid creation; non-positive amount throws; no
   update method exists at all (confirmed by code inspection in a test comment).

## 5. HARD CONSTRAINTS

- `Liability` has NO `branch_id` column at all — it is strictly
  tenant-level, per Plan Section 8.8's explicit decision. Do not add branch scoping to this
  entity.
- `UpdateDescriptiveData` can NEVER change `TotalPrincipal` — there is no method on `Liability`
  that does so after creation, ever. The current balance (computed in P-611) derives from
  `TotalPrincipal - SUM(repayments)`, so silently changing the principal would corrupt every
  historical balance calculation.
- `LiabilityPayment` is permanently immutable, identical in spirit to `CustomerPayment`
  (P-508) — no update method, ever.

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
dotnet ef migrations add 026_Liabilities --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter LiabilityTests
dotnet test backend/tests/StoreOS.Domain.Tests --filter LiabilityPaymentTests
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
   `BLOCKED: P-610 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-610 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-610` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Liability`/`LiabilityPayment`/`LiabilityType` signatures, migration name
`026_Liabilities`, and the immutable-principal / immutable-payment rules.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-610: <one-line summary>"` only after verification is
   fully green.
