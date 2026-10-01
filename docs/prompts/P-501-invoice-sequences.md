# P-501 — invoice_sequences table + upsert SQL helper

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-501** of the StoreOS Cloud build ("invoice_sequences table + upsert SQL helper") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 4 (Catalog/Inventory/Purchases/IMEIs, P-401–414) fully complete.
- `InvoiceNumberFormatter` (P-206) is a pure formatting function awaiting a real, concurrent
  sequence source. This prompt builds that source.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** Create the Application-layer contract `backend/src/StoreOS.Application/Abstractions/IInvoiceSequenceAllocator.cs` (CheckoutHandler/ConvertEstimateHandler live in Application):

```csharp
namespace StoreOS.Application.Abstractions;

public interface IInvoiceSequenceAllocator
{
    Task<int> AllocateNextAsync(Guid tenantId, Guid branchId, string seriesKey, CancellationToken ct);
}
```
`InvoiceSequenceAllocator` (Infrastructure) implements it; register `AddScoped<IInvoiceSequenceAllocator, InvoiceSequenceAllocator>()`.

**Series keys (two independent series per branch — see DECISIONS D-07):** tax invoices use `series_key = "FY26-27"` (or the daily key when the CA-approved format is daily); ESTIMATES use `series_key = "EST-FY26-27"`. Estimates must NEVER consume the tax-invoice series, otherwise the GST Rule 46(b) consecutive series gets gaps. Financial year runs 1 April–31 March, computed from the BUSINESS date in the tenant time zone.

Target DDL (Plan §8.5):

```sql
CREATE TABLE invoice_sequences (
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL,
  series_key text NOT NULL,
  last_seq int NOT NULL DEFAULT 0,
  PRIMARY KEY (tenant_id, branch_id, series_key)
);
```

The atomic upsert (Plan §8.5, verbatim):

```sql
INSERT INTO invoice_sequences (tenant_id, branch_id, series_key, last_seq)
VALUES (@t, @b, @seriesKey, 1)
ON CONFLICT (tenant_id, branch_id, series_key)
DO UPDATE SET last_seq = invoice_sequences.last_seq + 1
RETURNING last_seq;
```

Create `backend/src/StoreOS.Domain/Sales/InvoiceSequence.cs` (a thin EF-mapped entity purely
for schema/migration purposes — actual increments NEVER go through EF's change tracker, only
through the raw upsert SQL below, since EF cannot express `ON CONFLICT ... DO UPDATE ...
RETURNING` cleanly):

```csharp
namespace StoreOS.Domain.Sales;

public class InvoiceSequence
{
    public Guid TenantId { get; private set; }
    public Guid BranchId { get; private set; }
    public string SeriesKey { get; private set; } = default!;
    public int LastSeq { get; private set; }
    private InvoiceSequence() { }
}
```

Create `backend/src/StoreOS.Infrastructure/Sales/InvoiceSequenceAllocator.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Sales;

public class InvoiceSequenceAllocator : IInvoiceSequenceAllocator
{
    private readonly StoreOsDbContext _db;
    public InvoiceSequenceAllocator(StoreOsDbContext db) => _db = db;

    public async Task<int> AllocateNextAsync(Guid tenantId, Guid branchId, string seriesKey, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        var transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            INSERT INTO invoice_sequences (tenant_id, branch_id, series_key, last_seq)
            VALUES (@t, @b, @s, 1)
            ON CONFLICT (tenant_id, branch_id, series_key)
            DO UPDATE SET last_seq = invoice_sequences.last_seq + 1
            RETURNING last_seq;";
        AddParam(cmd, "t", tenantId);
        AddParam(cmd, "b", branchId);
        AddParam(cmd, "s", seriesKey);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (int)result!;
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
```

Note this deliberately uses the SAME ambient connection/transaction as the surrounding EF
`StoreOsDbContext` (via `_db.Database.GetDbConnection()` and
`_db.Database.CurrentTransaction`), so the allocated sequence number commits or rolls back
atomically with the rest of the checkout — this is critical: a rolled-back checkout must NOT
permanently consume/waste a sequence number gap... actually it WILL still create a gap if the
row's `ON CONFLICT DO UPDATE` already advanced `last_seq` before a later rollback, EXCEPT
Postgres transactional semantics mean the `UPDATE` itself is rolled back too if the enclosing
transaction rolls back (the row-level change is transactional, not committed until the whole
transaction commits) — so a rolled-back checkout correctly does NOT waste a sequence number.
Document this reasoning explicitly.

Register scoped: `builder.Services.AddScoped<IInvoiceSequenceAllocator, InvoiceSequenceAllocator>();`

## 4. TASK

1. Create `InvoiceSequence.cs` in `StoreOS.Domain/Sales/` (schema-only entity), its EF
   configuration (composite PK `(tenant_id, branch_id, series_key)`, no query filter needed
   at the EF level since this table is NEVER queried via LINQ — only via the raw allocator;
   still add branch-scoped RLS for defense-in-depth).
2. Add `DbSet<InvoiceSequence> InvoiceSequences` to `StoreOsDbContext` (for migration/schema
   purposes only).
3. Create `InvoiceSequenceAllocator.cs` exactly as shown in `StoreOS.Infrastructure/Sales/`,
   register scoped.
4. Generate migration `017_InvoiceSequences` including RLS, apply it.
5. Write `StoreOS.ConcurrencyTests/InvoiceSequenceConcurrencyTests.cs`: launch 50 CONCURRENT
   tasks, each in its own scope/transaction, all calling `AllocateNextAsync` for the SAME
   `(tenantId, branchId, seriesKey)` tuple, each committing immediately after. Assert: all 50
   returned values are UNIQUE integers forming the exact consecutive sequence `1..50` with no
   gaps and no duplicates (sort the collected results and assert equality with
   `Enumerable.Range(1, 50)`).
6. Write a SEPARATE test proving the rollback-doesn't-waste-a-number claim: allocate a number
   inside a transaction, THEN ROLL BACK instead of committing, then allocate again in a NEW
   transaction — assert the second allocation returns the SAME number the rolled-back one did
   (proving no gap was introduced), or explicitly document if Postgres's actual behavior here
   differs from this expectation (report your empirical finding either way — this is a subtle
   point worth verifying rather than assuming).

## 5. HARD CONSTRAINTS

- `AllocateNextAsync` MUST reuse the AMBIENT connection/transaction
  from the calling `StoreOsDbContext` — never open a new independent connection/transaction for
  this call, or the atomicity-with-rollback guarantee breaks.
- This is the ONLY sanctioned way to allocate an invoice or repair-job sequence number anywhere
  in the codebase (P-510's checkout and P-702's repair job numbering both depend on this exact
  class) — never implement a competing sequence mechanism (e.g. `MAX(seq)+1` queries, which
  are race-prone).

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
dotnet ef migrations add 017_InvoiceSequences --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.ConcurrencyTests --filter InvoiceSequenceConcurrencyTests
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
   `BLOCKED: P-501 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-501 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-501` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `InvoiceSequenceAllocator.AllocateNextAsync` signature, migration name
`017_InvoiceSequences`, and your empirical finding on whether a rolled-back allocation leaves
a gap or is correctly reusable (document the actual observed Postgres behavior).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-501: <one-line summary>"` only after verification is
   fully green.
