using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Tenancy.Branches.UpdateBranch;

public class UpdateBranchHandler : IRequestHandler<UpdateBranchCommand>
{
    private readonly IAppDbContext _db;

    public UpdateBranchHandler(IAppDbContext db) => _db = db;

    public async Task Handle(UpdateBranchCommand request, CancellationToken ct)
    {
        var branch = await _db.Set<Branch>().FirstOrDefaultAsync(b => b.Id == request.Id, ct);
        if (branch is null)
            throw new DomainException("branch_not_found", $"Branch with ID '{request.Id}' was not found.");

        branch.UpdateProfile(request.Name, request.Address, request.Phone, request.Email, request.Gstin);

        if (request.IsActive && !branch.IsActive)
        {
            branch.Activate();
        }
        else if (!request.IsActive && branch.IsActive)
        {
            branch.Deactivate();
        }

        await _db.SaveChangesAsync(ct);
    }
}
