using FluentValidation;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Liabilities.CreateLiability;

public class CreateLiabilityValidator : AbstractValidator<CreateLiabilityCommand>
{
    public CreateLiabilityValidator()
    {
        RuleFor(x => x.Title)
            .NotEmpty().WithErrorCode("liability_title_required").WithMessage("liability_title_required");

        RuleFor(x => x.Type)
            .NotEmpty().WithErrorCode("invalid_liability_type").WithMessage("invalid_liability_type")
            .Must(LiabilityType.IsValid).WithErrorCode("invalid_liability_type").WithMessage("invalid_liability_type");

        RuleFor(x => x.TotalPrincipal)
            .GreaterThan(0).WithErrorCode("invalid_principal").WithMessage("invalid_principal");
    }
}
