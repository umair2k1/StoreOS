using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Sales.Search;

public record GetPosProductQuery(Guid Id) : IRequest<PosProductDetailDto>, IReadOnlyRequest;

public record PosProductDetailDto(
    Guid ProductId,
    string Name,
    string Category,
    decimal SellPrice,
    decimal GstRate,
    string? HsnCode,
    bool IsMarginScheme,
    int StockQty,
    string StockStatus,
    IReadOnlyList<string> AvailableImeis);
