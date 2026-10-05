using FluentValidation;
using StoreOS.Domain.Sales;

namespace StoreOS.Application.Sales.Checkout;

public class CheckoutValidator : AbstractValidator<CheckoutCommand>
{
    public CheckoutValidator()
    {
        RuleFor(x => x.Lines).NotEmpty().WithMessage("Cart cannot be empty.");
        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.Qty).GreaterThan(0);
            line.RuleFor(l => l.OverridePricePerUnit).GreaterThanOrEqualTo(0)
                .When(l => l.OverridePricePerUnit.HasValue);
        });
        RuleFor(x => x.InvoiceDiscount).GreaterThanOrEqualTo(0);
        RuleFor(x => x.InvoiceType).Must(t => InvoiceType.All.Contains(t))
            .WithMessage("Invoice type must be TaxInvoice or Estimate.");
        RuleFor(x => x.Payments).NotNull();
        RuleForEach(x => x.Payments).ChildRules(p =>
        {
            p.RuleFor(x => x.Amount).GreaterThan(0);
            p.RuleFor(x => x.PaymentMode).Must(m => PaymentMode.All.Contains(m));
        });
        RuleFor(x => x.Finance).SetValidator(new CheckoutFinanceDetailsValidator()!)
            .When(x => x.Finance is not null);
        RuleFor(x => x)
            .Must(x => x.CustomerId is not null || !string.IsNullOrWhiteSpace(x.WalkInCustomerName) || true)
            .WithMessage("A walk-in name is recommended when no customer is selected.");
    }
}

public class CheckoutFinanceDetailsValidator : AbstractValidator<CheckoutFinanceDetails>
{
    public CheckoutFinanceDetailsValidator()
    {
        RuleFor(x => x.FinanceProvider).NotEmpty();
        RuleFor(x => x.DownPayment).GreaterThanOrEqualTo(0);
        RuleFor(x => x.TenureMonths).GreaterThan(0);
        RuleFor(x => x.AnnualInterestRate).GreaterThanOrEqualTo(0);
    }
}
