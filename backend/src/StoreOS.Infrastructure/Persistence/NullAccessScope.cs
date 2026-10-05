using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class NullAccessScope : IAccessScope
{
    private static readonly IReadOnlySet<Guid> EmptyBranchIds = new HashSet<Guid>();
    private static readonly IReadOnlySet<string> EmptyPermissions = new HashSet<string>();

    public Guid TenantId => Guid.Empty;
    public Guid UserId => Guid.Empty;
    public IReadOnlySet<Guid> BranchIds => EmptyBranchIds;
    public bool AllBranches => false;
    public IReadOnlySet<string> Permissions => EmptyPermissions;
    public int TokenVersion => 0;
    public bool IsAuthenticated => false;
    public bool HasTenantContext => false;

    public void UseAnonymousTenant(Guid tenantId) => throw new InvalidOperationException("NullAccessScope does not support switching tenant.");
    public void UseSystemContext(Guid tenantId) => throw new InvalidOperationException("NullAccessScope does not support system context.");

    public bool HasBranchAccess(Guid branchId) => false;
    public bool HasPermission(string permissionCode) => false;
}
