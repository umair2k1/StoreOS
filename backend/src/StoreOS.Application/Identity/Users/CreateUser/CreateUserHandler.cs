using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Identity.Users.CreateUser;

public class CreateUserHandler : IRequestHandler<CreateUserCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IPasswordHasher _hasher;

    public CreateUserHandler(IAppDbContext db, IAccessScope scope, IPasswordHasher hasher)
    {
        _db = db;
        _scope = scope;
        _hasher = hasher;
    }

    public async Task<Guid> Handle(CreateUserCommand request, CancellationToken ct)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();
        var exists = await _db.Set<User>().IgnoreQueryFilters()
            .AnyAsync(u => u.TenantId == _scope.TenantId && u.Email == normalizedEmail, ct);
        if (exists)
            throw new DomainException("email_already_registered", "A user with this email is already registered.");

        var passwordHash = _hasher.Hash(request.Password);
        var user = User.Create(_scope.TenantId, normalizedEmail, passwordHash, request.DisplayName);
        await _db.Set<User>().AddAsync(user, ct);

        if (request.InitialRoleId.HasValue)
        {
            var roleExists = await _db.Set<Role>()
                .AnyAsync(r => r.TenantId == _scope.TenantId && r.Id == request.InitialRoleId.Value, ct);
            if (!roleExists)
                throw new DomainException("role_not_found", "The specified role does not exist in this tenant.");

            if (request.InitialBranchId.HasValue)
            {
                var branchExists = await _db.Set<Branch>()
                    .AnyAsync(b => b.TenantId == _scope.TenantId && b.Id == request.InitialBranchId.Value, ct);
                if (!branchExists)
                    throw new DomainException("branch_not_found", "The specified branch does not exist in this tenant.");

                var branchAssignment = UserBranch.Create(user.Id, request.InitialBranchId.Value, isDefault: true);
                await _db.Set<UserBranch>().AddAsync(branchAssignment, ct);
            }

            if (request.InitialBranchId.HasValue)
            {
                var roleAssignment = UserRoleAssignment.Create(user.Id, request.InitialRoleId.Value, request.InitialBranchId.Value);
                await _db.Set<UserRoleAssignment>().AddAsync(roleAssignment, ct);
                await _db.SaveChangesAsync(ct);
            }
            else
            {
                await _db.SaveChangesAsync(ct);
                await _db.ExecuteRawSqlAsync(
                    "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES ({0}, {1}, NULL)",
                    new object[] { user.Id, request.InitialRoleId.Value }, ct);
            }
            return user.Id;
        }

        await _db.SaveChangesAsync(ct);
        return user.Id;
    }
}
