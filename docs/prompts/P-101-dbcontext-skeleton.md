# P-101 — Core EF DbContext skeleton + Npgsql registration

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-101** of the StoreOS Cloud build ("Core EF DbContext skeleton + Npgsql registration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- Phase 0 (P-001–007) complete: backend solution builds, packages pinned,
  Angular workspace builds, docker dev stack (postgres/minio/otel) runs, CI skeleton green.
- No EF Core DbContext exists yet. No database tables exist yet.
- `docs/ARTIFACTS.md` has only the P-001 header entry so far; check it for exact project
  paths before starting.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** **Two database roles (DECISIONS D-03).** Row Level Security is NEVER applied to superusers or roles with BYPASSRLS — not even with FORCE ROW LEVEL SECURITY. The docker-compose `POSTGRES_USER` (and the Testcontainers default user) is a superuser, so an application that connected as that user would silently have NO tenant isolation. Therefore:
- `storeos` (compose `POSTGRES_USER`, superuser/owner) is used ONLY for migrations and administration.
- `storeos_app` (`LOGIN NOSUPERUSER NOBYPASSRLS`, created by P-002) is the role the API and background jobs connect with. The `Default` and `Reporting` connection strings above use it.
Create `backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContextFactory.cs`:
```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StoreOS.Infrastructure.Persistence;

/// Used ONLY by `dotnet ef` (design time). EF prefers a design-time factory over the host's service
/// provider, so every `dotnet ef migrations/database update` in this build automatically runs as the
/// OWNER role. Override with env var STOREOS_MIGRATIONS_CONNECTION (CI, staging, production).
public sealed class StoreOsDbContextFactory : IDesignTimeDbContextFactory<StoreOsDbContext>
{
    public StoreOsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password";
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(cs).Options;
        return new StoreOsDbContext(options);
    }
}
```
(P-107 changes the constructor to `(options, IAccessScope)`; at that point this factory passes `new NullAccessScope()` — P-107 must make that edit.) Add `docs/RUNBOOK-db-roles.md` (short): the two roles, what each may do, `deploy/db/post-migrate.sql` must run after every migration (P-002 creates it), and how to reproduce locally with `docker compose down -v` when the init script changes.

> **REVISION NOTE (review v2):** This prompt ALSO creates the Application-layer database abstraction (layering fix). Create `backend/src/StoreOS.Application/Abstractions/IAppDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace StoreOS.Application.Abstractions;

public interface IAppDbContext
{
    DbSet<TEntity> Set<TEntity>() where TEntity : class;
    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
```

`StoreOsDbContext : DbContext, IAppDbContext` satisfies it with no extra members (all four are public members of `DbContext`). Add the package reference `Microsoft.EntityFrameworkCore` to `StoreOS.Application.csproj` (it is pinned centrally in P-004). Application code must use `_db.Set<T>()` for entity access and must never reference `StoreOS.Infrastructure`.

Create `backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs`:

```csharp
using Microsoft.EntityFrameworkCore;

namespace StoreOS.Infrastructure.Persistence;

public class StoreOsDbContext : DbContext, StoreOS.Application.Abstractions.IAppDbContext
{
    public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        // Entity configurations are added via IEntityTypeConfiguration<T> classes applied
        // from each module as entities are introduced in later prompts:
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);
    }
}
```

Register it and Npgsql in `backend/src/StoreOS.Api/Program.cs`. Use this exact connection
string key convention (matches P-002's docker-compose environment variables):

```csharp
builder.Services.AddDbContext<StoreOsDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<StoreOS.Application.Abstractions.IAppDbContext>(
    sp => sp.GetRequiredService<StoreOsDbContext>());
    // NOTE: do NOT call EnableRetryOnFailure(...) at all (not even with 0 retries). EF's retrying
    // execution strategy rejects user-initiated transactions, and serialization-failure retry is
    // implemented by SerializationRetryBehavior around the whole transaction (P-511).
```

Create `backend/src/StoreOS.Api/appsettings.json` connection strings section:

```json
{
  "ConnectionStrings": {
    "Default": "Host=localhost;Port=5432;Database=storeos;Username=storeos_app;Password=storeos_app_dev_password;Maximum Pool Size=50",
    "Reporting": "Host=localhost;Port=5432;Database=storeos;Username=storeos_app;Password=storeos_app_dev_password;Maximum Pool Size=20"
  }
}
```

(The `Reporting` key is unused until P-119 but declared now so the config shape is stable.)

## 4. TASK

1. Add `Microsoft.EntityFrameworkCore` and `Npgsql.EntityFrameworkCore.PostgreSQL`
   `PackageReference` entries (no version — central management) to
   `StoreOS.Infrastructure.csproj` if not already present from P-004.
2. Create `backend/src/StoreOS.Infrastructure/Persistence/StoreOsDbContext.cs` exactly as
   shown.
3. In `backend/src/StoreOS.Api/Program.cs`, add the `AddDbContext<StoreOsDbContext>` call
   exactly as shown, reading `ConnectionStrings:Default` from configuration.
4. Update `backend/src/StoreOS.Api/appsettings.json` to include the `ConnectionStrings`
   section shown (both `Default` and `Reporting` keys, identical values for now).
5. Add `Microsoft.EntityFrameworkCore.Design` as a `PackageReference` to `StoreOS.Api.csproj`
   (needed for `dotnet ef` tooling) and install the EF CLI tool if not already available:
   `dotnet tool restore` (the local `dotnet-ef` 10.0.0 tool manifest is created in P-004; do not install it globally).
6. Confirm `dotnet ef dbcontext info --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api`
   resolves the context successfully (requires the postgres container from P-002 running).
7. Add basic CORS scaffolding to `Program.cs` (full dynamic subdomain validator comes later
   in a dedicated security prompt referenced by Plan §13.1 — for now, add a named policy
   `"Default"` allowing `http://localhost:4200` only, applied via `app.UseCors("Default")`),
   so local frontend dev against the API works from day one.
8. Add basic health check endpoints now (`/health/live` returns 200 always; `/health/ready`
   checks DB connectivity via `AspNetCore.HealthChecks.NpgSql`), per Plan §15 — wire
   `builder.Services.AddHealthChecks().AddNpgSql(...)` and map
   `app.MapHealthChecks("/health/live")` / `app.MapHealthChecks("/health/ready")`.

## 5. HARD CONSTRAINTS

- `EnableRetryOnFailure` must be explicitly OFF (`maxRetryCount: 0`)
  — do not enable EF's built-in resiliency; it conflicts with the ambient-transaction
  pattern introduced in P-109 and the Polly wrapper in P-511.
- Do not create any `DbSet<T>` properties yet — there are no entities. This prompt is
  infrastructure wiring only.
- Do not attempt any migration yet (`dotnet ef migrations add`) — the first migration is
  P-102, once the `Tenant`/`Branch` entities exist.

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
docker-compose -f deploy/docker-compose.yml up -d postgres
dotnet build StoreOS.sln
dotnet ef dbcontext info --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
# Expected: prints "Provider name: Npgsql.EntityFrameworkCore.PostgreSQL" without error.
curl -sf http://localhost:5000/health/live  # (after `dotnet run --project backend/src/StoreOS.Api` in another shell)
# Expected: 200 OK (run this manually if not automated; document result)
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-101 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-101 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-101` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact `StoreOsDbContext` namespace/class signature, the exact
`ConnectionStrings` keys (`Default`, `Reporting`), the CORS policy name (`Default`), and the
health check route paths (`/health/live`, `/health/ready`).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-101: <one-line summary>"` only after verification is
   fully green.
