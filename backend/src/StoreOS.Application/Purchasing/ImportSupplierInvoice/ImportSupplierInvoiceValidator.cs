using FluentValidation;

namespace StoreOS.Application.Purchasing.ImportSupplierInvoice;

public class ImportSupplierInvoiceValidator : AbstractValidator<ImportSupplierInvoiceCommand>
{
    public ImportSupplierInvoiceValidator()
    {
        RuleFor(x => x.SupplierId)
            .NotEmpty().WithMessage("Supplier ID is required.");

        RuleFor(x => x.SupplierInvoiceNo)
            .NotEmpty().WithMessage("Supplier invoice number is required.");

        RuleFor(x => x.SourceRef)
            .NotEmpty().WithMessage("SourceRef is required.");

        RuleFor(x => x.Lines)
            .NotEmpty().WithMessage("Import invoice must contain at least one line.");

        RuleForEach(x => x.Lines).ChildRules(line =>
        {
            line.RuleFor(l => l.ProductNameRaw)
                .NotEmpty().WithMessage("Product name is required on each line.");

            line.RuleFor(l => l.Qty)
                .GreaterThan(0).WithMessage("Line quantity must be positive.");

            line.RuleFor(l => l.Rate)
                .GreaterThanOrEqualTo(0).WithMessage("Line rate cannot be negative.");
        });
    }
}
