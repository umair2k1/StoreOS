# P-102 — Tenants + Branches entities/migration

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-102** of the StoreOS Cloud build ("Tenants + Branches entities/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-101 complete: `StoreOsDbContext` exists, registered with Npgsql, connects to
  the P-002 postgres container, health checks respond.
- No entities exist in `StoreOS.Domain` yet. This is the first real domain entity + migration.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** Two additions vs the original text (details in DECISIONS D-05 / D-06):
1. **`tenants.subdomain`** (unique, lowercase slug) — the plan routes tenants by `<tenant>.pos.storeos.io` but the schema had no column to resolve it. Also create `backend/src/StoreOS.Domain/Tenancy/Slug.cs` (`public static class Slug { public static string FromName(string name) }` → lowercase, non-alphanumerics → `-`, trimmed, max 40 chars, plus `-` + 4 random hex chars so it is unique without a DB round-trip; unit-test it). Migration also adds the two CHECK constraints via raw SQL. Add an EF unique index `ux_tenants_subdomain`.
2. **Branch code ≤ 3 characters** (`^[A-Z1-9][A-Z0-9]{0,2}$`): the FY invoice number `<BRANCH>/<FY>/<NNNNNN>` is exactly 16 characters with a 3-character code and GST Rule 46(b)/e-invoice schema cap invoice numbers at 16 characters and forbid a leading 0.
3. Create `backend/src/StoreOS.Application/Abstractions/ITenantResolver.cs`:
```csharp
namespace StoreOS.Application.Abstractions;
public record TenantPublicProfile(Guid TenantId, string Name, string Subdomain, bool IsActive);
public interface ITenantResolver
{
    /// host like "apex.pos.storeos.io", "apex.localhost:4200"; null when no tenant matches.
    Task<TenantPublicProfile?> ResolveFromHostAsync(string host, CancellationToken ct);
    Task<TenantPublicProfile?> ResolveByIdAsync(Guid tenantId, CancellationToken ct);
}
```
and its implementation `backend/src/StoreOS.Infrastructure/Tenancy/TenantResolver.cs` (uses `StoreOsDbContext.Tenants`, `AsNoTracking()`; `tenants` is the one table without RLS). Host parsing: strip the port, lower-case, require the host to end with `"." + baseDomain` for a base domain in configuration `Tenancy:BaseDomains` (default `["pos.storeos.io","localhost"]`), and the remaining left label must match the subdomain regex. Register `AddScoped<ITenantResolver, TenantResolver>()`. Tests: resolves `apex.pos.storeos.io` and `apex.localhost:4200`; returns null for unknown subdomain, bare base domain, and `evil.com`.

Exact DDL target (from Plan §8.2) — your EF configuration must produce
schema equivalent to this:

```sql
CREATE TABLE tenants (
  id uuid PRIMARY KEY,
  name text NOT NULL,
  subdomain text NOT NULL UNIQUE CHECK (subdomain ~ '^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$'),  -- <subdomain>.pos.storeos.io
  is_active bool NOT NULL DEFAULT true,
  timezone text NOT NULL DEFAULT 'Asia/Kolkata',
  features jsonb NOT NULL DEFAULT '{}',
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE branches (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL REFERENCES tenants(id),
  code text NOT NULL CHECK (code ~ '^[A-Z1-9][A-Z0-9]{0,2}$'),  -- <=3 chars so <BRANCH>/<FY>/<NNNNNN> stays <=16 chars (Rule 46(b))
  name text NOT NULL,
  address text, phone text, email text,
  gstin text,
  is_active bool NOT NULL DEFAULT true,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, code)
);
```

Create `backend/src/StoreOS.Domain/Tenancy/Tenant.cs`:

```csharp
namespace StoreOS.Domain.Tenancy;

public class Tenant
{
    public Guid Id { get; private set; }
    public string Name { get; private set; } = default!;
    public string Subdomain { get; private set; } = default!;
    public bool IsActive { get; private set; } = true;
    public string Timezone { get; private set; } = "Asia/Kolkata";
    public string FeaturesJson { get; private set; } = "{}";
    public DateTimeOffset CreatedAt { get; private set; }

    private Tenant() { }

    public static Tenant Create(string name, string timezone = "Asia/Kolkata", string? subdomain = null)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        return new Tenant
        {
            Id = Guid.NewGuid(),
            Name = name.Trim(),
            Subdomain = subdomain ?? Slug.FromName(name),
            Timezone = timezone,
            FeaturesJson = "{}",
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void Rename(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Tenant name is required.", nameof(name));
        Name = name.Trim();
    }

    public void SetSubdomain(string subdomain)
    {
        if (!System.Text.RegularExpressions.Regex.IsMatch(subdomain ?? "", "^[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?$"))
            throw new ArgumentException("Subdomain must be lowercase letters, digits and hyphens.", nameof(subdomain));
        Subdomain = subdomain!;
    }
    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
    public void SetFeatures(string featuresJson) => FeaturesJson = featuresJson;
}
```

Create `backend/src/StoreOS.Domain/Tenancy/Branch.cs`:

```csharp
namespace StoreOS.Domain.Tenancy;

public class Branch
{
    public Guid Id { get; private set; }
    public Guid TenantId { get; private set; }
    public string Code { get; private set; } = default!;
    public string Name { get; private set; } = default!;
    public string? Address { get; private set; }
    public string? Phone { get; private set; }
    public string? Email { get; private set; }
    public string? Gstin { get; private set; }
    public bool IsActive { get; private set; } = true;
    public DateTimeOffset CreatedAt { get; private set; }

    private Branch() { }

    public static Branch Create(Guid tenantId, string code, string name)
    {
        code = (code ?? string.Empty).Trim().ToUpperInvariant();
        if (!System.Text.RegularExpressions.Regex.IsMatch(code, "^[A-Z1-9][A-Z0-9]{0,2}$"))
            throw new ArgumentException("Branch code must be 1-3 characters (A-Z, 0-9), not starting with 0.", nameof(code));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is required.", nameof(name));
        return new Branch
        {
            Id = Guid.NewGuid(),
            TenantId = tenantId,
            Code = code,
            Name = name.Trim(),
            CreatedAt = DateTimeOffset.UtcNow
        };
    }

    public void UpdateProfile(string name, string? address, string? phone, string? email, string? gstin)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Branch name is required.", nameof(name));
        Name = name.Trim();
        Address = address;
        Phone = phone;
        Email = email;
        Gstin = gstin;
    }

    public void Deactivate() => IsActive = false;
    public void Activate() => IsActive = true;
}
```

EF configuration `backend/src/StoreOS.Infrastructure/Persistence/Configurations/TenantConfiguration.cs`
and `BranchConfiguration.cs` must map `FeaturesJson` to a `jsonb` column named `features` via
`.HasColumnType("jsonb").HasColumnName("features")`, snake_case all column names explicitly
(EF Core does not auto-snake-case; you must call `.HasColumnName("tenant_id")` etc. on every
property), and configure the `UNIQUE (tenant_id, code)` index via
`.HasIndex(b => new { b.TenantId, b.Code }).IsUnique()`.

## 4. TASK

1. Create `Tenant.cs` and `Branch.cs` in `StoreOS.Domain/Tenancy/` exactly as shown
   (private setters, static factory methods, no public parameterless constructor except the
   private EF one).
2. Create `TenantConfiguration.cs` and `BranchConfiguration.cs` implementing
   `IEntityTypeConfiguration<Tenant>` / `IEntityTypeConfiguration<Branch>` in
   `StoreOS.Infrastructure/Persistence/Configurations/`, mapping every column to its exact
   snake_case name as shown in the DDL, including the `UNIQUE(tenant_id, code)` index, the FK
   `branches.tenant_id -> tenants.id`, and defaults (`is_active` default true, `timezone`
   default `'Asia/Kolkata'`, `created_at` default `now()` via `.HasDefaultValueSql("now()")`).
3. Add `DbSet<Tenant> Tenants` and `DbSet<Branch> Branches` to `StoreOsDbContext`.
4. Generate and apply the migration:
   ```bash
   dotnet ef migrations add 001_InitTenancyBranches \
     --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
   dotnet ef database update \
     --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
   ```
5. Confirm via `psql` (or `docker exec storeos-postgres psql -U storeos -d storeos -c "\d tenants"`
   and `"\d branches"`) that both tables exist with the exact columns from the DDL.

## 5. HARD CONSTRAINTS

- Column names MUST be explicit snake_case (`tenant_id`, `is_active`,
  `created_at`, etc.) — EF Core's default naming convention (PascalCase) is not acceptable
  anywhere in this schema; every later migration in this project must follow the same
  explicit-snake_case pattern.
- Entities must not have public setters — mutation only through named methods
  (`Rename`, `Deactivate`, etc.) per Plan §5.1's DDD aggregate-root guidance.
- RLS is NOT enabled on these tables yet — that happens in P-108. Do not add RLS SQL in this
  prompt.

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
dotnet build StoreOS.sln
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
docker exec storeos-postgres psql -U storeos -d storeos -c "\d tenants"
docker exec storeos-postgres psql -U storeos -d storeos -c "\d branches"
# Expected: both describe commands show all columns from the DDL with correct types.
docker exec storeos-postgres psql -U storeos -d storeos -c "SELECT indexdef FROM pg_indexes WHERE tablename='branches';"
# Expected: shows a UNIQUE index on (tenant_id, code).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-102 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-102 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-102` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact migration name `001_InitTenancyBranches`, the `Tenant`/`Branch`
class signatures (all public members), and the exact DbSet property names on
`StoreOsDbContext`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-102: <one-line summary>"` only after verification is
   fully green.
