# P-607 — supplier_returns + supplier_return_items entities/migration

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-607** of the StoreOS Cloud build ("supplier_returns + supplier_return_items entities/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-606 complete: supplier transactions/payable fully work.
- No supplier-return schema exists yet (Requirements Section 14).

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.7):

```sql
CREATE TABLE supplier_returns (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  return_number text NOT NULL,
  return_date date NOT NULL,
  supplier_id uuid NOT NULL,
  supplier_phone text, supplier_address text,
  total_qty int NOT NULL, total_amount numeric(18,2) NOT NULL,
  settlement_type text NOT NULL CHECK (settlement_type IN ('PayableDeduction','CashRefund','PendingCredit')),
  reason text, notes text,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, return_number)
);

CREATE TABLE supplier_return_items (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  return_id uuid NOT NULL REFERENCES supplier_returns(id),
  product_id uuid NULL,
  item_name text NOT NULL,
  qty int NOT NULL CHECK (qty > 0),
  purchase_price numeric(18,2) NOT NULL,
  return_rate numeric(18,2) NOT NULL,
  line_total numeric(18,2) NOT NULL,
  imei_numbers text[]
);
```

Create `backend/src/StoreOS.Domain/Suppliers/SettlementType.cs`
(`PayableDeduction`/`CashRefund`/`PendingCredit` + `All`).

Create `backend/src/StoreOS.Domain/Suppliers/SupplierReturn.cs` (aggregate root with a private
`List<SupplierReturnItem>` backing field) and `SupplierReturnItem.cs`, following the same
established style as `Sale`/`SaleItem` (P-502) — a `Create(...)` header factory,
`AddItem(...)` method. Generate the `ReturnNumber` using a SIMPLE per-branch daily/sequential
scheme reusing `InvoiceSequenceAllocator` (P-501) with a DIFFERENT series-key namespace prefix
(e.g. `seriesKey = $"RETURN-{branchCode}-{businessDate:yyyyMMdd}"`), formatted as
`RET-<BRANCH>-YYYYMMDD-XXX` via a small dedicated formatter function (do not reuse
`InvoiceNumberFormatter`'s FY-consecutive logic, which is invoice-specific; write a tiny
parallel formatter `SupplierReturnNumberFormatter.Format(branchCode, date, seq)` in
`StoreOS.Domain/Suppliers/`).

Apply the Archetype B RLS policy to both tables (branch-scoped, since a return happens at a
specific branch).

## 4. TASK

1. Create `SettlementType.cs`, `SupplierReturn.cs`, `SupplierReturnItem.cs`,
   `SupplierReturnNumberFormatter.cs` in `StoreOS.Domain/Suppliers/`, following the
   Sale/SaleItem aggregate-root style.
2. Create EF configurations: CHECK on `settlement_type` (raw SQL), CHECK `qty > 0` on
   return items (raw SQL), `UNIQUE(tenant_id, return_number)`, `text[]` mapping for
   `imei_numbers` (Npgsql supports native array columns — use
   `.HasColumnType("text[]")` with a `string[]` CLR property).
3. Add both `DbSet<>`s to `StoreOsDbContext`/`QueryFilters.ApplyAll` (branch-scoped).
4. Generate migration `024_SupplierReturns` including RLS, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Suppliers/SupplierReturnTests.cs`: valid creation with multiple
   line items; `AddItem` accumulates correctly; an invalid settlement type throws; zero/negative
   quantity on a line item throws. Write
   `StoreOS.Domain.Tests/Suppliers/SupplierReturnNumberFormatterTests.cs`: format produces the
   expected `RET-<BRANCH>-YYYYMMDD-XXX` shape for a known input.

## 5. HARD CONSTRAINTS

- `SupplierReturnNumberFormatter` reuses `InvoiceSequenceAllocator`
  (P-501) for the actual NUMBER allocation (with a distinct series-key namespace so it never
  collides with invoice numbering), but has its OWN, separate string-formatting function — do
  not conflate the two number formats.

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
dotnet ef migrations add 024_SupplierReturns --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter SupplierReturnTests
dotnet test backend/tests/StoreOS.Domain.Tests --filter SupplierReturnNumberFormatterTests
dotnet test backend/tests/StoreOS.IntegrationTests --filter RlsCoverageTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-607 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-607 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-607` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `SupplierReturn`/`SupplierReturnItem`/`SettlementType` signatures, the
return-number format, and migration name `024_SupplierReturns`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-607: <one-line summary>"` only after verification is
   fully green.
