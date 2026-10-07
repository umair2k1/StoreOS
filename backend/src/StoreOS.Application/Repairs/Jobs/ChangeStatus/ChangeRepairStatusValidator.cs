using FluentValidation;
using StoreOS.Domain.Repairs;

namespace StoreOS.Application.Repairs.Jobs.ChangeStatus;

public class ChangeRepairStatusValidator : AbstractValidator<ChangeRepairStatusCommand>
{
    public ChangeRepairStatusValidator()
    {
        RuleFor(x => x.RepairJobId)
            .NotEmpty().WithMessage("Repair job ID is required.");

        RuleFor(x => x.NewStatus)
            .NotEmpty().WithMessage("New status is required.")
            .Must(RepairStatus.IsValid).WithMessage("Invalid repair status.");
    }
}
