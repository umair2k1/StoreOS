using FluentValidation;

namespace StoreOS.Application.Tenancy.Branches.CreateBranch;

public class CreateBranchValidator : AbstractValidator<CreateBranchCommand>
{
    public CreateBranchValidator()
    {
        RuleFor(x => x.Code)
            .NotEmpty()
            .Matches("^[A-Z1-9][A-Z0-9]{0,2}$")
            .WithMessage("Branch code must be 1-3 uppercase alphanumeric characters, not starting with 0.");

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(100);

        When(x => !string.IsNullOrEmpty(x.Email), () =>
        {
            RuleFor(x => x.Email)
                .EmailAddress()
                .WithMessage("Email format is invalid.");
        });
    }
}
