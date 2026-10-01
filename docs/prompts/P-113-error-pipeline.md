# P-113 — ProblemDetails/DomainException/error pipeline

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-113** of the StoreOS Cloud build ("ProblemDetails/DomainException/error pipeline") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-112 complete: correlation IDs flow through every request and into Serilog
  log enrichment.
- No structured exception handling exists yet — an unhandled exception currently produces
  ASP.NET Core's default developer exception page or a raw 500.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** **Central error-code -> HTTP status catalog (DECISIONS D-08).** The original build patched status codes prompt by prompt (P-114 for 401/403, P-310 for 409, P-522 for 404), which is how auth failures ended up as 422 and broke the frontend's "refresh on 401" flow. Replace the single `catch (DomainException)` block with a lookup in `backend/src/StoreOS.Api/Middleware/ErrorCatalog.cs`:
```csharp
using System.Net;
namespace StoreOS.Api.Middleware;

public static class ErrorCatalog
{
    private static readonly Dictionary<string, HttpStatusCode> Explicit = new()
    {
        // 401 - the client must (re)authenticate; the Angular interceptor refreshes on 401 only
        ["unauthenticated"] = HttpStatusCode.Unauthorized,
        ["invalid_credentials"] = HttpStatusCode.Unauthorized,
        ["token_version_stale"] = HttpStatusCode.Unauthorized,
        ["invalid_refresh_token"] = HttpStatusCode.Unauthorized,
        ["refresh_token_expired"] = HttpStatusCode.Unauthorized,
        ["refresh_token_reuse_detected"] = HttpStatusCode.Unauthorized,
        ["tenant_required"] = HttpStatusCode.Unauthorized,
        // 403 - authenticated but not allowed
        ["forbidden"] = HttpStatusCode.Forbidden,
        ["tenant_inactive"] = HttpStatusCode.Forbidden,
        // 400 - malformed request context
        ["branch_context_required"] = HttpStatusCode.BadRequest,
        ["idempotency_key_required"] = HttpStatusCode.BadRequest,
        ["idempotency_key_invalid"] = HttpStatusCode.BadRequest,
        // 409 - state conflicts
        ["branch_code_taken"] = HttpStatusCode.Conflict,
        ["imei_already_active"] = HttpStatusCode.Conflict,
        ["idempotency_key_reuse"] = HttpStatusCode.Conflict,
        ["idempotency_in_progress"] = HttpStatusCode.Conflict,
        ["serialization_conflict"] = HttpStatusCode.Conflict,
    };

    public static HttpStatusCode StatusFor(string code)
    {
        if (Explicit.TryGetValue(code, out var s)) return s;
        if (code.EndsWith("_not_found", StringComparison.Ordinal)) return HttpStatusCode.NotFound;
        return HttpStatusCode.UnprocessableEntity;           // every other business-rule violation
    }
}
```
and in the middleware: `catch (DomainException dex) { await WriteProblemAsync(context, correlation.CorrelationId, ErrorCatalog.StatusFor(dex.Code), dex.Code, dex.Message, null); }`. Add `Retry-After: 1` for `serialization_conflict`. RULE for all later prompts: a prompt that introduces a new `DomainException` code that must NOT be 422 adds ONE line to `ErrorCatalog.Explicit` (do not touch the middleware). Add an architecture test in `StoreOS.IntegrationTests`: scan the Application/Domain source for `new DomainException("<code>"` / `new DomainValidationException("<code>"` string literals and assert each code that ends in `_not_found` maps to 404 and the codes above map as listed. Where P-114/P-310/P-410/P-522 tell you to special-case a code in the middleware, add it to this catalog instead.

> **REVISION NOTE (review v2):** `TransactionBehavior` is a MediatR behavior (P-109), not HTTP middleware. `ExceptionHandlingMiddleware` stays the second HTTP middleware (after CorrelationIdMiddleware). Because the transaction is closed BEFORE the exception reaches this middleware, a DomainException always means the transaction was already rolled back.

Create `backend/src/StoreOS.Domain/Exceptions/DomainException.cs` (the Domain layer owns its exceptions from the start; the original plan created them in Application and moved them in P-202, which only caused churn):

```csharp
namespace StoreOS.Domain.Exceptions;

public class DomainException : Exception
{
    public string Code { get; }
    public DomainException(string code, string message) : base(message) => Code = code;
}

public class DomainValidationException : DomainException
{
    public DomainValidationException(string code, string message) : base(code, message) { }
}
```

Create `backend/src/StoreOS.Api/Middleware/ExceptionHandlingMiddleware.cs` implementing
RFC 7807 `ProblemDetails` with `correlationId` in `extensions`, per Plan §6.6/§15:

```csharp
using System.Net;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Api.Middleware;

public class ExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionHandlingMiddleware> _logger;
    public ExceptionHandlingMiddleware(RequestDelegate next, ILogger<ExceptionHandlingMiddleware> logger)
    {
        _next = next; _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor correlation)
    {
        try
        {
            await _next(context);
        }
        catch (ValidationException vex)
        {
            await WriteProblemAsync(context, correlation.CorrelationId, HttpStatusCode.BadRequest,
                "validation_error", "One or more fields are invalid.",
                vex.Errors.GroupBy(e => e.PropertyName)
                    .ToDictionary(g => g.Key, g => g.Select(e => e.ErrorMessage).ToArray()));
        }
        catch (DomainException dex)
        {
            _logger.LogWarning(dex, "Domain rule violated: {Code}", dex.Code);
            await WriteProblemAsync(context, correlation.CorrelationId,
                HttpStatusCode.UnprocessableEntity, dex.Code, dex.Message, null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception");
            await WriteProblemAsync(context, correlation.CorrelationId,
                HttpStatusCode.InternalServerError, "internal_error",
                "An unexpected error occurred.", null);
        }
    }

    private static async Task WriteProblemAsync(HttpContext context, string correlationId,
        HttpStatusCode status, string code, string title, Dictionary<string, string[]>? errors)
    {
        context.Response.ContentType = "application/problem+json";
        context.Response.StatusCode = (int)status;
        var problem = new ProblemDetails
        {
            Status = (int)status,
            Title = title,
            Type = $"https://storeos.io/errors/{code}",
        };
        problem.Extensions["correlationId"] = correlationId;
        problem.Extensions["code"] = code;
        if (errors != null) problem.Extensions["errors"] = errors;
        await context.Response.WriteAsJsonAsync(problem);
    }
}
```

Register as the SECOND middleware (right after `CorrelationIdMiddleware`, before everything
else) so it can catch exceptions from all downstream middleware including
`TransactionBehavior`: `app.UseMiddleware<ExceptionHandlingMiddleware>();`

## 4. TASK

1. Create `DomainException.cs` and `DomainValidationException` in
   `StoreOS.Domain/Exceptions/` exactly as shown.
2. Create `ExceptionHandlingMiddleware.cs` in `StoreOS.Api/Middleware/` exactly as shown.
3. Wire it into `Program.cs` immediately after `CorrelationIdMiddleware` and before
   `TransactionBehavior`.
4. Write `StoreOS.IntegrationTests/Errors/ExceptionHandlingTests.cs` using a minimal test
   endpoint (added only within the test's `WebApplicationFactory` customization, not in
   production `Program.cs`) that throws each of: `FluentValidation.ValidationException`,
   `DomainException("test_code","test message")`, and a generic `Exception`. Assert: (a) 400
   with `application/problem+json` and a `errors` extension keyed by property name for the
   validation case; (b) 422 with `code`="test_code" and `correlationId` present for the domain
   case; (c) 500 with `code`="internal_error" (and NOT the raw exception message/stack trace
   leaked into the response body) for the generic case.

## 5. HARD CONSTRAINTS

- A `DomainException`/`DomainValidationException` must ALWAYS map to
  422, never 500 — this is a hard rule referenced throughout the rest of the build (margin
  scheme cost guard, credit limit block, stock/IMEI unavailability, etc. all throw this type).
- The generic-exception (500) branch must NEVER include the raw exception message or stack
  trace in the HTTP response body — only the correlation ID, so support can look it up in
  logs. Do log the full exception server-side via `_logger.LogError(ex, ...)`.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ExceptionHandlingTests
# Expected: 3 passed (400 validation, 422 domain, 500 generic without leaking stack trace).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-113 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-113 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-113` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `DomainException`/`DomainValidationException` signatures and the exact
`ProblemDetails` extension key names (`correlationId`, `code`, `errors`) that every later
prompt's error responses must use.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-113: <one-line summary>"` only after verification is
   fully green.
