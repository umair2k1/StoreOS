# P-904 — pz-migrate record-level dedupe (sales/IMEIs/purchases/customers two-tier) + reconcile-customers CLI

**Phase:** 9 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-904** of the StoreOS Cloud build ("pz-migrate record-level dedupe (sales/IMEIs/purchases/customers two-tier) + reconcile-customers CLI") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-903 complete: dry-run import + reconciliation reporting work against
  a real desktop-backup JSON fixture.
- No REAL (committing) import or deduplication logic exists yet.

## 3. CONTEXT BLOCK

Implement the REAL, committing `import` mode (without `--dry-run`) in
`tools/pz-migrate/Program.cs`, following Plan Section 17's exact steps:

1. Create a new `Tenant` (via `Tenant.Create(name)`) and a default `Branch` (code `"B01"`,
   name `"Main Branch"`).
2. Remap every desktop entity's legacy integer/string id to a NEW `Guid`, maintaining an
   in-memory `Dictionary<string, Guid>` per table for FK remapping during insertion (e.g.
   desktop `items.id` -> new `Guid` `Product.Id`, referenced later by `purchases.item_id`).
3. Stamp EVERY imported record with the new `tenant_id` and the new default branch's `branch_id`
   for whichever tables need one (products themselves are tenant-wide with no branch_id per the
   cloud schema, but branch_inventory rows need creating per product with the item's desktop
   `stock_qty` becoming the INITIAL branch_inventory row for the new default branch).
4. Insert in DEPENDENCY ORDER (mirroring P-902's ordering): tenant/branch first, then
   products, then branch_inventory (derived from desktop stock_qty), then customers,
   suppliers, then purchases/sales/imeis/etc.

**Record-level deduplication** (Plan Section 8.10/Section 17.3 - THIS prompt's core new
capability): running `import` a SECOND time against an UPDATED desktop backup (the desktop app
remained in use as a pilot fallback) must not create duplicate records. Implement natural-key
dedup exactly per Plan Section 17.3:
- Sales: `(tenant_id, original_invoice_number)` - store the desktop's original invoice number in
  a NEW nullable `sales.original_invoice_number` column (add a small migration for this) so a
  re-run can match against it, distinct from the CLOUD-allocated `invoice_number` which follows
  the new FY-consecutive scheme.
- IMEIs: `(tenant_id, imei)`.
- Purchases: `(tenant_id, supplier_invoice_no, supplier_id, purchase_date)`.
- Customers - TWO-TIER: primary match on `(tenant_id, desktop_id)` (add the `customers
  .desktop_id` column now, per Plan Section 8.7's schema - a small migration), secondary
  fallback match on `(tenant_id, phone)` when `desktop_id` is absent on either side.

Before inserting each record type, QUERY for an existing match on its natural key; if found,
SKIP (do not re-insert, do not update either, per "successive imports... skip existing records"
- Requirements Section 41.3) UNLESS it's a customer matched by `desktop_id` where the PHONE
NUMBER differs from what's stored (Plan Section 17.3's "if a customer's phone number was edited
on the desktop app... updating the phone in cloud rather than creating a duplicate customer") -
in that ONE case, UPDATE the existing customer's phone via `UpdateProfile(...)` rather than
skip OR insert.

**Conflict logging**: if a `desktop_id` match's phone ALSO happens to collide with a DIFFERENT
existing customer's phone (an edge case), log this to `migration-conflicts.json` next to the
report and SKIP that specific record without aborting the whole batch (per Plan Section 17.3).

## 4. TASK

1. Implement the full committing `import` mode (no `--dry-run`) in
   `pz-migrate/Program.cs`, following the id-remapping, branch-stamping, and dependency-ordered
   insertion sequence exactly as described.
2. Add migrations `sales.original_invoice_number` (nullable) and `customers.desktop_id`
   (nullable, with an index `ix_customers_tenant_desktop_id WHERE desktop_id IS NOT NULL` per
   Plan Section 8.7) to the MAIN backend solution (these are real cloud-schema columns, not
   part of the `pz-migrate` tool itself) - use the next sequential migration name in your actual
   running ledger.
3. Implement the natural-key dedup logic for Sales/IMEIs/Purchases/Customers exactly as
   described, including the two-tier customer matching and phone-update-on-desktop_id-match
   case.
4. Implement conflict logging to `migration-conflicts.json` for the customer-phone-collision
   edge case.
5. Write `pz-migrate.Tests/CommittingImportTests.cs`: (a) a first import against the fixture
   backup creates the expected tenant/branch/products/customers/sales etc. with correct counts
   matching the P-903 dry-run report; (b) re-running import against the SAME UNCHANGED backup
   file a second time creates ZERO additional rows (full idempotent re-run); (c) re-running
   against an UPDATED backup (same fixture with one NEW sale added and one EXISTING customer's
   phone number changed) correctly imports ONLY the new sale and UPDATES the existing customer's
   phone (matched via `desktop_id`) rather than creating a duplicate customer; (d) a contrived
   phone-collision edge case is logged to `migration-conflicts.json` and skipped without
   aborting the rest of the batch.
6. Create the `reconcile-customers --tenant <id> [--dry-run]` CLI command: lists customers with
   phone divergences detected during import (reading from `migration-conflicts.json` or
   re-scanning), and in NON-dry-run mode, provides an interactive prompt (simple console
   read-line) to merge two customer records (consolidating their ledger entries onto one
   surviving customer id, deleting the duplicate). Write a basic test proving the merge
   correctly re-points `customer_ledger_entries`/`sales.customer_id` from the duplicate onto the
   surviving customer.

## 5. HARD CONSTRAINTS

- Dedup keys are EXACTLY as specified: Sales by
  `(tenant_id, original_invoice_number)`, IMEIs by `(tenant_id, imei)`, Purchases by
  `(tenant_id, supplier_invoice_no, supplier_id, purchase_date)`, Customers two-tier
  (`desktop_id` primary, `phone` fallback) - do not substitute different natural keys.
- A re-run against an unchanged backup must be a complete no-op (zero new rows) - this is the
  definitive proof of correct deduplication.
- A customer phone-number edit on the desktop between two pilot syncs must UPDATE the cloud
  customer's phone (matched by the STABLE `desktop_id`), never create a duplicate.

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
dotnet ef migrations add <NextSequentialName>_MigrationSupportColumns --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test tools/pz-migrate.Tests --filter CommittingImportTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-904 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-904 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-904` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the full committing-import sequence, the exact dedup keys for all four
entity types, the new `sales.original_invoice_number`/`customers.desktop_id` columns (with
migration name), and the `reconcile-customers` CLI command behavior.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-904: <one-line summary>"` only after verification is
   fully green.
