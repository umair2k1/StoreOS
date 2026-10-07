using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.GetRepairJobs;

public record GetRepairJobsQuery(
    string? Status = null,
    string? Search = null,
    int Page = 1,
    int PageSize = 50) : IRequest<PagedResult<RepairJobDto>>, IReadOnlyRequest;
