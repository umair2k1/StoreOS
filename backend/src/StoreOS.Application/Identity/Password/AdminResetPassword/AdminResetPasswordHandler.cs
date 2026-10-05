using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Identity.Password.AdminResetPassword;

public class AdminResetPasswordHandler : IRequestHandler<AdminResetPasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    private readonly IMemoryCache _cache;

    public AdminResetPasswordHandler(
        IAppDbContext db,
        IAccessScope scope,
        IPasswordHasher hasher,
        IAuditWriter audit,
        IMemoryCache cache)
    {
        _db = db;
        _scope = scope;
        _hasher = hasher;
        _audit = audit;
        _cache = cache;
    }

    public async Task Handle(AdminResetPasswordCommand request, CancellationToken ct)
    {
        var targetUser = await _db.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.TenantId == _scope.TenantId, ct);
        if (targetUser is null)
            throw new DomainException("user_not_found", "The specified user does not exist in this tenant.");

        // Rule: Owner accounts (tenant-wide assignment, branch_id = null) may only be reset by another Owner
        var targetTotalAssignments = await _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
            .CountAsync(a => a.UserId == targetUser.Id, ct);
        var targetBranchAssignments = await (
            from a in _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
            join b in _db.Set<Branch>().IgnoreQueryFilters() on a.BranchId equals b.Id
            where a.UserId == targetUser.Id
            select a.RoleId
        ).CountAsync(ct);
        var targetIsOwner = targetTotalAssignments > targetBranchAssignments;

        var callerIsOwner = _scope.AllBranches;
        if (!callerIsOwner)
        {
            var callerTotal = await _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
                .CountAsync(a => a.UserId == _scope.UserId, ct);
            var callerBranch = await (
                from a in _db.Set<UserRoleAssignment>().IgnoreQueryFilters()
                join b in _db.Set<Branch>().IgnoreQueryFilters() on a.BranchId equals b.Id
                where a.UserId == _scope.UserId
                select a.RoleId
            ).CountAsync(ct);
            callerIsOwner = callerTotal > callerBranch;
        }

        if (targetIsOwner && !callerIsOwner)
            throw new DomainException("forbidden", "Only an Owner with tenant-wide access can reset an Owner account password.");

        targetUser.ChangePasswordHash(_hasher.Hash(request.NewTemporaryPassword));
        targetUser.BumpTokenVersion();
        _cache.Remove($"token_version:{targetUser.Id}");

        // Revoke all refresh tokens for target user
        var tokens = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .Where(t => t.UserId == targetUser.Id && !t.IsRevoked)
            .ToListAsync(ct);
        foreach (var t in tokens)
        {
            t.Revoke();
        }

        // Write audit log (never log password material)
        await _audit.WriteAsync(
            branchId: null,
            action: "user.password.reset",
            entityType: "user",
            entityId: targetUser.Id.ToString(),
            changes: new { note = "Admin reset user password" },
            ct: ct);

        await _db.SaveChangesAsync(ct);
    }
}
