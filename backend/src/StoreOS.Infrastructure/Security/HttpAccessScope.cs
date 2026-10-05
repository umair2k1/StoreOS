using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Security;

public class HttpAccessScope : IAccessScope
{
    private Guid _tenantId;
    private readonly Guid _userId;
    private readonly HashSet<Guid> _branchIds;
    private bool _allBranches;
    private readonly HashSet<string> _permissions;
    private readonly int _tokenVersion;
    private readonly bool _isAuthenticated;

    public HttpAccessScope(IHttpContextAccessor accessor)
    {
        var user = accessor.HttpContext?.User;
        _isAuthenticated = user?.Identity?.IsAuthenticated ?? false;
        if (!_isAuthenticated)
        {
            _tenantId = Guid.Empty;
            _userId = Guid.Empty;
            _branchIds = new HashSet<Guid>();
            _permissions = new HashSet<string>();
            return;
        }
        _tenantId = Guid.Parse(user!.FindFirstValue("tenant_id")!);
        _userId = Guid.Parse(user!.FindFirstValue("sub")!);
        _allBranches = user!.FindFirstValue("all_branches") == "true";
        _branchIds = user!.FindAll("branch_id").Select(c => Guid.Parse(c.Value)).ToHashSet();
        _permissions = user.FindAll("perm").Select(c => c.Value).ToHashSet();
        _tokenVersion = int.Parse(user!.FindFirstValue("token_version") ?? "0");
    }

    public Guid TenantId => _tenantId;
    public bool HasTenantContext => _tenantId != Guid.Empty;
    public void UseSystemContext(Guid tenantId)
    {
        if (_isAuthenticated) throw new InvalidOperationException("Authenticated scopes cannot switch tenant.");
        _tenantId = tenantId;
        _allBranches = true;
    }
    public void UseAnonymousTenant(Guid tenantId)
    {
        if (_isAuthenticated) throw new InvalidOperationException("Authenticated scopes cannot switch tenant.");
        _tenantId = tenantId;
    }
    public Guid UserId => _userId;
    public IReadOnlySet<Guid> BranchIds => _branchIds;
    public bool AllBranches => _allBranches;
    public IReadOnlySet<string> Permissions => _permissions;
    public int TokenVersion => _tokenVersion;
    public bool IsAuthenticated => _isAuthenticated;

    public bool HasBranchAccess(Guid branchId) => _allBranches || _branchIds.Contains(branchId);
    public bool HasPermission(string permissionCode) => _permissions.Contains(permissionCode);
}
