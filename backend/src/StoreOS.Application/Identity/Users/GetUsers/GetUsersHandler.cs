using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Identity.Users.GetUsers;

public class GetUsersHandler : IRequestHandler<GetUsersQuery, IReadOnlyList<UserDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetUsersHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IReadOnlyList<UserDto>> Handle(GetUsersQuery request, CancellationToken ct)
    {
        var users = await _db.Set<User>()
            .Where(u => u.TenantId == _scope.TenantId)
            .OrderBy(u => u.DisplayName)
            .ToListAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();

        var roles = await _db.Set<Role>()
            .Where(r => r.TenantId == _scope.TenantId)
            .ToDictionaryAsync(r => r.Id, r => r.Name, ct);

        var branches = await _db.Set<Branch>()
            .Where(b => b.TenantId == _scope.TenantId)
            .ToDictionaryAsync(b => b.Id, b => (b.Name, b.Code), ct);

        var assignments = await (
            from a in _db.Set<UserRoleAssignment>()
            join b in _db.Set<Branch>() on a.BranchId equals b.Id into branchGroup
            from b in branchGroup.DefaultIfEmpty()
            where userIds.Contains(a.UserId)
            select new { a.UserId, a.RoleId, BranchId = (Guid?)b.Id }
        ).ToListAsync(ct);

        var userBranches = await _db.Set<UserBranch>()
            .Where(ub => userIds.Contains(ub.UserId))
            .ToListAsync(ct);

        var roleLookup = assignments
            .GroupBy(a => a.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(a => new UserRoleAssignmentDto(
                    a.RoleId,
                    roles.TryGetValue(a.RoleId, out var rName) ? rName : "Unknown",
                    a.BranchId)).ToList());

        var branchLookup = userBranches
            .GroupBy(ub => ub.UserId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(ub => new UserBranchAssignmentDto(
                    ub.BranchId,
                    branches.TryGetValue(ub.BranchId, out var bInfo) ? bInfo.Name : "Unknown",
                    branches.TryGetValue(ub.BranchId, out var bInfo2) ? bInfo2.Code : "Unknown",
                    ub.IsDefault)).ToList());

        return users.Select(u => new UserDto(
            u.Id,
            u.Email,
            u.DisplayName,
            u.IsActive,
            u.TokenVersion,
            roleLookup.TryGetValue(u.Id, out var rList) ? rList : Array.Empty<UserRoleAssignmentDto>(),
            branchLookup.TryGetValue(u.Id, out var bList) ? bList : Array.Empty<UserBranchAssignmentDto>()))
            .ToList();
    }
}
