using FluentValidation;

namespace StoreOS.Application.Repairs.Deliver;

public class DeliverRepairJobValidator : AbstractValidator<DeliverRepairJobCommand>
{
    public DeliverRepairJobValidator()
    {
        RuleFor(x => x.RepairJobId).NotEmpty().WithMessage("Repair job ID is required.");
        When(x => x.WarrantyDaysOverride.HasValue, () =>
        {
            RuleFor(x => x.WarrantyDaysOverride!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Warranty days cannot be negative.");
        });
    }
}
