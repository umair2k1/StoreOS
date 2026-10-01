# P-506 — customer_ledger_entries + customers minimal entity/migration (needed for checkout)

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-506** of the StoreOS Cloud build ("customer_ledger_entries + customers minimal entity/migration (needed for checkout)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-505 complete: pricing resolution fully works end-to-end.
- No `Customer`/`customer_ledger_entries` tables exist yet. Checkout (P-510) needs a minimal
  customer master and an append-only ledger to post debits against. Full customer
  CRUD/directory/anonymize features come later in Phase 6 (P-601 onward) — this prompt is
  deliberately minimal, just enough for checkout to function.

## 3. CONTEXT BLOCK

Target DDL (Plan §8.7, customer master + ledger only — `credit_limit` and
`desktop_id` columns are added later in P-601/P-903 respectively; create the table WITHOUT
them for now, using a later `ALTER TABLE` in those future prompts):

```sql
CREATE TABLE customers (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  name text NOT NULL,
  phone text,
  gstin text,
  total_purchases numeric(18,2) NOT NULL DEFAULT 0,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_customers_tenant_phone ON customers (tenant_id, phone);

CREATE TABLE customer_ledger_entries (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  customer_id uuid NOT NULL REFERENCES customers(id),
  sale_id uuid NULL,
  entry_type text NOT NULL,
  debit numeric(18,2) NOT NULL DEFAULT 0,
  credit numeric(18,2) NOT NULL DEFAULT 0,
  branch_id uuid NULL,
  entry_date date NOT NULL,
  ref_id text,
  created_at timestamptz NOT NULL DEFAULT now()
);
CREATE INDEX ix_custledger_customer ON customer_ledger_entries (tenant_id, customer_id);
```

Create `backend/src/StoreOS.Domain/Customers/Customer.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Customers;

public class Customer
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Name { get; private set; } = default!;
    public string? Phone { get; private set; }
    public string? Gstin { get; private set; }
    public decimal TotalPurchases { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private Customer() { }

    public static Customer Create(Guid tenantId, string name, string? phone, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        return new Customer { Id = Guid.NewGuid(), TenantId = tenantId, Name = name.Trim(),
            Phone = phone, Gstin = gstin, CreatedAt = DateTimeOffset.UtcNow };
    }

    public void UpdateProfile(string name, string? phone, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("Customer name is required.", nameof(name));
        Name = name.Trim(); Phone = phone; Gstin = gstin;
    }

    public void AddToTotalPurchases(decimal amount)
    {
        if (amount < 0) throw new DomainValidationException("invalid_amount", "Amount cannot be negative.");
        TotalPurchases += amount;
    }

    public void SubtractFromTotalPurchases(decimal amount)
    {
        if (amount < 0) throw new DomainValidationException("invalid_amount", "Amount cannot be negative.");
        TotalPurchases = Math.Max(0, TotalPurchases - amount);
    }
}
```

Create `backend/src/StoreOS.Domain/Customers/CustomerLedgerEntryType.cs`
(`Invoice`/`Payment`/`InvoiceVoid`/`Refund`/`OpeningBalance` constants + `All`).

Create `backend/src/StoreOS.Domain/Customers/CustomerLedgerEntry.cs`:

```csharp
using StoreOS.Domain.Exceptions;

namespace StoreOS.Domain.Customers;

public class CustomerLedgerEntry
{
    public long Id { get; private set; }
    public Guid TenantId { get; private set; }
    public Guid CustomerId { get; private set; }
    public Guid? SaleId { get; private set; }
    public string EntryType { get; private set; } = default!;
    public decimal Debit { get; private set; }
    public decimal Credit { get; private set; }
    public Guid? BranchId { get; private set; }
    public DateOnly EntryDate { get; private set; }
    public string? RefId { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }

    private CustomerLedgerEntry() { }

    public static CustomerLedgerEntry CreateDebit(Guid tenantId, Guid customerId, Guid? saleId,
        string entryType, decimal amount, Guid? branchId, DateOnly entryDate, string? refId)
    {
        Validate(entryType, amount);
        return new CustomerLedgerEntry { TenantId = tenantId, CustomerId = customerId, SaleId = saleId,
            EntryType = entryType, Debit = amount, Credit = 0m, BranchId = branchId,
            EntryDate = entryDate, RefId = refId, CreatedAt = DateTimeOffset.UtcNow };
    }

    public static CustomerLedgerEntry CreateCredit(Guid tenantId, Guid customerId, Guid? saleId,
        string entryType, decimal amount, Guid? branchId, DateOnly entryDate, string? refId)
    {
        Validate(entryType, amount);
        return new CustomerLedgerEntry { TenantId = tenantId, CustomerId = customerId, SaleId = saleId,
            EntryType = entryType, Debit = 0m, Credit = amount, BranchId = branchId,
            EntryDate = entryDate, RefId = refId, CreatedAt = DateTimeOffset.UtcNow };
    }

    private static void Validate(string entryType, decimal amount)
    {
        if (!CustomerLedgerEntryType.All.Contains(entryType))
            throw new DomainValidationException("invalid_ledger_entry_type", $"Unknown entry type '{entryType}'.");
        if (amount <= 0) throw new DomainValidationException("invalid_amount", "Ledger amount must be positive.");
    }
}
```

Apply tenant-only RLS to `customers` (tenant-wide master, Plan §8.7); apply the "nullable
branch_id" query-filter/RLS variant (documented in P-107/P-116) to `customer_ledger_entries`
since `branch_id` there is nullable attribution, not a strict access boundary — actually
reconsider: ledger entries SHOULD be branch-filtered for RLS since they represent real
per-branch financial exposure and a branch-scoped cashier should not see another branch's
customer ledger detail even for a shared tenant-wide customer... Per Plan §2 Decision 4
("Customer credit is tenant-global... with per-branch attribution visible on the ledger"), the
BALANCE is tenant-global (visible to all authorized roles per Requirements §17.5's
cross-branch visibility rule), but the DETAILED ledger rows' branch attribution is still
informational. Resolve this deliberately: apply the "nullable branch_id, OR tenant-wide"
variant (`branch_id IS NULL OR all_branches OR branch_id = ANY(...)`) so entries stamped with a
specific branch are only detail-visible to users with access to THAT branch or all-branches
access, while the AGGREGATE credit balance (computed by summing entries, in P-601/P-509) is
computed via a query that explicitly uses `IgnoreQueryFilters()` scoped ONLY by `tenant_id`
(not branch) specifically for the balance calculation, since Requirements §17.5 mandates the
balance itself must be tenant-global regardless of the viewer's branch scope. Document this
carefully: two different query patterns for two different purposes on the SAME table.

## 4. TASK

1. Create `Customer.cs`, `CustomerLedgerEntryType.cs`, `CustomerLedgerEntry.cs`
   exactly as shown in `StoreOS.Domain/Customers/`.
2. Create EF configurations: `customers` tenant-only; `customer_ledger_entries` with the
   nullable-branch RLS/query-filter variant as reasoned above.
3. Add both `DbSet<>`s to `StoreOsDbContext`/`QueryFilters.ApplyAll`.
4. Generate migration `019_CustomersLedger` including both RLS policies, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Customers/CustomerAndLedgerTests.cs`: `Customer.Create`/
   `UpdateProfile` validation; `AddToTotalPurchases`/`SubtractFromTotalPurchases` never go
   negative and reject negative input amounts; `CustomerLedgerEntry.CreateDebit`/`CreateCredit`
   reject invalid entry types and non-positive amounts; a debit entry has `Credit == 0` and vice
   versa (mutual exclusivity, verify explicitly).
7. Write `StoreOS.IntegrationTests/Customers/CustomerLedgerRlsTests.cs` proving the
   two-query-pattern behavior: a branch-A-scoped user querying ledger entries NORMALLY
   (through the standard EF filter) does not see a branch-B-stamped entry for a shared
   tenant-wide customer, but a DELIBERATE tenant-only balance-calculation query (using
   `IgnoreQueryFilters()` filtered manually by `tenant_id` alone) DOES sum both entries
   correctly regardless of branch.

## 5. HARD CONSTRAINTS

- This is a documented, INTENTIONAL exception permitting
  `IgnoreQueryFilters()` outside of `LoginHandler` (P-303) — specifically and ONLY for
  tenant-global BALANCE aggregation queries (used by P-509's credit-limit check and P-601's
  balance display), never for returning detailed row-level ledger data to a branch-unscoped
  caller. Any future use of `IgnoreQueryFilters()` in this codebase must be similarly
  documented and justified, never used casually.
- `CreateDebit`/`CreateCredit` are the ONLY ways to construct a `CustomerLedgerEntry` — always
  exactly one of `Debit`/`Credit` is nonzero, enforced by construction, never both.

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
dotnet ef migrations add 019_CustomersLedger --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter CustomerAndLedgerTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter CustomerLedgerRlsTests
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
   `BLOCKED: P-506 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-506 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-506` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `Customer`/`CustomerLedgerEntry` signatures, migration name
`019_CustomersLedger`, and the critical two-query-pattern documentation (branch-filtered
detail view vs. tenant-only `IgnoreQueryFilters()` balance aggregation) that P-509/P-601 must
follow precisely.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-506: <one-line summary>"` only after verification is
   fully green.
