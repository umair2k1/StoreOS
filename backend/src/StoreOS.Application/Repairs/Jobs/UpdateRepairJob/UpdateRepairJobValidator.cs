using FluentValidation;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJob;

public class UpdateRepairJobValidator : AbstractValidator<UpdateRepairJobCommand>
{
    public UpdateRepairJobValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Repair job ID is required.");
    }
}
