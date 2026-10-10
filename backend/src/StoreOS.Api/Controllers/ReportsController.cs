using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Reporting.Compare;
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

    [HttpGet("daily-sales.pdf")]
    public async Task<IActionResult> GetDailySalesPdf(
        [FromQuery] DateOnly? date,
        [FromQuery] Guid[]? branchIds,
        [FromServices] StoreOS.Infrastructure.Printing.ReportPdfRenderer renderer,
        [FromServices] StoreOS.Application.Abstractions.IAppDbContext db,
        [FromServices] StoreOS.Application.Abstractions.IObjectStorageService storage,
        [FromServices] StoreOS.Application.Abstractions.IAccessScope scope,
        CancellationToken ct)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.Send(new GetDailySalesReportQuery(targetDate, branchIds), ct);
        var targetBranchId = branchIds != null && branchIds.Length > 0 ? branchIds[0] : (Guid?)null;
        var branding = await ShopBrandingHelper.ResolveAsync(targetBranchId, db, ct, storage, scope.TenantId);
        var bytes = renderer.RenderDailySales(result, branding);
        return File(bytes, "application/pdf", $"daily-sales-{targetDate:yyyyMMdd}.pdf");
    }

    [HttpGet("compare")]
    public async Task<IActionResult> GetBranchComparison(
        [FromQuery] DateOnly? date,
        CancellationToken ct)
    {
        var targetDate = date ?? DateOnly.FromDateTime(DateTime.UtcNow);
        var result = await _mediator.Send(new GetBranchComparisonQuery(targetDate), ct);
        return Ok(result);
    }
}
