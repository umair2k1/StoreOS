using FluentValidation;

namespace StoreOS.Application.Purchasing.StockIn;

public class StockInValidator : AbstractValidator<StockInCommand>
{
    public StockInValidator()
    {
        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required.");

        RuleFor(x => x.Qty)
            .GreaterThan(0).WithMessage("Purchase quantity must be positive.");

        RuleFor(x => x.PurchaseRate)
            .GreaterThanOrEqualTo(0).WithMessage("Purchase rate cannot be negative.");

        When(x => x.Imeis != null && x.Imeis.Count > 0, () =>
        {
            RuleFor(x => x.Imeis!.Count)
                .Equal(x => x.Qty)
                .WithMessage("The number of IMEIs must exactly match the purchase quantity.");

            RuleForEach(x => x.Imeis)
                .NotEmpty().WithMessage("IMEI cannot be empty.");

            RuleFor(x => x.Imeis)
                .Must(imeis => imeis == null || imeis.Distinct(StringComparer.OrdinalIgnoreCase).Count() == imeis.Count)
                .WithMessage("Duplicate IMEI submitted in the same request.");
        });
    }
}
