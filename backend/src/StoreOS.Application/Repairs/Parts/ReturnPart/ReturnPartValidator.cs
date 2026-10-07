using FluentValidation;

namespace StoreOS.Application.Repairs.Parts.ReturnPart;

public class ReturnPartValidator : AbstractValidator<ReturnPartCommand>
{
    public ReturnPartValidator()
    {
        RuleFor(x => x.RepairJobId).NotEmpty().WithMessage("Repair job ID is required.");
        RuleFor(x => x.PartId).NotEmpty().WithMessage("Part ID is required.");
    }
}
