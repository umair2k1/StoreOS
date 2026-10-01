# P-702 — repair_jobs entity/migration + job number sequence (REP-YYYYMMDD-XXXX)

**Phase:** 7 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-702** of the StoreOS Cloud build ("repair_jobs entity/migration + job number sequence (REP-YYYYMMDD-XXXX)") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-701 complete: repair services (as `products` rows) fully work.
- No `repair_jobs` table exists yet — this IS a genuinely separate, dedicated table per Plan
  Section 8.8 (unlike repair SERVICES, repair JOBS/tickets are a distinct workflow entity with
  no equivalent elsewhere in the schema).

## 3. CONTEXT BLOCK

Target DDL (Plan Section 8.8, core columns — parts/status-history are
separate tables built in P-704/P-705):

```sql
CREATE TABLE repair_jobs (
  id uuid PRIMARY KEY,
  tenant_id uuid NOT NULL,
  branch_id uuid NOT NULL REFERENCES branches(id),
  job_number text NOT NULL,
  customer_id uuid NULL,
  customer_name text, customer_phone text, customer_address text,
  device_brand text, device_model text, device_type text,
  serial_number text, imei1 text, imei2 text,
  device_condition text, accessories_received text,
  problem_description text, customer_notes text,
  diagnosis text, technician_notes text, internal_notes text, technician text,
  estimated_cost numeric(18,2), approved_cost numeric(18,2),
  parts_cost numeric(18,2), labor_charge numeric(18,2),
  discount numeric(18,2), tax numeric(18,2), final_total numeric(18,2),
  advance_paid numeric(18,2), balance_due numeric(18,2),
  status text NOT NULL CHECK (status IN
    ('Received','Diagnosing','WaitingForApproval','WaitingForParts',
     'InRepair','ReadyForPickup','Delivered','Cancelled')),
  received_at timestamptz, expected_completion timestamptz,
  completed_at timestamptz, delivered_at timestamptz,
  warranty_until date,
  approval_state text, approval_timestamp timestamptz,
  cancellation_reason text,
  invoice_number text, sale_id uuid NULL,
  created_at timestamptz NOT NULL DEFAULT now(),
  updated_at timestamptz NOT NULL DEFAULT now(),
  UNIQUE (tenant_id, job_number)
);
CREATE INDEX ix_repair_jobs_tenant_branch_status ON repair_jobs (tenant_id, branch_id, status);
CREATE INDEX ix_repair_jobs_tenant_branch_customer ON repair_jobs (tenant_id, branch_id, customer_id);
```

Create `backend/src/StoreOS.Domain/Repairs/RepairStatus.cs` with the 8 constants (`Received`,
`Diagnosing`, `WaitingForApproval`, `WaitingForParts`, `InRepair`, `ReadyForPickup`,
`Delivered`, `Cancelled`) + `All`, plus `IsActive(string status) => status != Delivered &&
status != Cancelled` (Requirements Section 22.2's exact active-repair definition).

Create `backend/src/StoreOS.Domain/Repairs/RepairJob.cs` — a substantial aggregate root; write
it yourself covering EVERY column above, following the established style (private setters,
static `Intake(...)` factory covering customer/device/intake fields with `Status =
RepairStatus.Received` and `ReceivedAt = DateTimeOffset.UtcNow` set automatically, plus
individual behavior methods: `RecordDiagnosis(diagnosis, technicianNotes, internalNotes,
technician)`, `SetCommercials(estimatedCost, approvedCost, partsCost, laborCharge, discount,
tax, finalTotal, advancePaid)` (computing `BalanceDue = finalTotal - advancePaid`
automatically), `RecordApproval(approvalState, timestamp)`, `SetExpectedCompletion(dateTime)`,
`Cancel(reason)` (guard: cannot cancel a `Delivered` job), `Deliver(deliveredAt,
warrantyUntil)` (guard: only from `ReadyForPickup`), `LinkToSale(saleId, invoiceNumber)`. Status
TRANSITIONS themselves (the actual `Status` field mutation with validation of legal
transitions) are handled by a SEPARATE method `TransitionTo(string newStatus)` built in P-704
alongside the status-history table — for THIS prompt, just expose `Status` with a private
setter and no public mutator yet (P-704 adds the guarded transition method).

Create `backend/src/StoreOS.Domain/Repairs/RepairJobNumberAllocator.cs` (mirrors
`InvoiceSequenceAllocator`'s pattern, reusing the SAME `invoice_sequences` table with a
DISTINCT series-key namespace, e.g. `seriesKey = $"REP-{branchCode}-{yyyyMMdd}"`) — actually,
simplify: REUSE `InvoiceSequenceAllocator` directly (P-501) rather than writing a parallel
allocator class, since the underlying mechanism (atomic upsert into `invoice_sequences`) is
identical; only the series-key STRING and the output FORMAT differ. Create a small
`RepairJobNumberFormatter.cs`:

```csharp
namespace StoreOS.Domain.Repairs;

public static class RepairJobNumberFormatter
{
    public static string Format(string branchCode, DateOnly date, int seq) =>
        $"REP-{branchCode}-{date:yyyyMMdd}-{seq:D4}";
}
```

matching Requirements Section 21.1's `REP-YYYYMMDD-XXXX` format extended with a branch code
prefix (consistent with how invoice numbers also carry a branch prefix per Plan Section 8.5),
"sequence increments per day" (Requirements Section 21.1) achieved by using
`$"REP-{branchCode}-{date:yyyyMMdd}"` as the `invoice_sequences.series_key`, resetting
naturally each day since a new date produces a new series key row.

Apply the Archetype B RLS policy to `repair_jobs`.

## 4. TASK

1. Create `RepairStatus.cs`, `RepairJob.cs` (write this substantial entity yourself,
   covering every DDL column with the described factory/behavior methods), and
   `RepairJobNumberFormatter.cs` in `StoreOS.Domain/Repairs/`.
2. Create the EF configuration: CHECK on `status` (raw SQL), `UNIQUE(tenant_id, job_number)`,
   both named indexes, all snake_case columns.
3. Add `DbSet<RepairJob> RepairJobs` to `StoreOsDbContext`/`QueryFilters.ApplyAll`
   (branch-scoped).
4. Generate migration `027_RepairJobs` including RLS, apply it.
5. Re-run `RlsCoverageTests`.
6. Write `StoreOS.Domain.Tests/Repairs/RepairJobTests.cs`: `Intake` sets `Status=Received`,
   `ReceivedAt` populated; `SetCommercials` correctly computes `BalanceDue`;
   `Cancel` on a `Delivered` job throws; `Deliver` from a non-`ReadyForPickup` status throws.
   `RepairJobNumberFormatterTests.cs`: format produces the exact expected string.
7. Write `StoreOS.ConcurrencyTests/RepairJobNumberConcurrencyTests.cs`: launch 20 concurrent
   job-intake operations on the SAME day/branch, assert all 20 `job_number` values are unique
   and correctly sequential (reusing `InvoiceSequenceAllocator`'s already-proven concurrency
   safety from P-501, now exercised through this new series-key namespace).

## 5. HARD CONSTRAINTS

- Reuse `InvoiceSequenceAllocator` (P-501) directly for repair job
  numbering — do not write a parallel, independent sequence-allocation mechanism; only the
  series-key string and output format differ.
- `RepairJob`'s `Status` field has NO public mutator in THIS prompt — P-704 adds the
  validated `TransitionTo` method alongside the status-history table, since status changes
  must be auditable there, not silently settable here.

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
dotnet ef migrations add 027_RepairJobs --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet ef database update --project backend/src/StoreOS.Infrastructure --startup-project backend/src/StoreOS.Api
dotnet test backend/tests/StoreOS.Domain.Tests --filter RepairJobTests
dotnet test backend/tests/StoreOS.Domain.Tests --filter RepairJobNumberFormatterTests
dotnet test backend/tests/StoreOS.ConcurrencyTests --filter RepairJobNumberConcurrencyTests
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
   `BLOCKED: P-702 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-702 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-702` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the complete `RepairJob`/`RepairStatus` signatures, the job-number format,
migration name `027_RepairJobs`, and confirmation `InvoiceSequenceAllocator` is reused (not
duplicated) for job numbering.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-702: <one-line summary>"` only after verification is
   fully green.
