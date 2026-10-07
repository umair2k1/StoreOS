using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJobDiagnosis;

public record UpdateRepairJobDiagnosisCommand(
    Guid Id,
    string? Diagnosis,
    string? TechnicianNotes,
    string? InternalNotes,
    string? Technician) : IRequest, IRequiresPermission, IRequiresRepairsFeature
{
    public string RequiredPermission => "repairs:manage";
}
