namespace StoreOS.Application.Abstractions;

public interface IAccessScope
{
    Guid TenantId { get; }
    Guid UserId { get; }
    IReadOnlySet<Guid> BranchIds { get; }
    bool AllBranches { get; }
    IReadOnlySet<string> Permissions { get; }
    int TokenVersion { get; }
    bool IsAuthenticated { get; }
    /// <summary>
    /// True when a tenant id is known (authenticated user OR an anonymous flow that called UseAnonymousTenant).
    /// </summary>
    bool HasTenantContext { get; }
    /// <summary>
    /// Pre-authentication flows (login, refresh, logout) declare the tenant explicitly. Sets TenantId only;
    /// BranchIds stays empty, AllBranches=false, IsAuthenticated stays false.
    /// </summary>
    void UseAnonymousTenant(Guid tenantId);
    /// <summary>
    /// Background jobs (reconciliation, imports): TenantId set, AllBranches=true, no permissions, IsAuthenticated=false.
    /// </summary>
    void UseSystemContext(Guid tenantId);

    bool HasBranchAccess(Guid branchId);
    bool HasPermission(string permissionCode);
}
