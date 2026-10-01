# P-1009 — Tenant logo upload (validation, ImageSharp resize) endpoint + Angular uploader

**Phase:** 10 &nbsp;|&nbsp; **Minimum executor reasoning level:** high

## 1. ROLE & MISSION

You are implementing **Prompt P-1009** of the StoreOS Cloud build ("Tenant logo upload (validation, ImageSharp resize) endpoint + Angular uploader") in the Oh My Pi IDE. You have no memory of any other prompt; everything you need is in this document.

## 2. WORLD STATE

- P-1008 complete: dynamic manifest works and is tenant-branded.
- No logo upload capability exists yet (Requirements Section 3.5's optional logo, Plan Section
  12.1 point 3's detailed validation/processing requirements).

## 3. CONTEXT BLOCK

> **REVISION NOTE (review v2):** the original prompt uploaded the logo but never recorded WHICH object is the current logo and never served it, although the manifest (P-1008), the login screen (P-1010) and the PDFs (P-806) all need it. Add: (1) after a successful upload, persist the object key in the tenant-level setting `branding.logo_key` (settings table, P-103; overwrite the previous value; the old object stays in storage); (2) a public endpoint `GET /api/tenant/logo?size=192|512|1024` (also reachable as `/api/tenant/logo.png?size=512`, `[AllowAnonymous]`): resolve the tenant from the Host header with `ITenantResolver` (P-102), run `GetTenantLogoQuery : IRequest<LogoImage>, IReadOnlyRequest, IAnonymousTenantRequest` to read `branding.logo_key`, fetch the object with `IObjectStorageService.GetAsync`, re-encode as PNG at the requested square size with ImageSharp (contain, transparent padding) and return it with `Cache-Control: public, max-age=3600` and an `ETag` (the key's hash). Only sizes 192, 512 and 1024 are accepted (400 otherwise) to avoid cache-busting abuse. (3) When the tenant has no logo return a solid `#1e293b` square PNG generated in memory at the requested size (NO vendor artwork, Plan §3.5). (4) Tests: upload then GET returns a PNG of the requested size; unknown Host => 404; tenant A's Host never returns tenant B's logo; no logo => fallback PNG.

Add `SixLabors.ImageSharp` (already pinned) to
`StoreOS.Infrastructure.csproj`.

Create `backend/src/StoreOS.Application/Platform/LogoUpload/UploadLogoCommand.cs`
(`IRequiresPermission`, `settings:manage`):

```csharp
public record UploadLogoCommand(byte[] FileBytes, string DeclaredContentType) : IRequest<UploadLogoResult>, IRequiresPermission
{
    public string RequiredPermission => "settings:manage";
}
public record UploadLogoResult(string LogoUrl);
```

`UploadLogoHandler.cs` implements EVERY validation rule from Plan Section 12.1 point 3
precisely:
1. **MIME allowlist**: only `image/png`, `image/jpeg`, `image/webp` accepted via
   `DeclaredContentType` - reject anything else (explicitly reject `image/svg+xml`, per the
   plan's explicit XSS-vector concern) with `DomainValidationException("logo_invalid_type", ...)`.
2. **Max size**: 2 MB - reject larger with `logo_too_large`.
3. **Magic-byte verification**: inspect the FIRST FEW BYTES of `FileBytes` against known
   signatures (PNG: `89 50 4E 47`; JPEG: `FF D8 FF`; WebP: `52 49 46 46` followed by `57 45 42
   50` at offset 8) - reject a mismatch (a file claiming `image/png` but with JPEG magic bytes,
   or ANY unrecognized header) with `logo_magic_bytes_mismatch`, even if the declared content
   type passed the allowlist check.
4. **Processing**: use ImageSharp to load the image, resize to max 512x512 (maintaining aspect
   ratio, per Plan's "max 512x512 icon"), strip EXIF metadata (`image.Metadata.ExifProfile =
   null;`), re-encode as WebP.
5. **Storage**: upload via `IObjectStorageService` (P-901) to
   `tenants/{tenantId}/branding/logo_{hash}.webp` (hash = SHA-256 of the processed bytes, for a
   content-addressed, cache-friendly filename), return a URL.

```csharp
public class UploadLogoHandler : IRequestHandler<UploadLogoCommand, UploadLogoResult>
{
    private static readonly Dictionary<string, byte[]> MagicBytes = new()
    {
        ["image/png"] = new byte[] { 0x89, 0x50, 0x4E, 0x47 },
        ["image/jpeg"] = new byte[] { 0xFF, 0xD8, 0xFF },
    };
    // WebP checked specially (RIFF....WEBP) due to its non-contiguous signature.

    public async Task<UploadLogoResult> Handle(UploadLogoCommand request, CancellationToken ct)
    {
        var allowedTypes = new[] { "image/png", "image/jpeg", "image/webp" };
        if (!allowedTypes.Contains(request.DeclaredContentType))
            throw new DomainValidationException("logo_invalid_type", "Only PNG, JPEG, and WebP images are allowed.");
        if (request.FileBytes.Length > 2 * 1024 * 1024)
            throw new DomainValidationException("logo_too_large", "Logo must be 2MB or smaller.");
        if (!MatchesMagicBytes(request.FileBytes, request.DeclaredContentType))
            throw new DomainValidationException("logo_magic_bytes_mismatch", "File content does not match its declared type.");

        using var image = SixLabors.ImageSharp.Image.Load(request.FileBytes);
        image.Metadata.ExifProfile = null;
        image.Mutate(x => x.Resize(new SixLabors.ImageSharp.Processing.ResizeOptions
        {
            Size = new SixLabors.ImageSharp.Size(512, 512),
            Mode = SixLabors.ImageSharp.Processing.ResizeMode.Max
        }));
        using var outputStream = new MemoryStream();
        await image.SaveAsWebpAsync(outputStream, ct);
        var processedBytes = outputStream.ToArray();
        var hash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(processedBytes))[..16].ToLowerInvariant();

        var key = $"tenants/{_scope.TenantId}/branding/logo_{hash}.webp";
        var url = await _storage.UploadAsync("storeos-media", key, processedBytes, "image/webp", ct);
        return new UploadLogoResult(url);
    }

    private static bool MatchesMagicBytes(byte[] bytes, string contentType)
    {
        if (contentType == "image/webp")
            return bytes.Length >= 12 && bytes[0]==0x52 && bytes[1]==0x49 && bytes[2]==0x46 && bytes[3]==0x46
                && bytes[8]==0x57 && bytes[9]==0x45 && bytes[10]==0x42 && bytes[11]==0x50;
        return MagicBytes.TryGetValue(contentType, out var sig) && bytes.Length >= sig.Length
            && bytes.Take(sig.Length).SequenceEqual(sig);
    }
}
```

Add `POST /api/tenants/me/logo` to `PlatformController.cs` (accepting `multipart/form-data`, per
Plan Section 12.1's endpoint listing), mapping the uploaded `IFormFile` to `FileBytes`/
`DeclaredContentType`.

Frontend: create `frontend/src/app/features/onboarding/logo-uploader.component.ts` - a file
input + preview, calling the endpoint via the generated client, displayed within the
onboarding wizard (P-1007) AND reusable later in the Settings screen (P-1120).

## 4. TASK

1. Add `SixLabors.ImageSharp` to `StoreOS.Infrastructure.csproj`.
2. Create `UploadLogoCommand.cs`/handler exactly as shown in
   `StoreOS.Application/Platform/LogoUpload/`, implementing all 5 validation/processing steps
   precisely.
3. Add `POST /api/tenants/me/logo` to `PlatformController.cs`.
4. Write `StoreOS.IntegrationTests/Platform/UploadLogoTests.cs`: (a) a valid small PNG succeeds,
   returns a URL, and the stored object (fetched back via `IAmazonS3`) is WebP-encoded, <=512px
   on its longest side, with EXIF stripped; (b) a file over 2MB -> `logo_too_large`; (c) a file
   claiming `image/png` but containing JPEG magic bytes -> `logo_magic_bytes_mismatch`; (d) a
   file with `Content-Type: image/svg+xml` -> `logo_invalid_type`, rejected outright regardless
   of its actual byte content (SVGs are never accepted, full stop); (e) a caller without
   `settings:manage` -> 403.
5. Create `LogoUploaderComponent` in the frontend, wire it into the onboarding wizard from
   P-1007.
6. Write `logo-uploader.component.spec.ts`: selecting a file and submitting calls the correct
   endpoint with multipart form data; a rejected upload (mocked 422 response) displays the
   error message to the user.

## 5. HARD CONSTRAINTS

- SVG uploads are REJECTED OUTRIGHT regardless of content, per Plan Section
  12.1's explicit stored-XSS concern - there is no sanitization path attempted, just an
  unconditional rejection of the SVG content type.
- Magic-byte verification happens even when the declared content type passed the allowlist
  check - a mismatched/spoofed content type is caught by this second, independent check.

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
dotnet test backend/tests/StoreOS.IntegrationTests --filter UploadLogoTests
cd frontend
ng test --include='**/logo-uploader.component.spec.ts' --watch=false
```

## 8. FAILURE PROTOCOL

If any build or test command in VERIFICATION fails:
1. You may attempt up to **3** fix iterations, confined strictly to files you created or
   modified in this prompt. Do not touch unrelated files.
2. Re-run the full VERIFICATION block after each fix attempt.
3. If still failing after 3 attempts, STOP immediately. Do not stub code to force a pass.
   Do not delete or weaken any test to make the build green. Instead, write as the first line below the `---`
   separator of `docs/BUILD_LOG.md` (see P-001 for the exact rule):
   `BLOCKED: P-1009 — <exact error message/stack trace>`
   and end your turn without committing.

## 9. REPORT

1. Append one line to `docs/BUILD_LOG.md`:
   `P-1009 | DONE | <list of files you created/modified>`
2. Append to `docs/ARTIFACTS.md`, under a new `## P-1009` heading, the exact signature of
   every new interface, class, entity, DB table/column, endpoint route, or Angular
   component/service you created, so that later prompts can quote them verbatim. At minimum
   include: the `UploadLogoCommand`/handler validation sequence, the
`POST /api/tenants/me/logo` route, and the `LogoUploaderComponent`.
3. If you touched migrations, record the exact migration name and the `dotnet ef` command
   used, so later prompts know the current migration HEAD.
4. Run `git add -A && git commit -m "P-1009: <one-line summary>"` only after verification is
   fully green.
