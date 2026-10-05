using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.Search;

public record PosSearchQuery(string Q) : IRequest<IReadOnlyList<PosSearchResultDto>>, IReadOnlyRequest;

public record PosSearchResultDto(
    Guid ProductId,
    string Name,
    string Category,
    decimal SellPrice,
    decimal GstRate,
    string? HsnCode,
    bool IsMarginScheme,
    int StockQty,
    string StockStatus);
