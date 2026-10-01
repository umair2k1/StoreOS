# P-304 — Refresh token issuance + rotation on login

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-304** of the StoreOS Cloud build ("Refresh token issuance + rotation on login") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-303 complete: `POST /auth/login` issues a 15-minute access token.
- `RefreshToken` entity exists (P-105) with `IssueNew`, `RotateTo`, `Revoke` methods, but no
  service creates one during login, and no `HttpOnly` cookie is set.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** Refresh tokens are **tenant-prefixed** so the anonymous `/auth/refresh` and `/auth/logout` calls can establish the tenant for RLS BEFORE any query runs (the cookie is the only thing the browser sends). Create in Application:

```csharp
// backend/src/StoreOS.Application/Identity/RefreshTokenFormat.cs
using System.Security.Cryptography;
using System.Text;
namespace StoreOS.Application.Identity;

public static class RefreshTokenFormat
{
    // raw token = "<tenantId as 32 hex chars, no dashes>.<base64url(32 random bytes)>"
    public static string Compose(Guid tenantId, byte[] secret) =>
        $"{tenantId:N}.{Convert.ToBase64String(secret).TrimEnd('=').Replace('+', '-').Replace('/', '_')}";

    public static bool TryParseTenantId(string? raw, out Guid tenantId)
    {
        tenantId = Guid.Empty;
        if (string.IsNullOrWhiteSpace(raw)) return false;
        var dot = raw.IndexOf('.');
        return dot == 32 && Guid.TryParseExact(raw[..32], "N", out tenantId);
    }

    public static string Hash(string raw) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(raw)));
}
```
```csharp
// backend/src/StoreOS.Application/Abstractions/IRefreshTokenService.cs
using StoreOS.Domain.Identity;
namespace StoreOS.Application.Abstractions;

public interface IRefreshTokenService
{
    Task<(string rawToken, RefreshToken record)> IssueAsync(Guid tenantId, Guid userId, Guid familyId, CancellationToken ct);
}
```
The raw value is NEVER stored; only `RefreshTokenFormat.Hash(raw)` is. Tests: `TryParseTenantId` accepts a composed token and rejects garbage/empty/wrong-length input. Register `services.AddScoped<IRefreshTokenService, RefreshTokenService>()`. Wherever this prompt (or P-303/P-305/P-306) still shows `HashToken(...)` or `IssueAsync(userId, familyId, ct)`, use `RefreshTokenFormat.Hash(...)` and `IssueAsync(tenantId, userId, familyId, ct)`.

Create `backend/src/StoreOS.Infrastructure/Identity/RefreshTokenService.cs`:

```csharp
using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Identity;

public class RefreshTokenService : IRefreshTokenService
{
    private readonly StoreOsDbContext _db;
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);

    public RefreshTokenService(StoreOsDbContext db) => _db = db;

    public async Task<(string rawToken, RefreshToken record)> IssueAsync(Guid tenantId, Guid userId, Guid familyId, CancellationToken ct)
    {
        var rawToken = RefreshTokenFormat.Compose(tenantId, RandomNumberGenerator.GetBytes(32));
        var hash = RefreshTokenFormat.Hash(rawToken);
        var record = RefreshToken.IssueNew(userId, hash, familyId, DateTimeOffset.UtcNow.Add(Lifetime));
        await _db.Set<RefreshToken>().AddAsync(record, ct);
        return (rawToken, record);
    }

}
```

Note: a BRAND NEW login starts a BRAND NEW token family (`familyId = Guid.NewGuid()`);
ROTATION during `/auth/refresh` (P-305) reuses the SAME family. Update `LoginHandler` (P-303)
to also issue a refresh token and return the raw value so the controller can set it as a
cookie (the raw value itself is NEVER persisted, only its hash, per P-105's design):

```csharp
// Inside LoginHandler.Handle, after issuing the access token:
var familyId = Guid.NewGuid();
var (rawRefreshToken, _) = await _refreshTokenService.IssueAsync(user.TenantId, user.Id, familyId, ct);
await _db.SaveChangesAsync(ct);
return new LoginResult(token, expiresAt, user.Id, user.DisplayName, user.TenantId,
    allBranches, userBranchIds, permissions, rawRefreshToken);
```

Extend `LoginResult` with a `RawRefreshToken` field (inject `RefreshTokenService` into
`LoginHandler`'s constructor). In `AuthController.Login`, set the cookie instead of returning
the raw token in the JSON body:

```csharp
[HttpPost("login")]
[AllowAnonymous]
public async Task<IActionResult> Login(LoginCommand command, CancellationToken ct)
{
    var result = await _mediator.Send(command, ct);
    Response.Cookies.Append("storeos_refresh_token", result.RawRefreshToken, new CookieOptions
    {
        HttpOnly = true,
        Secure = true,
        SameSite = SameSiteMode.Strict,
        Path = "/api/auth",
        Expires = DateTimeOffset.UtcNow.AddDays(30)
    });
    return Ok(new
    {
        accessToken = result.AccessToken, expiresAt = result.ExpiresAt, userId = result.UserId,
        displayName = result.DisplayName, tenantId = result.TenantId, allBranches = result.AllBranches,
        branchIds = result.BranchIds, permissions = result.Permissions
        // Note: RawRefreshToken is deliberately NEVER included in the JSON body.
    });
}
```

## 4. TASK

1. Create `RefreshTokenService.cs` exactly as shown in `StoreOS.Infrastructure/Identity/`,
   register scoped (it depends on the scoped `StoreOsDbContext`).
2. Update `LoginResult` to add a `RawRefreshToken` field; update `LoginHandler` to inject
   `RefreshTokenService`, issue a new family + refresh token on every login, and return the raw
   token in the (internal, application-layer) result.
3. Update `AuthController.Login` to set the `storeos_refresh_token` `HttpOnly`/`Secure`/
   `SameSite=Strict` cookie scoped to path `/api/auth`, and OMIT the raw refresh token from the
   JSON response body entirely.
4. Write `StoreOS.IntegrationTests/Identity/RefreshTokenIssuanceTests.cs`: (a) after a
   successful login, the HTTP response has a `Set-Cookie` header for `storeos_refresh_token`
   with `HttpOnly`, `Secure`, and `SameSite=Strict` attributes present (parse the raw
   `Set-Cookie` header string to confirm these flags, since `HttpClient`'s cookie container
   may not expose all attributes directly); (b) the JSON response body does NOT contain any
   field matching the raw refresh token value or a field literally named containing
   "refresh"; (c) querying `refresh_tokens` table directly after login shows exactly one new
   row for that user with `is_revoked = false` and a `token_hash` that does NOT equal the raw
   cookie value (proving only the hash is stored).

## 5. HARD CONSTRAINTS

- `Secure = true` on the cookie means it will only be sent over
  HTTPS — note in a code comment that local HTTP-only dev testing (e.g. via
  `WebApplicationFactory`'s in-memory `TestServer`, which uses HTTPS-agnostic requests) may
  need `Secure = false` conditionally in `Development` environment; if your `TestServer`-based
  integration test cannot observe the cookie due to this, add an
  `if (env.IsDevelopment()) options.Secure = false;` branch and note this explicitly rather
  than silently weakening production behavior.
- The raw refresh token must NEVER appear in any JSON response body, log line, or audit_log
  entry — only ever transmitted via the `HttpOnly` cookie.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RefreshTokenIssuanceTests
# Expected: 3 passed (cookie flags correct, no raw token in body, DB stores only the hash).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-304 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-304 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-304` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `RefreshTokenService.IssueAsync` signature, the cookie name
(`storeos_refresh_token`), its path (`/api/auth`), and its exact attribute set (HttpOnly,
Secure, SameSite=Strict, 30-day expiry).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-304: <one-line summary>"` only after verification is
   fully green.
