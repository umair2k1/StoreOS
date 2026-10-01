# P-307 — token_version stale-JWT invalidation check (30s cache)

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-307** of the StoreOS Cloud build ("token_version stale-JWT invalidation check (30s cache)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-306 complete: login/refresh/logout/me all function; default-deny
  authorization baseline is active.
- `users.token_version` is bumped on password change (not yet built), deactivation (P-104's
  `Deactivate()` already bumps it), and refresh-token-family theft detection (P-305). However,
  NOTHING currently checks a live access token's `token_version` claim against the CURRENT
  database value — a token issued before a bump remains valid (per JWT signature) for its full
  remaining 15-minute lifetime. This prompt closes that gap with a cached DB check.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Behaviors/TokenVersionBehavior.cs`.
This MUST run as a MediatR pipeline behavior so it applies uniformly to every command/query
without per-handler boilerplate. Use `Microsoft.Extensions.Caching.Memory` (built into ASP.NET
Core, no new package needed) for the 30-second cache:

```csharp
using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public class TokenVersionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAccessScope _scope;
    private readonly IAppDbContext _db;
    private readonly IMemoryCache _cache;
    private static readonly TimeSpan CacheTtl = TimeSpan.FromSeconds(30);

    public TokenVersionBehavior(IAccessScope scope, IAppDbContext db, IMemoryCache cache)
    {
        _scope = scope; _db = db; _cache = cache;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_scope.IsAuthenticated) return await next();

        var cacheKey = $"token_version:{_scope.UserId}";
        var currentVersion = await _cache.GetOrCreateAsync(cacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheTtl;
            var user = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
                .Where(u => u.Id == _scope.UserId)
                .Select(u => (int?)u.TokenVersion)
                .FirstOrDefaultAsync(cancellationToken);
            return user;
        });

        if (currentVersion is null || currentVersion.Value != _scope.TokenVersion)
            throw new DomainException("token_version_stale",
                "Your session is no longer valid. Please log in again.");

        return await next();
    }
}
```

Register in `Program.cs`'s `AddMediatR` config, placed AFTER `AuthorizationBehavior` (need to
know the caller IS a real user before bothering to check version) but BEFORE
`ValidationBehavior` (fail fast on staleness before spending effort on field validation):

```csharp
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.AuthorizationBehavior<,>));
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.TokenVersionBehavior<,>));
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.ValidationBehavior<,>));
```

Register `IMemoryCache`: `builder.Services.AddMemoryCache();` (idempotent if already added by
a framework default; confirm it's explicitly registered).

Map `token_version_stale` to 401 in `ExceptionHandlingMiddleware`.

## 4. TASK

1. Create `TokenVersionBehavior.cs` exactly as shown in
   `StoreOS.Application/Behaviors/`.
2. Register `IMemoryCache` and insert the new behavior into the MediatR pipeline in the exact
   order: Authorization -> TokenVersion -> Validation.
3. Add `token_version_stale` -> 401 to `ExceptionHandlingMiddleware`'s switch.
4. Write `StoreOS.IntegrationTests/Behaviors/TokenVersionBehaviorTests.cs`: (a) issue a login,
   immediately call an authenticated endpoint (e.g. `GET /api/auth/me` — note: `Me` is a plain
   controller action reading `IAccessScope` directly, NOT a MediatR request, so it bypasses
   this behavior entirely; use the P-114 throwaway MediatR test endpoint instead, or create a
   small real one, to exercise the pipeline) -> succeeds; (b) directly bump the user's
   `token_version` in the DB (simulating a role change or deactivation elsewhere), then
   IMMEDIATELY retry the same still-unexpired access token against the MediatR-routed endpoint
   -> since the 30-second cache hasn't expired yet, THIS SPECIFIC CALL clarifies the exact
   guarantee: **because the test bumps the DB directly (bypassing the cache-population path),
   the cache may still hold the stale value for up to 30 seconds** — document this window
   explicitly as the acceptable tradeoff Plan §8.3 describes ("Stale Permissions & 15-Minute
   JWT Window Mitigation" says "fast distributed cache check (or 30s in-memory cache)"), and
   write the test to either (i) wait out the 30-second window (slow but definitive), or
   (ii) directly clear/evict the specific cache key between the DB bump and the retry to prove
   the CHECK LOGIC itself is correct without waiting on the TTL, clearly commenting that this
   simulates "the cache would have expired" rather than testing the TTL duration itself; use
   approach (ii) for a fast, deterministic test, and add a SEPARATE, explicitly slow-marked
   test (or a documented manual verification note) for the full 30-second TTL behavior since
   automated tests should not sleep 30+ seconds by default.

## 5. HARD CONSTRAINTS

- This behavior does NOT apply to plain controller actions that read
  `IAccessScope` directly without going through `IMediator.Send` (like `GET /api/auth/me`) —
  it is a MediatR-pipeline-only mechanism. Any future controller action that reads
  `IAccessScope` directly without dispatching a command/query does not get this staleness
  check automatically; be aware of this gap and prefer routing all business logic through
  MediatR precisely so this check applies uniformly.
- The 30-second cache window is an ACCEPTED tradeoff, not a bug — Plan §8.3 explicitly sizes
  it this way. High-impact operations get a STRONGER, live-DB-only check with ZERO caching,
  built separately in P-308 — do not conflate the two mechanisms.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter TokenVersionBehaviorTests
# Expected: fast deterministic case (cache-evict simulation) passes; note any long-running
# TTL-duration test is either skipped by default or clearly marked as a slow/manual test.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-307 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-307 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-307` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `TokenVersionBehavior` pipeline position (Authorization -> TokenVersion ->
Validation), the 30-second cache TTL constant, and the documented gap that plain controller
actions bypass this check (only MediatR-routed requests are covered).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-307: <one-line summary>"` only after verification is
   fully green.
