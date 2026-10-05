using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Identity.Roles.GetRoles;

public class GetRolesHandler : IRequestHandler<GetRolesQuery, IReadOnlyList<RoleDto>>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetRolesHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<IReadOnlyList<RoleDto>> Handle(GetRolesQuery request, CancellationToken ct)
    {
        var roles = await _db.Set<Role>()
            .Where(r => r.TenantId == _scope.TenantId)
            .OrderBy(r => r.Name)
            .ToListAsync(ct);

        var roleIds = roles.Select(r => r.Id).ToList();
        var permissions = await _db.Set<RolePermission>()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .ToListAsync(ct);

        var permLookup = permissions
            .GroupBy(rp => rp.RoleId)
            .ToDictionary(g => g.Key, g => g.Select(rp => rp.PermissionCode).ToList());

        return roles.Select(r => new RoleDto(
            r.Id,
            r.Name,
            permLookup.TryGetValue(r.Id, out var perms) ? perms : Array.Empty<string>()))
            .ToList();
    }
}
