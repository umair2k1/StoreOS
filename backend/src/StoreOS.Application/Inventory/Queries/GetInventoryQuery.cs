using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Queries;

public record GetInventoryQuery(
    string? Category = null,
    string? StockStatus = null,
    bool? IsMarginScheme = null,
    string? Search = null,
    Guid[]? BranchIds = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<InventoryLineDto>>, IReportingRequest;

public record InventoryLineDto(
    Guid ProductId,
    Guid BranchId,
    string ProductName,
    string Category,
    int StockQty,
    decimal CostPrice,
    decimal EffectiveSellPrice,
    decimal GstRate,
    bool IsMarginScheme,
    string? HsnCode,
    string StockStatus);
