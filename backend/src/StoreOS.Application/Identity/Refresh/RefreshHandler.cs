using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Identity.Refresh;

public class RefreshHandler : IRequestHandler<RefreshCommand, RefreshResult>
{
    private readonly IAppDbContext _db;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IJwtTokenService _jwt;

    public RefreshHandler(IAppDbContext db, IRefreshTokenService refreshTokenService, IJwtTokenService jwt)
    {
        _db = db;
        _refreshTokenService = refreshTokenService;
        _jwt = jwt;
    }

    public async Task<RefreshResult> Handle(RefreshCommand request, CancellationToken ct)
    {
        var hash = RefreshTokenFormat.Hash(request.RawRefreshToken);
        var token = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);

        if (token is null)
            throw new DomainException("invalid_refresh_token", "Refresh token is invalid.");

        if (token.IsRevoked)
        {
            // THEFT / REPLAY DETECTED: this token was already rotated once before. Revoke
            // the ENTIRE family and bump the user's token_version so all active access
            // tokens are invalidated on next stale-check (P-307).
            var family = await _db.Set<RefreshToken>().IgnoreQueryFilters()
                .Where(t => t.FamilyId == token.FamilyId).ToListAsync(ct);
            foreach (var t in family) t.Revoke();
            var affectedUser = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
                .FirstAsync(u => u.Id == token.UserId, ct);
            affectedUser.BumpTokenVersion();
            await _db.SaveChangesAsync(ct);

            return new RefreshResult(
                string.Empty,
                DateTimeOffset.MinValue,
                string.Empty,
                affectedUser.Id,
                affectedUser.DisplayName,
                affectedUser.TenantId,
                false,
                Array.Empty<Guid>(),
                Array.Empty<string>(),
                ReuseDetected: true);
        }

        if (token.IsExpired(DateTimeOffset.UtcNow))
            throw new DomainException("refresh_token_expired", "Refresh token has expired. Please log in again.");

        var user = await _db.Set<StoreOS.Domain.Identity.User>().IgnoreQueryFilters()
            .FirstAsync(u => u.Id == token.UserId, ct);
        if (!user.IsActive)
            throw new DomainException("invalid_refresh_token", "Refresh token is invalid.");

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

        var permissions = await _db.Set<RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .Select(rp => rp.PermissionCode)
            .Distinct()
            .ToListAsync(ct);

        var userBranchIds = await _db.Set<UserBranch>().IgnoreQueryFilters()
            .Where(ub => ub.UserId == user.Id)
            .Select(ub => ub.BranchId)
            .ToListAsync(ct);

        // Rotate: issue a new refresh token in the SAME family, mark the presented one as
        // rotated-into it.
        var (newRawToken, newRecord) = await _refreshTokenService.IssueAsync(user.TenantId, user.Id, token.FamilyId, ct);
        await _db.SaveChangesAsync(ct); // ensure newRecord has its Id assigned before linking
        token.RotateTo(newRecord.Id);
        await _db.SaveChangesAsync(ct);

        var (accessToken, expiresAt) = _jwt.IssueAccessToken(
            user.Id, user.TenantId, allBranches, userBranchIds, permissions, user.TokenVersion);

        return new RefreshResult(
            accessToken,
            expiresAt,
            newRawToken,
            user.Id,
            user.DisplayName,
            user.TenantId,
            allBranches,
            userBranchIds,
            permissions);
    }
}
