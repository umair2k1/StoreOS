# P-306 — POST /auth/logout + GET /auth/me

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-306** of the StoreOS Cloud build ("POST /auth/logout + GET /auth/me") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-305 complete: login, refresh, and token-family theft detection all work.
- No logout or "who am I" endpoint exists yet.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the logout CONTROLLER must check `RefreshTokenFormat.TryParseTenantId(cookieValue, out _)` itself: for a missing/garbage cookie it clears the cookie and returns 204 WITHOUT sending `LogoutCommand` (otherwise `TransactionBehavior` would answer 401 `tenant_required`).

> **REVISION NOTE (review v2):** Logout is idempotent: an unknown or already-revoked token still clears the cookie and returns 204. `LogoutCommand` is an `IAnonymousTenantRequest` (see P-305).

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

> **REVISION NOTE (review v2):** Test class names: `TransactionBehaviorTests` replaces the old `TenantTransactionMiddlewareTests`; `IdempotencyBehaviorTests` replaces `IdempotencyMiddlewareTests`.

Create `backend/src/StoreOS.Application/Identity/Logout/LogoutCommand.cs`:

```csharp
using MediatR;

namespace StoreOS.Application.Identity.Logout;

public record LogoutCommand(string RawRefreshToken) : IRequest, IAnonymousTenantRequest
{
    public Guid TenantId => RefreshTokenFormat.TryParseTenantId(RawRefreshToken, out var t) ? t : Guid.Empty;
}
```

`LogoutHandler.cs`: looks up the token by hash (IgnoreQueryFilters, same pre-auth-adjacent
reasoning as refresh — though technically the caller IS authenticated via access token at this
point for the controller action, the refresh-token table lookup itself is still keyed by hash
across the whole table, not tenant-scoped, since a revoked/expired token might belong to a
user whose tenant context we want to double check anyway; simplest correct approach: revoke
ONLY that specific token, not the whole family — logout is a deliberate, legitimate action, not
a theft signal):

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Identity;

using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Logout;

public class LogoutHandler : IRequestHandler<LogoutCommand>
{
    private readonly IAppDbContext _db;
    public LogoutHandler(IAppDbContext db) => _db = db;

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        var hash = RefreshTokenFormat.Hash(request.RawRefreshToken);
        var token = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        token?.Revoke(); // idempotent: if already revoked or not found, logout still "succeeds"
        await _db.SaveChangesAsync(ct);
    }
}
```

`AuthController.cs` additions:

```csharp
[HttpPost("logout")]
public async Task<IActionResult> Logout(CancellationToken ct)
{
    if (Request.Cookies.TryGetValue("storeos_refresh_token", out var rawToken) && !string.IsNullOrEmpty(rawToken))
        await _mediator.Send(new LogoutCommand(rawToken), ct);
    Response.Cookies.Delete("storeos_refresh_token", new CookieOptions { Path = "/api/auth" });
    return NoContent();
}

[HttpGet("me")]
public IActionResult Me([FromServices] IAccessScope scope)
{
    if (!scope.IsAuthenticated) return Unauthorized();
    return Ok(new
    {
        userId = scope.UserId, tenantId = scope.TenantId, allBranches = scope.AllBranches,
        branchIds = scope.BranchIds, permissions = scope.Permissions, tokenVersion = scope.TokenVersion
    });
}
```

Note `Logout` is intentionally NOT `[AllowAnonymous]` — it requires a valid (even if about to
be discarded) access token, consistent with typical logout UX (you must be "logged in" to log
out); `Me` likewise requires authentication (no explicit attribute needed since
`[AllowAnonymous]` is absent and a global `[Authorize]` fallback policy should be the default —
confirm `Program.cs`'s `AddAuthorization()` call sets
`options.FallbackPolicy = new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build();`
so EVERY controller action requires authentication BY DEFAULT unless explicitly marked
`[AllowAnonymous]` — add this now, since `Login`/`Refresh` are the only two actions so far
that need the anonymous exception, and this fallback-deny-by-default posture is a critical
security baseline for every endpoint added in every later prompt.

## 4. TASK

1. Create `LogoutCommand.cs`/`LogoutHandler.cs` in
   `StoreOS.Application/Identity/Logout/` exactly as shown.
2. Add `Logout` and `Me` actions to `AuthController.cs` exactly as shown.
3. Update `Program.cs`'s `AddAuthorization()` call to set a `FallbackPolicy` requiring
   authentication by default, exactly as described — this is a significant, load-bearing
   security default for the rest of the build.
4. Re-verify EVERY existing test that hits an endpoint WITHOUT explicit `[AllowAnonymous]`
   still passes (the fallback policy could newly reject requests that previously succeeded
   only because no authorization was enforced at all) — specifically re-run
   `TransactionBehaviorTests`, `IdempotencyBehaviorTests`, `PipelineBehaviorTests`,
   `ExceptionHandlingTests`, adjusting their test setup to include a valid bearer token via
   `TestAuthHandler` wherever they don't already.
5. Write `StoreOS.IntegrationTests/Identity/LogoutAndMeTests.cs`: (a) `GET /api/auth/me`
   without a token -> 401; (b) with a valid token -> 200 with correct
   userId/tenantId/branchIds/permissions matching the token's claims; (c) login, then
   `POST /api/auth/logout` with the refresh cookie -> 204, cookie is cleared in the response
   (`Set-Cookie` with an expired/deleted marker), and the refresh token's DB row is now
   `is_revoked=true`; (d) attempting `/auth/refresh` with the now-logged-out token -> 401.

## 5. HARD CONSTRAINTS

- The `FallbackPolicy` requiring authentication by default is a
  PERMANENT security baseline — every future controller action must either be reachable only
  by an authenticated caller, or explicitly opt out via `[AllowAnonymous]` with a clear
  justification comment. Do not weaken this policy in any later prompt.
- Logout revokes only ONE token (the presented one), not the whole family — this is
  intentionally different from the theft-detection path in P-305, which revokes the whole
  family. Do not conflate the two.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter LogoutAndMeTests
dotnet test backend/tests/StoreOS.IntegrationTests
# Expected: new tests pass; full existing suite still green after the FallbackPolicy change
# (fix any test that now needs an auth token it previously didn't supply).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-306 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-306 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-306` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `POST /api/auth/logout` and `GET /api/auth/me` routes/response shapes,
and the critical `FallbackPolicy` change (RequireAuthenticatedUser by default) that every
future controller-authoring prompt must be aware of.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-306: <one-line summary>"` only after verification is
   fully green.
