using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Security;

namespace StoreOS.Api.Middleware;

public class BranchContextMiddleware
{
    public const string HeaderName = "X-Branch-Id";
    private readonly RequestDelegate _next;
    public BranchContextMiddleware(RequestDelegate next) => _next = next;

    public async Task InvokeAsync(
        HttpContext context,
        IAccessScope scope,
        MutableBranchContext branchContext,
        StoreOsDbContext db)
    {
        if (!scope.IsAuthenticated || context.Request.Path.StartsWithSegments("/api/auth"))
        {
            await _next(context);
            return;
        }

        var isMutation = HttpMethods.IsPost(context.Request.Method)
            || HttpMethods.IsPut(context.Request.Method)
            || HttpMethods.IsPatch(context.Request.Method)
            || HttpMethods.IsDelete(context.Request.Method);

        Guid? branchId = null;
        if (context.Request.Headers.TryGetValue(HeaderName, out var headerVal) && Guid.TryParse(headerVal, out var parsed))
        {
            branchId = parsed;
        }
        else
        {
            await using var tx = await db.Database.BeginTransactionAsync();
            var defaultBranch = await db.Set<UserBranch>().IgnoreQueryFilters()
                .Where(ub => ub.UserId == scope.UserId)
                .OrderByDescending(ub => ub.IsDefault)
                .Select(ub => (Guid?)ub.BranchId)
                .FirstOrDefaultAsync();
            await tx.CommitAsync();
            branchId = defaultBranch;
        }

        if (branchId is null && isMutation)
            throw new DomainException("branch_context_required",
                "This operation requires an X-Branch-Id header (no default branch could be determined).");

        if (branchId is not null && !scope.HasBranchAccess(branchId.Value))
            throw new DomainException("forbidden", "You do not have access to the specified branch.");

        branchContext.CurrentBranchId = branchId;
        await _next(context);
    }
}
