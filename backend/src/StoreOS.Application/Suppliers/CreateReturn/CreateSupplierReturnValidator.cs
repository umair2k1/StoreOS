using FluentValidation;
using StoreOS.Domain.Suppliers;

namespace StoreOS.Application.Suppliers.CreateReturn;

public class CreateSupplierReturnValidator : AbstractValidator<CreateSupplierReturnCommand>
{
    public CreateSupplierReturnValidator()
    {
        RuleFor(x => x.SupplierId)
            .NotEmpty().WithMessage("SupplierId is required.");

        RuleFor(x => x.ReturnDate)
            .NotEmpty().WithMessage("ReturnDate is required.");

        RuleFor(x => x.SettlementType)
            .NotEmpty().WithMessage("SettlementType is required.")
            .Must(SettlementType.IsValid).WithMessage("Invalid settlement type.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("At least one return line is required.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ItemName)
                .NotEmpty().WithMessage("Item name is required.");

            line.RuleFor(l => l.Qty)
                .GreaterThan(0).WithMessage("Quantity must be greater than zero.");

            line.RuleFor(l => l.ReturnRate)
                .GreaterThanOrEqualTo(0).WithMessage("Return rate cannot be negative.");

            line.RuleFor(l => l.PurchasePrice)
                .GreaterThanOrEqualTo(0).WithMessage("Purchase price cannot be negative.");
        });
    }
}
