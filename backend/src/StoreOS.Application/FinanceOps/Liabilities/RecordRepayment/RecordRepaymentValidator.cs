using FluentValidation;

namespace StoreOS.Application.FinanceOps.Liabilities.RecordRepayment;

public class RecordRepaymentValidator : AbstractValidator<RecordRepaymentCommand>
{
    public RecordRepaymentValidator()
    {
        RuleFor(x => x.LiabilityId)
            .NotEmpty().WithErrorCode("liability_id_required").WithMessage("liability_id_required");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithErrorCode("invalid_payment_amount").WithMessage("invalid_payment_amount");

        RuleFor(x => x.PaymentMode)
            .NotEmpty().WithErrorCode("payment_mode_required").WithMessage("payment_mode_required");

        RuleFor(x => x.PaymentDate)
            .Must(d => d != default).WithErrorCode("payment_date_required").WithMessage("payment_date_required");
    }
}
