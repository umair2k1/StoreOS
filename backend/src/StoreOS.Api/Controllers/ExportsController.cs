using System.Text;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Reporting.Gstr1Export;
using StoreOS.Application.Reporting.InventoryExport;
using StoreOS.Domain.Identity;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/exports")]
public class ExportsController : ControllerBase
{
    private readonly IMediator _mediator;
    private readonly IAccessScope _scope;

    public ExportsController(IMediator mediator, IAccessScope scope)
    {
        _mediator = mediator;
        _scope = scope;
    }

    [HttpGet("inventory")]
    public async Task<IActionResult> ExportInventory(
        [FromQuery] string[]? categories,
        [FromQuery] Guid[]? branchIds,
        [FromQuery] string? format = null,
        CancellationToken ct = default)
    {
        var cats = categories ?? Array.Empty<string>();
        var result = await _mediator.Send(new GetInventoryExportQuery(cats, branchIds), ct);

        bool wantsJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            || (format == null && Request.Headers.Accept.ToString().Contains("application/json"));

        if (wantsJson)
        {
            return Ok(result);
        }

        if (string.Equals(format, "pdf", StringComparison.OrdinalIgnoreCase))
        {
            var db = HttpContext.RequestServices.GetService(typeof(IAppDbContext)) as IAppDbContext;
            var storage = HttpContext.RequestServices.GetService(typeof(IObjectStorageService)) as IObjectStorageService;
            var renderer = HttpContext.RequestServices.GetService(typeof(StoreOS.Infrastructure.Printing.ReportPdfRenderer)) as StoreOS.Infrastructure.Printing.ReportPdfRenderer;

            var targetBranchId = branchIds != null && branchIds.Length > 0 ? branchIds[0] : (Guid?)null;
            var branding = db != null ? await ShopBrandingHelper.ResolveAsync(targetBranchId, db, ct, storage, _scope.TenantId) : null;
            var pdfBytes = (renderer ?? new StoreOS.Infrastructure.Printing.ReportPdfRenderer()).RenderInventoryExport(result, branding);
            return File(pdfBytes, "application/pdf", $"inventory-{DateTime.UtcNow:yyyyMMdd}.pdf");
        }

        bool canViewCost = _scope.HasPermission(PermissionCatalog.InventoryCostView);
        var csv = InventoryCsvWriter.ToCsv(result, canViewCost);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var data = encoding.GetBytes(csv);
        var bytes = new byte[preamble.Length + data.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(data, 0, bytes, preamble.Length, data.Length);
        return File(bytes, "text/csv; charset=utf-8", $"inventory-{DateTime.UtcNow:yyyyMMdd}.csv");
    }

    [HttpGet("gstr1")]
    public async Task<IActionResult> ExportGstr1(
        [FromQuery] DateOnly from,
        [FromQuery] DateOnly to,
        [FromQuery] string? format = null,
        CancellationToken ct = default)
    {
        var result = await _mediator.Send(new GetGstr1ExportQuery(from, to), ct);

        bool wantsJson = string.Equals(format, "json", StringComparison.OrdinalIgnoreCase)
            || (format == null && Request.Headers.Accept.ToString().Contains("application/json"));

        if (wantsJson)
        {
            return Ok(result);
        }

        var csv = Gstr1CsvWriter.ToCsv(result);
        var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: true);
        var preamble = encoding.GetPreamble();
        var data = encoding.GetBytes(csv);
        var bytes = new byte[preamble.Length + data.Length];
        Buffer.BlockCopy(preamble, 0, bytes, 0, preamble.Length);
        Buffer.BlockCopy(data, 0, bytes, preamble.Length, data.Length);
        return File(bytes, "text/csv; charset=utf-8", $"gstr1-{from:yyyyMMdd}-{to:yyyyMMdd}.csv");
    }
}
