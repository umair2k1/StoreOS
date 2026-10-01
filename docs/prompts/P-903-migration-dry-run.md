# P-903 — Desktop migration tool pz-migrate: schema detect + dry-run + reconciliation report

**Phase:** 9 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-903** of the StoreOS Cloud build ("Desktop migration tool pz-migrate: schema detect + dry-run + reconciliation report") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-902 complete: tenant-scoped restore fully works with round-trip
  proof and transactional safety.
- No desktop-to-cloud MIGRATION tool exists yet (Plan Section 17, Requirements Section 51's
  "Handover Interpretation" and the desktop app's own JSON backup format described in
  Appendix A/B of the Requirements document). This is a DIFFERENT scenario from P-901/902's
  tenant export/restore: migration CREATES a brand-new tenant from a desktop SQLite app's
  JSON backup, remapping ids, assigning a default branch, and stamping every record with a
  branch_id - it does not restore INTO an existing tenant.

## 3. CONTEXT BLOCK

Create a NEW console application project (per the master prompt's
Section 10 mandatory-coverage list: "Desktop migration import with desktop_id natural-key
dedup"). Add it to the solution:

```bash
dotnet new console -n pz-migrate -o tools/pz-migrate -f net8.0
dotnet sln StoreOS.sln add tools/pz-migrate/pz-migrate.csproj
dotnet add tools/pz-migrate reference backend/src/StoreOS.Domain
dotnet add tools/pz-migrate reference backend/src/StoreOS.Application
dotnet add tools/pz-migrate reference backend/src/StoreOS.Infrastructure
```

The desktop backup's assumed JSON shape (per Requirements Appendix B's table list -
`settings, items, purchases, sales, sale_items, customers, customer_payments, expenses,
suppliers, supplier_transactions, liabilities, liability_payments, inventory_imeis,
supplier_returns, supplier_return_items, repair_services, repair_jobs, repair_job_parts,
repair_job_status_history`) uses the DESKTOP schema's field names directly (e.g. `items` not
`products`, no `tenant_id`/`branch_id` columns at all since it was single-tenant/single-branch).
Define `tools/pz-migrate/DesktopBackupSchema.cs` with plain POCOs matching this legacy shape
EXACTLY as Requirements Section 29's table definitions describe (e.g.
`DesktopItem { Id, Name, Category, StockQty, PurchasePrice, SellPrice, GstRate, IsMarginScheme,
HsnCode, SellerName, SellerPhone, SellerAddress, SupplierName, SupplierPhone, SupplierAddress }`
matching Requirements Section 29.2 exactly).

Create `tools/pz-migrate/Program.cs` implementing a CLI with two commands:
`pz-migrate import --backup <path> --tenant-name <name> [--dry-run]` and
`pz-migrate reconcile-customers --tenant <id> [--dry-run]` (the latter built in P-904).

For THIS prompt, implement `import` with DRY-RUN mode only (actual committing import + dedup
logic is P-904): parse the desktop JSON backup file, detect its schema by checking for the
presence of expected top-level keys (reject with a clear error if the file doesn't look like a
recognized desktop backup shape), and produce a RECONCILIATION REPORT (Plan Section 17 step 4):
per-table record counts, sum of sale grand totals, sum of payments, sum of stock quantities,
active credit balances - computed from the SOURCE desktop file's data directly (no DB writes
yet in dry-run mode), printed to console AND written to a `reconciliation-report.json` file
next to the input backup.

```csharp
public record ReconciliationReport(
    Dictionary<string, int> RecordCountsByTable,
    decimal SumSaleGrandTotals, decimal SumPayments, int SumStockQty, decimal SumActiveCreditBalances);
```

## 4. TASK

1. Scaffold the `tools/pz-migrate` console project exactly as shown, add it to
   the solution with the three project references.
2. Create `DesktopBackupSchema.cs` with POCOs for every desktop table listed, matching
   Requirements Section 29's field lists exactly.
3. Implement `Program.cs`'s `import --dry-run` mode: parse the file, validate its shape, compute
   and output a `ReconciliationReport` (console + JSON file), WITHOUT touching any database.
4. Create a test fixture: `tools/pz-migrate/tests`... note there's no dedicated test project for
   this tool per the original manifest's Phase 0 scaffolding (only `StoreOS.Domain.Tests`/
   `StoreOS.IntegrationTests`/`StoreOS.ConcurrencyTests` exist under `backend/tests/`) - create
   a NEW test project now: `dotnet new xunit -n pz-migrate.Tests -o tools/pz-migrate.Tests -f
   net8.0`, add it to the solution, reference `tools/pz-migrate`.
5. Write `tools/pz-migrate.Tests/DryRunImportTests.cs`: (a) a well-formed sample desktop backup
   JSON fixture file (create a small, hand-crafted realistic fixture with a handful of items/
   sales/customers) produces a correct `ReconciliationReport` with exact hand-computed sums; (b)
   a malformed/unrecognized JSON file is rejected with a clear error message, producing NO
   report file; (c) dry-run mode makes ZERO database connections at all (verify by NOT even
   configuring a connection string in the test, confirming the tool doesn't need one for
   dry-run).

## 5. HARD CONSTRAINTS

- Dry-run mode NEVER touches any database - it operates purely on the input JSON
  file, producing a report for human review before any real import (P-904) is attempted.
- Create a genuine, realistic sample desktop-backup JSON fixture file for testing - do not
  test against an empty or trivial file only.

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
dotnet test tools/pz-migrate.Tests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-903 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-903 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-903` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `pz-migrate` project structure, the `DesktopBackupSchema` POCO
shapes, the `ReconciliationReport` shape, and the CLI command syntax
(`pz-migrate import --backup <path> --tenant-name <name> [--dry-run]`).
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-903: <one-line summary>"` only after verification is
   fully green.
