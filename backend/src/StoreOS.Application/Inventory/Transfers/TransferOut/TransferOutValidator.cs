using FluentValidation;

namespace StoreOS.Application.Inventory.Transfers.TransferOut;

public class TransferOutValidator : AbstractValidator<TransferOutCommand>
{
    public TransferOutValidator()
    {
        RuleFor(x => x.ToBranchId)
            .NotEmpty().WithMessage("Destination branch ID is required.");

        RuleFor(x => x.ProductId)
            .NotEmpty().WithMessage("Product ID is required.");

        RuleFor(x => x.Qty)
            .GreaterThan(0).WithMessage("Transfer quantity must be positive.");

        When(x => x.ImeiStrings != null && x.ImeiStrings.Count > 0, () =>
        {
            RuleFor(x => x.ImeiStrings!.Count)
                .Equal(x => x.Qty)
                .WithMessage("The number of IMEIs must match the transfer quantity.");

            RuleForEach(x => x.ImeiStrings)
                .NotEmpty().WithMessage("IMEI string cannot be empty.");
        });
    }
}
