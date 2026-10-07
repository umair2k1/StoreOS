using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Services.GetRepairServices;

public record GetRepairServicesQuery(
    bool? IsActive = true,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<RepairServiceDto>>, IReadOnlyRequest;
