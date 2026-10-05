using FluentValidation;
using StoreOS.Domain.Catalog;

namespace StoreOS.Application.Catalog.Products.CreateProduct;

public class CreateProductValidator : AbstractValidator<CreateProductCommand>
{
    public CreateProductValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Product name is required.");

        RuleFor(x => x.Category)
            .NotEmpty().WithMessage("Product category is required.")
            .Must(ProductCategory.IsValid)
            .WithErrorCode("invalid_product_category")
            .WithMessage("Unknown product category.");

        RuleFor(x => x.SellPrice)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Sell price cannot be negative.");

        RuleFor(x => x.PurchasePrice)
            .GreaterThanOrEqualTo(0)
            .WithMessage("Purchase price cannot be negative.");

        RuleFor(x => x.GstRate)
            .GreaterThanOrEqualTo(0)
            .WithMessage("GST rate cannot be negative.");
    }
}
