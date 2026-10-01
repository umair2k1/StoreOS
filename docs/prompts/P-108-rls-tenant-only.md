# P-108 — RLS SQL migration: enable+force RLS, tenant-only policy archetype

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-108** of the StoreOS Cloud build ("RLS SQL migration: enable+force RLS, tenant-only policy archetype") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-107 complete: EF query filters are the first line of defense on `Branch`
  and `Setting`.
- No PostgreSQL Row Level Security policies exist yet. This prompt establishes the SQL
  pattern and applies it to the tables that exist so far (`tenants` is special — it has no
  parent tenant scope, so it is NOT RLS-protected the same way; `branches`, `settings`,
  `users`, `roles`, `role_permissions` (via role_id join — see note), `user_branches`,
  `user_role_assignments`, `refresh_tokens` (via user_id join) all need policies).
- Session variables `app.tenant_id` / `app.branch_ids` / `app.all_branches` are NOT yet being
  SET by any middleware — that is P-109. This prompt only creates the SQL policies; they will
  correctly return ZERO rows for any connection that hasn't SET them (which is exactly the
  fail-safe behavior wanted, verified here with raw `SET LOCAL` in a test transaction rather
  than through the app).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** (1) **The shared test fixture is created HERE, not in P-111** (P-108/P-109/P-110 all need a real database and run before P-111). Create `backend/tests/StoreOS.IntegrationTests/PostgresTestFixture.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Infrastructure.Persistence;
using Testcontainers.PostgreSql;
using Xunit;

namespace StoreOS.IntegrationTests;

/// OwnerConnectionString: superuser/owner. Use ONLY for migrations, seeding cross-tenant fixture data and
/// schema inspection. AppConnectionString: role storeos_app (NOSUPERUSER, NOBYPASSRLS) - the ONLY connection
/// that may be used to assert RLS behaviour or to build the application under test.
public sealed class PostgresTestFixture : IAsyncLifetime
{
    private const string AppPassword = "storeos_app_test_pw";
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder()
        .WithImage("postgres:16-alpine").WithDatabase("storeos_test")
        .WithUsername("storeos_owner").WithPassword("storeos_owner_test_pw").Build();

    public string OwnerConnectionString => _container.GetConnectionString();
    public string AppConnectionString { get; private set; } = default!;

    public async Task InitializeAsync()
    {
        await _container.StartAsync();
        await using (var conn = new NpgsqlConnection(OwnerConnectionString))
        {
            await conn.OpenAsync();
            await Exec(conn, $"CREATE ROLE storeos_app LOGIN PASSWORD '{AppPassword}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE");
            await Exec(conn, "GRANT CONNECT ON DATABASE storeos_test TO storeos_app");
            await Exec(conn, "GRANT USAGE ON SCHEMA public TO storeos_app");
            await Exec(conn, "ALTER DEFAULT PRIVILEGES FOR ROLE storeos_owner IN SCHEMA public GRANT SELECT, INSERT, UPDATE, DELETE ON TABLES TO storeos_app");
            await Exec(conn, "ALTER DEFAULT PRIVILEGES FOR ROLE storeos_owner IN SCHEMA public GRANT USAGE, SELECT ON SEQUENCES TO storeos_app");
        }
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(OwnerConnectionString).Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
            await db.Database.MigrateAsync();
        await ApplyPostMigrateAsync();
        AppConnectionString = new NpgsqlConnectionStringBuilder(OwnerConnectionString)
            { Username = "storeos_app", Password = AppPassword, MaxPoolSize = 20 }.ToString();
    }

    /// Re-run after any test that adds tables/migrations. Reads deploy/db/post-migrate.sql (P-002).
    public async Task ApplyPostMigrateAsync()
    {
        var sql = await File.ReadAllTextAsync(FindRepoFile("deploy/db/post-migrate.sql"));
        await using var conn = new NpgsqlConnection(OwnerConnectionString);
        await conn.OpenAsync();
        await Exec(conn, sql);
    }

    private static async Task Exec(NpgsqlConnection c, string sql)
    { await using var cmd = new NpgsqlCommand(sql, c); await cmd.ExecuteNonQueryAsync(); }

    private static string FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, relative))) dir = dir.Parent;
        return dir is null ? throw new FileNotFoundException(relative) : Path.Combine(dir.FullName, relative);
    }

    public async Task DisposeAsync() => await _container.DisposeAsync();
}
```
(If the design at that moment still has the options-only constructor, use `new StoreOsDbContext(options)`; P-107 switches it to `(options, new NullAccessScope())`.) Add `[CollectionDefinition("postgres")] public class PostgresCollection : ICollectionFixture<PostgresTestFixture>` so all integration tests share ONE container.
(2) Every RLS behaviour assertion in this prompt's tests (fail-closed with no session variable, cross-tenant read returns 0 rows, WITH CHECK rejects a cross-tenant insert) MUST use `AppConnectionString`. Seed data through `OwnerConnectionString` by wrapping each seed statement's transaction with `set_config('app.tenant_id', <id>, true)` OR by inserting as owner (owner/superuser bypasses RLS, which is what you want for seeding). Add a test `AppRole_CannotBypassRls`: `SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user` on the app connection returns `false, false`.
(3) Create `RlsRoleGuard : IHostedService` in `StoreOS.Infrastructure/Persistence/` and register it in the API (`AddHostedService<RlsRoleGuard>()`): on start it runs the same pg_roles query on the application connection and THROWS `InvalidOperationException("Refusing to start: the database role bypasses row level security.")` when either flag is true. Unit/integration test both outcomes (owner connection string => throws; app connection string => starts).

Exact pattern (Plan §9.2, Policy Archetype A):

```sql
ALTER TABLE branches ENABLE ROW LEVEL SECURITY;
ALTER TABLE branches FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_policy ON branches USING (
  tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid
);
```

Note the `true` second argument to `current_setting` (missing_ok) and the `NULLIF(..., '')` wrapper: after ANY transaction on a pooled connection has used `SET LOCAL`/`set_config(..., true)`, PostgreSQL leaves the custom setting defined as an EMPTY STRING (not NULL) for the rest of that session, and `''::uuid` raises `invalid input syntax for type uuid`. `NULLIF(..., '')` turns both the never-set and the emptied cases into NULL, so
`current_setting` yields NULL instead of raising an error when the session variable was
never set, so `tenant_id = NULL` evaluates to NULL (falsy), correctly hiding all rows rather
than crashing the query. **Never** write `OR current_setting(...) IS NULL` to "fix" this —
that is the exact catastrophic bypass Plan §9.2 forbids.

Tables needing the tenant-only archetype right now, with their join path noted where the
table itself lacks a direct `tenant_id` column:

- `branches` — direct `tenant_id`.
- `users` — direct `tenant_id`.
- `roles` — direct `tenant_id`.
- `role_permissions` — NO direct `tenant_id` (only `role_id`). Add policy via a subquery:
  ```sql
  ALTER TABLE role_permissions ENABLE ROW LEVEL SECURITY;
  ALTER TABLE role_permissions FORCE ROW LEVEL SECURITY;
  CREATE POLICY tenant_isolation_policy ON role_permissions USING (
    role_id IN (SELECT id FROM roles WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  );
  ```
- `user_branches` — NO direct `tenant_id` (only `user_id`, `branch_id`). Policy via subquery
  on `users`:
  ```sql
  ALTER TABLE user_branches ENABLE ROW LEVEL SECURITY;
  ALTER TABLE user_branches FORCE ROW LEVEL SECURITY;
  CREATE POLICY tenant_isolation_policy ON user_branches USING (
    user_id IN (SELECT id FROM users WHERE tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid)
  );
  ```
- `user_role_assignments` — same subquery-on-`users` pattern as `user_branches`.
- `refresh_tokens` — same subquery-on-`users` pattern.
- `settings` — direct `tenant_id`.

`tenants` itself is NEVER RLS-protected by a `tenant_id` policy (it has no parent scope);
instead it is only ever queried by `id = @tenantId` at the application layer during
login/tenant-resolution, before any `IAccessScope` exists. Document this explicitly as the
one intentional exception to the "100% RLS coverage" rule, and note it precisely so P-111's
automated CI schema test can special-case it.

## 4. TASK

1. Generate an empty migration: `dotnet ef migrations add 005_RlsTenantOnlyPolicies
   --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`.
2. Edit the generated migration file's `Up()` method to contain ONLY raw
   `migrationBuilder.Sql(...)` calls implementing every policy shown in the CONTEXT BLOCK, for
   all six tables: `branches`, `users`, `roles`, `role_permissions`, `user_branches`,
   `user_role_assignments`, `refresh_tokens`, `settings` (that's eight tables total — list
   corrected: branches, users, roles, role_permissions, user_branches,
   user_role_assignments, refresh_tokens, settings).
3. Edit `Down()` to `DROP POLICY tenant_isolation_policy ON <table>;` and
   `ALTER TABLE <table> DISABLE ROW LEVEL SECURITY;` for each of the eight tables, in reverse
   order.
4. Apply the migration.
5. Write `StoreOS.IntegrationTests/Security/RlsPolicyTests.cs` that opens a RAW Npgsql
   connection (bypassing EF entirely), runs `BEGIN; SET LOCAL app.tenant_id = '<guid-A>';`,
   inserts a row into `branches` for tenant A directly via SQL, commits, then in a NEW
   transaction with NO `SET LOCAL` at all, runs `SELECT * FROM branches` and asserts **zero**
   rows are returned (proving the fail-closed behavor) even though the row exists. Then in
   another transaction `SET LOCAL app.tenant_id = '<guid-A>'` and confirm the row IS visible.
   Then `SET LOCAL app.tenant_id = '<guid-B>'` (a different tenant) and confirm it is NOT
   visible.
6. Document in `docs/ARTIFACTS.md` the exact list of RLS-protected tables so far and the one
   documented exception (`tenants`).

## 5. HARD CONSTRAINTS

- Every `current_setting` call MUST use the two-argument form
  (`current_setting('app.xxx', true)`) — the `missing_ok=true` second argument is mandatory
  everywhere in this codebase, in every RLS policy, forever.
- `FORCE ROW LEVEL SECURITY` (not just `ENABLE`) is mandatory on every table — `FORCE` also
  applies RLS to the table owner, which by default is bypassed by `ENABLE` alone. Since the
  application's DB role IS typically the table owner in local/dev setups, omitting `FORCE`
  would silently defeat RLS entirely.
- Never write a policy with `OR current_setting(...) IS NULL` — this is the forbidden
  bypass pattern called out explicitly in Plan §9.2 and must never appear anywhere in this
  repository.

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
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
docker exec storeos-postgres psql -U storeos -d storeos -c "SELECT relname, relrowsecurity, relforcerowsecurity FROM pg_class WHERE relname IN ('branches','users','roles','role_permissions','user_branches','user_role_assignments','refresh_tokens','settings');"
# Expected: relrowsecurity=t and relforcerowsecurity=t for all eight rows.
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsPolicyTests
# Expected: fail-closed (no SET LOCAL -> 0 rows), tenant A sees own row, tenant B sees none.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-108 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-108 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-108` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the full list of eight RLS-protected tables, the exact policy SQL pattern
(direct-column vs. subquery-join variants), and the documented `tenants` exception.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-108: <one-line summary>"` only after verification is
   fully green.
