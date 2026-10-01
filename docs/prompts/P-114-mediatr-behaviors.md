# P-114 — FluentValidation + MediatR pipeline behaviors (validation, authorization)

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-114** of the StoreOS Cloud build ("FluentValidation + MediatR pipeline behaviors (validation, authorization)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-113 complete: exceptions from anywhere in the pipeline map to correct
  HTTP status codes with correlation IDs.
- No MediatR pipeline exists yet. This prompt wires MediatR itself plus the two foundational
  pipeline behaviors: validation (runs FluentValidation validators) and authorization
  (checks required permission attributes against `IAccessScope`).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** **Pipeline order and the transaction (DECISIONS D-04).** The MediatR behavior order is FIXED, outermost first: `ValidationBehavior` -> `SerializationRetryBehavior` (P-511) -> `TransactionBehavior` (P-109) -> `TokenVersionBehavior` (P-307) -> `AuthorizationBehavior` (this prompt; the live-DB permission check of P-308 needs the open transaction) -> `IdempotencyBehavior` (P-117) -> handler. This REPLACES the sentences below that say 'authorization runs before validation': validation goes first because it is pure (validators must never touch the database) and rejecting garbage before opening a transaction is cheap. At this prompt only Validation and Authorization exist; register them in the order above and add the others as their prompts land. Also add `backend/tests/StoreOS.IntegrationTests/ArchitectureTests.cs` (NetArchTest is NOT in the allowlist: use plain reflection): (a) no type in `StoreOS.Application` references an assembly named `StoreOS.Infrastructure`; (b) every concrete `IRequest<>` type whose name ends in `Query` implements `IReadOnlyRequest` or `IReportingRequest`; (c) no type ending in `Command` implements `IReadOnlyRequest`. Create the marker interfaces file `StoreOS.Application/Abstractions/IRequestMarkers.cs` if P-109 has not (it is created there; P-114 only consumes it — if it does not exist yet, create it exactly as in P-109). Also every `AuthorizationBehavior`/`DomainException` status mapping now lives in `ErrorCatalog` (P-113).

Register MediatR in `Program.cs`:

```csharp
builder.Services.AddMediatR(cfg =>
    cfg.RegisterServicesFromAssembly(typeof(StoreOS.Application.AssemblyMarker).Assembly));
builder.Services.AddValidatorsFromAssembly(typeof(StoreOS.Application.AssemblyMarker).Assembly);
```

Create `backend/src/StoreOS.Application/AssemblyMarker.cs` (empty marker class used purely for
assembly scanning):

```csharp
namespace StoreOS.Application;
public sealed class AssemblyMarker { private AssemblyMarker() { } }
```

Create `backend/src/StoreOS.Application/Behaviors/ValidationBehavior.cs`:

```csharp
using FluentValidation;
using MediatR;

namespace StoreOS.Application.Behaviors;

public class ValidationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEnumerable<IValidator<TRequest>> _validators;
    public ValidationBehavior(IEnumerable<IValidator<TRequest>> validators) => _validators = validators;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (!_validators.Any()) return await next();
        var context = new ValidationContext<TRequest>(request);
        var failures = (await Task.WhenAll(_validators.Select(v => v.ValidateAsync(context, cancellationToken))))
            .SelectMany(r => r.Errors)
            .Where(f => f != null)
            .ToList();
        if (failures.Count != 0) throw new ValidationException(failures);
        return await next();
    }
}
```

Create the authorization attribute + behavior. First, a marker interface commands/queries
implement to declare their required permission:
`backend/src/StoreOS.Application/Abstractions/IRequiresPermission.cs`:

```csharp
namespace StoreOS.Application.Abstractions;

public interface IRequiresPermission
{
    string RequiredPermission { get; }
}
```

`backend/src/StoreOS.Application/Behaviors/AuthorizationBehavior.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public class AuthorizationBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IAccessScope _scope;
    public AuthorizationBehavior(IAccessScope scope) => _scope = scope;

    public Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        if (request is IRequiresPermission permissioned)
        {
            if (!_scope.IsAuthenticated)
                throw new DomainException("unauthenticated", "Authentication is required.");
            if (!_scope.HasPermission(permissioned.RequiredPermission))
                throw new DomainException("forbidden", $"Missing required permission: {permissioned.RequiredPermission}");
        }
        return next();
    }
}
```

Register both behaviors, IN ORDER (validation before authorization — reject malformed
requests before spending effort on permission checks... actually Plan/security best practice
is AUTHORIZATION FIRST so an unauthorized user cannot use validation error messages to probe
data shape; reverse the order: authorization runs first, then validation):

```csharp
builder.Services.AddMediatR(cfg =>
{
    cfg.RegisterServicesFromAssembly(typeof(StoreOS.Application.AssemblyMarker).Assembly);
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.AuthorizationBehavior<,>));
    cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.ValidationBehavior<,>));
});
```

Note: `DomainException("forbidden", ...)` currently maps to 422 per P-113's
`ExceptionHandlingMiddleware` — for `unauthenticated`/`forbidden` specifically, override the
mapping so `unauthenticated` -> 401 and `forbidden` -> 403 (both more conventional than 422
for auth failures). Update `ExceptionHandlingMiddleware`'s `catch (DomainException dex)` block
to special-case these two codes:

```csharp
catch (DomainException dex)
{
    var status = dex.Code switch
    {
        "unauthenticated" => HttpStatusCode.Unauthorized,
        "forbidden" => HttpStatusCode.Forbidden,
        _ => HttpStatusCode.UnprocessableEntity
    };
    _logger.LogWarning(dex, "Domain rule violated: {Code}", dex.Code);
    await WriteProblemAsync(context, correlation.CorrelationId, status, dex.Code, dex.Message, null);
}
```

## 4. TASK

1. Create `AssemblyMarker.cs`, `IRequiresPermission.cs`, `ValidationBehavior.cs`, and
   `AuthorizationBehavior.cs` exactly as shown.
2. Register MediatR with both open behaviors (authorization first, then validation) and
   `AddValidatorsFromAssembly` in `Program.cs`.
3. Patch `ExceptionHandlingMiddleware` (from P-113) to special-case `"unauthenticated"` -> 401
   and `"forbidden"` -> 403 as shown, leaving all other domain codes at 422.
4. Write a throwaway test command+handler+validator INSIDE
   `StoreOS.IntegrationTests` (not production code) implementing `IRequest<Unit>`,
   `IRequiresPermission` (e.g. `RequiredPermission => "test:permission"`), and a
   `IValidator<T>` requiring a non-empty string field, to exercise the pipeline end-to-end via
   `WebApplicationFactory` + a minimal test-only endpoint that dispatches it through
   `IMediator`.
5. Write `StoreOS.IntegrationTests/Behaviors/PipelineBehaviorTests.cs`: (a) unauthenticated
   request -> 401 `unauthenticated`; (b) authenticated but missing permission -> 403
   `forbidden`; (c) authenticated with permission but invalid payload -> 400 with field
   errors; (d) authenticated, permitted, valid -> success.

## 5. HARD CONSTRAINTS

- Behavior order is fixed: **authorization runs before validation**
  — do not swap this. An unauthorized caller must never learn field-validation details about
  a command they aren't allowed to run.
- `IRequiresPermission` is OPTIONAL per command — commands/queries that don't implement it
  (e.g. public read endpoints once they exist) skip the permission check entirely, they are
  NOT denied by default. Only commands that explicitly opt in via the interface are gated.
- Do not implement the "live DB permission re-check for high-impact operations" here — that
  is a SEPARATE, additional behavior built in P-308 that layers on top of this one for a
  specific subset of sensitive commands.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter PipelineBehaviorTests
# Expected: 4 passed (401, 403, 400, 200-success paths).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-114 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-114 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-114` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `IRequiresPermission` interface signature, the exact behavior
registration order (Authorization then Validation), and the status-code mapping table
(`unauthenticated`->401, `forbidden`->403, all other DomainException codes->422).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-114: <one-line summary>"` only after verification is
   fully green.
