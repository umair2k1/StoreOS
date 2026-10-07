using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Parts.GetRepairJobParts;

public record GetRepairJobPartsQuery(Guid RepairJobId)
    : IRequest<IReadOnlyList<RepairJobPartDto>>, IReadOnlyRequest;
