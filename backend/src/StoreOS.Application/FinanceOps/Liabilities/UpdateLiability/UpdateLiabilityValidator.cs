using FluentValidation;

namespace StoreOS.Application.FinanceOps.Liabilities.UpdateLiability;

public class UpdateLiabilityValidator : AbstractValidator<UpdateLiabilityCommand>
{
    public UpdateLiabilityValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithErrorCode("liability_id_required").WithMessage("liability_id_required");

        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode("liability_title_required").WithMessage("liability_title_required");
    }
}
