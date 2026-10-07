using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Reporting.Dashboard;

public record GetDashboardQuery(Guid[]? BranchIds = null) : IRequest<DashboardDto>, IReportingRequest;

public record DashboardDto(
    decimal TodayRevenue,
    int TodayInvoiceCount,
    decimal MonthlyProfit,
    int ActiveRepairsCount,
    int ReadyForPickupCount,
    IReadOnlyList<LowStockItemDto> LowStockItems,
    IReadOnlyList<TopSellingItemDto> TopSellingItems,
    IReadOnlyList<DailyRevenuePointDto> SevenDayRevenue);

public record LowStockItemDto(Guid ProductId, string Name, int StockQty, int Threshold);
public record TopSellingItemDto(string ItemName, int TotalQtySold);
public record DailyRevenuePointDto(DateOnly Date, decimal Revenue);

public class GetDashboardHandler : IRequestHandler<GetDashboardQuery, DashboardDto>
{
    private readonly IReportingDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IFeatureManager _featureManager;

    public GetDashboardHandler(
        IReportingDbContext db,
        IAccessScope scope,
        IFeatureManager featureManager)
    {
        _db = db;
        _scope = scope;
        _featureManager = featureManager;
    }

    public async Task<DashboardDto> Handle(GetDashboardQuery request, CancellationToken ct)
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

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.UtcNow.UtcDateTime, tz);
        var localToday = DateOnly.FromDateTime(localNow);

        // 1. Today's Revenue and Invoice Count
        var todayStartLocal = localToday.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var todayStartOffset = tz.GetUtcOffset(todayStartLocal);
        var todayStartUtc = new DateTimeOffset(todayStartLocal, todayStartOffset).ToUniversalTime();

        var todayEndLocal = localToday.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var todayEndOffset = tz.GetUtcOffset(todayEndLocal);
        var todayEndUtc = new DateTimeOffset(todayEndLocal, todayEndOffset).ToUniversalTime();

        var todaySalesQuery = _db.Set<Sale>().AsNoTracking()
            .Where(s => s.InvoiceType == InvoiceType.TaxInvoice
                     && s.InvoiceType != InvoiceType.Estimate
                     && s.Status == SaleStatus.Active
                     && s.Status != SaleStatus.Voided
                     && s.Status != SaleStatus.Refunded
                     && s.SaleDate >= todayStartUtc
                     && s.SaleDate < todayEndUtc);

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            todaySalesQuery = todaySalesQuery.Where(s => request.BranchIds.Contains(s.BranchId));
        }

        var todayRevenue = await todaySalesQuery.SumAsync(s => (decimal?)s.GrandTotal, ct) ?? 0m;
        var todayInvoiceCount = await todaySalesQuery.CountAsync(ct);

        // 2. Monthly Profit: Revenue - COGS - OperationalExpenses for current calendar month
        var monthStart = new DateOnly(localToday.Year, localToday.Month, 1);
        var nextMonth = monthStart.AddMonths(1);

        var monthStartLocal = monthStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var monthStartUtc = new DateTimeOffset(monthStartLocal, tz.GetUtcOffset(monthStartLocal)).ToUniversalTime();

        var monthEndLocal = nextMonth.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var monthEndUtc = new DateTimeOffset(monthEndLocal, tz.GetUtcOffset(monthEndLocal)).ToUniversalTime();

        var monthlyLinesQuery = from s in _db.Set<Sale>().AsNoTracking()
                                join si in _db.Set<SaleItem>().AsNoTracking() on s.Id equals si.SaleId
                                where s.InvoiceType == InvoiceType.TaxInvoice
                                   && s.InvoiceType != InvoiceType.Estimate
                                   && s.Status == SaleStatus.Active
                                   && s.Status != SaleStatus.Voided
                                   && s.Status != SaleStatus.Refunded
                                   && s.SaleDate >= monthStartUtc
                                   && s.SaleDate < monthEndUtc
                                   && !si.IsGift
                                select new
                                {
                                    s.BranchId,
                                    LineRevenue = si.Taxable + si.Cgst + si.Sgst,
                                    LineCogs = si.Qty * (si.PurchasePriceSnapshot ?? 0m)
                                };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            monthlyLinesQuery = monthlyLinesQuery.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        var revAndCogs = await monthlyLinesQuery
            .GroupBy(_ => 1)
            .Select(g => new
            {
                Revenue = g.Sum(x => x.LineRevenue),
                Cogs = g.Sum(x => x.LineCogs)
            })
            .FirstOrDefaultAsync(ct);

        var monthlyRevenue = revAndCogs?.Revenue ?? 0m;
        var monthlyCogs = revAndCogs?.Cogs ?? 0m;

        var expensesQuery = _db.Set<Expense>().AsNoTracking()
            .Where(e => e.ExpenseDate >= monthStart && e.ExpenseDate < nextMonth);

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            expensesQuery = expensesQuery.Where(e => e.BranchId.HasValue && request.BranchIds.Contains(e.BranchId.Value));
        }

        var monthlyExpenses = await expensesQuery.SumAsync(e => (decimal?)e.Amount, ct) ?? 0m;
        var monthlyProfit = monthlyRevenue - monthlyCogs - monthlyExpenses;

        // 3. Active Repairs & Ready for Pickup (gated by IFeatureManager)
        int activeRepairsCount = 0;
        int readyForPickupCount = 0;

        if (await _featureManager.IsRepairsEnabledAsync(ct))
        {
            var repairsQuery = _db.Set<RepairJob>().AsNoTracking();
            if (request.BranchIds != null && request.BranchIds.Length > 0)
            {
                repairsQuery = repairsQuery.Where(r => request.BranchIds.Contains(r.BranchId));
            }

            activeRepairsCount = await repairsQuery
                .Where(r => r.Status != RepairStatus.Delivered && r.Status != RepairStatus.Cancelled)
                .CountAsync(ct);

            readyForPickupCount = await repairsQuery
                .Where(r => r.Status == RepairStatus.ReadyForPickup)
                .CountAsync(ct);
        }

        // 4. Low-Stock Alerts (stock_qty > 0 AND stock_qty <= low_stock_threshold, excluding RepairService)
        var lowStockQuery = from bi in _db.Set<BranchInventory>().AsNoTracking()
                            join p in _db.Set<Product>().AsNoTracking() on bi.ProductId equals p.Id
                            where p.Category != ProductCategory.RepairService
                               && bi.StockQty > 0
                               && bi.StockQty <= bi.LowStockThreshold
                            select new
                            {
                                bi.ProductId,
                                p.Name,
                                bi.StockQty,
                                bi.LowStockThreshold,
                                bi.BranchId
                            };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            lowStockQuery = lowStockQuery.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        var lowStockItems = await lowStockQuery
            .OrderBy(x => x.StockQty)
            .ThenBy(x => x.Name)
            .Select(x => new LowStockItemDto(x.ProductId, x.Name, x.StockQty, x.LowStockThreshold))
            .ToListAsync(ct);

        // 5. Top-Selling Items (Current month, top 5, is_gift = false, Active TaxInvoices only)
        var topSellingQuery = from s in _db.Set<Sale>().AsNoTracking()
                              join si in _db.Set<SaleItem>().AsNoTracking() on s.Id equals si.SaleId
                              where s.InvoiceType == InvoiceType.TaxInvoice
                                 && s.InvoiceType != InvoiceType.Estimate
                                 && s.Status == SaleStatus.Active
                                 && s.Status != SaleStatus.Voided
                                 && s.Status != SaleStatus.Refunded
                                 && s.SaleDate >= monthStartUtc
                                 && s.SaleDate < monthEndUtc
                                 && !si.IsGift
                              select new { s.BranchId, si.ItemName, si.Qty };

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            topSellingQuery = topSellingQuery.Where(x => request.BranchIds.Contains(x.BranchId));
        }

        var topSellingRaw = await topSellingQuery
            .GroupBy(x => x.ItemName)
            .Select(g => new { ItemName = g.Key, TotalQtySold = g.Sum(x => x.Qty) })
            .OrderByDescending(x => x.TotalQtySold)
            .ThenBy(x => x.ItemName)
            .Take(5)
            .ToListAsync(ct);

        var topSellingItems = topSellingRaw
            .Select(x => new TopSellingItemDto(x.ItemName, x.TotalQtySold))
            .ToList();

        // 6. Seven-Day Revenue Chart (Last 7 days including today, zero-filled)
        var sevenDaysStart = localToday.AddDays(-6);
        var chartStartLocal = sevenDaysStart.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var chartStartUtc = new DateTimeOffset(chartStartLocal, tz.GetUtcOffset(chartStartLocal)).ToUniversalTime();

        var chartEndLocal = localToday.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var chartEndUtc = new DateTimeOffset(chartEndLocal, tz.GetUtcOffset(chartEndLocal)).ToUniversalTime();

        var sevenDaySalesQuery = _db.Set<Sale>().AsNoTracking()
            .Where(s => s.InvoiceType == InvoiceType.TaxInvoice
                     && s.InvoiceType != InvoiceType.Estimate
                     && s.Status == SaleStatus.Active
                     && s.Status != SaleStatus.Voided
                     && s.Status != SaleStatus.Refunded
                     && s.SaleDate >= chartStartUtc
                     && s.SaleDate < chartEndUtc);

        if (request.BranchIds != null && request.BranchIds.Length > 0)
        {
            sevenDaySalesQuery = sevenDaySalesQuery.Where(s => request.BranchIds.Contains(s.BranchId));
        }

        var salesForChart = await sevenDaySalesQuery
            .Select(s => new { s.SaleDate, s.GrandTotal })
            .ToListAsync(ct);

        var revenueByDate = salesForChart
            .GroupBy(s => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.SaleDate, tz).DateTime))
            .ToDictionary(g => g.Key, g => g.Sum(x => x.GrandTotal));

        var sevenDayRevenue = new List<DailyRevenuePointDto>(7);
        for (int i = 6; i >= 0; i--)
        {
            var date = localToday.AddDays(-i);
            revenueByDate.TryGetValue(date, out var rev);
            sevenDayRevenue.Add(new DailyRevenuePointDto(date, rev));
        }

        return new DashboardDto(
            TodayRevenue: todayRevenue,
            TodayInvoiceCount: todayInvoiceCount,
            MonthlyProfit: monthlyProfit,
            ActiveRepairsCount: activeRepairsCount,
            ReadyForPickupCount: readyForPickupCount,
            LowStockItems: lowStockItems,
            TopSellingItems: topSellingItems,
            SevenDayRevenue: sevenDayRevenue);
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
