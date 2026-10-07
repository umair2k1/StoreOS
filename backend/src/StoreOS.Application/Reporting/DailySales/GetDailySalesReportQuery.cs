using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Reporting.DailySales;

public record GetDailySalesReportQuery(DateOnly Date, Guid[]? BranchIds = null)
    : IRequest<DailySalesReportDto>, IReportingRequest;

public record SalesSummaryDto(
    decimal Revenue,
    decimal BuyingCost,
    decimal Profit,
    decimal GstCollected,
    int InvoiceCount,
    int ItemsSold);

public record InventorySummaryDto(
    int TotalStockVolume,
    decimal StockWorthAtCost,
    decimal RetailValuation,
    int LowStockCount,
    int OutOfStockCount);

public record PaymentBreakdownDto(
    decimal Cash,
    decimal Upi,
    decimal Finance,
    decimal Other,
    decimal Total);

public record CategorySalesDto(
    string Category,
    decimal Revenue,
    int ItemsSold);

public record ItemizedSaleLineDto(
    string InvoiceNumber,
    DateOnly Date,
    string CustomerName,
    string ItemName,
    int Qty,
    decimal SellingValue,
    decimal CostValue,
    decimal Gst,
    string? Imei,
    string? PaymentMode);

public record DailySalesReportDto(
    DateOnly Date,
    SalesSummaryDto Sales,
    InventorySummaryDto Inventory,
    PaymentBreakdownDto Payments,
    IReadOnlyList<CategorySalesDto> ByCategory,
    IReadOnlyList<ItemizedSaleLineDto> ItemizedLog,
    decimal CollectedAmount,
    decimal OutstandingAmount);

public class GetDailySalesReportHandler : IRequestHandler<GetDailySalesReportQuery, DailySalesReportDto>
{
    private readonly IReportingDbContext _db;
    private readonly IAccessScope _scope;

    public GetDailySalesReportHandler(IReportingDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<DailySalesReportDto> Handle(GetDailySalesReportQuery request, CancellationToken ct)
    {
        var tenant = await _db.Set<Tenant>()
            .Where(t => t.Id == _scope.TenantId)
            .Select(t => new { t.Timezone })
            .FirstOrDefaultAsync(ct);

        var timezoneId = tenant?.Timezone ?? "Asia/Kolkata";
        if (!string.Equals(timezoneId, "Asia/Kolkata", StringComparison.OrdinalIgnoreCase))
        {
            throw new DomainValidationException(
                "unsupported_timezone",
                $"Tenant timezone '{timezoneId}' is not supported. Only 'Asia/Kolkata' is currently supported.");
        }

        var tz = ResolveTimeZone("Asia/Kolkata");

        // Compute start and end of requested Date in tenant's timezone (converted to UTC for DB queries)
        var startLocal = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startOffset = tz.GetUtcOffset(startLocal);
        var startUtc = new DateTimeOffset(startLocal, startOffset).ToUniversalTime();

        var endLocal = request.Date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endOffset = tz.GetUtcOffset(endLocal);
        var endUtc = new DateTimeOffset(endLocal, endOffset).ToUniversalTime();

        // 1. Sales on Date (Active TaxInvoices only, excluding Voided, Refunded, and Estimates)
        var salesQuery = _db.Set<Sale>().AsNoTracking()
            .Where(s => s.InvoiceType == InvoiceType.TaxInvoice
                     && s.InvoiceType != InvoiceType.Estimate
                     && s.Status == SaleStatus.Active
                     && s.Status != SaleStatus.Voided
                     && s.Status != SaleStatus.Refunded
                     && s.SaleDate >= startUtc
                     && s.SaleDate < endUtc);

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            salesQuery = salesQuery.Where(s => request.BranchIds.Contains(s.BranchId));
        }

        var sales = await salesQuery.ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToList();

        var linesQuery = _db.Set<SaleItem>().AsNoTracking()
            .Where(si => saleIds.Contains(si.SaleId));

        var lines = await linesQuery.ToListAsync(ct);

        var metrics = ReportAggregationHelpers.ComputeSalesMetrics(sales, lines);
        decimal revenue = metrics.Revenue;
        var salesSummary = new SalesSummaryDto(
            metrics.Revenue,
            metrics.BuyingCost,
            metrics.Profit,
            metrics.GstCollected,
            metrics.InvoiceCount,
            metrics.ItemsSold);

        // 2. Inventory Summary (as of report run-time, excluding RepairService)
        var invQuery = from bi in _db.Set<BranchInventory>().AsNoTracking()
                       join p in _db.Set<Product>().AsNoTracking() on bi.ProductId equals p.Id
                       where p.Category != ProductCategory.RepairService
                       select new
                       {
                           bi.BranchId,
                           p.Category,
                           bi.StockQty,
                           bi.CostPrice,
                           bi.SellPriceOverride,
                           ProductSellPrice = p.SellPrice,
                           bi.LowStockThreshold
                       };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            invQuery = invQuery.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        var invRows = await invQuery.ToListAsync(ct);

        var valuationLines = invRows.Select(x =>
        {
            var sellPrice = x.SellPriceOverride ?? x.ProductSellPrice;
            return new StockValuationLine(x.Category, x.StockQty, x.CostPrice, sellPrice);
        });

        var valResult = ValuationCalculator.Calculate(valuationLines);

        int totalStockVolume = invRows.Sum(x => Math.Max(0, x.StockQty));
        int lowStockCount = invRows.Count(x => x.StockQty > 0 && x.StockQty <= x.LowStockThreshold);
        int outOfStockCount = invRows.Count(x => x.StockQty <= 0);

        var invSummary = new InventorySummaryDto(
            TotalStockVolume: totalStockVolume,
            StockWorthAtCost: valResult.CostValuation,
            RetailValuation: valResult.RetailValuation,
            LowStockCount: lowStockCount,
            OutOfStockCount: outOfStockCount);

        // 3. Payment Breakdown & Dual Interpretation of Collected vs Outstanding:
        // - Collected amount: broader same-day collected figure across ALL customer payments recorded on this Date,
        //   regardless of when the original sale occurred (e.g. including installments on older sales).
        // - Outstanding amount: narrower, same-day-sale-specific figure = (Today's Sale Revenue) - (Payments for Today's Sales).
        var paymentsQuery = from cp in _db.Set<CustomerPayment>().AsNoTracking()
                            join s in _db.Set<Sale>().AsNoTracking() on cp.SaleId equals s.Id
                            where cp.PaymentDate == request.Date
                               && s.Status != SaleStatus.Voided
                            select new
                            {
                                s.BranchId,
                                cp.Amount,
                                cp.PaymentMode,
                                cp.SaleId
                            };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            paymentsQuery = paymentsQuery.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        var paymentsToday = await paymentsQuery.ToListAsync(ct);

        decimal cash = 0m, upi = 0m, finance = 0m, other = 0m;
        foreach (var p in paymentsToday)
        {
            var mode = p.PaymentMode.Trim();
            if (string.Equals(mode, PaymentMode.Cash, StringComparison.OrdinalIgnoreCase))
            {
                cash += p.Amount;
            }
            else if (string.Equals(mode, PaymentMode.UPI, StringComparison.OrdinalIgnoreCase))
            {
                upi += p.Amount;
            }
            else if (string.Equals(mode, PaymentMode.Finance, StringComparison.OrdinalIgnoreCase))
            {
                finance += p.Amount;
            }
            else
            {
                // All other modes (Card, Credit, etc.) bucket into Other per Section 24
                other += p.Amount;
            }
        }

        decimal totalPayments = cash + upi + finance + other;
        var paymentBreakdown = new PaymentBreakdownDto(cash, upi, finance, other, totalPayments);

        decimal collectedAmount = totalPayments;

        var todaySaleIdsSet = saleIds.ToHashSet();
        decimal collectedForTodaySales = paymentsToday
            .Where(p => todaySaleIdsSet.Contains(p.SaleId))
            .Sum(p => p.Amount);

        decimal outstandingAmount = Math.Max(0m, revenue - collectedForTodaySales);

        // 4. Sales by Category (at minimum all 4 standard categories, zero-filled if no sales)
        var standardCategories = new[]
        {
            ProductCategory.NewPhone,
            ProductCategory.UsedPhone,
            ProductCategory.Accessory,
            ProductCategory.RepairService
        };

        var categoryGroups = lines
            .GroupBy(l => l.Category)
            .ToDictionary(
                g => g.Key,
                g => new
                {
                    Revenue = g.Sum(l => l.Taxable + l.Cgst + l.Sgst),
                    ItemsSold = g.Where(l => !l.IsGift).Sum(l => l.Qty)
                });

        var allCategoryNames = standardCategories
            .Union(categoryGroups.Keys)
            .Distinct()
            .ToList();

        var byCategory = allCategoryNames.Select(cat =>
        {
            if (categoryGroups.TryGetValue(cat, out var stats))
            {
                return new CategorySalesDto(cat, stats.Revenue, stats.ItemsSold);
            }
            return new CategorySalesDto(cat, 0m, 0);
        }).ToList();

        // 5. Itemized Sales Log (audit detail for every sold line)
        var salesDict = sales.ToDictionary(s => s.Id);
        var itemizedLog = lines
            .OrderBy(l => salesDict[l.SaleId].InvoiceNumber)
            .ThenBy(l => l.ItemName)
            .Select(l =>
            {
                var s = salesDict[l.SaleId];
                return new ItemizedSaleLineDto(
                    InvoiceNumber: s.InvoiceNumber,
                    Date: request.Date,
                    CustomerName: s.CustomerName ?? "",
                    ItemName: l.ItemName,
                    Qty: l.Qty,
                    SellingValue: l.Taxable + l.Cgst + l.Sgst,
                    CostValue: l.Qty * (l.PurchasePriceSnapshot ?? 0m),
                    Gst: l.Cgst + l.Sgst,
                    Imei: l.Imei,
                    PaymentMode: s.PaymentMode);
            })
            .ToList();

        return new DailySalesReportDto(
            Date: request.Date,
            Sales: salesSummary,
            Inventory: invSummary,
            Payments: paymentBreakdown,
            ByCategory: byCategory,
            ItemizedLog: itemizedLog,
            CollectedAmount: collectedAmount,
            OutstandingAmount: outstandingAmount);
    }

    private static TimeZoneInfo ResolveTimeZone(string timezoneId)
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById(timezoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            if (string.Equals(timezoneId, "Asia/Kolkata", StringComparison.OrdinalIgnoreCase))
            {
                return TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
            }
            throw;
        }
    }
}
