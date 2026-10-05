using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;

namespace StoreOS.Application.Identity.Login;

public class LoginHandler : IRequestHandler<LoginCommand, LoginResult>
{
    private readonly IAppDbContext _db;
    private readonly IPasswordHasher _hasher;
    private readonly IJwtTokenService _jwt;

    public LoginHandler(IAppDbContext db, IPasswordHasher hasher, IJwtTokenService jwt)
    {
        _db = db;
        _hasher = hasher;
        _jwt = jwt;
    }

    public async Task<LoginResult> Handle(LoginCommand request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        // IgnoreQueryFilters: this is the one sanctioned pre-authentication exception noted
        // in P-107 — no IAccessScope exists yet, and tenant is disambiguated explicitly by
        // request.TenantId rather than by an ambient scope.
        var user = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.TenantId == request.TenantId && u.Email == normalizedEmail, ct);

        if (user is null || !user.IsActive || !_hasher.Verify(request.Password, user.PasswordHash))
            throw new DomainException("invalid_credentials", "Invalid email or password.");

        var tenant = await _db.Set<StoreOS.Domain.Tenancy.Tenant>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.Id == request.TenantId, ct);
        if (tenant is null || !tenant.IsActive)
            throw new DomainException("tenant_inactive", "This tenant account is not active.");

        var totalAssignments = await _db.Set<StoreOS.Domain.Identity.UserRoleAssignment>().IgnoreQueryFilters()
            .CountAsync(a => a.UserId == user.Id, ct);
        var branchSpecificCount = await (
            from a in _db.Set<StoreOS.Domain.Identity.UserRoleAssignment>().IgnoreQueryFilters()
            join b in _db.Set<StoreOS.Domain.Tenancy.Branch>().IgnoreQueryFilters() on a.BranchId equals b.Id
            where a.UserId == user.Id
            select a.RoleId
        ).CountAsync(ct);
        var allBranches = totalAssignments > branchSpecificCount;

        var roleIds = await _db.Set<StoreOS.Domain.Identity.UserRoleAssignment>().IgnoreQueryFilters()
            .Where(a => a.UserId == user.Id)
            .Select(a => a.RoleId)
            .Distinct()
            .ToListAsync(ct);

        var permissions = await _db.Set<StoreOS.Domain.Identity.RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionCode)
            .Distinct()
            .ToListAsync(ct);

        var userBranchIds = await _db.Set<StoreOS.Domain.Identity.UserBranch>().IgnoreQueryFilters()
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => ub.BranchId)
            .ToListAsync(ct);

        var (token, expiresAt) = _jwt.IssueAccessToken(
            user.Id, user.TenantId, allBranches, userBranchIds, permissions, user.TokenVersion);

        return new LoginResult(
            token,
            expiresAt,
            user.Id,
            user.DisplayName,
            user.TenantId,
            allBranches,
            userBranchIds,
            permissions);
    }
}
