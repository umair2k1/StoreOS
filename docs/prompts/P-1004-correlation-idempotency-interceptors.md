# P-1004 — Correlation-id + idempotency-key interceptors

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1004** of the StoreOS Cloud build ("Correlation-id + idempotency-key interceptors") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1003 complete: silent refresh on bootstrap works correctly.
- No correlation-id or idempotency-key interceptors exist in the frontend yet (Plan Section
  6.7/6.4's client-side counterparts).

## 3. CONTEXT BLOCK

Create `frontend/src/app/core/interceptors/correlation-id.interceptor.ts`:

```typescript
import { HttpInterceptorFn } from '@angular/common/http';
import { tap } from 'rxjs';

function generateUuid(): string {
  return crypto.randomUUID();
}

export const correlationIdInterceptor: HttpInterceptorFn = (req, next) => {
  const correlationId = generateUuid();
  const clonedReq = req.clone({ setHeaders: { 'X-Correlation-ID': correlationId } });
  return next(clonedReq).pipe(
    tap({
      error: (err) => {
        // Attach the correlation id to the error object so error-toast/problem-details UI
        // (built in later Phase 11 prompts) can surface it to the user for support purposes.
        if (err && typeof err === 'object') (err as any).correlationId = correlationId;
      }
    })
  );
};
```

Create `frontend/src/app/core/interceptors/idempotency.interceptor.ts` - this one is
SELECTIVE: it must NOT add a key to every request, only to the SPECIFIC mutation endpoints
that need one (checkout, payments, estimate conversion, repair billing, tenant import) per each
backend prompt's documented requirement. Implement via a small allowlist of URL patterns:

```typescript
import { HttpInterceptorFn } from '@angular/common/http';

const IDEMPOTENT_MUTATION_PATTERNS: RegExp[] = [
  /\/pos\/checkout$/,
  /\/pos\/estimate\/[^/]+\/convert$/,
  /\/sales\/[^/]+\/payments$/,
  /\/repair-jobs\/[^/]+\/bill$/,
  /\/tenant\/import$/,
];

export const idempotencyInterceptor: HttpInterceptorFn = (req, next) => {
  if (req.method !== 'POST' || !IDEMPOTENT_MUTATION_PATTERNS.some(p => p.test(req.url))) {
    return next(req);
  }
  const key = crypto.randomUUID();
  const clonedReq = req.clone({ setHeaders: { 'Idempotency-Key': key } });
  return next(clonedReq);
};
```

Register both interceptors (alongside `authInterceptor` from P-1002) in `app.config.ts`'s
`provideHttpClient(withInterceptors([...]))` call, in a sensible order: correlation-id and
idempotency headers should be attached BEFORE the auth interceptor potentially retries a
request (so a retried request keeps the SAME idempotency key but gets a FRESH correlation id
per attempt - order the interceptor array so `idempotencyInterceptor` runs before
`authInterceptor`'s retry logic re-enters the chain, while `correlationIdInterceptor` generates
a new id on every actual HTTP send including retries).

## 4. TASK

1. Create both interceptors exactly as shown in
   `frontend/src/app/core/interceptors/`.
2. Register both (plus the existing `authInterceptor`) in `app.config.ts` in the order
   described.
3. Write `correlation-id.interceptor.spec.ts`: every outgoing request gets a fresh, valid UUID
   `X-Correlation-ID` header; an error response has the correlation id attached to the thrown
   error object.
4. Write `idempotency.interceptor.spec.ts`: a POST to `/api/pos/checkout` gets an
   `Idempotency-Key` header; a POST to an UNLISTED endpoint (e.g. `/api/products`) does NOT get
   one; a GET request never gets one regardless of URL.

## 5. HARD CONSTRAINTS

- The idempotency interceptor's URL allowlist is INTENTIONALLY narrow, matching
  exactly the endpoints the backend prompts documented as requiring (or optionally accepting) an
  Idempotency-Key - do not broaden it to "every POST" (many POSTs, like simple CRUD creates for
  products/customers/etc., have no backend-side idempotency handling expectation tied to a
  frontend-generated key in this manifest's design).

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
cd store-os-cloud/frontend
ng test --include='**/correlation-id.interceptor.spec.ts' --include='**/idempotency.interceptor.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1004 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1004 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1004` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: both interceptor implementations, the exact URL allowlist for idempotency
keys, and the registration order in `app.config.ts`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1004: <one-line summary>"` only after verification is
   fully green.
