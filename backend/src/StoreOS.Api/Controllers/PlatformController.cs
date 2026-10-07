using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Behaviors;
using StoreOS.Application.Platform.BackupExport;
using StoreOS.Application.Platform.BackupImport;

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
}
