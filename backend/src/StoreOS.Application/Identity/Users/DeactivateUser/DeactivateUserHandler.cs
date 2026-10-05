using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Identity.Users.DeactivateUser;

public class DeactivateUserHandler : IRequestHandler<DeactivateUserCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public DeactivateUserHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task Handle(DeactivateUserCommand request, CancellationToken ct)
    {
        var user = await _db.Set<User>()
            .FirstOrDefaultAsync(u => u.Id == request.UserId && u.TenantId == _scope.TenantId, ct);
        if (user is null)
            throw new DomainException("user_not_found", "The specified user does not exist in this tenant.");

        user.Deactivate();
        await _db.SaveChangesAsync(ct);
    }
}
