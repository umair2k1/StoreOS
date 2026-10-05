using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Inventory.Queries;

public record GetValuationQuery(Guid[]? BranchIds = null) : IRequest<ValuationDto>, IReportingRequest;

public record ValuationDto(decimal CostValuation, decimal RetailValuation, decimal PotentialGrossValue);
