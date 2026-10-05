using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Tenancy.Branches.CreateBranch;

public class CreateBranchHandler : IRequestHandler<CreateBranchCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public CreateBranchHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(CreateBranchCommand request, CancellationToken ct)
    {
        var exists = await _db.Set<Branch>().AnyAsync(b => b.Code == request.Code, ct);
        if (exists) throw new DomainException("branch_code_taken", $"Branch code '{request.Code}' is already in use.");

        var branch = Branch.Create(_scope.TenantId, request.Code, request.Name);
        branch.UpdateProfile(request.Name, request.Address, request.Phone, request.Email, request.Gstin);

        await _db.Set<Branch>().AddAsync(branch, ct);
        await _db.SaveChangesAsync(ct);
        return branch.Id;
    }
}
