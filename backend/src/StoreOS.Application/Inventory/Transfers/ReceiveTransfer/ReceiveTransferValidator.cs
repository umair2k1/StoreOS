using FluentValidation;

namespace StoreOS.Application.Inventory.Transfers.ReceiveTransfer;

public class ReceiveTransferValidator : AbstractValidator<ReceiveTransferCommand>
{
    public ReceiveTransferValidator()
    {
        RuleFor(x => x.TransferId)
            .NotEmpty().WithMessage("Transfer ID is required.");
    }
}
