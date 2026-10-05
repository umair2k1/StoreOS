using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Identity.Users.AssignBranch;

public class AssignBranchHandler : IRequestHandler<AssignBranchCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public AssignBranchHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(AssignBranchCommand request, CancellationToken ct)
    {
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.TenantId == _scope.TenantId, ct);
        if (user is null)
            throw new DomainException("user_not_found", "The specified user does not exist in this tenant.");

        var branch = await _db.Set<Branch>()
            .FirstOrDefaultAsync(b => b.Id == request.BranchId && b.TenantId == _scope.TenantId, ct);
        if (branch is null)
            throw new DomainException("branch_not_found", "The specified branch does not exist in this tenant.");

        // If setting as default, clear any existing default for this user
        if (request.IsDefault)
        {
            var existingDefaults = await _db.Set<UserBranch>()
                .Where(ub => ub.UserId == request.UserId && ub.IsDefault)
                .ToListAsync(ct);
            foreach (var ub in existingDefaults)
            {
                ub.SetDefault(false);
            }
        }

        var existingAssignment = await _db.Set<UserBranch>()
            .FirstOrDefaultAsync(ub => ub.UserId == request.UserId && ub.BranchId == request.BranchId, ct);

        if (existingAssignment is not null)
        {
            existingAssignment.SetDefault(request.IsDefault);
        }
        else
        {
            var assignment = UserBranch.Create(request.UserId, request.BranchId, request.IsDefault);
            await _db.Set<UserBranch>().AddAsync(assignment, ct);
        }

        await _db.SaveChangesAsync(ct);
    }
}
