namespace StoreOS.Domain.Identity;

public class UserRoleAssignment
{
    public Guid UserId { get; private set; }
    public Guid RoleId { get; private set; }
    public Guid? BranchId { get; private set; }

    private UserRoleAssignment() { }

    public static UserRoleAssignment Create(Guid userId, Guid roleId, Guid? branchId = null)
    {
        return new UserRoleAssignment
        {
            UserId = userId,
            RoleId = roleId,
            BranchId = branchId
        };
    }
}
