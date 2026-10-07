using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Services.GetRepairServiceById;

public record GetRepairServiceByIdQuery(Guid Id) : IRequest<RepairServiceDto>, IReadOnlyRequest, IRequiresRepairsFeature;
