# P-605 — supplier_transactions + supplier_ledger_entries entities/migration

**Phase:** 6 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-605** of the StoreOS Cloud build ("supplier_transactions + supplier_ledger_entries entities/migration") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-604 complete: customer-side ledger/anonymization fully built.
- P-411 (minimal supplier master) exists. No supplier TRANSACTION/ledger tables exist yet —
  this prompt mirrors the customer-ledger pattern (P-506/507) for the supplier side.

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.7):

```sql
CREATE TABLE supplier_transactions (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  supplier_id uuid NOT NULL REFERENCES suppliers(id),
  branch_id uuid NULL REFERENCES branches(id),
  type text NOT NULL CHECK (type IN ('Bill','Payment','OpeningBalance')),
  amount numeric(18,2) NOT NULL CHECK (amount > 0),
  payment_mode text CHECK (payment_mode IN ('Cash','UPI','BankTransfer','Cheque','Credit')),
  reference_no text, txn_date date NOT NULL, notes text,
  created_by uuid NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now()
);

CREATE TABLE supplier_ledger_entries (
  id bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  tenant_id uuid NOT NULL,
  supplier_id uuid NOT NULL REFERENCES suppliers(id),
  branch_id uuid NULL,
  entry_type text NOT NULL,
  debit numeric(18,2) NOT NULL DEFAULT 0,
  credit numeric(18,2) NOT NULL DEFAULT 0,
  entry_date date NOT NULL,
  ref_id text,
  created_at timestamptz NOT NULL DEFAULT now()
);
```

Create `backend/src/StoreOS.Domain/Suppliers/SupplierTransactionType.cs`
(`Bill`/`Payment`/`OpeningBalance` + `All`), `SupplierPaymentMode.cs`
(`Cash`/`UPI`/`BankTransfer`/`Cheque`/`Credit` + `All` — a SEPARATE enum from
`StoreOS.Domain.Sales.PaymentMode` since the valid value sets differ, per Requirements Section
13.3 vs. Section 9.1), `SupplierLedgerEntryType.cs`
(`Bill`/`Payment`/`OpeningBalance`/`ReturnCredit` + `All`).

Create `backend/src/StoreOS.Domain/Suppliers/SupplierTransaction.cs` and
`SupplierLedgerEntry.cs` following the EXACT same immutable-entity/factory-method style as
`CustomerPayment` (P-508) and `CustomerLedgerEntry` (P-506) respectively — write these two
classes yourself, mirroring the customer-side pattern precisely: `SupplierTransaction` is
created via a `Create(...)` static factory validating `Amount > 0`, `Type` in the valid set,
`PaymentMode` in the valid set (when provided — nullable since `OpeningBalance`/`Bill` types
might not always specify a mode... actually per Requirements Section 13.3, payment mode is
listed as a transaction field generally; keep it nullable for `Bill`/`OpeningBalance` types
where a mode may not apply, required-ish for `Payment` type — validate conditionally: if
`Type == Payment`, `PaymentMode` must be non-null and valid); `SupplierLedgerEntry` mirrors
`CustomerLedgerEntry` with `CreateDebit`/`CreateCredit` factories.

Apply tenant-only RLS to `supplier_transactions` (it has `tenant_id`, and `branch_id` is
nullable/informational per Plan's "NULL = head office" note — use the nullable-branch query
filter variant, same reasoning as `expenses` will later need). Apply the SAME nullable-branch
variant to `supplier_ledger_entries`.

Retrofit the supplier PAYABLE calculation formula from Requirements Section 13.4:
```text
Payable = Opening Balance + Bills - Payments - Applicable Credits/Returns
```
This translates directly to `SUM(supplier_ledger_entries.credit) - SUM(supplier_ledger_entries.debit)`
if `Bill`/`OpeningBalance` are CREDITS (increasing what's owed) and `Payment`/`ReturnCredit`
are DEBITS (decreasing what's owed) — this is the OPPOSITE debit/credit polarity convention
from the customer ledger (where `Invoice` is a DEBIT increasing what the customer owes). Choose
and document this polarity explicitly and consistently: for `supplier_ledger_entries`,
`credit` increases the payable (bills, opening balance), `debit` decreases it (payments,
return credits) — the payable itself is `SUM(credit) - SUM(debit)`.

## 4. TASK

1. Create `SupplierTransactionType.cs`, `SupplierPaymentMode.cs`,
   `SupplierLedgerEntryType.cs`, `SupplierTransaction.cs`, `SupplierLedgerEntry.cs` (writing the
   latter two yourself, mirroring the customer-side pattern) in `StoreOS.Domain/Suppliers/`.
2. Create EF configurations: CHECK constraints (raw SQL) on `type`/`payment_mode` for
   `supplier_transactions`; nullable-branch query filter/RLS for both tables.
3. Add both `DbSet<>`s to `StoreOsDbContext`/`QueryFilters.ApplyAll`.
4. Generate migration `023_SupplierTransactions` including both RLS policies, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Suppliers/SupplierTransactionAndLedgerTests.cs`: valid creation
   of each transaction type; a `Payment` type WITHOUT a payment mode throws a validation error;
   `SupplierLedgerEntry.CreateDebit`/`CreateCredit` mutual exclusivity, matching the
   `CustomerLedgerEntry` test pattern from P-506.

## 5. HARD CONSTRAINTS

- Document the supplier-ledger debit/credit POLARITY explicitly
  (credit increases payable, debit decreases it) — this is the OPPOSITE convention from the
  customer ledger, and P-606's payable-calculation handler and P-608's supplier-return handler
  both depend on getting this polarity right.

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
dotnet ef migrations add 023_SupplierTransactions --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter SupplierTransactionAndLedgerTests
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
   `BLOCKED: P-605 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-605 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-605` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the five new domain type signatures, migration name
`023_SupplierTransactions`, and the EXPLICIT supplier-ledger debit/credit polarity convention
(credit=increases payable, debit=decreases payable).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-605: <one-line summary>"` only after verification is
   fully green.
