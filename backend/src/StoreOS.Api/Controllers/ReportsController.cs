using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Reporting.Dashboard;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/reports")]
public class ReportsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ReportsController(IMediator mediator) => _mediator = mediator;

    [HttpGet("dashboard")]
    public async Task<IActionResult> GetDashboard([FromQuery] Guid[]? branchIds, CancellationToken ct)
    {
        var result = await _mediator.Send(new GetDashboardQuery(branchIds), ct);
        return Ok(result);
    }
}
