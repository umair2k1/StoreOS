using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.GetSaleById;

public record GetSaleByIdQuery(Guid Id) : IRequest<SaleDetailDto>, IReadOnlyRequest;

public record SaleLineDto(
    Guid Id,
    string ItemName,
    string Category,
    int Qty,
    decimal PricePerUnit,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    bool IsMarginScheme,
    bool IsGift,
    string? Imei,
    string? HsnSnapshot);

public record PaymentRecordDto(
    Guid Id,
    decimal Amount,
    string PaymentMode,
    DateOnly PaymentDate);

public record SaleDetailDto(
    Guid Id,
    string InvoiceNumber,
    string InvoiceType,
    DateTimeOffset SaleDate,
    Guid? CustomerId,
    string CustomerName,
    string? CustomerGstin,
    decimal TotalTaxable,
    decimal TotalCgst,
    decimal TotalSgst,
    decimal GrandTotal,
    decimal Discount,
    decimal AmountPaid,
    decimal Balance,
    string Status,
    string? PaymentMode,
    string? FinanceProvider,
    string? FinancerName,
    decimal? DownPayment,
    string? EmiDetailsJson,
    string? GiftDetailsJson,
    string? Description,
    Guid? RepairJobId,
    Guid? ConvertedFromEstimateId,
    Guid? LinkedTaxInvoiceId,
    IReadOnlyList<SaleLineDto> Items,
    IReadOnlyList<PaymentRecordDto> Payments);

public class GetSaleByIdHandler : IRequestHandler<GetSaleByIdQuery, SaleDetailDto>
{
    private readonly IAppDbContext _db;

    public GetSaleByIdHandler(IAppDbContext db)
    {
        _db = db;
    }

    public async Task<SaleDetailDto> Handle(GetSaleByIdQuery request, CancellationToken ct)
    {
        var sale = await _db.Set<Sale>()
            .Include(s => s.Items)
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Id == request.Id, ct);

        if (sale is null)
        {
            throw new DomainValidationException("sale_not_found", $"Sale with id '{request.Id}' was not found.");
        }

        var payments = await _db.Set<CustomerPayment>()
            .AsNoTracking()
            .Where(p => p.SaleId == request.Id)
            .OrderBy(p => p.CreatedAt)
            .Select(p => new PaymentRecordDto(p.Id, p.Amount, p.PaymentMode, p.PaymentDate))
            .ToListAsync(ct);

        var items = sale.Items.Select(i => new SaleLineDto(
            i.Id,
            i.ItemName,
            i.Category,
            i.Qty,
            i.PricePerUnit,
            i.Taxable,
            i.Cgst,
            i.Sgst,
            i.IsMarginScheme,
            i.IsGift,
            i.Imei,
            i.HsnSnapshot
        )).ToList();

        return new SaleDetailDto(
            sale.Id,
            sale.InvoiceNumber,
            sale.InvoiceType,
            sale.SaleDate,
            sale.CustomerId,
            sale.CustomerName ?? "",
            sale.CustomerGstin,
            sale.TotalTaxable,
            sale.TotalCgst,
            sale.TotalSgst,
            sale.GrandTotal,
            sale.Discount,
            sale.AmountPaid,
            sale.GrandTotal - sale.AmountPaid,
            sale.Status,
            sale.PaymentMode,
            sale.FinanceProvider,
            sale.FinancerName,
            sale.DownPayment,
            sale.EmiDetailsJson,
            sale.GiftDetailsJson,
            sale.Description,
            sale.RepairJobId,
            sale.ConvertedFromEstimateId,
            sale.LinkedTaxInvoiceId,
            items,
            payments
        );
    }
}
