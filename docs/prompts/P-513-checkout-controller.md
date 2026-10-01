# P-513 — POST /pos/checkout controller wiring + response DTO

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-513** of the StoreOS Cloud build ("POST /pos/checkout controller wiring + response DTO") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-510, P-511, P-512 complete: `CheckoutHandler` with retry wrapper is fully
  tested via `IMediator.Send`.
- No HTTP endpoint exposes checkout yet. This prompt wires the final controller action,
  including the `Idempotency-Key` requirement per Plan §11's API surface table.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** The controller does NOT implement idempotency itself. Read the header with `[FromHeader(Name = "Idempotency-Key")] string? idempotencyKey`, build `command with { IdempotencyKey = idempotencyKey }`, call `mediator.Send`, and add the response header `Idempotent-Replayed: true` when the scoped `IdempotencyReplayFlag.Replayed` is set. Missing/invalid keys are rejected by `IdempotencyBehavior` (400 via `ErrorCatalog`). Ignore any text that mentions `IdempotencyMiddleware`.

Extend `backend/src/StoreOS.Api/Controllers/PosController.cs` (created in
P-503):

```csharp
[HttpPost("checkout")]
public async Task<IActionResult> Checkout(CheckoutCommand command, CancellationToken ct)
{
    if (!Request.Headers.ContainsKey("Idempotency-Key"))
        return BadRequest(new { code = "idempotency_key_required",
            message = "POST /pos/checkout requires an Idempotency-Key header." });
    var result = await _mediator.Send(command, ct);
    return CreatedAtAction(nameof(GetSaleSummary), new { id = result.SaleId }, new
    {
        saleId = result.SaleId, invoiceNumber = result.InvoiceNumber, grandTotal = result.GrandTotal,
        amountPaid = result.AmountPaid, balance = result.Balance, saleDate = result.SaleDate
    });
}

[HttpGet("sale-summary/{id:guid}")]
public IActionResult GetSaleSummary(Guid id) => Ok(new { id }); // minimal stub for CreatedAtAction's route resolution;
    // the REAL detailed sale view is GET /api/sales/{id}, built in P-522 — this action exists
    // ONLY to satisfy CreatedAtAction's route-name requirement and returns a minimal
    // acknowledgement, not full sale detail (avoid duplicating P-522's real logic here).
```

Wait — the note above risks violating the anti-stub rule ("stub" language). Resolve this
properly: do NOT create a placeholder `GetSaleSummary` action at all. Instead, use
`CreatedAtAction` pointing at the REAL `SalesController.GetById` action (from P-522) once it
exists — but P-522 doesn't exist yet in build order. Use the simpler, fully-correct pattern
instead: return `Created($"/api/sales/{result.SaleId}", new {...})` using the `Created(Uri,
value)` overload with a hand-built relative URI string, which requires no forward reference to
a not-yet-existing action method at all, and is not a stub — it's a complete, correct HTTP 201
response with a valid `Location` header pointing at where the resource WILL be once P-522 ships
(a client requesting that path before P-522 exists would get a 404, which is accurate and
expected mid-build, not a defect in this prompt's own scope).

## 4. TASK

1. Add the `POST /api/pos/checkout` action to `PosController.cs` using the
   `Created($"/api/sales/{result.SaleId}", new {...})` pattern (no forward-reference stub
   action), requiring the `Idempotency-Key` header explicitly (return 400
   `idempotency_key_required` if absent, BEFORE dispatching to MediatR).
2. Make `CheckoutCommand` carry the key (`IdempotencyKey` property, P-511) and rely on `IdempotencyBehavior` (P-117); there is no route-specific registration and no HTTP middleware.
3. Write `StoreOS.IntegrationTests/Sales/CheckoutControllerTests.cs`: (a) POST without
   `Idempotency-Key` -> 400 `idempotency_key_required`; (b) POST with a valid key and a
   well-formed cart -> 201 with `Location` header pointing at `/api/sales/{saleId}` and a JSON
   body matching the shape shown; (c) repeating the EXACT same request with the SAME
   `Idempotency-Key` -> replays the cached 201 response WITHOUT creating a second sale (query
   `sales` table count to confirm exactly one row); (d) repeating with the SAME key but a
   DIFFERENT cart payload -> 409 (per P-117's idempotency conflict behavior).

## 5. HARD CONSTRAINTS

- Do not create any placeholder/stub controller action anywhere in
  this prompt — the `Created(Uri, value)` overload with a hand-built path string is the
  complete, correct solution requiring no forward reference.
- The `Idempotency-Key` header check happens BEFORE `_mediator.Send(...)` — fail fast with a
  clear 400 rather than letting the request proceed without idempotency protection.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CheckoutControllerTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-513 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-513 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-513` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the final `POST /api/pos/checkout` route, its required `Idempotency-Key`
header behavior, and the response JSON shape.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-513: <one-line summary>"` only after verification is
   fully green.
