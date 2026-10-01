# P-305 — POST /auth/refresh with family reuse detection

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-305** of the StoreOS Cloud build ("POST /auth/refresh with family reuse detection") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-304 complete: login issues an access token + sets an HttpOnly refresh
  cookie, storing only the token's hash.
- No `/auth/refresh` endpoint exists yet. This is one of the most security-critical prompts —
  implements Plan §8.3's token family theft/reuse detection.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `refresh_tokens` is RLS-protected through `users`, so the refresh lookup needs a tenant scope. `RefreshCommand` implements `IAnonymousTenantRequest` (tenant parsed from the token prefix); if the prefix is missing/invalid `TransactionBehavior` rejects the request with `401 invalid_refresh_token` before any DB access. A token whose prefix names tenant A but whose hash exists only under tenant B is simply not found (RLS) — add that as a test. Theft detection (family revocation) must COMMIT even though the request ends in 401: implement the revocation, `token_version` bump and audit write, then return a `RefreshResult` flagged `ReuseDetected=true` (controller maps it to 401 AFTER the transaction commits) — throwing an exception would roll the revocation back.

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Identity/Refresh/RefreshCommand.cs`:

```csharp
using MediatR;

namespace StoreOS.Application.Identity.Refresh;

public record RefreshCommand(string RawRefreshToken) : IRequest<RefreshResult>, IAnonymousTenantRequest
{
    // Tenant is encoded in the token prefix (P-304 RefreshTokenFormat) so RLS can be scoped before the lookup.
    public Guid TenantId => RefreshTokenFormat.TryParseTenantId(RawRefreshToken, out var t) ? t : Guid.Empty;
}

public record RefreshResult(string AccessToken, DateTimeOffset ExpiresAt, string NewRawRefreshToken,
    Guid UserId, string DisplayName, Guid TenantId, bool AllBranches,
    IReadOnlyList<Guid> BranchIds, IReadOnlyList<string> Permissions);
```

Create `backend/src/StoreOS.Application/Identity/Refresh/RefreshHandler.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Identity.Refresh;

public class RefreshHandler : IRequestHandler<RefreshCommand, RefreshResult>
{
    private readonly IAppDbContext _db;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IJwtTokenService _jwt;

    public RefreshHandler(IAppDbContext db, IRefreshTokenService refreshTokenService, IJwtTokenService jwt)
    {
        _db = db; _refreshTokenService = refreshTokenService; _jwt = jwt;
    }

    public async Task<RefreshResult> Handle(RefreshCommand request, CancellationToken ct)
    {
        var hash = RefreshTokenFormat.Hash(request.RawRefreshToken);
        var token = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null)
            throw new DomainException("invalid_refresh_token", "Refresh token is invalid.");

        if (token.IsRevoked)
        {
            // THEFT / REPLAY DETECTED: this token was already rotated once before. Revoke
            // the ENTIRE family and bump the user's token_version so all active access
            // tokens are invalidated on next stale-check (P-307).
            var family = await _db.Set<RefreshToken>().IgnoreQueryFilters()
                .Where(t => t.FamilyId == token.FamilyId).ToListAsync(ct);
            foreach (var t in family) t.Revoke();
            var affectedUser = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
                .FirstAsync(u => u.Id == token.UserId, ct);
            affectedUser.BumpTokenVersion();
            await _db.SaveChangesAsync(ct);
            throw new DomainException("refresh_token_reuse_detected",
                "This refresh token has already been used. All sessions have been revoked for security. Please log in again.");
        }

        if (token.IsExpired(DateTimeOffset.UtcNow))
            throw new DomainException("refresh_token_expired", "Refresh token has expired. Please log in again.");

        var user = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters().FirstAsync(u => u.Id == token.UserId, ct);
        if (!user.IsActive)
            throw new DomainException("invalid_refresh_token", "Refresh token is invalid.");

        var assignments = await _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
            .Where(a => a.UserId == user.Id).ToListAsync(ct);
        var allBranches = assignments.Any(a => a.BranchId == null);
        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var permissions = await _db.Set<RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId)).Select(rp => rp.PermissionCode).Distinct().ToListAsync(ct);
        var userBranchIds = await _db.Set<UserBranch>().IgnoreQueryFilters()
            .Where(ub => ub.UserId == user.Id).Select(ub => ub.BranchId).ToListAsync(ct);

        // Rotate: issue a new refresh token in the SAME family, mark the presented one as
        // rotated-into it.
        var (newRawToken, newRecord) = await _refreshTokenService.IssueAsync(user.TenantId, user.Id, token.FamilyId, ct);
        await _db.SaveChangesAsync(ct); // ensure newRecord has its Id assigned before linking
        token.RotateTo(newRecord.Id);
        await _db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = _jwt.IssueAccessToken(
            user.Id, user.TenantId, allBranches, userBranchIds, permissions, user.TokenVersion);

        return new RefreshResult(accessToken, expiresAt, newRawToken, user.Id, user.DisplayName,
            user.TenantId, allBranches, userBranchIds, permissions);
    }
}
```

Controller action (`AuthController.cs`, extend):

```csharp
[HttpPost("refresh")]
[AllowAnonymous]
public async Task<IActionResult> Refresh(CancellationToken ct)
{
    if (!Request.Cookies.TryGetValue("storeos_refresh_token", out var rawToken) || string.IsNullOrEmpty(rawToken))
        return Unauthorized(new { code = "missing_refresh_token", message = "No refresh token cookie present." });

    var result = await _mediator.Send(new RefreshCommand(rawToken), ct);
    Response.Cookies.Append("storeos_refresh_token", result.NewRawRefreshToken, new CookieOptions
    {
        HttpOnly = true, Secure = true, SameSite = SameSiteMode.Strict,
        Path = "/api/auth", Expires = DateTimeOffset.UtcNow.AddDays(30)
    });
    return Ok(new
    {
        accessToken = result.AccessToken, expiresAt = result.ExpiresAt, userId = result.UserId,
        displayName = result.DisplayName, tenantId = result.TenantId, allBranches = result.AllBranches,
        branchIds = result.BranchIds, permissions = result.Permissions
    });
}
```

Map `invalid_refresh_token`, `refresh_token_expired`, `refresh_token_reuse_detected` all to
401 in `ExceptionHandlingMiddleware`'s switch.

## 4. TASK

1. Create `RefreshCommand.cs`, `RefreshHandler.cs` exactly as shown in
   `StoreOS.Application/Identity/Refresh/`.
2. Add the `POST /auth/refresh` action to `AuthController.cs` exactly as shown.
3. Update `ExceptionHandlingMiddleware`'s status mapping to add `invalid_refresh_token`,
   `refresh_token_expired`, `refresh_token_reuse_detected` all -> 401.
4. Write `StoreOS.IntegrationTests/Identity/RefreshTokenReuseTests.cs`: (a) login, then call
   `/auth/refresh` with the cookie -> 200, new access token, new rotated cookie value
   (different from the login one), and the ORIGINAL token's DB row now has `is_revoked=true`
   and `replaced_by_token_id` set; (b) **the critical case**: call `/auth/refresh` AGAIN using
   the ORIGINAL (now-rotated, already-used) raw token value (simulating a stolen/replayed
   token) -> 401 `refresh_token_reuse_detected`, AND assert every token in that family
   (including the SECOND, legitimately-rotated one from step (a)) is now `is_revoked=true` in
   the DB, AND the user's `token_version` has been incremented; (c) confirm that after the
   reuse event, even the LEGITIMATE second token from step (a) can no longer be used to refresh
   (since its family was revoked) -> 401 for a third `/auth/refresh` call using that token.
5. Write a test for plain expiry: manually set a refresh token's `expires_at` to the past via
   direct DB manipulation in the test, then attempt refresh -> 401 `refresh_token_expired`.

## 5. HARD CONSTRAINTS

- Family-wide revocation on reuse detection is NON-NEGOTIABLE — a
  single replayed token must revoke every token in its family, not just itself, per Plan
  §8.3's exact wording. Verify this with a test that checks ALL family members, not just the
  one that was replayed.
- `token_version` must be bumped on reuse detection specifically (not on every normal
  rotation) — normal rotation does NOT bump `token_version` (that would force the legitimate
  user to re-authenticate on every refresh, which is wrong); ONLY the theft-detection branch
  bumps it.
- The two `SaveChangesAsync` calls in `RefreshHandler` (one to get `newRecord.Id` assigned,
  one after `RotateTo`) are intentional — `RotateTo` needs the new token's real database ID to
  link `replaced_by_token_id` correctly, and that ID isn't available until the insert is
  flushed. Do not try to collapse these into a single call.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RefreshTokenReuseTests
# Expected: normal rotation works; replay revokes entire family + bumps token_version;
# subsequent use of the legitimate-but-now-revoked second token also fails; expiry handled.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-305 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-305 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-305` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RefreshCommand`/`RefreshResult` shapes, the `POST /api/auth/refresh`
route, and the exact family-revocation + token_version-bump behavior on replay detection
(critical for P-307's stale-JWT mitigation to work correctly).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-305: <one-line summary>"` only after verification is
   fully green.
