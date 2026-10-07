using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Reporting.Compare;

public record GetBranchComparisonQuery(DateOnly Date) : IRequest<BranchComparisonDto>, IReportingRequest;

public record BranchRowDto(
    Guid BranchId,
    string BranchName,
    decimal Revenue,
    int InvoiceCount,
    decimal Profit,
    int ActiveRepairs);

public record BranchComparisonDto(
    IReadOnlyList<BranchRowDto> Branches,
    BranchRowDto Consolidated);

public class GetBranchComparisonHandler : IRequestHandler<GetBranchComparisonQuery, BranchComparisonDto>
{
    private readonly IReportingDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IFeatureManager _featureManager;

    public GetBranchComparisonHandler(
        IReportingDbContext db,
        IAccessScope scope,
        IFeatureManager featureManager)
    {
        _db = db;
        _scope = scope;
        _featureManager = featureManager;
    }

    public async Task<BranchComparisonDto> Handle(GetBranchComparisonQuery request, CancellationToken ct)
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

        var startLocal = request.Date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startOffset = tz.GetUtcOffset(startLocal);
        var startUtc = new DateTimeOffset(startLocal, startOffset).ToUniversalTime();

        var endLocal = request.Date.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endOffset = tz.GetUtcOffset(endLocal);
        var endUtc = new DateTimeOffset(endLocal, endOffset).ToUniversalTime();

        // 1. Determine branches accessible to caller
        var branchesQuery = _db.Set<Branch>().AsNoTracking()
            .Where(b => b.TenantId == _scope.TenantId);

        if (!_scope.AllBranches)
        {
            branchesQuery = branchesQuery.Where(b => _scope.BranchIds.Contains(b.Id));
        }

        var branches = await branchesQuery.OrderBy(b => b.Name).ToListAsync(ct);
        var accessibleBranchIds = branches.Select(b => b.Id).ToHashSet();

        // 2. Query Active TaxInvoices on Date for accessible branches
        var salesQuery = _db.Set<Sale>().AsNoTracking()
            .Where(s => s.InvoiceType == InvoiceType.TaxInvoice
                     && s.InvoiceType != InvoiceType.Estimate
                     && s.Status == SaleStatus.Active
                     && s.Status != SaleStatus.Voided
                     && s.Status != SaleStatus.Refunded
                     && s.SaleDate >= startUtc
                     && s.SaleDate < endUtc
                     && accessibleBranchIds.Contains(s.BranchId));

        var sales = await salesQuery.ToListAsync(ct);
        var saleIds = sales.Select(s => s.Id).ToList();

        var lines = await _db.Set<SaleItem>().AsNoTracking()
            .Where(si => saleIds.Contains(si.SaleId))
            .ToListAsync(ct);

        // 3. Query active repair jobs if repairs feature is enabled
        bool isRepairsEnabled = await _featureManager.IsRepairsEnabledAsync(ct);
        List<RepairJob> repairs = new();
        if (isRepairsEnabled)
        {
            repairs = await _db.Set<RepairJob>().AsNoTracking()
                .Where(r => r.Status != RepairStatus.Delivered
                         && r.Status != RepairStatus.Cancelled
                         && accessibleBranchIds.Contains(r.BranchId))
                .ToListAsync(ct);
        }

        // 4. Group data per branch and compute metrics using ReportAggregationHelpers
        var salesByBranch = sales.GroupBy(s => s.BranchId).ToDictionary(g => g.Key, g => g.ToList());
        var linesBySaleId = lines.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());
        var repairsByBranch = repairs.GroupBy(r => r.BranchId).ToDictionary(g => g.Key, g => g.ToList());

        var branchRows = new List<BranchRowDto>(branches.Count);
        foreach (var b in branches)
        {
            salesByBranch.TryGetValue(b.Id, out var bSales);
            bSales ??= new List<Sale>();

            var bLines = bSales.SelectMany(s => linesBySaleId.TryGetValue(s.Id, out var sl) ? sl : Enumerable.Empty<SaleItem>()).ToList();

            repairsByBranch.TryGetValue(b.Id, out var bRepairs);
            bRepairs ??= new List<RepairJob>();

            var metrics = ReportAggregationHelpers.ComputeSalesMetrics(bSales, bLines);
            int activeRepairs = ReportAggregationHelpers.CountActiveRepairs(bRepairs, isRepairsEnabled);

            branchRows.Add(new BranchRowDto(
                BranchId: b.Id,
                BranchName: b.Name,
                Revenue: metrics.Revenue,
                InvoiceCount: metrics.InvoiceCount,
                Profit: metrics.Profit,
                ActiveRepairs: activeRepairs));
        }

        // 5. Consolidated row: sum of all visible branches
        decimal totalRevenue = branchRows.Sum(r => r.Revenue);
        int totalInvoiceCount = branchRows.Sum(r => r.InvoiceCount);
        decimal totalProfit = branchRows.Sum(r => r.Profit);
        int totalActiveRepairs = branchRows.Sum(r => r.ActiveRepairs);

        // Documented single-branch behavior:
        // When scoped to a single branch, Consolidated row values are identical to that single visible branch row.
        var consolidatedRow = new BranchRowDto(
            BranchId: branches.Count == 1 ? branches[0].Id : Guid.Empty,
            BranchName: branches.Count == 1 ? branches[0].Name : "Consolidated",
            Revenue: totalRevenue,
            InvoiceCount: totalInvoiceCount,
            Profit: totalProfit,
            ActiveRepairs: totalActiveRepairs);

        return new BranchComparisonDto(branchRows, consolidatedRow);
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
