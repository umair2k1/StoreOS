using FluentValidation;

namespace StoreOS.Application.Repairs.Jobs.CreateRepairJob;

public class CreateRepairJobValidator : AbstractValidator<CreateRepairJobCommand>
{
    public CreateRepairJobValidator()
    {
        When(x => x.EstimatedCost.HasValue, () =>
        {
            RuleFor(x => x.EstimatedCost!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Estimated cost cannot be negative.");
        });

        When(x => x.AdvancePaid.HasValue, () =>
        {
            RuleFor(x => x.AdvancePaid!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Advance paid cannot be negative.");
        });
    }
}
