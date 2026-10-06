using FluentValidation;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.RecordPayment;

public class RecordPaymentValidator : AbstractValidator<RecordPaymentCommand>
{
    public RecordPaymentValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty().WithMessage("SaleId is required.");
        RuleFor(x => x.Amount).GreaterThan(0).WithMessage("Payment amount must be greater than zero.");
        RuleFor(x => x.PaymentMode)
            .Must(m => PaymentMode.All.Contains(m))
            .WithMessage("Invalid payment mode.");
    }
}
