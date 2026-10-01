# P-312 — Rate limiting (login 5/min/IP, global 200/min/user)

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-312** of the StoreOS Cloud build ("Rate limiting (login 5/min/IP, global 200/min/user)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-311 complete: full identity/RBAC admin surface exists.
- No rate limiting exists yet. Uses ASP.NET Core's built-in `Microsoft.AspNetCore.RateLimiting`
  middleware (no extra NuGet package needed — it's part of the shared framework since .NET 7).

## 3. CONTEXT BLOCK

In `Program.cs`, configure two named rate limiter policies:

```csharp
using System.Threading.RateLimiting;

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    options.AddPolicy("login", context =>
        RateLimiterPartition.GetFixedWindowLimiter(
            partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.AddPolicy("global-per-user", context =>
        RateLimiterPartition.GetFixedWindowLimiter(
            partitionKey: context.User?.FindFirst("sub")?.Value
                ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
            factory: _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 200,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
            }));

    options.OnRejected = async (context, ct) =>
    {
        context.HttpContext.Response.ContentType = "application/problem+json";
        await context.HttpContext.Response.WriteAsJsonAsync(new
        {
            status = 429, title = "Too many requests. Please slow down.", code = "rate_limited"
        }, ct);
    };
});
```

Apply `app.UseRateLimiter();` early in the pipeline (after `CorrelationIdMiddleware` and
`ExceptionHandlingMiddleware`, before `UseAuthentication()`, since the login policy must apply
even to failed/anonymous attempts). Apply the `"login"` policy specifically to the login
endpoint via `[EnableRateLimiting("login")]` on `AuthController.Login`, and apply
`"global-per-user"` GLOBALLY via `options.GlobalLimiter = ...` OR by wrapping
`app.MapControllers()` in `.RequireRateLimiting("global-per-user")` — use the global-limiter
approach for simplicity:

```csharp
// Instead of a named "global-per-user" policy applied per-endpoint, set it as the actual
// global limiter so it applies to every request automatically:
options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
    RateLimiterPartition.GetFixedWindowLimiter(
        partitionKey: context.User?.FindFirst("sub")?.Value ?? context.Connection.RemoteIpAddress?.ToString() ?? "anonymous",
        factory: _ => new FixedWindowRateLimiterOptions { PermitLimit = 200, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
```
Keep the separate named `"login"` policy layered ADDITIONALLY on top via
`[EnableRateLimiting("login")]` specifically on the login action (ASP.NET Core supports both a
global limiter AND named per-endpoint policies applying simultaneously — the stricter of the
two effectively governs login attempts).

## 4. TASK

1. Configure `AddRateLimiter` in `Program.cs` exactly as shown (global 200/min/user
   limiter plus a named `"login"` 5/min/IP policy).
2. Apply `[EnableRateLimiting("login")]` to `AuthController.Login`.
3. Add `app.UseRateLimiter();` at the correct pipeline position (after correlation/exception
   handling middleware, before authentication).
4. Write `StoreOS.IntegrationTests/RateLimiting/RateLimitingTests.cs`: (a) hit
   `POST /api/auth/login` with bad credentials 5 times rapidly from the "same IP" (the
   `WebApplicationFactory`'s `TestServer` typically reports a consistent loopback address,
   which is sufficient to exercise the partition key) -> all 5 return 401 as normal; the 6th
   within the same window -> 429 with `code: "rate_limited"`; (b) confirm the global
   200/min/user limiter triggers if you simulate 201 rapid authenticated requests as the same
   user to any endpoint (use a fast no-op endpoint to make this practical) -> the 201st
   request -> 429.

## 5. HARD CONSTRAINTS

- The login-specific limiter is keyed by IP (not user, since an
  attacker guessing passwords isn't authenticated yet) — the global limiter is keyed by user id
  when authenticated, falling back to IP for anonymous traffic. Do not swap these partition
  keys.
- `QueueLimit = 0` means excess requests are REJECTED immediately (429), never queued/delayed
  — this is the correct behavior for both policies; do not introduce queuing.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter RateLimitingTests
# Expected: 6th rapid login attempt -> 429; 201st rapid authenticated request -> 429.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-312 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-312 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-312` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the two rate limiter policy names/limits (`login`: 5/min/IP,
global-per-user: 200/min/user), and the `rate_limited` response code shape.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-312: <one-line summary>"` only after verification is
   fully green.
