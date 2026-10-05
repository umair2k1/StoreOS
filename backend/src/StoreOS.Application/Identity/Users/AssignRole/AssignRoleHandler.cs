using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Identity.Users.AssignRole;

public class AssignRoleHandler : IRequestHandler<AssignRoleCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public AssignRoleHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(AssignRoleCommand request, CancellationToken ct)
    {
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.TenantId == _scope.TenantId, ct);
        if (user is null)
            throw new DomainException("user_not_found", "The specified user does not exist in this tenant.");

        var role = await _db.Set<Role>()
            .FirstOrDefaultAsync(r => r.Id == request.RoleId && r.TenantId == _scope.TenantId, ct);
        if (role is null)
            throw new DomainException("role_not_found", "The specified role does not exist in this tenant.");

        if (request.BranchId.HasValue)
        {
            var branch = await _db.Set<Branch>()
                .FirstOrDefaultAsync(b => b.Id == request.BranchId.Value && b.TenantId == _scope.TenantId, ct);
            if (branch is null)
                throw new DomainException("branch_not_found", "The specified branch does not exist in this tenant.");

            var assignment = UserRoleAssignment.Create(request.UserId, request.RoleId, request.BranchId.Value);
            await _db.Set<UserRoleAssignment>().AddAsync(assignment, ct);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            await _db.ExecuteRawSqlAsync(
                "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES ({0}, {1}, NULL)",
                new object[] { request.UserId, request.RoleId }, ct);
        }
    }
}
