# P-309 — X-Branch-Id middleware + branch access enforcement

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-309** of the StoreOS Cloud build ("X-Branch-Id middleware + branch access enforcement") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-308 complete: full permission-checking pipeline (claims-cached,
  token-version-cached, and live-DB) is in place.
- No endpoint currently reads or validates an `X-Branch-Id` header. Per Plan §9.3, every
  MUTATION must carry an explicit branch context; there is no such thing as a branchless
  write, even for an Owner with `AllBranches=true`.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** There is no HTTP-level transaction middleware any more. `BranchContextMiddleware` only has to run after authentication + AccessScope resolution and before controllers; `TransactionBehavior` (MediatR) reads the resolved scope when it opens the transaction.

Create `backend/src/StoreOS.Application/Abstractions/IBranchContext.cs`:

```csharp
namespace StoreOS.Application.Abstractions;

public interface IBranchContext
{
    Guid? CurrentBranchId { get; }
    bool HasBranchContext { get; }
}
```

Create `backend/src/StoreOS.Api/Middleware/BranchContextMiddleware.cs`. Behavior: reads
`X-Branch-Id` header; if absent AND the caller has exactly one accessible branch (their default
via `user_branches.is_default` or their only assignment), default to it server-side; if absent
and the caller has multiple/no clear default, and the request is a MUTATION (POST/PUT/PATCH/
DELETE), reject with 400; if present, validate via `IAccessScope.HasBranchAccess(...)` and
reject with 403 if the caller lacks access to that specific branch:

```csharp
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Api.Middleware;

public class BranchContextMiddleware
{
    public const string HeaderName = "X-Branch-Id";
    private readonly RequestDelegate _next;
    public BranchContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, IAccessScope scope,
        MutableBranchContext branchContext, StoreOsDbContext db)
    {
        if (!scope.IsAuthenticated) { await _next(context); return; }

        var isMutation = HttpMethods.IsPost(context.Request.Method) || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method) || HttpMethods.IsDelete(context.Request.Method);

        Guid? branchId = null;
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerVal) && Guid.TryParse(headerVal, out var parsed))
        {
            branchId = parsed;
        }
        else
        {
            var defaultBranch = await db.Set<UserBranch>().IgnoreQueryFilters()
                .Where(ub => ub.UserId == scope.UserId)
                .OrderByDescending(ub => ub.IsDefault)
                .Select(ub => (Guid?)ub.BranchId)
                .FirstOrDefaultAsync();
            branchId = defaultBranch;
        }

        if (branchId is null && isMutation)
            throw new DomainException("branch_context_required",
                "This operation requires an X-Branch-Id header (no default branch could be determined).");

        if (branchId is not null && !scope.HasBranchAccess(branchId.Value))
            throw new DomainException("forbidden", "You do not have access to the specified branch.");

        branchContext.CurrentBranchId = branchId;
        await _next(context);
    }
}
```

Create the mutable, scoped implementation
`backend/src/StoreOS.Infrastructure/Security/MutableBranchContext.cs`:

```csharp
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Security;

public class MutableBranchContext : IBranchContext
{
    public Guid? CurrentBranchId { get; set; }
    public bool HasBranchContext => CurrentBranchId.HasValue;
}
```

Register `builder.Services.AddScoped<MutableBranchContext>();
builder.Services.AddScoped<IBranchContext>(sp => sp.GetRequiredService<MutableBranchContext>());`
(both the concrete mutable type AND the read-only interface resolve to the SAME scoped
instance, so the middleware can write to it via the concrete type while handlers read it via
the interface). Place `app.UseMiddleware<BranchContextMiddleware>();` AFTER
`UseAuthorization()` and BEFORE `TransactionBehavior` (branch resolution needs
`IAccessScope` populated, and `TransactionBehavior`'s `SET LOCAL app.branch_ids` is
about the caller's FULL accessible set, not the per-request selected branch — these are
different concepts: `app.branch_ids` (RLS) = "which branches can this user ever see",
`IBranchContext.CurrentBranchId` (this prompt) = "which ONE branch is THIS request operating
against". Keep them conceptually distinct in your implementation and any documentation.

## 4. TASK

1. Create `IBranchContext.cs` in `StoreOS.Application/Abstractions/` and
   `MutableBranchContext.cs` in `StoreOS.Infrastructure/Security/` exactly as shown.
2. Create `BranchContextMiddleware.cs` exactly as shown; register both DI mappings and the
   middleware itself at the correct pipeline position (after UseAuthorization, before
   TransactionBehavior).
3. Write `StoreOS.IntegrationTests/Security/BranchContextMiddlewareTests.cs`: (a) a GET
   request (non-mutation) with no `X-Branch-Id` header and no default branch succeeds with
   `CurrentBranchId == null` (reads don't require branch context); (b) a POST request with no
   header and no default branch -> 400 `branch_context_required`; (c) a POST with a valid
   `X-Branch-Id` the user HAS access to -> succeeds, `CurrentBranchId` set correctly; (d) a
   POST with an `X-Branch-Id` the user does NOT have access to -> 403 `forbidden`; (e) a user
   with exactly one `user_branches` row and no header -> that branch is auto-selected as
   default, even for a mutation.

## 5. HARD CONSTRAINTS

- "There is no branchless sale: even an Owner must operate in a
  branch context" (Plan §9.3) — do not add any exception path that allows a mutation to
  proceed with `CurrentBranchId == null`, regardless of the caller's role or `AllBranches`
  status.
- `IBranchContext` (per-request selected branch) and `IAccessScope.BranchIds`/`AllBranches`
  (the caller's full accessible set, used for RLS) are DIFFERENT concepts serving different
  purposes — do not merge them into one abstraction; every later prompt that stamps a new
  record's `branch_id` column uses `IBranchContext.CurrentBranchId`, while RLS policies
  continue to use the broader `app.branch_ids`/`app.all_branches` session variables.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter BranchContextMiddlewareTests
# Expected: 5 passed (no-branch-read-ok, no-branch-mutation-400, valid-branch-ok,
# invalid-branch-403, auto-default-single-branch).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-309 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-309 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-309` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IBranchContext` interface, the `X-Branch-Id` header name constant, the
middleware's pipeline position, and the conceptual distinction between `IBranchContext`
(single selected branch per request) vs. `IAccessScope.BranchIds`/`AllBranches` (full
accessible set for RLS).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-309: <one-line summary>"` only after verification is
   fully green.
