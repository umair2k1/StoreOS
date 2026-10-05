using FluentValidation;

namespace StoreOS.Application.Identity.Roles.CreateRole;

public class CreateRoleValidator : AbstractValidator<CreateRoleCommand>
{
    public CreateRoleValidator()
    {
        RuleFor(x => x.Name).NotEmpty().MaximumLength(100);
        RuleFor(x => x.PermissionCodes).NotNull();
    }
}
