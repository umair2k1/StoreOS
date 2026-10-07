using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Platform.BackupExport;

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
}
