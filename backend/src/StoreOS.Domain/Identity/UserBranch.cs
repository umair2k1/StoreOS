namespace StoreOS.Domain.Identity;

public class UserBranch
{
    public Guid UserId { get; private set; }
    public Guid BranchId { get; private set; }
    public bool IsDefault { get; private set; }

    private UserBranch() { }

    public static UserBranch Create(Guid userId, Guid branchId, bool isDefault = false)
    {
        return new UserBranch
        {
            UserId = userId,
            BranchId = branchId,
            IsDefault = isDefault
        };
    }

    public void SetDefault(bool isDefault) => IsDefault = isDefault;
}
