# P-518 — POST /pos/estimate endpoint

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-518** of the StoreOS Cloud build ("POST /pos/estimate endpoint") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-517 complete: `CheckoutHandler` correctly bypasses stock/ledger effects for
  Estimates.
- No DEDICATED estimate-creation endpoint exists — currently the ONLY way to create an estimate
  is via `POST /pos/checkout` with `InvoiceType="Estimate"` in the body (P-513). Per Plan
  §11's API table, `POST /pos/estimate` is listed as a SEPARATE route. Resolve this: make
  `POST /pos/estimate` a thin wrapper that forces `InvoiceType="Estimate"` server-side
  (ignoring/overriding whatever the client sent for that field) and otherwise dispatches the
  SAME `CheckoutCommand`/`CheckoutHandler` — this avoids duplicating any logic while giving a
  semantically clearer, harder-to-misuse endpoint for the specific estimate-creation UI flow.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `CreateEstimateCommand` implements `IIdempotentRequest` with scope `pos.estimate` and `IdempotencyRequired=false` (P-117). There is no idempotency middleware.

> **REVISION NOTE (review v2):** the estimate number comes from the ESTIMATE series (`EST-<fy>`), see P-517 and DECISIONS D-07.

Add to `PosController.cs`:

```csharp
public record CreateEstimateBody(IReadOnlyList<CheckoutCartLine> Lines, Guid? CustomerId,
    string? WalkInCustomerName, decimal InvoiceDiscount, IReadOnlyList<CheckoutPayment> Payments,
    string? Description);

[HttpPost("estimate")]
public async Task<IActionResult> CreateEstimate(CreateEstimateBody body, CancellationToken ct)
{
    var command = new CheckoutCommand(body.Lines, body.CustomerId, body.WalkInCustomerName,
        body.InvoiceDiscount, StoreOS.Domain.Sales.InvoiceType.Estimate, body.Payments,
        Finance: null, body.Description);
    var result = await _mediator.Send(command, ct);
    return Created($"/api/sales/{result.SaleId}", new
    {
        saleId = result.SaleId, invoiceNumber = result.InvoiceNumber, grandTotal = result.GrandTotal
    });
}
```

Note `CreateEstimateBody` deliberately has NO `InvoiceType` field at all (it's implicit/forced
to `Estimate` by the action itself) and NO `Finance` field (financing an estimate that hasn't
been finalized doesn't make sense — force `Finance: null`). This is the key semantic value of
having a separate endpoint: it's structurally IMPOSSIBLE to accidentally create a financed
estimate or to forget to set the invoice type, compared to the general-purpose
`/pos/checkout` route.

`CreateEstimate` does NOT require an `Idempotency-Key` header (unlike `/pos/checkout`) since
creating a duplicate estimate is low-stakes (no stock/payment consequences) — but for
consistency and to prevent duplicate estimate spam from a flaky client retry, still ACCEPT an
optional `Idempotency-Key` if provided (the general `IdempotencyBehavior` from P-117 already
activates automatically whenever the header IS present, on ANY POST route, so no extra code is
needed here — just don't make it MANDATORY like checkout does).

## 4. TASK

1. Add `CreateEstimateBody` and the `POST /api/pos/estimate` action to
   `PosController.cs` exactly as shown.
2. Write `StoreOS.IntegrationTests/Sales/CreateEstimateEndpointTests.cs`: (a) POST without an
   `Idempotency-Key` header still succeeds (201) — proving it's optional here, unlike checkout;
   (b) the resulting sale has `invoice_type='Estimate'` regardless of any attempt to smuggle a
   different type (there's no field for it in `CreateEstimateBody` at all, so this is
   structurally guaranteed — the test just confirms the DB row is correct); (c) stock is
   unaffected (reusing P-517's proven bypass); (d) providing an `Idempotency-Key` still works
   correctly (replay-on-same-key-and-payload, 409-on-different-payload), proving the general
   middleware activates here too even though it's optional.

## 5. HARD CONSTRAINTS

- `CreateEstimateBody` must have NO field for `InvoiceType` or
  `Finance` — the whole point of this endpoint is to make those two mistakes structurally
  impossible, not just validated away.
- `Idempotency-Key` is OPTIONAL on this route (contrast with `/pos/checkout`'s MANDATORY
  requirement from P-513) — do not add a required-header check here.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter CreateEstimateEndpointTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-518 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-518 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-518` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `CreateEstimateBody` shape and the `POST /api/pos/estimate` route,
noting its optional (not mandatory) Idempotency-Key requirement.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-518: <one-line summary>"` only after verification is
   fully green.
