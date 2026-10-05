using MediatR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Identity.Password.ChangePassword;

public class ChangePasswordHandler : IRequestHandler<ChangePasswordCommand>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IPasswordHasher _hasher;
    private readonly IAuditWriter _audit;
    private readonly IMemoryCache _cache;

    public ChangePasswordHandler(
        IAppDbContext db,
        IAccessScope scope,
        IPasswordHasher hasher,
        IAuditWriter audit,
        IMemoryCache cache)
    {
        _db = db;
        _scope = scope;
        _hasher = hasher;
        _audit = audit;
        _cache = cache;
    }

    public async Task Handle(ChangePasswordCommand request, CancellationToken ct)
    {
        if (!_scope.IsAuthenticated)
            throw new DomainException("unauthenticated", "Authentication is required.");

        var user = await _db.Set<User>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(u => u.Id == _scope.UserId, ct);

        if (user is null)
            throw new DomainException("user_not_found", "User not found.");

        if (!_hasher.Verify(request.CurrentPassword, user.PasswordHash))
            throw new DomainException("current_password_incorrect", "The current password provided is incorrect.");

        if (request.CurrentPassword == request.NewPassword)
            throw new DomainValidationException("password_must_differ", "New password must differ from current password.");

        user.ChangePasswordHash(_hasher.Hash(request.NewPassword));
        user.BumpTokenVersion();
        _cache.Remove($"token_version:{user.Id}");

        // Revoke all refresh tokens for this user
        var tokens = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .Where(t => t.UserId == user.Id && !t.IsRevoked)
            .ToListAsync(ct);
        foreach (var t in tokens)
        {
            t.Revoke();
        }

        // Audit log (never log password material)
        await _audit.WriteAsync(
            branchId: null,
            action: "user.password.changed",
            entityType: "user",
            entityId: user.Id.ToString(),
            changes: new { note = "User changed own password" },
            ct: ct);

        await _db.SaveChangesAsync(ct);
    }
}
