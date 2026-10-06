using FluentValidation;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.RecordTransaction;

public class RecordSupplierTransactionValidator : AbstractValidator<RecordSupplierTransactionCommand>
{
    public RecordSupplierTransactionValidator()
    {
        RuleFor(x => x.SupplierId)
            .NotEmpty().WithMessage("SupplierId is required.");

        RuleFor(x => x.Amount)
            .GreaterThan(0).WithMessage("Amount must be greater than zero.");

        RuleFor(x => x.Type)
            .NotEmpty().WithMessage("Transaction type is required.")
            .Must(SupplierTransactionType.IsValid).WithMessage("Invalid transaction type.");

        RuleFor(x => x.PaymentMode)
            .NotEmpty().WithMessage("Payment mode is required for Payment transaction.")
            .When(x => x.Type == SupplierTransactionType.Payment);

        RuleFor(x => x.PaymentMode)
            .Must(m => m == null || SupplierPaymentMode.IsValid(m)).WithMessage("Invalid payment mode.");
    }
}
