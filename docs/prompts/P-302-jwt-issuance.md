# P-302 — JWT issuance service (access token, 15 min, claims)

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-302** of the StoreOS Cloud build ("JWT issuance service (access token, 15 min, claims)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-301 complete: `IPasswordHasher`/`Argon2PasswordHasher` work.
- P-106's `HttpAccessScope` reads claims `sub`, `tenant_id`, `all_branches`, `branch_id`
  (repeated), `perm` (repeated), `token_version` from `ClaimsPrincipal`. This prompt builds
  the ISSUANCE side that must produce EXACTLY these claim types.
- No real `AddJwtBearer` authentication scheme is wired yet — this prompt wires it for real,
  replacing the `TestAuthHandler`-only setup from P-106 (the test handler remains usable ONLY
  within `StoreOS.IntegrationTests`' `WebApplicationFactory` customization, never in the real
  pipeline).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** First create the Application-layer contract `backend/src/StoreOS.Application/Abstractions/IJwtTokenService.cs` (the login/refresh handlers live in Application and cannot see the Infrastructure class):

```csharp
namespace StoreOS.Application.Abstractions;

public interface IJwtTokenService
{
    (string token, DateTimeOffset expiresAt) IssueAccessToken(
        Guid userId, Guid tenantId, bool allBranches, IEnumerable<Guid> branchIds,
        IEnumerable<string> permissions, int tokenVersion);
}
```
`JwtTokenService` (Infrastructure, below) implements it. Register `services.AddScoped<IJwtTokenService, JwtTokenService>()`.

> **REVISION NOTE (review v2):** Pipeline order for HTTP middleware is: CorrelationId -> ExceptionHandling -> Authentication -> AccessScope -> BranchContext -> Authorization -> controllers. There is no transaction middleware; see P-109.

Add a signing key setting. In `appsettings.json` (dev only — production
value comes from the cloud secret manager per Plan §14.4, out of scope for this local build):

```json
"Jwt": {
  "SigningKey": "dev-only-signing-key-change-in-production-min-32-bytes-long!!",
  "Issuer": "storeos-cloud",
  "Audience": "storeos-cloud-clients",
  "AccessTokenMinutes": 15
}
```

Create `backend/src/StoreOS.Infrastructure/Identity/JwtTokenService.cs`:

```csharp
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using StoreOS.Domain.Identity;

namespace StoreOS.Infrastructure.Identity;

public class JwtTokenService : IJwtTokenService
{
    private readonly IConfiguration _config;
    public JwtTokenService(IConfiguration config) => _config = config;

    public (string token, DateTimeOffset expiresAt) IssueAccessToken(
        Guid userId, Guid tenantId, bool allBranches, IEnumerable<Guid> branchIds,
        IEnumerable<string> permissions, int tokenVersion)
    {
        var signingKey = _config["Jwt:SigningKey"]!;
        var issuer = _config["Jwt:Issuer"]!;
        var audience = _config["Jwt:Audience"]!;
        var minutes = int.Parse(_config["Jwt:AccessTokenMinutes"] ?? "15");

        var claims = new List<Claim>
        {
            new("sub", userId.ToString()),
            new("tenant_id", tenantId.ToString()),
            new("all_branches", allBranches ? "true" : "false"),
            new("token_version", tokenVersion.ToString())
        };
        if (!allBranches)
            claims.AddRange(branchIds.Select(id => new Claim("branch_id", id.ToString())));
        claims.AddRange(permissions.Select(p => new Claim("perm", p)));

        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(signingKey));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
        var expires = DateTimeOffset.UtcNow.AddMinutes(minutes);

        var token = new JwtSecurityToken(issuer, audience, claims,
            expires: expires.UtcDateTime, signingCredentials: creds);
        return (new JwtSecurityTokenHandler().WriteToken(token), expires);
    }
}
```

Wire real JWT bearer authentication in `Program.cs`:

```csharp
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidateAudience = true,
            ValidAudience = builder.Configuration["Jwt:Audience"],
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(
                Encoding.UTF8.GetBytes(builder.Configuration["Jwt:SigningKey"]!)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            NameClaimType = "sub"
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<JwtTokenService>();
```

Confirm `app.UseAuthentication(); app.UseAuthorization();` are positioned correctly: AFTER
`UseRouting()` (implicit with minimal APIs/controllers) and BEFORE
`CorrelationIdMiddleware`... no — re-check P-112's placement: `CorrelationIdMiddleware` must
be FIRST (before auth, so even a 401 gets a correlation id), then
`ExceptionHandlingMiddleware` (P-113) second, THEN `UseAuthentication()`/`UseAuthorization()`,
THEN the AccessScope/BranchContext middleware (P-106/P-309), then `app.MapControllers()`. Transactions and
idempotency are MediatR behaviors (P-109, P-117), not HTTP middleware. Reorder `Program.cs`
to this exact sequence if it currently differs.

## 4. TASK

1. Add the `Jwt` configuration section to `appsettings.json` exactly as shown (dev
   values only).
2. Create `JwtTokenService.cs` exactly as shown in `StoreOS.Infrastructure/Identity/`.
3. Wire real `AddAuthentication().AddJwtBearer(...)` and `AddAuthorization()` in `Program.cs`
   exactly as shown, register `JwtTokenService` as singleton.
4. Reorder the full middleware pipeline in `Program.cs` to the exact sequence stated in the
   CONTEXT BLOCK: CorrelationId -> ExceptionHandling -> UseAuthentication -> UseAuthorization
   -> TenantTransaction -> Idempotency -> MapControllers. Update `docs/ARTIFACTS.md`'s
   pipeline-order record accordingly (supersedes any earlier partial ordering notes).
5. Write `StoreOS.Domain.Tests`... actually this needs real crypto + config, so write
   `StoreOS.IntegrationTests/Identity/JwtTokenServiceTests.cs`: issue a token for a known
   user/tenant/branches/permissions/tokenVersion combination, decode it with
   `JwtSecurityTokenHandler` directly (bypassing ASP.NET auth), and assert every claim
   (`sub`, `tenant_id`, `all_branches`, repeated `branch_id`, repeated `perm`,
   `token_version`) matches exactly what was passed in. Also assert the token's `exp` claim is
   approximately 15 minutes from issuance (within a few seconds tolerance).
6. Write a SEPARATE end-to-end test using `WebApplicationFactory` with REAL JWT bearer auth
   (not the P-106 `TestAuthHandler`) confirming a request bearing a validly-signed token
   populates `IAccessScope` correctly via the real `HttpAccessScope` (P-106) reading real
   validated claims — this proves issuance and consumption are compatible end-to-end.

## 5. HARD CONSTRAINTS

- The claim type names issued here (`sub`, `tenant_id`,
  `all_branches`, `branch_id`, `perm`, `token_version`) MUST exactly match what
  `HttpAccessScope` (P-106) reads — any mismatch silently breaks authorization everywhere.
  Cross-check against `docs/ARTIFACTS.md`'s P-106 record before finalizing.
- The dev signing key in `appsettings.json` is explicitly a LOCAL-ONLY placeholder — note in
  a code comment and in `docs/BUILD_LOG.md` that production deployment (out of scope for this
  prompt sequence per the Plan's IaC notes) must source this from a cloud secret manager, never
  commit a real production key to source control.
- `ClockSkew` is set to 30 seconds (not the ASP.NET default of 5 minutes) to keep the 15-minute
  access token window tight and predictable for the stale-permission mitigation logic built in
  P-307.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter JwtTokenServiceTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter QueryFilterTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter TransactionBehaviorTests
# Expected: JWT tests pass; existing tests using TestAuthHandler still pass (confirm the
# TestAuthHandler-based WebApplicationFactory customization still overrides the real scheme
# correctly for those specific test classes without interference).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-302 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-302 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-302` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `JwtTokenService.IssueAccessToken` signature, the exact `Jwt:*`
configuration keys, the confirmed final middleware pipeline order (CorrelationId ->
ExceptionHandling -> Authentication -> Authorization -> TenantTransaction -> Idempotency ->
MapControllers), and confirmation that claim names match P-106 exactly.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-302: <one-line summary>"` only after verification is
   fully green.
