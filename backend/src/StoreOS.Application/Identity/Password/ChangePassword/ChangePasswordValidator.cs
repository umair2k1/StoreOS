using FluentValidation;

namespace StoreOS.Application.Identity.Password.ChangePassword;

public class ChangePasswordValidator : AbstractValidator<ChangePasswordCommand>
{
    public ChangePasswordValidator()
    {
        RuleFor(x => x.CurrentPassword)
            .NotEmpty();

        RuleFor(x => x.NewPassword)
            .NotEmpty()
            .MinimumLength(10)
            .Must(p => p != null && p.Any(char.IsLetter))
            .WithMessage("Password must contain at least one letter.")
            .Must(p => p != null && p.Any(char.IsDigit))
            .WithMessage("Password must contain at least one digit.")
            .Must((cmd, newPass) => newPass != cmd.CurrentPassword)
            .WithMessage("New password must differ from current password.");
    }
}
