# P-1008 — Dynamic W3C manifest endpoint + Angular link wiring

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** med

## 1. ROLE & MISSION

You are implementing **Prompt P-1008** of the StoreOS Cloud build ("Dynamic W3C manifest endpoint + Angular link wiring") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1007 complete: onboarding wizard collects shop profile data.
- No dynamic PWA manifest exists yet - the static one from P-006's `ng add @angular/pwa`
  scaffold still serves generic StoreOS-looking defaults, violating the strict white-labeling
  requirement the moment a real tenant installs the PWA.

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the original text left `ResolveTenantFromHost` as an always-null placeholder (forbidden by the anti-stub rules) and accepted an anonymous `?tenantId=` override. Both are replaced by `ITenantResolver` from P-102. Local dev uses `http://apex.localhost:4200` (browsers resolve `*.localhost` to 127.0.0.1); the Angular dev-server proxy must forward `/api` with the ORIGINAL Host header (`changeOrigin: false`) so the backend sees the subdomain. The icon route `/api/tenant/logo.png` is served by P-1009 with the same Host-based resolution. Where the rest of this prompt still mentions a `tenantId` query parameter, ignore it and test with `Host: apex.localhost` instead.

Backend: create `backend/src/StoreOS.Api/Controllers/ManifestController.cs`:

```csharp
[ApiController]
public class ManifestController : ControllerBase
{
    private readonly ITenantResolver _resolver;
    public ManifestController(ITenantResolver resolver) => _resolver = resolver;

    // Tenant comes ONLY from the Host header (<subdomain>.pos.storeos.io or <subdomain>.localhost).
    // There is deliberately no ?tenantId= override: this endpoint is anonymous.
    [HttpGet("/api/tenant/manifest.webmanifest")]
    [AllowAnonymous]
    [ResponseCache(Duration = 300, VaryByHeader = "Host")]
    public async Task<IActionResult> GetManifest(CancellationToken ct)
    {
        var tenant = await _resolver.ResolveFromHostAsync(Request.Host.Value, ct);
        if (tenant is null || !tenant.IsActive) return NotFound();

        var shortName = tenant.Name.Length > 12 ? tenant.Name[..12] : tenant.Name;
        var manifest = new
        {
            id = "/", name = tenant.Name, short_name = shortName,
            start_url = "/", scope = "/", display = "standalone",
            theme_color = "#1e293b", background_color = "#0f172a",
            icons = new[]
            {
                new { src = "/api/tenant/logo.png?size=192", sizes = "192x192", type = "image/png", purpose = "any" },
                new { src = "/api/tenant/logo.png?size=512", sizes = "512x512", type = "image/png", purpose = "any" }
            }
        };
        return new JsonResult(manifest) { ContentType = "application/manifest+json" };
    }
}
```

Frontend: update `frontend/src/index.html`'s `<link rel="manifest" ...>` tag to point at the
dynamic backend endpoint instead of the static `manifest.webmanifest` file
`ng add @angular/pwa` created:
```html
<link rel="manifest" href="/api/tenant/manifest.webmanifest">
```
(In production this resolves via subdomain automatically since the API and app share an
origin behind the CDN per Plan Section 4; in local dev, the browser's fetch of this URL won't
have a `tenantId` query param attached automatically since `<link>` tags don't support dynamic
query strings - for LOCAL DEV specifically, note this limitation explicitly: the dynamic
manifest endpoint is FULLY FUNCTIONAL and correctly tested via direct HTTP calls, but wiring a
per-session dynamic query param onto a static HTML `<link>` tag requires either a build-time
templating step or a runtime `<link>` tag rewrite via JavaScript after login - implement the
LATTER: add a small script in `AuthSignalStore`'s `setAuthenticated` flow (or a dedicated
service) that, once `tenantId` is known post-login, updates the `<link rel="manifest">` tag's
`href` attribute via `document.querySelector('link[rel="manifest"]')?.setAttribute('href', ...)`
with the correct tenant-scoped URL).

## 4. TASK

1. Create `ManifestController.cs` exactly as shown (with the documented,
   honest limitation on subdomain resolution - implement the query-param path fully, leave the
   Host-header path as a real but currently-always-null placeholder ONLY because subdomain
   infrastructure doesn't exist in this schema yet, and document this clearly and specifically
   rather than silently deferring).
2. Update `index.html`'s manifest link tag.
3. Create `frontend/src/app/core/branding/dynamic-manifest.service.ts` that rewrites the
   `<link rel="manifest">` tag's href once the tenant id is known (call it from
   `AuthSignalStore.setAuthenticated` or a subscriber to its state).
4. Write `StoreOS.IntegrationTests/Platform/ManifestTests.cs`: (a) `GET
   /api/tenant/manifest.webmanifest?tenantId=<valid>` returns correct JSON with the tenant's
   actual name (never "StoreOS"), correct content type; (b) an invalid/nonexistent tenant id ->
   404; (c) confirm the response NEVER contains the literal string "StoreOS" anywhere in its
   body for a properly-named tenant.
5. Write `dynamic-manifest.service.spec.ts`: confirms the DOM's manifest link href updates
   correctly after `setAuthenticated` is called with a tenant id.

## 5. HARD CONSTRAINTS

- The manifest response must NEVER contain the literal string "StoreOS" for a
  properly onboarded tenant - test this explicitly, since it is the direct, testable proof of
  Requirements Section 3.5's white-labeling requirement at this specific integration point.
- The Host-header-based subdomain resolution path is HONESTLY documented as unimplemented
  pending real subdomain infrastructure (which is explicitly an IaC/deployment concern out of
  this manifest's scope per Plan Section 14's IaC notes flagged in the coverage checklist) -
  this is not a stub of BUSINESS LOGIC (the manifest generation itself is fully implemented and
  tested), it is an honestly-scoped limitation of an infrastructure dependency this prompt
  correctly cannot fabricate.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter ManifestTests
cd frontend
ng test --include='**/dynamic-manifest.service.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1008 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1008 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1008` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `ManifestController` route and behavior, the honest scoping note on
subdomain resolution, and the `DynamicManifestService` DOM-rewriting behavior.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1008: <one-line summary>"` only after verification is
   fully green.
