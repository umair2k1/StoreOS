using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Queries;

public record GetInventoryCategoriesQuery : IRequest<IReadOnlyList<string>>, IReportingRequest;
