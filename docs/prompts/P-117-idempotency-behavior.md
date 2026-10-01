# P-117 — idempotency_keys + IdempotencyBehavior (in-transaction idempotency)

**Phase:** 1 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-117** of the StoreOS Cloud build ("idempotency_keys + IdempotencyBehavior (in-transaction idempotency)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document. Read `prompts/_shared/CONTRACTS.md` and `prompts/_shared/DECISIONS.md` first: they define the shared interfaces and the decisions this prompt relies on.

## 2. WORLD STATE

- P-108/P-110 (RLS), P-109 (`TransactionBehavior`), P-114 (behaviors) complete. Every request that reaches a handler already runs inside ONE
  transaction with the RLS session variables set.
- The original design was an HTTP middleware that inserted a 'pending' row and buffered the response. It had three defects: (1) two concurrent
  requests with the same key both tried to insert the pending row inside their own uncommitted transactions and the loser crashed with a
  primary-key violation (HTTP 500); (2) the response was written to the client BEFORE the transaction committed; (3) it could not take part in
  the retry of P-511. It is replaced by a MediatR behavior + store that live INSIDE the business transaction (DECISIONS D-02).

## 3. CONTEXT BLOCK

### 3.1 Semantics

`INSERT ... ON CONFLICT DO NOTHING` on the idempotency row, the business operation, and the completion `UPDATE` of the stored response all happen in the SAME transaction:
- First request: inserts the row, runs the handler, stores the serialized response, commits. All three succeed or none does, so a request that fails (or is rolled back) leaves NO idempotency record and can simply be retried.
- A concurrent duplicate (same tenant, user, scope, key) blocks on the unique index until the first transaction finishes. If the first COMMITs, PostgreSQL raises 40001 in the second (RepeatableRead/Serializable can no longer see a conflicting row it did not snapshot); `SerializationRetryBehavior` (P-511) re-runs the second request from scratch, it now SEES the committed row and replays the stored response. If the first rolls back, the second proceeds normally.
- A later duplicate with the SAME payload replays the stored response; with a DIFFERENT payload it fails with `idempotency_key_reuse` (409).
- Replays return the same result object, therefore the same HTTP status the controller maps it to, plus the header `Idempotent-Replayed: true` (controllers add it when `IdempotencyReplayFlag.Replayed` is set by the behavior — see below).

### 3.2 DDL (unchanged from Plan §6.4) and RLS

```sql
CREATE TABLE idempotency_keys (
  tenant_id  uuid NOT NULL,
  user_id    uuid NOT NULL,
  key        text NOT NULL,
  endpoint   text NOT NULL,          -- the request's IdempotencyScope, e.g. 'pos.checkout'
  request_hash text NOT NULL,
  response_status int,
  response_body  jsonb,
  created_at timestamptz NOT NULL DEFAULT now(),
  PRIMARY KEY (tenant_id, user_id, endpoint, key)
);
ALTER TABLE idempotency_keys ENABLE ROW LEVEL SECURITY;
ALTER TABLE idempotency_keys FORCE ROW LEVEL SECURITY;
CREATE POLICY tenant_isolation_policy ON idempotency_keys
  USING (tenant_id = NULLIF(current_setting('app.tenant_id', true), '')::uuid);
CREATE INDEX ix_idempotency_created ON idempotency_keys (created_at);
```
Put the RLS statements in the migration with `migrationBuilder.Sql(...)` (same pattern as P-108). Migration name `008_Idempotency`.

### 3.3 Domain entity (`backend/src/StoreOS.Domain/Platform/IdempotencyRecord.cs`)

```csharp
namespace StoreOS.Domain.Platform;

public class IdempotencyRecord
{
    public Guid TenantId { get; private set; }
    public Guid UserId { get; private set; }
    public string Key { get; private set; } = default!;
    public string Endpoint { get; private set; } = default!;
    public string RequestHash { get; private set; } = default!;
    public int? ResponseStatus { get; private set; }
    public string? ResponseBodyJson { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    private IdempotencyRecord() { }
}
```
EF configuration: table `idempotency_keys`, composite key `(TenantId, UserId, Endpoint, Key)`, snake_case columns, `ResponseBodyJson` mapped to `jsonb` column `response_body`, entity filtered by the tenant query filter (tenant-only archetype, P-107). The Application layer never touches this entity; only the store below does.

### 3.4 Application contracts (`StoreOS.Application/Abstractions/`)

```csharp
namespace StoreOS.Application.Abstractions;

/// Implemented by commands that must be idempotent. Controllers copy the Idempotency-Key header into IdempotencyKey.
public interface IIdempotentRequest
{
    string? IdempotencyKey { get; }
    /// Stable, low-cardinality scope. Examples: "pos.checkout", "pos.estimate.convert:{estimateId}", "sales.payment:{saleId}".
    string IdempotencyScope { get; }
    /// true => a missing key is rejected with idempotency_key_required (400).
    bool IdempotencyRequired { get; }
}

public enum IdempotencyOutcome { Started, Replay, Mismatch, InProgress }
public sealed record IdempotencyBegin(IdempotencyOutcome Outcome, string? ResponseJson);

public interface IIdempotencyStore
{
    Task<IdempotencyBegin> BeginAsync(string key, string scope, string requestHash, CancellationToken ct);
    Task CompleteAsync(string key, string scope, string responseJson, CancellationToken ct);
}
```

### 3.5 Behavior (`StoreOS.Application/Behaviors/IdempotencyBehavior.cs`)

```csharp
using System.Security.Cryptography;
using System.Text.Json;
using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Behaviors;

public sealed class IdempotencyBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : notnull
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    private readonly IIdempotencyStore _store;
    private readonly IdempotencyReplayFlag _flag;
    public IdempotencyBehavior(IIdempotencyStore store, IdempotencyReplayFlag flag) { _store = store; _flag = flag; }

    public async Task<TResponse> Handle(TRequest request, RequestHandlerDelegate<TResponse> next, CancellationToken ct)
    {
        if (request is not IIdempotentRequest idem) return await next();

        if (string.IsNullOrWhiteSpace(idem.IdempotencyKey))
        {
            if (idem.IdempotencyRequired)
                throw new DomainException("idempotency_key_required", "The Idempotency-Key header is required for this operation.");
            return await next();
        }
        if (!Guid.TryParse(idem.IdempotencyKey, out _))
            throw new DomainException("idempotency_key_invalid", "Idempotency-Key must be a UUID.");

        var hash = Convert.ToHexString(SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(request, Json)));
        var begin = await _store.BeginAsync(idem.IdempotencyKey, idem.IdempotencyScope, hash, ct);
        switch (begin.Outcome)
        {
            case IdempotencyOutcome.Mismatch:
                throw new DomainException("idempotency_key_reuse", "This Idempotency-Key was already used with a different request.");
            case IdempotencyOutcome.InProgress:
                throw new DomainException("idempotency_in_progress", "A request with this Idempotency-Key is still being processed.");
            case IdempotencyOutcome.Replay:
                _flag.Replayed = true;
                return JsonSerializer.Deserialize<TResponse>(begin.ResponseJson!, Json)!;
        }

        var response = await next();
        await _store.CompleteAsync(idem.IdempotencyKey, idem.IdempotencyScope, JsonSerializer.Serialize(response, Json), ct);
        return response;
    }
}

/// Scoped flag read by controllers to add the 'Idempotent-Replayed: true' response header.
public sealed class IdempotencyReplayFlag { public bool Replayed { get; set; } }
```
Response types of idempotent commands must be plain serializable records (no entities, no cycles). Register `IdempotencyReplayFlag` as scoped.

### 3.6 Store (`StoreOS.Infrastructure/Persistence/IdempotencyStore.cs`)

Implements `IIdempotencyStore` with `StoreOsDbContext` + `IAccessScope` (tenant and user id come from the scope, never from the caller). `BeginAsync`:
1. `INSERT INTO idempotency_keys (tenant_id, user_id, key, endpoint, request_hash) VALUES (...) ON CONFLICT (tenant_id, user_id, endpoint, key) DO NOTHING` via `Database.ExecuteSqlInterpolatedAsync`; 1 row affected => `Started`.
2. Otherwise read the existing row (`AsNoTracking`, same transaction): different `request_hash` => `Mismatch`; `response_body IS NULL` => `InProgress`; else `Replay` with the stored JSON.
`CompleteAsync`: `UPDATE idempotency_keys SET response_status = 200, response_body = {json}::jsonb WHERE tenant_id/user_id/endpoint/key = ...`. Register `AddScoped<IIdempotencyStore, IdempotencyStore>()`.
Never let the raw SQL run outside the request transaction (it relies on the transaction for RLS variables and atomicity).

### 3.7 Registration and rollout
MediatR behavior order (D-04): `... -> TransactionBehavior -> TokenVersion -> Authorization -> IdempotencyBehavior -> handler`. The HTTP middleware `IdempotencyMiddleware` from the old design MUST NOT exist. Which commands are idempotent:
| Command (prompt) | Scope string | Required |
|---|---|---|
| `CheckoutCommand` (P-504/P-511) | `pos.checkout` | yes |
| `ConvertEstimateCommand` (P-520) | `pos.estimate.convert:{EstimateId}` | yes |
| `RecordPaymentCommand` (P-523) | `sales.payment:{SaleId}` | yes |
| `CreateEstimateCommand` (P-518) | `pos.estimate` | no |
Other money/stock commands MAY implement `IIdempotentRequest` later; none is required now.
A purge job for rows older than 7 days is added to P-905 (jobs).

## 4. TASK

1. Create the contracts (3.4), the behavior + flag (3.5), the domain entity + EF configuration (3.3), the store (3.6).
2. Generate migration `008_Idempotency` including the RLS statements (3.2) and apply it (`dotnet ef database update` as in P-102, then `deploy/db/apply-post-migrate.sh`).
3. Register everything; add `IdempotencyBehavior` to the behavior order.
4. Tests (`IdempotencyBehaviorTests`, `[Collection("postgres")]`, app role connection) using a test-only command `IncrementProbe : IRequest<CounterResult>, IIdempotentRequest` that inserts one row into a probe table and returns its count:
   (a) same key + same payload twice => handler ran ONCE, both results equal, second sets `Replayed`;
   (b) same key + different payload => `DomainException{Code="idempotency_key_reuse"}`;
   (c) missing key with `IdempotencyRequired=true` => `idempotency_key_required`; malformed key => `idempotency_key_invalid`;
   (d) handler throws => NO row in `idempotency_keys` (rolled back) and a retry with the same key runs the handler again;
   (e) TWO CONCURRENT requests with the same key (run through the full pipeline incl. `SerializationRetryBehavior` from P-511 — if P-511 is not implemented yet, write this test now and mark it `[Fact(Skip = "enabled by P-511")]`, then P-511 removes the Skip): exactly one handler execution, both callers receive the same response, no unhandled exception;
   (f) tenant B cannot replay tenant A's key (same key string, different tenant => independent);
   (g) same key on two different `IdempotencyScope`s is independent.

## 5. HARD CONSTRAINTS

- The idempotency insert, the business operation and the response `UPDATE` MUST be in the same transaction. No separate `SaveChanges`/connection for idempotency.
- Never cache failed responses: failures roll back and leave no record.
- The Application layer must not reference `StoreOsDbContext` or the entity; use `IIdempotencyStore`.
- `endpoint` in the PK stores the command's `IdempotencyScope`, not the raw URL path (paths contain ids and would fragment the key space).

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter "FullyQualifiedName~IdempotencyBehaviorTests"
# Expected: all pass (concurrency test (e) may be Skipped until P-511).
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-117 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-117 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-117` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: `IIdempotentRequest`, `IIdempotencyStore`, `IdempotencyBegin`, `IdempotencyBehavior`, `IdempotencyReplayFlag`, the table DDL + RLS policy, the migration name, and the behavior registration order.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-117: <one-line summary>"` only after verification is
   fully green.
