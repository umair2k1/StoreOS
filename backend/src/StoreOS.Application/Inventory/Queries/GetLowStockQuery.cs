using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Queries;

public record GetLowStockQuery(Guid[]? BranchIds = null) : IRequest<IReadOnlyList<InventoryLineDto>>, IReportingRequest;
