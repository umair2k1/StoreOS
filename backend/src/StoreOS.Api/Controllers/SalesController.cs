using MediatR;
using Microsoft.AspNetCore.Mvc;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Search;
using StoreOS.Application.Sales.GetSaleById;
using StoreOS.Application.Sales.VoidSale;
using StoreOS.Application.Sales.RefundSale;
using StoreOS.Application.Behaviors;
using StoreOS.Application.Sales.RecordPayment;

using StoreOS.Infrastructure.Printing;

namespace StoreOS.Api.Controllers;

[ApiController]
[Route("api/sales")]
public class SalesController : ControllerBase
{
    private readonly IMediator _mediator;

    public SalesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    public async Task<ActionResult<PagedResult<SaleSummaryDto>>> GetSales(
        [FromQuery] GetSalesQuery query,
        CancellationToken ct)
    {
        var result = await _mediator.Send(query, ct);
        return Ok(result);
    }

    [HttpGet("{id:guid}")]
    public async Task<ActionResult<SaleDetailDto>> GetSaleById(
        Guid id,
        CancellationToken ct)
    {
        var result = await _mediator.Send(new GetSaleByIdQuery(id), ct);
        return Ok(result);
    }
    [HttpGet("{id:guid}/invoice.pdf")]
    public async Task<IActionResult> GetInvoicePdf(
        Guid id,
        [FromServices] InvoicePdfRenderer renderer,
        [FromServices] IAppDbContext db,
        [FromServices] IObjectStorageService storage,
        [FromServices] IAccessScope scope,
        CancellationToken ct)
    {
        var detail = await _mediator.Send(new GetSaleByIdQuery(id), ct);
        var dto = await MapToInvoicePdfDto(detail, db, storage, scope.TenantId, ct);
        var bytes = renderer.Render(dto);
        return File(bytes, "application/pdf", $"{detail.InvoiceNumber}.pdf");
    }

    private static async Task<InvoicePdfDto> MapToInvoicePdfDto(
        SaleDetailDto detail,
        IAppDbContext db,
        IObjectStorageService storage,
        Guid tenantId,
        CancellationToken ct)
    {
        var branding = await ShopBrandingHelper.ResolveAsync(detail.BranchId, db, ct, storage, tenantId);

        var lines = detail.Items.Select(item => new InvoicePdfLineDto(
            ItemName: item.ItemName,
            Hsn: item.HsnSnapshot,
            Qty: item.Qty,
            PricePerUnit: item.PricePerUnit,
            Taxable: item.Taxable,
            Cgst: item.Cgst,
            Sgst: item.Sgst,
            Imei: item.Imei,
            IsGift: item.IsGift,
            IsMarginScheme: item.IsMarginScheme
        )).ToList();

        return new InvoicePdfDto(
            ShopName: branding.ShopName,
            ShopAddress: branding.Address,
            ShopPhone: branding.Phone,
            ShopEmail: branding.Email,
            ShopGstin: branding.Gstin,
            InvoiceNumber: detail.InvoiceNumber,
            InvoiceType: detail.InvoiceType,
            SaleDate: detail.SaleDate,
            CustomerName: string.IsNullOrWhiteSpace(detail.CustomerName) ? "Walk-in Customer" : detail.CustomerName,
            CustomerGstin: detail.CustomerGstin,
            Lines: lines,
            TotalTaxable: detail.TotalTaxable,
            TotalCgst: detail.TotalCgst,
            TotalSgst: detail.TotalSgst,
            Discount: detail.Discount,
            GrandTotal: detail.GrandTotal,
            AmountPaid: detail.AmountPaid,
            Balance: detail.Balance,
            PaymentMode: detail.PaymentMode,
            FinanceProvider: detail.FinanceProvider,
            FinancerName: detail.FinancerName,
            Description: detail.Description,
            IrnDisplay: null,
            IrnQrDataUrl: null,
            LogoBytes: branding.LogoBytes,
            CustomerPhone: null,
            DownPayment: detail.DownPayment,
            EmiDetails: detail.EmiDetailsJson
        );
    }


    public record RecordPaymentBody(decimal Amount, string PaymentMode);

    [HttpPost("{id:guid}/payments")]
    public async Task<ActionResult<RecordPaymentResult>> RecordPayment(
        Guid id,
        [FromBody] RecordPaymentBody body,
        [FromHeader(Name = "Idempotency-Key")] string? idempotencyKey,
        [FromServices] IdempotencyReplayFlag replayFlag,
        CancellationToken ct)
    {
        var command = new RecordPaymentCommand(id, body.Amount, body.PaymentMode)
        {
            IdempotencyKey = idempotencyKey
        };

        var result = await _mediator.Send(command, ct);

        if (replayFlag.Replayed)
        {
            Response.Headers["Idempotent-Replayed"] = "true";
        }

        return Ok(result);
    }

    public record VoidSaleBody(string Reason);

    [HttpPost("{id:guid}/void")]
    public async Task<IActionResult> VoidSale(
        Guid id,
        [FromBody] VoidSaleBody body,
        CancellationToken ct)
    {
        await _mediator.Send(new VoidSaleCommand(id, body.Reason), ct);
        return Ok();
    }

    public record RefundSaleBody(
        IReadOnlyList<Guid>? SaleItemIds,
        string Reason,
        decimal RefundAmount);

    [HttpPost("{id:guid}/refund")]
    public async Task<IActionResult> RefundSale(
        Guid id,
        [FromBody] RefundSaleBody body,
        CancellationToken ct)
    {
        await _mediator.Send(new RefundSaleCommand(id, body.SaleItemIds, body.Reason, body.RefundAmount), ct);
        return Ok();
    }
}
