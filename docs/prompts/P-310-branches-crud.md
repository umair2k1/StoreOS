# P-310 — Branches CRUD endpoints

**Phase:** 3 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-310** of the StoreOS Cloud build ("Branches CRUD endpoints") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-309 complete: branch context resolution/enforcement works for every
  mutation.
- No REST endpoints exist for managing `Branch` entities themselves (creating a new branch,
  editing its profile) — only the `Branch` domain entity (P-102) exists.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** `StoreOS.Application` must not reference `StoreOS.Infrastructure`. Code in this prompt that lives in the Application layer uses `IAppDbContext` (use `_db.Set<T>()` for entities), `IJwtTokenService`, `IRefreshTokenService`, `RefreshTokenFormat` and `IInvoiceSequenceAllocator` from `StoreOS.Application.Abstractions` / `StoreOS.Application.Identity`. The concrete classes stay in Infrastructure. Definitions: `_shared/CONTRACTS.md` §2.

Create `backend/src/StoreOS.Application/Tenancy/Branches/CreateBranch/CreateBranchCommand.cs`:

```csharp
using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Tenancy.Branches.CreateBranch;

public record CreateBranchCommand(string Code, string Name, string? Address, string? Phone,
    string? Email, string? Gstin) : IRequest<Guid>, IHighImpactOperation
{
    public string RequiredPermission => "branches:manage";
}
```

`CreateBranchValidator.cs`: `Code` and `Name` `NotEmpty()`, `Code` must match `^[A-Z1-9][A-Z0-9]{0,2}$` (max 3 characters — this IS the
invoice-number budget from P-206: `<BRANCH>/<FY>/<NNNNNN>` is 16 characters only with a 3-character code); reject with a field error, not a 500, `Email` valid email format when provided.

`CreateBranchHandler.cs`:

```csharp
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Tenancy.Branches.CreateBranch;

public class CreateBranchHandler : IRequestHandler<CreateBranchCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    public CreateBranchHandler(IAppDbContext db, IAccessScope scope) { _db = db; _scope = scope; }

    public async Task<Guid> Handle(CreateBranchCommand request, CancellationToken ct)
    {
        var exists = await _db.Set<StoreOS.Domain.Tenancy.Branch>().AnyAsync(b => b.Code == request.Code, ct);
        if (exists) throw new DomainException("branch_code_taken", $"Branch code '{request.Code}' is already in use.");
        var branch = Branch.Create(_scope.TenantId, request.Code, request.Name);
        branch.UpdateProfile(request.Name, request.Address, request.Phone, request.Email, request.Gstin);
        await _db.Set<StoreOS.Domain.Tenancy.Branch>().AddAsync(branch, ct);
        await _db.SaveChangesAsync(ct);
        return branch.Id;
    }
}
```

Similarly create `GetBranches/GetBranchesQuery.cs` (`IRequest<IReadOnlyList<BranchDto>>`, no
special permission needed — every authenticated user can see the LIST of branches they have
access to, per `IAccessScope.BranchIds`/`AllBranches` via the existing EF query filter),
`UpdateBranch/UpdateBranchCommand.cs` (`IHighImpactOperation`, `branches:manage`, calls
`branch.UpdateProfile(...)` and optionally `Activate()`/`Deactivate()` based on an `IsActive`
field in the command).

`BranchDto`:
```csharp
public record BranchDto(Guid Id, string Code, string Name, string? Address, string? Phone,
    string? Email, string? Gstin, bool IsActive);
```

Create `backend/src/StoreOS.Api/Controllers/BranchesController.cs`:

```csharp
[ApiController]
[Route("api/branches")]
public class BranchesController : ControllerBase
{
    private readonly IMediator _mediator;
    public BranchesController(IMediator mediator) => _mediator = mediator;

    [HttpGet]
    public async Task<IActionResult> List(CancellationToken ct) => Ok(await _mediator.Send(new GetBranchesQuery(), ct));

    [HttpPost]
    public async Task<IActionResult> Create(CreateBranchCommand command, CancellationToken ct)
    {
        var id = await _mediator.Send(command, ct);
        return CreatedAtAction(nameof(GetById), new { id }, new { id });
    }

    [HttpGet("{id:guid}")]
    public async Task<IActionResult> GetById(Guid id, CancellationToken ct) =>
        Ok(await _mediator.Send(new GetBranchByIdQuery(id), ct));

    [HttpPatch("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, UpdateBranchBody body, CancellationToken ct)
    {
        await _mediator.Send(new UpdateBranchCommand(id, body.Name, body.Address, body.Phone,
            body.Email, body.Gstin, body.IsActive), ct);
        return NoContent();
    }
}

public record UpdateBranchBody(string Name, string? Address, string? Phone, string? Email, string? Gstin, bool IsActive);
```

## 4. TASK

1. Create the full `CreateBranch`, `GetBranches`, `GetBranchById`, `UpdateBranch`
   use-case folders under `StoreOS.Application/Tenancy/Branches/` following the exact vertical
   slice pattern shown (command/query + validator + handler per folder, per Plan §5.1's
   vertical-slice convention).
2. Create `BranchesController.cs` exactly as shown.
3. Write `StoreOS.IntegrationTests/Tenancy/BranchesControllerTests.cs`: (a) create a branch as
   a user WITH `branches:manage` -> 201, retrievable via GET; (b) create as a user WITHOUT that
   permission -> 403 (via the `LiveDbPermissionBehavior` from P-308, since this is
   `IHighImpactOperation`); (c) duplicate branch code -> 409 or 422 `branch_code_taken` (map
   this new domain code to 409 Conflict in `ExceptionHandlingMiddleware`, since it's a
   uniqueness conflict, not a generic validation failure); (d) `GET /api/branches` for a user
   scoped to ONE branch only returns just that branch, not all tenant branches (proving the EF
   query filter + RLS combo from earlier prompts correctly restricts branch visibility here
   too); (e) update a branch's profile fields -> reflected on subsequent GET; (f) deactivate a
   branch -> `IsActive=false` on subsequent GET, but the branch row is NOT deleted.

## 5. HARD CONSTRAINTS

- All four commands/queries use the vertical-slice folder
  convention (`StoreOS.Application/<Module>/<UseCase>/`) per Plan §5.1 — every later
  Application-layer prompt in this manifest must follow this exact folder shape, never the
  older horizontal `Commands/`/`Queries/`/`Handlers/` layout.
- `branch_code_taken` maps to 409 Conflict — add this mapping to
  `ExceptionHandlingMiddleware`'s switch now.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter BranchesControllerTests
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-310 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-310 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-310` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the four Branches use-case folder paths, the `BranchesController` routes
(`GET/POST /api/branches`, `GET/PATCH /api/branches/{id}`), the `BranchDto` shape, and the new
`branch_code_taken`->409 mapping.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-310: <one-line summary>"` only after verification is
   fully green.
