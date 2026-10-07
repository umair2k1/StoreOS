using Hangfire;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform.Reconciliation;
using StoreOS.Infrastructure.Jobs;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Behaviors;
using StoreOS.Application.Platform.BackupExport;
using StoreOS.Application.Platform.BackupImport;
using Microsoft.AspNetCore.Authorization;
using StoreOS.Application.Platform.LogoUpload;
namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/tenant")]
public class PlatformController : ControllerBase
{
    private readonly IMediator _mediator;

    public PlatformController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("export")]
    public async Task<ActionResult<TenantBackupDto>> Export(CancellationToken ct)
    {
        var result = await _mediator.Send(new ExportTenantDataCommand(), ct);
        return Ok(result);
    }

    [HttpPost("import")]
    public async Task<IActionResult> Import(
        [FromBody] TenantBackupDto backup,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return BadRequest(new
            {
                code = "idempotency_key_required",
                message = "POST /api/tenant/import requires an Idempotency-Key header."
            });
        }

        var command = new ImportTenantDataCommand(backup)
        {
            IdempotencyKey = idempotencyKey
        };

        var result = await _mediator.Send(command, ct);

        if (replayFlag.Replayed)
        {
            Response.Headers["Idempotency-Replayed"] = "true";
        }

        if (!result.Success)
        {
            return BadRequest(result);
        }

        return Ok(result);
    }

    [HttpPost("/api/platform/reconciliation/run-now")]
    public async Task<IActionResult> RunReconciliationNow(
        [FromServices] ICorrelationContextAccessor correlation,
        [FromServices] IBackgroundJobClient backgroundJobs,
        CancellationToken ct)
    {
        await _mediator.Send(new TriggerReconciliationCommand(), ct);
        var jobId = backgroundJobs.Enqueue<ReconciliationJob>(
            job => job.RunAsync(correlation.CorrelationId, CancellationToken.None));
        return Ok(new { jobId, correlationId = correlation.CorrelationId });
    }

    [HttpPost("/api/tenants/me/logo")]
    [Consumes("multipart/form-data")]
    public async Task<IActionResult> UploadLogo(IFormFile? file, CancellationToken ct)
    {
        if (file == null || file.Length == 0)
        {
            return BadRequest(new { code = "file_required", message = "A logo file is required." });
        }

        using var memoryStream = new MemoryStream();
        await file.CopyToAsync(memoryStream, ct);
        var fileBytes = memoryStream.ToArray();

        var result = await _mediator.Send(new UploadLogoCommand(fileBytes, file.ContentType), ct);
        return Ok(result);
    }

    [HttpGet("logo")]
    [HttpGet("logo.png")]
    [AllowAnonymous]
    [ResponseCache(Duration = 3600, VaryByHeader = "Host")]
    public async Task<IActionResult> GetLogo(
        [FromQuery] int size = 512,
        [FromQuery] Guid? tenantId = null,
        [FromServices] ITenantResolver resolver = null!,
        CancellationToken ct = default)
    {
        if (size != 192 && size != 512 && size != 1024)
        {
            return BadRequest(new { code = "invalid_size", message = "Size must be 192, 512, or 1024." });
        }

        TenantPublicProfile? tenant = null;
        if (tenantId.HasValue && tenantId.Value != Guid.Empty)
        {
            tenant = await resolver.ResolveByIdAsync(tenantId.Value, ct);
        }
        else if (Request.Host.HasValue)
        {
            tenant = await resolver.ResolveFromHostAsync(Request.Host.Value, ct);
        }

        if (tenant is null || !tenant.IsActive)
        {
            return NotFound();
        }

        var result = await _mediator.Send(new GetTenantLogoQuery(size, tenant.TenantId), ct);
        Response.Headers.CacheControl = "public, max-age=3600";
        Response.Headers.ETag = result.ETag;
        return File(result.Bytes, result.ContentType);
    }
}
