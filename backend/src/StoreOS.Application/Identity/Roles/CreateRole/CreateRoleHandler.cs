using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Identity.Roles.CreateRole;

public class CreateRoleHandler : IRequestHandler<CreateRoleCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IPermissionChecker _permissionChecker;

    public CreateRoleHandler(IAppDbContext db, IAccessScope scope, IPermissionChecker permissionChecker)
    {
        _db = db;
        _scope = scope;
        _permissionChecker = permissionChecker;
    }

    public async Task<Guid> Handle(CreateRoleCommand request, CancellationToken ct)
    {
        var invalid = request.PermissionCodes.Where(c => !_permissionChecker.IsValidPermissionCode(c)).ToList();
        if (invalid.Count > 0)
            throw new DomainValidationException("invalid_permission_code",
                $"Unknown permission code(s): {string.Join(", ", invalid)}");

        var role = Role.Create(_scope.TenantId, request.Name);
        await _db.Set<Role>().AddAsync(role, ct);

        foreach (var code in request.PermissionCodes.Distinct())
            await _db.Set<RolePermission>().AddAsync(RolePermission.Create(role.Id, code), ct);

        await _db.SaveChangesAsync(ct);
        return role.Id;
    }
}
