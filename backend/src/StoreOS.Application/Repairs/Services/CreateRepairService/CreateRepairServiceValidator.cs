using FluentValidation;

namespace StoreOS.Application.Repairs.Services.CreateRepairService;

public class CreateRepairServiceValidator : AbstractValidator<CreateRepairServiceCommand>
{
    public CreateRepairServiceValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Service name is required.");

        RuleFor(x => x.StandardCharge)
            .GreaterThanOrEqualTo(0).WithMessage("Standard charge cannot be negative.");

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
