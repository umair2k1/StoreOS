using FluentValidation;

namespace StoreOS.Application.Repairs.Jobs.UpdateRepairJobCommercials;

public class UpdateRepairJobCommercialsValidator : AbstractValidator<UpdateRepairJobCommercialsCommand>
{
    public UpdateRepairJobCommercialsValidator()
    {
        RuleFor(x => x.Id).NotEmpty().WithMessage("Repair job ID is required.");

        When(x => x.EstimatedCost.HasValue, () =>
        {
            RuleFor(x => x.EstimatedCost!.Value).GreaterThanOrEqualTo(0).WithMessage("Estimated cost cannot be negative.");
        });

        When(x => x.AdvancePaid.HasValue, () =>
        {
            RuleFor(x => x.AdvancePaid!.Value).GreaterThanOrEqualTo(0).WithMessage("Advance paid cannot be negative.");
        });

        When(x => x.FinalTotal.HasValue, () =>
        {
            RuleFor(x => x.FinalTotal!.Value).GreaterThanOrEqualTo(0).WithMessage("Final total cannot be negative.");
        });
    }
}
