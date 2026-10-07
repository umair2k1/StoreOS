using FluentValidation;

namespace StoreOS.Application.Repairs.Parts.ConsumePart;

public class ConsumePartValidator : AbstractValidator<ConsumePartCommand>
{
    public ConsumePartValidator()
    {
        RuleFor(x => x.RepairJobId).NotEmpty().WithMessage("Repair job ID is required.");
        RuleFor(x => x.PartId).NotEmpty().WithMessage("Part ID is required.");
    }
}
