using FluentValidation;

namespace StoreOS.Application.Repairs.Parts.ReservePart;

public class ReservePartValidator : AbstractValidator<ReservePartCommand>
{
    public ReservePartValidator()
    {
        RuleFor(x => x.RepairJobId).NotEmpty().WithMessage("Repair job ID is required.");
        RuleFor(x => x.PartName).NotEmpty().WithMessage("Part name is required.");
        RuleFor(x => x.Quantity).GreaterThan(0).WithMessage("Quantity must be positive.");
        RuleFor(x => x.UnitCost).GreaterThanOrEqualTo(0).WithMessage("Unit cost cannot be negative.");
        RuleFor(x => x.ChargeAmount).GreaterThanOrEqualTo(0).WithMessage("Charge amount cannot be negative.");
    }
}
