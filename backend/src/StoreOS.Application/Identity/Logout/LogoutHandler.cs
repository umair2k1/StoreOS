using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;

namespace StoreOS.Application.Identity.Logout;

public class LogoutHandler : IRequestHandler<LogoutCommand>
{
    private readonly IAppDbContext _db;
    public LogoutHandler(IAppDbContext db) => _db = db;

    public async Task Handle(LogoutCommand request, CancellationToken ct)
    {
        var hash = RefreshTokenFormat.Hash(request.RawRefreshToken);
        var token = await _db.Set<RefreshToken>().IgnoreQueryFilters()
            .FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        token?.Revoke(); // idempotent: if already revoked or not found, logout still "succeeds"
        await _db.SaveChangesAsync(ct);
    }
}
