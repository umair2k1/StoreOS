# P-106 — IAccessScope + AccessScopeMiddleware (tenant/branch resolution from JWT)

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-106** of the StoreOS Cloud build ("IAccessScope + AccessScopeMiddleware (tenant/branch resolution from JWT)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-101–105 complete: full tenancy+identity schema exists and migrates.
- JWT issuance does NOT exist yet (that's P-302) — this prompt builds the CONSUMPTION side
  of claims (reading a JWT's claims into a request-scoped access object), using a
  hand-crafted test JWT in its integration test since real login doesn't exist yet.
- No authentication middleware is wired in `Program.cs` yet.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `IAccessScope` also gets `void UseSystemContext(Guid tenantId)` (background jobs, see P-109 `ITenantJobRunner`). Also create `backend/src/StoreOS.Infrastructure/Persistence/NullAccessScope.cs` — a public sealed `IAccessScope` with `TenantId = Guid.Empty`, empty `BranchIds`/`Permissions`, `AllBranches=false`, `IsAuthenticated=false`, `HasTenantContext=false`, no-op `UseAnonymousTenant`/`UseSystemContext` (throw `InvalidOperationException`). It is used by the design-time DbContext factory (P-101/P-107) and by integration-test fixtures that only need schema access (it replaces the `NoOpAccessScope` that P-111 used to define).

> **REVISION NOTE (review v2):** `IAccessScope` gained `HasTenantContext` and `UseAnonymousTenant(Guid)` so anonymous flows (login/refresh/logout) can run RLS-protected queries for an explicitly named tenant. `IgnoreQueryFilters()` only disables EF filters — PostgreSQL RLS still applies, so without this the anonymous login query would fail. Every test double implementing `IAccessScope` must implement both members too. Also create `backend/src/StoreOS.Application/Abstractions/IAnonymousTenantRequest.cs`: `public interface IAnonymousTenantRequest { Guid TenantId { get; } }` — commands that legitimately run before authentication implement it (see P-109 `TransactionBehavior`).

Create `backend/src/StoreOS.Application/Abstractions/IAccessScope.cs`:

```csharp
namespace StoreOS.Application.Abstractions;

public interface IAccessScope
{
    Guid TenantId { get; }
    Guid UserId { get; }
    IReadOnlySet<Guid> BranchIds { get; }
    bool AllBranches { get; }
    IReadOnlySet<string> Permissions { get; }
    int TokenVersion { get; }
    bool IsAuthenticated { get; }
    /// True when a tenant id is known (authenticated user OR an anonymous flow that called UseAnonymousTenant).
    bool HasTenantContext { get; }
    /// Pre-authentication flows (login, refresh, logout) declare the tenant explicitly. Sets TenantId only;
    /// BranchIds stays empty, AllBranches=false, IsAuthenticated stays false.
    void UseAnonymousTenant(Guid tenantId);
    /// Background jobs (reconciliation, imports): TenantId set, AllBranches=true, no permissions, IsAuthenticated=false.
    void UseSystemContext(Guid tenantId);

    bool HasBranchAccess(Guid branchId);
    bool HasPermission(string permissionCode);
}
```

Create the concrete scoped implementation
`backend/src/StoreOS.Infrastructure/Security/HttpAccessScope.cs`:

```csharp
using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Security;

public class HttpAccessScope : IAccessScope
{
    private Guid _tenantId;
    private readonly Guid _userId;
    private readonly HashSet<Guid> _branchIds;
    private bool _allBranches;
    private readonly HashSet<string> _permissions;
    private readonly int _tokenVersion;
    private readonly bool _isAuthenticated;

    public HttpAccessScope(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        _isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
        if (!_isAuthenticated)
        {
            _tenantId = Guid.Empty; _userId = Guid.Empty;
            _branchIds = new HashSet<Guid>(); _permissions = new HashSet<string>();
            return;
        }
        _tenantId = Guid.Parse(user!.FindFirstValue("tenant_id")!);
        _userId = Guid.Parse(user.FindFirstValue("sub")!);
        _allBranches = user.FindFirstValue("all_branches") == "true";
        _branchIds = user.FindAll("branch_id").Select(c => Guid.Parse(c.Value)).ToHashSet();
        _permissions = user.FindAll("perm").Select(c => c.Value).ToHashSet();
        _tokenVersion = int.Parse(user.FindFirstValue("token_version") ?? "0");
    }

    public Guid TenantId => _tenantId;
    public bool HasTenantContext => _tenantId != Guid.Empty;
    public void UseSystemContext(Guid tenantId)
    {
        if (_isAuthenticated) throw new InvalidOperationException("Authenticated scopes cannot switch tenant.");
        _tenantId = tenantId; _allBranches = true;
    }
    public void UseAnonymousTenant(Guid tenantId)
    {
        if (_isAuthenticated) throw new InvalidOperationException("Authenticated scopes cannot switch tenant.");
        _tenantId = tenantId;
    }
    public Guid UserId => _userId;
    public IReadOnlySet<Guid> BranchIds => _branchIds;
    public bool AllBranches => _allBranches;
    public IReadOnlySet<string> Permissions => _permissions;
    public int TokenVersion => _tokenVersion;
    public bool IsAuthenticated => _isAuthenticated;

    public bool HasBranchAccess(Guid branchId) => _allBranches || _branchIds.Contains(branchId);
    public bool HasPermission(string permissionCode) => _permissions.Contains(permissionCode);
}
```

Register as **scoped** (one instance per HTTP request) in `Program.cs`:
```csharp
builder.Services.AddHttpContextAccessor();
builder.Services.AddScoped<IAccessScope, HttpAccessScope>();
```

Claim naming convention (fixed forever — P-302's JWT issuance must produce exactly these
claim types): `sub` (user id), `tenant_id`, `all_branches` (`"true"`/`"false"`), `branch_id`
(repeated claim, one per accessible branch — omitted entirely when `all_branches=true`),
`perm` (repeated claim, one per granted permission code), `token_version`.

This prompt does NOT wire real JWT bearer authentication yet (no signing key exists before
P-302); instead, add a **test-only authentication handler** used solely by
`StoreOS.IntegrationTests` to inject a `ClaimsPrincipal` with the above claims directly,
proving `HttpAccessScope` reads them correctly. Real `AddJwtBearer` wiring happens in P-302.

## 4. TASK

1. Create `IAccessScope.cs` in `StoreOS.Application/Abstractions/` exactly as shown.
2. Create `HttpAccessScope.cs` in `StoreOS.Infrastructure/Security/` exactly as shown.
3. Register `IHttpContextAccessor` and `IAccessScope` (scoped) in `Program.cs`.
4. In `StoreOS.IntegrationTests`, create a `TestAuthHandler : AuthenticationHandler<AuthenticationSchemeOptions>`
   that reads a static `ClaimsPrincipal` from a test-settable `AsyncLocal<ClaimsPrincipal?>`
   (or a similar mechanism) and succeeds authentication with it, registered only in a
   `WebApplicationFactory` test fixture (not in the real `Program.cs` pipeline).
5. Add a minimal test-only endpoint (e.g. `GET /internal/_test/access-scope`, compiled ONLY
   under `#if DEBUG` or gated by an `IWebHostEnvironment.IsDevelopment()` check AND only
   mapped when running under the test fixture, to avoid ever shipping this in production) OR,
   preferably, write the test using `WebApplicationFactory`'s `ConfigureTestServices` to
   resolve `IAccessScope` directly from a scoped `IServiceScope` inside the test after setting
   up a fake `HttpContext` with the claims principal — this avoids needing any test-only
   production endpoint. Prefer this second approach: it keeps `Program.cs` free of test-only
   routes.
6. Write `StoreOS.IntegrationTests/Security/AccessScopeTests.cs` with at least these cases:
   unauthenticated request -> `IsAuthenticated=false`, empty branch/permission sets;
   authenticated with 2 branch_id claims and `all_branches=false` -> `BranchIds.Count==2`,
   `HasBranchAccess` true for those two only; `all_branches=true` with zero branch_id claims
   -> `HasBranchAccess` true for any random Guid.

## 5. HARD CONSTRAINTS

- `HttpAccessScope` must be registered as **Scoped**, never Singleton
  (it reads per-request `HttpContext.User`) and never Transient (would re-parse claims
  multiple times per request unnecessarily, though not incorrect — Scoped is the contract).
- Claim type names (`sub`, `tenant_id`, `all_branches`, `branch_id`, `perm`,
  `token_version`) are fixed here and must be produced identically by P-302's JWT issuance —
  record them precisely in `docs/ARTIFACTS.md`.
- Do not wire real `AddAuthentication().AddJwtBearer(...)` in this prompt — no signing key
  exists yet. That is P-302's job.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter AccessScopeTests
# Expected: all AccessScopeTests pass (unauthenticated, partial-branch, all-branches cases).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-106 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-106 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-106` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IAccessScope` interface signature, the exact JWT claim type names
(`sub`, `tenant_id`, `all_branches`, `branch_id`, `perm`, `token_version`), and how the
integration test fixture injects a fake principal (so P-107+ integration tests reuse the same
mechanism instead of reinventing it).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-106: <one-line summary>"` only after verification is
   fully green.
