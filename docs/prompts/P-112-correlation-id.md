# P-112 — CorrelationIdMiddleware + Serilog enrichment

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-112** of the StoreOS Cloud build ("CorrelationIdMiddleware + Serilog enrichment") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-108 complete (RLS on tenant-only tables, `PostgresTestFixture`, app-role guard). In this revised build P-112 and P-113 run BEFORE P-109 so that `DomainException` and correlation ids exist when the transaction behavior is written.
- No logging framework beyond ASP.NET Core's default console logger is configured yet.
- No correlation ID handling exists anywhere.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `TransactionBehavior` is a MediatR pipeline behavior (P-109), NOT HTTP middleware: ignore any instruction here about ordering HTTP middleware relative to it. Correlation-id middleware simply stays the first HTTP middleware.

Add Serilog per Plan §6.7. In `Program.cs`, before `builder.Build()`:

```csharp
builder.Host.UseSerilog((context, services, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] CorrelationId={CorrelationId} {Message:lj}{NewLine}{Exception}"));
```

Create `backend/src/StoreOS.Api/Middleware/CorrelationIdMiddleware.cs`:

```csharp
using Serilog.Context;

namespace StoreOS.Api.Middleware;

public class CorrelationIdMiddleware
{
    public const string HeaderName = "X-Correlation-ID";
    private readonly RequestDelegate _next;
    public CorrelationIdMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(HttpContext context, ICorrelationContextAccessor accessor)
    {
        var correlationId = context.Request.Headers.TryGetValue(HeaderName, out var incoming)
            && !string.IsNullOrWhiteSpace(incoming)
            && Guid.TryParse(incoming, out _)
            ? incoming.ToString()
            : Guid.NewGuid().ToString("D");

        accessor.CorrelationId = correlationId;
        context.Items["CorrelationId"] = correlationId;
        context.Response.Headers[HeaderName] = correlationId;

        using (LogContext.PushProperty("CorrelationId", correlationId))
        {
            await _next(context);
        }
    }
}
```

Create the accessor abstraction `backend/src/StoreOS.Application/Abstractions/ICorrelationContextAccessor.cs`:

```csharp
namespace StoreOS.Application.Abstractions;

public interface ICorrelationContextAccessor
{
    string CorrelationId { get; set; }
}
```

Implementation `backend/src/StoreOS.Infrastructure/Observability/CorrelationContextAccessor.cs`
(scoped service, plain backing field is fine since it's one-per-request):

```csharp
namespace StoreOS.Infrastructure.Observability;

public class CorrelationContextAccessor : StoreOS.Application.Abstractions.ICorrelationContextAccessor
{
    public string CorrelationId { get; set; } = string.Empty;
}
```

Register scoped in `Program.cs`: `builder.Services.AddScoped<ICorrelationContextAccessor, CorrelationContextAccessor>();`
Wire the middleware FIRST in the pipeline, before everything else (including
`TransactionBehavior` from P-109), so the correlation ID is available to enrich logs
even for requests that fail authentication or fail before reaching the transaction scope:
`app.UseMiddleware<CorrelationIdMiddleware>();` immediately after `var app = builder.Build();`
and before any other `app.Use...` call.

## 4. TASK

1. Add `Serilog.AspNetCore` and `Serilog.Sinks.Console` package references to
   `StoreOS.Api.csproj` (already pinned centrally in P-004).
2. Wire `UseSerilog` in `Program.cs` exactly as shown.
3. Create `ICorrelationContextAccessor.cs` in `StoreOS.Application/Abstractions/` and
   `CorrelationContextAccessor.cs` in `StoreOS.Infrastructure/Observability/`, register scoped.
4. Create `CorrelationIdMiddleware.cs` exactly as shown and register it as the FIRST
   middleware in the pipeline.
5. Write `StoreOS.IntegrationTests/Observability/CorrelationIdTests.cs`: (a) a request with no
   `X-Correlation-ID` header receives a response with a freshly generated valid GUID in that
   header; (b) a request WITH a valid GUID in `X-Correlation-ID` gets that EXACT value echoed
   back unchanged; (c) a request with a garbage (non-GUID) value in the header gets a NEW
   generated GUID instead (not the garbage value) in the response.

## 5. HARD CONSTRAINTS

- The correlation ID middleware MUST run before ANY other
  middleware, including `TransactionBehavior` — reorder `Program.cs` accordingly if
  P-109 placed things differently.
- Correlation IDs must always be valid GUID strings in `"D"` format
  (`xxxxxxxx-xxxx-xxxx-xxxx-xxxxxxxxxxxx`) — never accept or generate any other shape.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CorrelationIdTests
# Expected: 3 passed (missing header generates one, valid header echoed, invalid header replaced).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-112 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-112 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-112` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ICorrelationContextAccessor` signature, the `CorrelationIdMiddleware`
header name constant (`X-Correlation-ID`), and its exact pipeline position (first).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-112: <one-line summary>"` only after verification is
   fully green.
