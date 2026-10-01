# P-519 — converted_from_estimate_id / linked_tax_invoice_id columns + migration

**Phase:** 5 &nbsp;|&nbsp; **Minimum executor reasoning level:** low

## 1. ROLE & MISSION

You are implementing **Prompt P-519** of the StoreOS Cloud build ("converted_from_estimate_id / linked_tax_invoice_id columns + migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-518 complete: dedicated estimate-creation endpoint exists.
- No bidirectional linkage columns exist yet for the estimate-to-invoice conversion workflow
  (Plan §10.7/Requirements §8.1's "Estimate to Invoice Conversion").

## 3. CONTEXT BLOCK

Migration addition to `sales`:

```sql
ALTER TABLE sales ADD COLUMN converted_from_estimate_id uuid NULL REFERENCES sales(id);
ALTER TABLE sales ADD COLUMN linked_tax_invoice_id uuid NULL REFERENCES sales(id);
```

`converted_from_estimate_id` is set on the NEW tax invoice row, pointing BACK at the original
estimate. `linked_tax_invoice_id` is set on the ORIGINAL estimate row, pointing FORWARD at the
new tax invoice. Both directions are stored explicitly (rather than relying on a reverse
lookup query) so BOTH "show me the estimate this invoice came from" and "show me what this
estimate became" are simple direct column reads, per Requirements §8.1 step 4's
"bidirectional reference."

Update `backend/src/StoreOS.Domain/Sales/Sale.cs`: add
`ConvertedFromEstimateId`/`LinkedTaxInvoiceId` properties and two methods:

```csharp
public void MarkConvertedFrom(Guid estimateId) => ConvertedFromEstimateId = estimateId;

public void MarkConvertedToTaxInvoice(Guid taxInvoiceId)
{
    if (InvoiceType != StoreOS.Domain.Sales.InvoiceType.Estimate)
        throw new StoreOS.Domain.Exceptions.DomainValidationException("not_an_estimate",
            "Only an Estimate can be marked as converted.");
    if (LinkedTaxInvoiceId is not null)
        throw new StoreOS.Domain.Exceptions.DomainValidationException("estimate_already_converted",
            "This estimate has already been converted to a tax invoice.");
    LinkedTaxInvoiceId = taxInvoiceId;
}
```

## 4. TASK

1. Create migration `022_EstimateConversion` (extends `sales`) adding both nullable
   self-referencing FK columns exactly as shown.
2. Extend `Sale.cs` with the two properties and two methods exactly as shown; update its EF
   configuration to map the new columns (snake_case, self-FK).
3. Apply the migration.
4. Write `StoreOS.Domain.Tests/Sales/EstimateConversionMarkersTests.cs`: `MarkConvertedFrom` on
   a tax invoice simply sets the field (no restriction on this side — any sale, estimate or
   not, could theoretically reference where it came from, though in practice only tax invoices
   use this); `MarkConvertedToTaxInvoice` on a non-Estimate sale throws `not_an_estimate`;
   calling it TWICE on the same estimate throws `estimate_already_converted` the second time.

## 5. HARD CONSTRAINTS

- `MarkConvertedToTaxInvoice`'s double-conversion guard
  (`estimate_already_converted`) is the domain-level enforcement that an estimate can only ever
  be converted ONCE — P-520's handler relies on this guard as a safety net in addition to its
  own idempotency-key-based duplicate-prevention.

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
dotnet ef migrations add 022_EstimateConversion --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter EstimateConversionMarkersTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-519 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-519 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-519` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the two new `Sale` columns/methods and migration name
`022_EstimateConversion`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-519: <one-line summary>"` only after verification is
   fully green.
