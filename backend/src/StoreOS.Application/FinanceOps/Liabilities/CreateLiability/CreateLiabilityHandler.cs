using MediatR;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.FinanceOps;

namespace StoreOS.Application.FinanceOps.Liabilities.CreateLiability;

public class CreateLiabilityHandler : IRequestHandler<CreateLiabilityCommand, Guid>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public CreateLiabilityHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Guid> Handle(CreateLiabilityCommand request, CancellationToken ct)
    {
        var liability = Liability.Create(
            _scope.TenantId,
            request.Title,
            request.Type,
            request.LenderName,
            request.TotalPrincipal,
            request.DueDate,
            request.Notes);

        await _db.Set<Liability>().AddAsync(liability, ct);
        await _db.SaveChangesAsync(ct);

        return liability.Id;
    }
}
