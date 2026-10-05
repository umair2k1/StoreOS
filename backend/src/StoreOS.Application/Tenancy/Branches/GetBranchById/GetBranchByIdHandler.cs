using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Tenancy.Branches.GetBranchById;

public class GetBranchByIdHandler : IRequestHandler<GetBranchByIdQuery, BranchDto>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public GetBranchByIdHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<BranchDto> Handle(GetBranchByIdQuery request, CancellationToken ct)
    {
        if (!_scope.HasBranchAccess(request.Id))
            throw new DomainException("forbidden", "You do not have access to the specified branch.");

        var branch = await _db.Set<Branch>()
            .Where(b => b.Id == request.Id)
            .Select(b => new BranchDto(
                b.Id,
                b.Code,
                b.Name,
                b.Address,
                b.Phone,
                b.Email,
                b.Gstin,
                b.IsActive))
            .FirstOrDefaultAsync(ct);

        if (branch is null)
            throw new DomainException("branch_not_found", $"Branch with ID '{request.Id}' was not found.");

        return branch;
    }
}
