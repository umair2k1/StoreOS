using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Catalog.Products.GetProducts;

public record GetProductsQuery(
    string? Category = null,
    bool? IsActive = null,
    bool? IsMarginScheme = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<ProductDto>>, IReadOnlyRequest;
