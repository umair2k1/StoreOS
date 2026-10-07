using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Reporting.DailySales;
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

    [HttpGet("daily-sales")]
    public async Task<IActionResult> GetDailySales(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid[]? branchIds,
        CancellationToken ct)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.Send(new GetDailySalesReportQuery(targetDate, branchIds), ct);
        return Ok(result);
    }
}
