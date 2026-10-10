using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Suppliers.CreateReturn;

using StoreOS.Application.Abstractions;
using StoreOS.Application.Suppliers.GetReturnById;
using StoreOS.Infrastructure.Printing;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/supplier-returns")]
public class SupplierReturnsController : ControllerBase
{
    private readonly IMediator _mediator;

    public SupplierReturnsController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] CreateSupplierReturnCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}/note.pdf")]
    public async Task<IActionResult> GetReturnNotePdf(
        Guid id,
        [FromServices] SupplierReturnPdfRenderer renderer,
        [FromServices] IAppDbContext db,
        [FromServices] IObjectStorageService storage,
        [FromServices] IAccessScope scope,
        CancellationToken ct)
    {
        var ret = await _mediator.Send(new GetSupplierReturnByIdQuery(id), ct);
        var dto = await MapToSupplierReturnPdfDto(ret, db, storage, scope.TenantId, ct);
        var bytes = renderer.Render(dto);
        return File(bytes, "application/pdf", $"return-{ret.ReturnNumber}.pdf");
    }

    private static async Task<SupplierReturnPdfDto> MapToSupplierReturnPdfDto(
        SupplierReturnDto ret,
        IAppDbContext db,
        IObjectStorageService storage,
        Guid tenantId,
        CancellationToken ct)
    {
        var branding = await ShopBrandingHelper.ResolveAsync(ret.BranchId, db, ct, storage, tenantId);

        var lines = ret.Items.Select(i => new SupplierReturnLineDto(
            ItemName: i.ItemName,
            Qty: i.Qty,
            ReturnRate: i.ReturnRate,
            LineTotal: i.LineTotal,
            ImeiNumbers: i.ImeiNumbers
        )).ToList();

        return new SupplierReturnPdfDto(
            ShopName: branding.ShopName,
            ReturnNumber: ret.ReturnNumber,
            ReturnDate: ret.ReturnDate,
            SupplierName: ret.SupplierName,
            SupplierPhone: ret.SupplierPhone,
            SupplierAddress: ret.SupplierAddress,
            Lines: lines,
            TotalAmount: ret.TotalAmount,
            SettlementType: ret.SettlementType,
            Reason: ret.Reason,
            LogoBytes: branding.LogoBytes,
            ShopAddress: branding.Address,
            ShopPhone: branding.Phone
        );
    }
}
