using FluentValidation;

namespace StoreOS.Application.Sales.VoidSale;

public class VoidSaleValidator : AbstractValidator<VoidSaleCommand>
{
    public VoidSaleValidator()
    {
        RuleFor(x => x.SaleId).NotEmpty().WithMessage("SaleId is required.");
        RuleFor(x => x.Reason).NotEmpty().WithMessage("Void reason is required.");
    }
}
