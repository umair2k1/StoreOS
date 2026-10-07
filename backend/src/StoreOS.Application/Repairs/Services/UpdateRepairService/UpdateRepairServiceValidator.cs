using FluentValidation;

namespace StoreOS.Application.Repairs.Services.UpdateRepairService;

public class UpdateRepairServiceValidator : AbstractValidator<UpdateRepairServiceCommand>
{
    public UpdateRepairServiceValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Service ID is required.");

        When(x => x.Name != null, () =>
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Service name cannot be empty.");
        });

        When(x => x.StandardCharge.HasValue, () =>
        {
            RuleFor(x => x.StandardCharge!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Standard charge cannot be negative.");
        });

        When(x => x.GstRate.HasValue, () =>
        {
            RuleFor(x => x.GstRate!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("GST rate cannot be negative.");
        });

        When(x => x.EstimatedDurationMinutes.HasValue, () =>
        {
            RuleFor(x => x.EstimatedDurationMinutes!.Value)
                .GreaterThan(0).WithMessage("Estimated duration must be positive.");
        });

        When(x => x.WarrantyDays.HasValue, () =>
        {
            RuleFor(x => x.WarrantyDays!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Warranty days cannot be negative.");
        });
    }
}
