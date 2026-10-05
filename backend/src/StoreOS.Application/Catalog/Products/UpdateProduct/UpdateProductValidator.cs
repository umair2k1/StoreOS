using FluentValidation;

namespace StoreOS.Application.Catalog.Products.UpdateProduct;

public class UpdateProductValidator : AbstractValidator<UpdateProductCommand>
{
    public UpdateProductValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Product ID is required.");

        When(x => x.Name != null, () =>
        {
            RuleFor(x => x.Name)
                .NotEmpty().WithMessage("Product name cannot be empty.");
        });

        When(x => x.SellPrice.HasValue, () =>
        {
            RuleFor(x => x.SellPrice!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Sell price cannot be negative.");
        });

        When(x => x.PurchasePrice.HasValue, () =>
        {
            RuleFor(x => x.PurchasePrice!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("Purchase price cannot be negative.");
        });

        When(x => x.GstRate.HasValue, () =>
        {
            RuleFor(x => x.GstRate!.Value)
                .GreaterThanOrEqualTo(0).WithMessage("GST rate cannot be negative.");
        });
    }
}
