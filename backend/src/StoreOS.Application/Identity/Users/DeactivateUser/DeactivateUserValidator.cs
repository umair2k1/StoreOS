using FluentValidation;

namespace StoreOS.Application.Identity.Users.DeactivateUser;

public class DeactivateUserValidator : AbstractValidator<DeactivateUserCommand>
{
    public DeactivateUserValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
