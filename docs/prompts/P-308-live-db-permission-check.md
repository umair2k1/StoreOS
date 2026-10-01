# P-308 — High-impact operation live-DB permission re-check behavior

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-308** of the StoreOS Cloud build ("High-impact operation live-DB permission re-check behavior") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-307 complete: token_version staleness is checked (with 30s caching) for
  every MediatR-routed request.
- No mechanism yet exists for the STRONGER guarantee Plan §8.3/§13.3 requires for high-impact
  operations (`sales:void`, `sales:refund`, `inventory:adjustments`, `settings:manage`,
  `branches:manage`): those must bypass BOTH the JWT claim cache AND the 30-second
  token-version cache, hitting the database directly, every single time, with zero latency
  tolerance for stale permission data.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create the marker interface
`backend/src/StoreOS.Application/Abstractions/IHighImpactOperation.cs`:

```csharp
namespace StoreOS.Application.Abstractions;

public interface IHighImpactOperation
{
    string RequiredPermission { get; }
}
```

(Distinct from `IRequiresPermission` — a command can implement EITHER the normal
claims-based check OR this stronger live-DB check, never expected to implement both; document
this clearly.)

Create `backend/src/StoreOS.Application/Behaviors/LiveDbPermissionBehavior.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Behaviors;

public class LiveDbPermissionBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAccessScope _scope;
    private readonly IAppDbContext _db;

    public LiveDbPermissionBehavior(IAccessScope scope, IAppDbContext db)
    {
        _scope = scope; _db = db;
    }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is not IHighImpactOperation highImpact) return await next();

        if (!_scope.IsAuthenticated)
            throw new DomainException("unauthenticated", "Authentication is required.");

        // NO CACHE. Direct DB query, every time, inside the ambient transaction.
        var assignments = await _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
            .Where(a => a.UserId == _scope.UserId)
            .ToListAsync(cancellationToken);
        var roleIds = assignments.Select(a => a.RoleId).Distinct().ToList();
        var livePermissions = await _db.Set<RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId) && rp.PermissionCode == highImpact.RequiredPermission)
            .AnyAsync(cancellationToken);

        if (!livePermissions)
            throw new DomainException("forbidden",
                $"Missing required permission (live check): {highImpact.RequiredPermission}");

        // Also re-check the user is still active (covers deactivation mid-session even
        // faster than the token_version cache would).
        var stillActive = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
            .Where(u => u.Id == _scope.UserId).Select(u => u.IsActive).FirstOrDefaultAsync(cancellationToken);
        if (!stillActive)
            throw new DomainException("forbidden", "Account is no longer active.");

        return await next();
    }
}
```

Register in the pipeline as the FIRST behavior of all (even before the normal
`AuthorizationBehavior`), since a high-impact command should never waste time on the cached
claims check if it's going to do a live check anyway — actually the ORDER should still put
plain authentication existence checks first; place `LiveDbPermissionBehavior` immediately
after `AuthorizationBehavior` and before `TokenVersionBehavior` (no point checking a 30-second
cache for a request that's about to do a live, uncached check anyway — but it's harmless
either way; for clarity and minimal redundant DB calls, place it so it runs INSTEAD of relying
on the cached checks for `IHighImpactOperation` commands specifically):

```csharp
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.AuthorizationBehavior<,>));
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.LiveDbPermissionBehavior<,>));
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.TokenVersionBehavior<,>));
cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.ValidationBehavior<,>));
```

Note `AuthorizationBehavior` only acts on `IRequiresPermission`, and `LiveDbPermissionBehavior`
only acts on `IHighImpactOperation` — a command implementing NEITHER interface skips both
harmlessly; a command should implement AT MOST ONE of the two (document this as a design rule,
and note that commands like `VoidSaleCommand` (P-524), `RefundSaleCommand` (P-525),
`InventoryAdjustmentCommand` (P-413), and settings/branches management commands (P-1120/P-310
backends) implement `IHighImpactOperation`, NOT `IRequiresPermission`).

## 4. TASK

1. Create `IHighImpactOperation.cs` in `StoreOS.Application/Abstractions/` and
   `LiveDbPermissionBehavior.cs` in `StoreOS.Application/Behaviors/` exactly as shown.
2. Insert it into the MediatR pipeline registration in `Program.cs` at the exact position
   shown (Authorization -> LiveDbPermission -> TokenVersion -> Validation).
3. Write a throwaway test command in `StoreOS.IntegrationTests` implementing
   `IHighImpactOperation` (`RequiredPermission => "sales:void"`) with a no-op handler, exposed
   via a minimal test-only endpoint.
4. Write `StoreOS.IntegrationTests/Behaviors/LiveDbPermissionBehaviorTests.cs`: (a) user WITH
   `sales:void` permission (granted via a real `role_permissions` row) succeeds; (b) user
   WITHOUT it -> 403 `forbidden`; (c) **the critical case**: grant the permission, log in
   (JWT now carries `sales:void` in its `perm` claims), THEN directly delete the
   `role_permissions` row from the DB (revoking it) WITHOUT re-issuing a new JWT — retry the
   SAME still-valid, still-unexpired access token against the high-impact endpoint -> 403
   `forbidden` IMMEDIATELY (proving the live DB check catches what the stale JWT claims would
   have incorrectly allowed — this is the whole point of this prompt, contrast this with
   `TokenVersionBehaviorTests`' 30-second-cache case for NORMAL permissions); (d) deactivate
   the user directly in the DB mid-session (without touching `role_permissions`) -> the SAME
   still-valid JWT is rejected immediately via the `stillActive` check.

## 5. HARD CONSTRAINTS

- `LiveDbPermissionBehavior` must NEVER use `IMemoryCache` or any
  other caching layer — every single invocation must hit the database fresh. This is the
  entire point of the behavior; introducing caching here (even a 1-second TTL) defeats Plan
  §8.3's "zero latency on revocation" guarantee.
- A command must implement at most one of `IRequiresPermission` / `IHighImpactOperation` —
  document this rule in `docs/ARTIFACTS.md` so later prompts (P-413, P-524, P-525, etc.)
  correctly choose `IHighImpactOperation` for sensitive operations instead of the weaker
  claims-based interface.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter LiveDbPermissionBehaviorTests
# Expected: 4 passed, especially case (c) proving zero-lag revocation vs. the cached
# TokenVersionBehavior's accepted 30-second staleness window for ordinary permissions.
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-308 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-308 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-308` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IHighImpactOperation` interface signature, the full pipeline order
(Authorization -> LiveDbPermission -> TokenVersion -> Validation), and the explicit list of
which future commands must use `IHighImpactOperation` instead of `IRequiresPermission`
(sales:void, sales:refund, inventory:adjustments, settings:manage, branches:manage).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-308: <one-line summary>"` only after verification is
   fully green.
