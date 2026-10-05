using FluentValidation;

namespace StoreOS.Application.Identity.Password.AdminResetPassword;

public class AdminResetPasswordValidator : AbstractValidator<AdminResetPasswordCommand>
{
    public AdminResetPasswordValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.NewTemporaryPassword)
            .NotEmpty()
            .MinimumLength(10)
            .Must(p => p != null && p.Any(char.IsLetter))
            .WithMessage("Password must contain at least one letter.")
            .Must(p => p != null && p.Any(char.IsDigit))
            .WithMessage("Password must contain at least one digit.");
    }
}
