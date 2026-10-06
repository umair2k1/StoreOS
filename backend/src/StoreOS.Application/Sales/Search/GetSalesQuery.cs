using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Sales.Search;

public record GetSalesQuery(
    string? Search = null,
    string? DateFilter = null,
    DateOnly? CustomFrom = null,
    DateOnly? CustomTo = null,
    string? Status = null,
    string? PaymentMode = null,
    string? InvoiceType = null,
    bool? PaidOnly = null,
    bool? DueOnly = null,
    bool? FinancedOnly = null,
    string? Category = null,
    decimal? MinAmount = null,
    decimal? MaxAmount = null,
    string SortBy = "SaleDate",
    string SortDirection = "desc",
    int Page = 1,
    int PageSize = 50
) : IRequest<PagedResult<SaleSummaryDto>>, IReportingRequest;

public record SaleSummaryDto(
    Guid Id,
    string InvoiceNumber,
    DateTimeOffset SaleDate,
    string CustomerName,
    decimal GrandTotal,
    decimal AmountPaid,
    decimal Balance,
    string Status,
    string InvoiceType,
    string? PaymentMode,
    bool IsFinanced
);

public class GetSalesHandler : IRequestHandler<GetSalesQuery, PagedResult<SaleSummaryDto>>
{
    private readonly IReportingDbContext _reportingDb;
    private readonly IAccessScope _scope;

    public GetSalesHandler(IReportingDbContext reportingDb, IAccessScope scope)
    {
        _reportingDb = reportingDb;
        _scope = scope;
    }

    public async Task<PagedResult<SaleSummaryDto>> Handle(GetSalesQuery request, CancellationToken ct)
    {
        var tenant = await _reportingDb.Set<Tenant>()
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
        var query = _reportingDb.Set<Sale>().AsNoTracking();

        if (!string.IsNullOrWhiteSpace(request.Search))
        {
            var term = $"%{request.Search.Trim().ToLower()}%";
            query = query.Where(s =>
                EF.Functions.Like(s.InvoiceNumber.ToLower(), term) ||
                EF.Functions.Like((s.CustomerName ?? "").ToLower(), term) ||
                EF.Functions.Like((s.CustomerGstin ?? "").ToLower(), term) ||
                EF.Functions.Like((s.FinanceProvider ?? "").ToLower(), term) ||
                EF.Functions.Like((s.FinancerName ?? "").ToLower(), term) ||
                EF.Functions.Like((s.Description ?? "").ToLower(), term) ||
                _reportingDb.Set<SaleItem>().Any(si => si.SaleId == s.Id &&
                    (EF.Functions.Like(si.ItemName.ToLower(), term) || EF.Functions.Like((si.Imei ?? "").ToLower(), term))));
        }

        query = ApplyDateFilter(query, request, tz);

        if (!string.IsNullOrWhiteSpace(request.Status))
        {
            query = query.Where(s => s.Status == request.Status);
        }

        if (!string.IsNullOrWhiteSpace(request.PaymentMode))
        {
            query = query.Where(s => s.PaymentMode == request.PaymentMode);
        }

        if (!string.IsNullOrWhiteSpace(request.InvoiceType))
        {
            query = query.Where(s => s.InvoiceType == request.InvoiceType);
        }

        if (request.PaidOnly == true)
        {
            query = query.Where(s => s.AmountPaid >= s.GrandTotal);
        }

        if (request.DueOnly == true)
        {
            query = query.Where(s => s.AmountPaid < s.GrandTotal);
        }

        if (request.FinancedOnly == true)
        {
            query = query.Where(s => s.PaymentMode == "Finance");
        }
        else if (request.FinancedOnly == false)
        {
            query = query.Where(s => s.PaymentMode != "Finance" || s.PaymentMode == null);
        }

        if (!string.IsNullOrWhiteSpace(request.Category))
        {
            query = query.Where(s => _reportingDb.Set<SaleItem>().Any(si => si.SaleId == s.Id && si.Category == request.Category));
        }

        if (request.MinAmount.HasValue)
        {
            query = query.Where(s => s.GrandTotal >= request.MinAmount.Value);
        }

        if (request.MaxAmount.HasValue)
        {
            query = query.Where(s => s.GrandTotal <= request.MaxAmount.Value);
        }

        var sortBy = request.SortBy?.Trim().ToLowerInvariant() ?? "saledate";
        var isAsc = string.Equals(request.SortDirection, "asc", StringComparison.OrdinalIgnoreCase);

        query = (sortBy, isAsc) switch
        {
            ("saledate", true) => query.OrderBy(s => s.SaleDate),
            ("saledate", false) => query.OrderByDescending(s => s.SaleDate),
            ("grandtotal", true) => query.OrderBy(s => s.GrandTotal),
            ("grandtotal", false) => query.OrderByDescending(s => s.GrandTotal),
            ("invoicenumber", true) => query.OrderBy(s => s.InvoiceNumber),
            ("invoicenumber", false) => query.OrderByDescending(s => s.InvoiceNumber),
            ("customername", true) => query.OrderBy(s => s.CustomerName),
            ("customername", false) => query.OrderByDescending(s => s.CustomerName),
            ("balance", true) => query.OrderBy(s => s.GrandTotal - s.AmountPaid),
            ("balance", false) => query.OrderByDescending(s => s.GrandTotal - s.AmountPaid),
            (_, true) => query.OrderBy(s => s.SaleDate),
            (_, false) => query.OrderByDescending(s => s.SaleDate),
        };

        var totalCount = await query.CountAsync(ct);

        var page = request.Page < 1 ? 1 : request.Page;
        var pageSize = request.PageSize < 1 ? 50 : request.PageSize;

        var items = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SaleSummaryDto(
                s.Id,
                s.InvoiceNumber,
                s.SaleDate,
                s.CustomerName ?? "",
                s.GrandTotal,
                s.AmountPaid,
                s.GrandTotal - s.AmountPaid,
                s.Status,
                s.InvoiceType,
                s.PaymentMode,
                s.PaymentMode == "Finance"
            ))
            .ToListAsync(ct);

        return new PagedResult<SaleSummaryDto>(items, totalCount, page, pageSize);
    }

    private static IQueryable<Sale> ApplyDateFilter(IQueryable<Sale> query, GetSalesQuery request, TimeZoneInfo tz)
    {
        DateOnly? rangeFrom = null;
        DateOnly? rangeTo = null;

        if (!string.IsNullOrWhiteSpace(request.DateFilter))
        {
            var filter = request.DateFilter.Trim().ToLowerInvariant();
            var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.UtcNow.UtcDateTime, tz);
            var localToday = DateOnly.FromDateTime(localNow);

            switch (filter)
            {
                case "today":
                    rangeFrom = localToday;
                    rangeTo = localToday;
                    break;
                case "yesterday":
                    rangeFrom = localToday.AddDays(-1);
                    rangeTo = localToday.AddDays(-1);
                    break;
                case "thisweek":
                case "this_week":
                case "this week":
                    var diff = (7 + ((int)localToday.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
                    rangeFrom = localToday.AddDays(-diff);
                    rangeTo = rangeFrom.Value.AddDays(6);
                    break;
                case "thismonth":
                case "this_month":
                case "this month":
                    rangeFrom = new DateOnly(localToday.Year, localToday.Month, 1);
                    rangeTo = rangeFrom.Value.AddMonths(1).AddDays(-1);
                    break;
                case "lastmonth":
                case "last_month":
                case "last month":
                    var firstOfCurrent = new DateOnly(localToday.Year, localToday.Month, 1);
                    rangeFrom = firstOfCurrent.AddMonths(-1);
                    rangeTo = firstOfCurrent.AddDays(-1);
                    break;
                case "thisyear":
                case "this_year":
                case "this year":
                    rangeFrom = new DateOnly(localToday.Year, 1, 1);
                    rangeTo = new DateOnly(localToday.Year, 12, 31);
                    break;
                case "custom":
                    rangeFrom = request.CustomFrom;
                    rangeTo = request.CustomTo;
                    break;
                case "all":
                    break;
                default:
                    throw new DomainValidationException("invalid_date_filter", $"Unknown date filter '{request.DateFilter}'.");
            }
        }
        else if (request.CustomFrom.HasValue || request.CustomTo.HasValue)
        {
            rangeFrom = request.CustomFrom;
            rangeTo = request.CustomTo;
        }

        if (rangeFrom.HasValue)
        {
            var localStart = rangeFrom.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var offsetStart = tz.GetUtcOffset(localStart);
            var startUtc = new DateTimeOffset(localStart, offsetStart).ToUniversalTime();
            query = query.Where(s => s.SaleDate >= startUtc);
        }

        if (rangeTo.HasValue)
        {
            var localNext = rangeTo.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
            var offsetEnd = tz.GetUtcOffset(localNext);
            var endUtc = new DateTimeOffset(localNext, offsetEnd).ToUniversalTime();
            query = query.Where(s => s.SaleDate < endUtc);
        }

        return query;
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
