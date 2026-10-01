# P-511 — SerializationRetryBehavior — retry the whole transaction on 40001/40P01

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-511** of the StoreOS Cloud build ("SerializationRetryBehavior — retry the whole transaction on 40001/40P01") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document. Read `prompts/_shared/CONTRACTS.md` and `prompts/_shared/DECISIONS.md` first: they define the shared interfaces and the decisions this prompt relies on.

## 2. WORLD STATE

- P-510 complete: `CheckoutHandler` works for the happy path; P-117 (`IdempotencyBehavior`) and P-109 (`TransactionBehavior`) exist.
- The original version of this prompt wrapped the handler in a Polly retry INSIDE an already-open transaction and tried to 'escalate to Serializable' with `SET LOCAL transaction_isolation`. Neither works in PostgreSQL: after a serialization failure the whole transaction is aborted (a retry inside it fails with 25P02), and the isolation level cannot be changed after the first query (`set_config` is a query). The retry must wrap the WHOLE transaction (DECISIONS D-01).

## 3. CONTEXT BLOCK

### 3.1 `SerializationRetryBehavior` (Infrastructure — needs Npgsql and Polly)

`backend/src/StoreOS.Infrastructure/Persistence/SerializationRetryBehavior.cs`:
```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Polly;
using Polly.Retry;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class SerializationRetryBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly ResiliencePipeline Pipeline = new ResiliencePipelineBuilder()
        .AddRetry(new RetryStrategyOptions
        {
            ShouldHandle = new PredicateBuilder().Handle<Exception>(IsSerializationFailure),
            MaxRetryAttempts = 3,
            BackoffType = DelayBackoffType.Exponential,
            UseJitter = true,
            Delay = TimeSpan.FromMilliseconds(25),
            MaxDelay = TimeSpan.FromMilliseconds(250)
        })
        .Build();

    private readonly IServiceProvider _services;
    public SerializationRetryBehavior(IServiceProvider services) => _services = services;

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        // Only OUTERMOST read-write requests are retried. Queries are never retried; a nested Send() inside an open
        // transaction must never retry on its own (the surrounding transaction is already aborted).
        if (request is IReadOnlyRequest or IReportingRequest) return await next();
        if (_services.GetRequiredService<StoreOsDbContext>().Database.CurrentTransaction is not null) return await next();

        try
        {
            return await Pipeline.ExecuteAsync(async _ => await next(), ct);
        }
        catch (Exception ex) when (IsSerializationFailure(ex))
        {
            throw new DomainException("serialization_conflict",
                "The system is busy processing a conflicting change. Please try again.");
        }
    }

    public static bool IsSerializationFailure(Exception ex)
    {
        for (var e = ex; e is not null; e = e.InnerException)
            if (e is PostgresException { SqlState: "40001" or "40P01" }) return true;
        return false;
    }
}
```
Each attempt re-enters `TransactionBehavior`, i.e. opens a FRESH transaction (new snapshot) and `ChangeTracker.Clear()` has already run for the failed one. Consequences that every handler author must respect: handlers are re-executed from the top, so they must be stateless (no fields mutated across calls) and must not perform non-database side effects (no e-mail/SMS/HTTP calls, no file writes, no Hangfire `Enqueue`) inside the transaction; do those in the controller after `Send` returns.

### 3.2 Order and status mapping
Register it OUTSIDE `TransactionBehavior` and INSIDE `ValidationBehavior`: `Validation -> SerializationRetry -> Transaction -> TokenVersion -> Authorization -> Idempotency -> handler` (D-04). After exhaustion the client gets `409 serialization_conflict` (`ErrorCatalog`, P-113) with the correlation id; if the competing transaction consumed the stock, the retry sees the new state and the handler throws `out_of_stock` (422) instead.

### 3.3 Wire the checkout command
- `CheckoutCommand` (P-504) additionally implements `ISerializableRequest` (P-109) and `IIdempotentRequest` (P-117): add `string? IdempotencyKey { get; init; }` and the members `IdempotencyScope => "pos.checkout"`, `IdempotencyRequired => true`. The controller (P-513) fills `IdempotencyKey` from the `Idempotency-Key` header.
- Remove every use of `SET LOCAL transaction_isolation`, `EnableRetryOnFailure`, and any Polly code you find inside `CheckoutHandler`.
- `ConvertEstimateCommand` (P-520) gets the same two markers with scope `pos.estimate.convert:{EstimateId}`; `RecordPaymentCommand` (P-523) implements `IIdempotentRequest` with scope `sales.payment:{SaleId}` (RepeatableRead default, retried).

## 4. TASK

1. Create `SerializationRetryBehavior.cs`; register it in the MediatR pipeline in the order above (also register `IdempotencyBehavior` from P-117 if not yet in the list).
2. Add the markers/members to `CheckoutCommand`; remove any leftover in-handler transaction/retry code.
3. Unit tests (`SerializationRetryBehaviorTests`, no database): with a fake `next` that throws `new PostgresException("x", "ERROR", "ERROR", "40001")` twice then returns => 3 invocations and success; always throwing => `DomainException{Code="serialization_conflict"}` after 4 total invocations; throwing an ordinary exception => NOT retried; `IReadOnlyRequest` => NOT retried; the exception wrapped in `DbUpdateException` is still recognised.
4. Integration tests (`CheckoutContentionTests`, `[Collection("postgres")]`, real authenticated HTTP through `WebApplicationFactory` on the APP role): (a) two parallel checkouts of the LAST unit of a product => exactly one 201 and one 422 `out_of_stock`, stock ends at 0; (b) 20 parallel checkouts of DIFFERENT products at ONE branch (they contend on the `invoice_sequences` row) => all 201, invoice numbers unique and consecutive; (c) the SAME `Idempotency-Key` sent twice in parallel => exactly one sale row, both responses carry the same invoice number; (d) same key + different cart => 409 `idempotency_key_reuse`; (e) enable the Skipped test (e) of P-117.

## 5. HARD CONSTRAINTS

- Retry wraps the WHOLE transaction. No retry logic, `BeginTransaction` or `SET LOCAL transaction_isolation` may appear inside a handler.
- Max 3 retries (4 executions), exponential backoff with jitter, 25 ms initial, 250 ms cap (Plan §10.1).
- Retries apply to every read-write request, not only checkout: RepeatableRead also raises 40001 on concurrent updates of the same row.
- The behavior lives in Infrastructure; Application code never references Npgsql or Polly.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter "FullyQualifiedName~SerializationRetryBehaviorTests|FullyQualifiedName~CheckoutContentionTests|FullyQualifiedName~IdempotencyBehaviorTests"
# Expected: all pass, including the parallel last-unit and same-key concurrency tests (run them 5 times: no flakiness).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-511 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-511 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-511` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the exact `SerializationRetryBehavior` signature, the final MediatR behavior order, and the markers/members added to `CheckoutCommand`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-511: <one-line summary>"` only after verification is
   fully green.
