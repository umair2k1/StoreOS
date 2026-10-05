using FluentValidation;

namespace StoreOS.Application.Suppliers.CreateSupplier;

public class CreateSupplierValidator : AbstractValidator<CreateSupplierCommand>
{
    public CreateSupplierValidator()
    {
        RuleFor(x => x.Name)
            .NotEmpty().WithMessage("Supplier name is required.");
    }
}
