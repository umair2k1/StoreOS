using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Catalog.Products.GetProductById;

public record GetProductByIdQuery(Guid Id) : IRequest<ProductDto>, IReadOnlyRequest;
