using FluentValidation;

namespace StoreOS.Application.Repairs.BillRepair;

public class BillRepairValidator : AbstractValidator<BillRepairCommand>
{
    public BillRepairValidator()
    {
        RuleFor(x => x.RepairJobId).NotEmpty().WithMessage("Repair job ID is required.");
        When(x => x.Payments != null, () =>
        {
            RuleForEach(x => x.Payments).ChildRules(payment =>
            {
                payment.RuleFor(p => p.Amount).GreaterThanOrEqualTo(0).WithMessage("Payment amount cannot be negative.");
                payment.RuleFor(p => p.PaymentMode).NotEmpty().WithMessage("Payment mode is required.");
            });
        });
    }
}
