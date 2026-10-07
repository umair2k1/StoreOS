using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Reporting.Gstr1Export;

public record GetGstr1ExportQuery(DateOnly From, DateOnly To) : IRequest<Gstr1ExportDto>, IReportingRequest;

public record Gstr1RowDto(
    string InvoiceNumber,
    DateOnly Date,
    string CustomerName,
    string? CustomerGstin,
    decimal TaxableValue,
    decimal Cgst,
    decimal Sgst,
    decimal TotalValue,
    string? HsnOrSac,
    string Category);

public record Gstr1GroupDto(
    string Gstin,
    IReadOnlyList<Gstr1RowDto> Rows,
    decimal TotalTaxable,
    decimal TotalCgst,
    decimal TotalSgst,
    decimal GrandTotal);

public record Gstr1ExportDto(IReadOnlyList<Gstr1GroupDto> ByGstin);

public class GetGstr1ExportHandler : IRequestHandler<GetGstr1ExportQuery, Gstr1ExportDto>
{
    private readonly IReportingDbContext _db;
    private readonly IAccessScope _scope;

    public GetGstr1ExportHandler(IReportingDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Gstr1ExportDto> Handle(GetGstr1ExportQuery request, CancellationToken ct)
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

        // Convert [From, To] date bounds in tenant timezone to UTC
        var startLocal = request.From.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var startOffset = tz.GetUtcOffset(startLocal);
        var startUtc = new DateTimeOffset(startLocal, startOffset).ToUniversalTime();

        var endLocal = request.To.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var endOffset = tz.GetUtcOffset(endLocal);
        var endUtc = new DateTimeOffset(endLocal, endOffset).ToUniversalTime();

        // 1. Fetch branches accessible to caller to know each branch's GSTIN
        var branchesQuery = _db.Set<Branch>().AsNoTracking()
            .Where(b => b.TenantId == _scope.TenantId);

        if (!_scope.AllBranches)
        {
            branchesQuery = branchesQuery.Where(b => _scope.BranchIds.Contains(b.Id));
        }

        var branches = await branchesQuery.ToListAsync(ct);
        var branchDict = branches.ToDictionary(b => b.Id);
        var accessibleBranchIds = branchDict.Keys.ToHashSet();

        // 2. Query Active TaxInvoices within [From, To]
        // Explicitly excluding Estimates, Voided, and Refunded invoices (reusing P-801/P-802 exclusion pattern)
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

        // 3. Query sale line items to obtain HSN and Category per invoice
        var lines = await _db.Set<SaleItem>().AsNoTracking()
            .Where(si => saleIds.Contains(si.SaleId))
            .ToListAsync(ct);

        var linesBySaleId = lines.GroupBy(l => l.SaleId).ToDictionary(g => g.Key, g => g.ToList());

        // 4. Group rows by SELLING branch's GSTIN (branches.gstin, NOT customer GSTIN)
        // Branches sharing the same GSTIN are combined into one group.
        var rowsWithGstin = sales.Select(s =>
        {
            branchDict.TryGetValue(s.BranchId, out var branch);
            var gstin = branch?.Gstin?.Trim() ?? "UNREGISTERED";
            if (string.IsNullOrWhiteSpace(gstin)) gstin = "UNREGISTERED";

            linesBySaleId.TryGetValue(s.Id, out var sLines);
            sLines ??= new List<SaleItem>();

            var hsnList = sLines
                .Select(l => l.HsnSnapshot)
                .Where(h => !string.IsNullOrWhiteSpace(h))
                .Distinct()
                .ToList();
            var hsnOrSac = hsnList.Count > 0 ? string.Join(", ", hsnList) : null;

            var catList = sLines
                .Select(l => l.Category)
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Distinct()
                .ToList();
            var category = catList.Count > 0 ? string.Join(", ", catList) : "";

            var localDate = DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(s.SaleDate, tz).DateTime);

            var row = new Gstr1RowDto(
                InvoiceNumber: s.InvoiceNumber,
                Date: localDate,
                CustomerName: s.CustomerName ?? "",
                CustomerGstin: s.CustomerGstin,
                TaxableValue: s.TotalTaxable,
                Cgst: s.TotalCgst,
                Sgst: s.TotalSgst,
                TotalValue: s.GrandTotal,
                HsnOrSac: hsnOrSac,
                Category: category);

            return new { Gstin = gstin, Row = row };
        }).ToList();

        var groups = rowsWithGstin
            .GroupBy(x => x.Gstin)
            .OrderBy(g => g.Key)
            .Select(g =>
            {
                var rows = g.Select(x => x.Row).OrderBy(r => r.InvoiceNumber).ToList();
                decimal totalTaxable = rows.Sum(r => r.TaxableValue);
                decimal totalCgst = rows.Sum(r => r.Cgst);
                decimal totalSgst = rows.Sum(r => r.Sgst);
                decimal grandTotal = rows.Sum(r => r.TotalValue);

                return new Gstr1GroupDto(
                    Gstin: g.Key,
                    Rows: rows,
                    TotalTaxable: totalTaxable,
                    TotalCgst: totalCgst,
                    TotalSgst: totalSgst,
                    GrandTotal: grandTotal);
            })
            .ToList();

        return new Gstr1ExportDto(groups);
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
