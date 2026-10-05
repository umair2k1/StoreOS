using FluentValidation;

namespace StoreOS.Application.Inventory.Adjustments;

public class AdjustStockValidator : AbstractValidator<AdjustStockCommand>
{
    public AdjustStockValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required.");

        RuleFor(x => x.QtyDelta)
            .NotEqual(0).WithMessage("Adjustment quantity delta cannot be zero.");

        RuleFor(x => x.Reason)
            .NotEmpty().WithMessage("Adjustment reason is required.");
    }
}
