# P-303 — POST /auth/login endpoint + handler

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-303** of the StoreOS Cloud build ("POST /auth/login endpoint + handler") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-302 complete: `JwtTokenService` issues correctly-shaped tokens; real JWT
  bearer auth is wired; middleware pipeline order is finalized.
- No login endpoint exists yet. `User` entities can be created via `User.Create` (P-104) but
  no seed data or registration flow exists — this prompt's integration test must manually
  seed a user directly via `StoreOsDbContext` (bypassing any nonexistent registration
  endpoint) before testing login against it.
- Effective-access resolution (`BranchIds`, `Permissions` from `user_role_assignments` +
  `role_permissions` + `user_branches`) has not been implemented as a query yet — build it now
  as part of this handler, since login is the first place it's needed.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** Login runs BEFORE authentication, but `users`/`roles`/`role_permissions`/`user_branches` are protected by FORCE ROW LEVEL SECURITY. `IgnoreQueryFilters()` does NOT bypass RLS. The flow is therefore: (1) the controller resolves the tenant — request body `tenantId` if present, otherwise `ITenantResolver.ResolveFromHostAsync(Request.Host.Value)` from P-102 — and returns `422 tenant_not_found` when neither yields a tenant; (2) `LoginCommand` implements `IAnonymousTenantRequest`, so `TransactionBehavior` (P-109) calls `IAccessScope.UseAnonymousTenant(command.TenantId)` before opening the transaction; (3) every query in the handler then runs under `app.tenant_id = <that tenant>`, and the `IgnoreQueryFilters()` calls in the snippet are still needed (EF filters key off an authenticated scope). Use the same generic error (`invalid_credentials`) for unknown email, wrong password and inactive user, and run the password hash comparison even when the user is not found (hash a dummy value) to avoid a user-enumeration timing leak. The request DTO is `LoginRequest(string Email, string Password, Guid? TenantId)`; the controller builds the `LoginCommand`.

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create the command `backend/src/StoreOS.Application/Identity/Login/LoginCommand.cs`:

```csharp
using MediatR;

namespace StoreOS.Application.Identity.Login;

public record LoginCommand(string Email, string Password) : IRequest<LoginResult>;

public record LoginResult(string AccessToken, DateTimeOffset ExpiresAt, Guid UserId,
    string DisplayName, Guid TenantId, bool AllBranches, IReadOnlyList<Guid> BranchIds,
    IReadOnlyList<string> Permissions);
```

Create `backend/src/StoreOS.Application/Identity/Login/LoginValidator.cs`:

```csharp
using FluentValidation;

namespace StoreOS.Application.Identity.Login;

public class LoginValidator : AbstractValidator<LoginCommand>
{
    public LoginValidator()
    {
        RuleFor(x => x.Email).NotEmpty().EmailAddress();
        RuleFor(x => x.Password).NotEmpty();
    }
}
```

Create `backend/src/StoreOS.Application/Identity/Login/LoginHandler.cs`. Note: login is a
SPECIAL case — it runs BEFORE any `IAccessScope` exists (the user isn't authenticated yet), so
it must query `users`/`roles`/etc. WITHOUT tenant-scoped RLS filtering being meaningfully
applicable (the query is genuinely cross-tenant BY EMAIL, since email is only unique per
tenant — Plan's `UNIQUE(tenant_id, email)` means the SAME email string could theoretically
exist in multiple tenants). Resolve this ambiguity: **the login endpoint requires the tenant to
be resolved BEFORE lookup**, either via subdomain (Plan §12.1, `<tenant>.pos.storeos.io` — not
buildable/testable in this backend-only prompt since there's no reverse-proxy subdomain
resolution here) OR by accepting an explicit `TenantSlugOrId` in the login request for
non-subdomain (API-direct/testing) scenarios. Add this field now:

```csharp
public record LoginCommand(string Email, string Password, Guid TenantId)
    : IRequest<LoginResult>, IAnonymousTenantRequest;
```

(Simplify to require the caller to already know their `TenantId` — a real subdomain-based
resolution layer is added in P-1001/P-1008 on the frontend/gateway side, translating a
subdomain into this `TenantId` before calling the API; document this explicitly as the
handoff point.)

`LoginHandler.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Identity;
using StoreOS.Domain.Exceptions;

using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Login;

public class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public LoginHandler(IAppDbContext db, IPasswordHasher hasher, IJwtTokenService jwt)
    {
        _db = db; _hasher = hasher; _jwt = jwt;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // IgnoreQueryFilters: this is the one sanctioned pre-authentication exception noted
        // in P-107 — no IAccessScope exists yet, and tenant is disambiguated explicitly by
        // request.TenantId rather than by an ambient scope.
        var user = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == request.TenantId && u.Email == normalizedEmail, ct);

        if (user is null || !user.IsActive || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new DomainException("invalid_credentials", "Invalid email or password.");

        var tenant = await _db.Set<StoreOS.Domain.Tenancy.Tenant>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new DomainException("tenant_inactive", "This tenant account is not active.");

        var assignments = await _db.Set<StoreOS.Domain.Identity.UserRoleAssignment>().IgnoreQueryFilters()
            .Where(a => a.UserId == user.Id)
            .ToListAsync(ct);
        var allBranches = assignments.Any(a => a.BranchId == null);

        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var permissions = await _db.Set<StoreOS.Domain.Identity.RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionCode)
            .Distinct()
            .ToListAsync(ct);

        var userBranchIds = await _db.Set<StoreOS.Domain.Identity.UserBranch>().IgnoreQueryFilters()
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => ub.BranchId)
            .ToListAsync(ct);

        var (token, expiresAt) = _jwt.IssueAccessToken(
            user.Id, user.TenantId, allBranches, userBranchIds, permissions, user.TokenVersion);

        return new LoginResult(token, expiresAt, user.Id, user.DisplayName, user.TenantId,
            allBranches, userBranchIds, permissions);
    }
}
```

Note the `BranchIds` returned in `LoginResult`/embedded in the JWT for an `allBranches=true`
user is their OWN `user_branches` rows (used for e.g. a default-branch UI hint), NOT the full
set of all tenant branches — `HttpAccessScope.HasBranchAccess` already treats `allBranches=true`
as "any branch", the JWT's `branch_id` claims are simply omitted in that case per
`JwtTokenService`'s `if (!allBranches)` guard.

Create `backend/src/StoreOS.Api/Controllers/AuthController.cs`:

```csharp
using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Identity.Login;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IMediator _mediator;
    public AuthController(IMediator mediator) => _mediator = mediator;

    [HttpPost("login")]
    [AllowAnonymous]
    public async Task<IActionResult> Login(LoginCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(new
        {
            accessToken = result.AccessToken, expiresAt = result.ExpiresAt, userId = result.UserId,
            displayName = result.DisplayName, tenantId = result.TenantId, allBranches = result.AllBranches,
            branchIds = result.BranchIds, permissions = result.Permissions
        });
    }
}
```

`invalid_credentials` and `tenant_inactive` are `DomainException` codes — per P-114's status
mapping these currently fall through to 422; for login specifically, map `invalid_credentials`
to 401 instead (more conventional) by adding it to `ExceptionHandlingMiddleware`'s switch
alongside `unauthenticated`/`forbidden`. `tenant_inactive` can stay 422 (it's a legitimate
business-state rejection, not an auth failure) — or map it to 403; choose 403 since it
represents "you exist but you're not allowed in" which is closer to Forbidden than
Unprocessable. Update the switch:

```csharp
var status = dex.Code switch
{
    "unauthenticated" => HttpStatusCode.Unauthorized,
    "invalid_credentials" => HttpStatusCode.Unauthorized,
    "forbidden" => HttpStatusCode.Forbidden,
    "tenant_inactive" => HttpStatusCode.Forbidden,
    _ => HttpStatusCode.UnprocessableEntity
};
```

## 4. TASK

1. Create `LoginCommand.cs`, `LoginValidator.cs`, `LoginHandler.cs` exactly as shown
   in `StoreOS.Application/Identity/Login/`.
2. Create `AuthController.cs` with the `POST /auth/login` action exactly as shown, marked
   `[AllowAnonymous]`.
3. Update `ExceptionHandlingMiddleware`'s status-code switch to add `invalid_credentials`->401
   and `tenant_inactive`->403.
4. Write `StoreOS.IntegrationTests/Identity/LoginTests.cs`: seed (via direct
   `StoreOsDbContext` calls with `IgnoreQueryFilters` bypass in test setup, or via a
   `NoOpAccessScope`-scoped context instance) a tenant, a role with `pos:checkout` permission,
   a user with a known Argon2-hashed password assigned that role tenant-wide (`branch_id =
   null`), then: (a) valid email+password+tenantId -> 200 with a well-formed JWT whose claims
   match; (b) wrong password -> 401 `invalid_credentials`; (c) unknown email -> 401
   `invalid_credentials` (same generic message/code as wrong password — never reveal whether
   the email exists); (d) inactive user -> 401 `invalid_credentials` (also generic, do not leak
   "user is disabled" distinctly, to avoid user enumeration); (e) inactive tenant -> 403
   `tenant_inactive`.

## 5. HARD CONSTRAINTS

- Login failure messages for "wrong password", "unknown email", and
  "inactive user" must ALL use the SAME generic `invalid_credentials` code/message — never let
  an attacker distinguish "this email doesn't exist" from "this email exists but the password
  is wrong" via response differences (standard anti-enumeration practice).
- `IgnoreQueryFilters()` in `LoginHandler` is the ONLY place in the entire backend where this
  is acceptable in production code — it is required because no `IAccessScope` exists yet at
  this point in the request lifecycle. Every other production handler must NEVER call
  `IgnoreQueryFilters()`.
- Refresh token issuance is NOT part of this prompt — `LoginResult` currently only returns an
  access token. P-304 adds the refresh-token/cookie side on top of this same handler.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter LoginTests
# Expected: 5 passed (valid login, wrong password, unknown email, inactive user - all 401
# invalid_credentials; inactive tenant - 403 tenant_inactive).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-303 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-303 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-303` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `LoginCommand`/`LoginResult` shapes, the `POST /api/auth/login` route and
response JSON shape, and the anti-enumeration rule (all credential failures share one generic
code).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-303: <one-line summary>"` only after verification is
   fully green.
