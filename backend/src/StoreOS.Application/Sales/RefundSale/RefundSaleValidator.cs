using FluentValidation;

namespace StoreOS.Application.Sales.RefundSale;

public class RefundSaleValidator : AbstractValidator<RefundSaleCommand>
{
    public RefundSaleValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty().WithMessage("SaleId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Refund reason is required.");
        RuleFor(x => x.RefundAmount).GreaterThan(0).WithMessage("Refund amount must be greater than zero.");
    }
}
