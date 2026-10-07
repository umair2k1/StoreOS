using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Suppliers.GetReturnById;

public record GetSupplierReturnByIdQuery(Guid Id) : IRequest<SupplierReturnDto>, IReadOnlyRequest;
