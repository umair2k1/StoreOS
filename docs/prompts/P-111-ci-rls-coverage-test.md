# P-111 — CI schema test: 100% RLS coverage assertion

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-111** of the StoreOS Cloud build ("CI schema test: 100% RLS coverage assertion") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-108 and P-110 complete: both RLS archetypes (tenant-only, branch-scoped)
  are proven on real/fixture tables.
- This prompt builds the AUTOMATED GUARD that fails CI the moment any future migration adds
  a `tenant_id` column without a matching RLS policy — this must run before every later
  entity-creation prompt is considered complete, and every such prompt's own VERIFICATION
  block should re-run this test.

## 3. CONTEXT BLOCK

Create `backend/tests/StoreOS.IntegrationTests/Schema/RlsCoverageTests.cs`:

```csharp
using FluentAssertions;
using Npgsql;
using Xunit;

namespace StoreOS.IntegrationTests.Schema;

public class RlsCoverageTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;
    public RlsCoverageTests(PostgresTestFixture fixture) => _fixture = fixture;

    private static readonly HashSet<string> DocumentedExceptions = new() { "tenants" };

    [Fact]
    public async Task Every_table_with_tenant_id_column_has_rls_enabled_and_forced()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r'
              AND EXISTS (
                SELECT 1 FROM information_schema.columns col
                WHERE col.table_schema = 'public' AND col.table_name = c.relname
                  AND col.column_name = 'tenant_id'
              )
              AND (c.relrowsecurity = false OR c.relforcerowsecurity = false);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync()) offenders.Add(reader.GetString(0));
        offenders = offenders.Where(o => !DocumentedExceptions.Contains(o)).ToList();
        offenders.Should().BeEmpty("every table with a tenant_id column must have RLS enabled AND forced, except documented exceptions: {0}", string.Join(",", DocumentedExceptions));
    }

    [Fact]
    public async Task No_policy_contains_forbidden_null_bypass()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = "SELECT polname, tablename, qual FROM pg_policies WHERE schemaname = 'public';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync())
        {
            var qual = reader.GetString(2);
            if (qual.Contains("IS NULL", StringComparison.OrdinalIgnoreCase) &&
                qual.Contains("current_setting", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{reader.GetString(1)}.{reader.GetString(0)}");
        }
        offenders.Should().BeEmpty("no RLS policy may bypass isolation via 'current_setting(...) IS NULL': {0}", string.Join(",", offenders));
    }

    [Fact]
    public async Task Every_table_with_tenant_id_has_at_least_one_policy()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT col.table_name
            FROM information_schema.columns col
            WHERE col.table_schema = 'public' AND col.column_name = 'tenant_id'
              AND NOT EXISTS (
                SELECT 1 FROM pg_policies p
                WHERE p.schemaname = 'public' AND p.tablename = col.table_name
              );";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync()) offenders.Add(reader.GetString(0));
        offenders = offenders.Where(o => !DocumentedExceptions.Contains(o)).ToList();
        offenders.Should().BeEmpty("tables with tenant_id must have at least one RLS policy: {0}", string.Join(",", offenders));
    }
}
```

This requires a `PostgresTestFixture` (Testcontainers-based, spinning up a real Postgres 16
container per test class run, applying all EF migrations against it) — if this fixture does
not exist yet, create it now as
`backend/tests/StoreOS.IntegrationTests/PostgresTestFixture.cs`:

`PostgresTestFixture` already exists (created in P-108, with `OwnerConnectionString` / `AppConnectionString`). Do NOT recreate it. Schema/catalog inspection tests below use `OwnerConnectionString`; anything that asserts behaviour under RLS uses `AppConnectionString`. `NullAccessScope` (P-106) replaces the old `NoOpAccessScope`.

> **REVISION NOTE (review v2):** in addition to the existing assertions, add ONE more assertion to `RlsCoverageTests`: every policy `qual`/`with_check` that contains `current_setting` MUST also contain `NULLIF(` (proves the empty-string-after-SET-LOCAL pitfall is handled). Add a second test that runs two consecutive transactions on ONE raw Npgsql connection (first with `set_config('app.tenant_id', <A>, true)`, then a second with NO set_config) and asserts the second `SELECT` returns ZERO rows (not an exception).

## 4. TASK

1. Confirm `PostgresTestFixture` (P-108) and `NullAccessScope` (P-106) exist; do not recreate them.
2. Create `RlsCoverageTests.cs` in `StoreOS.IntegrationTests/Schema/` exactly as shown.
3. Confirm all three test methods pass against the CURRENT migration set (8 real/fixture
   tables from P-108/P-110, all RLS-protected, `tenants` excluded by name).
4. Wire this test class to run in the CI skeleton note: update `docs/BUILD_LOG.md` with an
   explicit instruction that P-1203 MUST include `RlsCoverageTests` in the CI integration-test
   stage — do not modify `.github/workflows/ci.yml` in this prompt (integration tests need
   Docker-in-Docker / Testcontainers support wired in P-1203), just leave the note.

## 5. HARD CONSTRAINTS

- These three tests must be re-run (and stay green) after EVERY
  later migration that adds a `tenant_id` column. Any prompt from Phase 4 onward that adds
  such a column MUST include `dotnet test --filter RlsCoverageTests` in its own VERIFICATION
  block.
- `PostgresTestFixture` spins up a REAL ephemeral Postgres container per test class (not the
  P-002 dev container) — this requires Docker to be available in the execution environment;
  if Docker-in-Docker is unavailable in the current sandbox, note this limitation explicitly
  in `docs/BUILD_LOG.md` rather than silently skipping the test.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
# Expected: 3 passed, 0 failed (requires Docker available for Testcontainers).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-111 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-111 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-111` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `PostgresTestFixture` and `NoOpAccessScope` class locations/signatures
(reused by nearly every later integration test), and the reminder embedded in
`docs/BUILD_LOG.md` that P-1203 must wire this into CI.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-111: <one-line summary>"` only after verification is
   fully green.
