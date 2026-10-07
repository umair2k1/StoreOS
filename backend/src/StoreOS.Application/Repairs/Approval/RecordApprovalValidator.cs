using FluentValidation;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Approval;

public class RecordApprovalValidator : AbstractValidator<RecordApprovalCommand>
{
    public RecordApprovalValidator()
    {
        RuleFor(x => x.RepairJobId).NotEmpty().WithMessage("Repair job ID is required.");
        RuleFor(x => x.ApprovalState)
            .NotEmpty().WithMessage("Approval state is required.")
            .Must(ApprovalState.IsValid).WithMessage("Approval state must be one of: Pending, Approved, Rejected.");
    }
}
