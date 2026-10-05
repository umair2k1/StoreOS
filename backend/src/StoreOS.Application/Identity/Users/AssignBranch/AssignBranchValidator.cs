using FluentValidation;

namespace StoreOS.Application.Identity.Users.AssignBranch;

public class AssignBranchValidator : AbstractValidator<AssignBranchCommand>
{
    public AssignBranchValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.BranchId).NotEmpty();
    }
}
