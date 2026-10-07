using MediatR;
using StoreOS.Application.Abstractions;

namespace StoreOS.Application.Settings.PatchSetting;

public class PatchSettingHandler : IRequestHandler<PatchSettingCommand, Unit>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public PatchSettingHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<Unit> Handle(PatchSettingCommand request, CancellationToken ct)
    {
        var key = request.Key.Trim();

        if (request.BranchId == null)
        {
            await _db.ExecuteRawSqlAsync(@"
                INSERT INTO settings (tenant_id, branch_id, key, value)
                VALUES ({0}, NULL, {1}, {2}::jsonb)
                ON CONFLICT (tenant_id, branch_id_key, key)
                DO UPDATE SET value = EXCLUDED.value",
                new object[] { _scope.TenantId, key, request.ValueJson },
                ct);
        }
        else
        {
            await _db.ExecuteRawSqlAsync(@"
                INSERT INTO settings (tenant_id, branch_id, key, value)
                VALUES ({0}, {1}, {2}, {3}::jsonb)
                ON CONFLICT (tenant_id, branch_id_key, key)
                DO UPDATE SET value = EXCLUDED.value",
                new object[] { _scope.TenantId, request.BranchId.Value, key, request.ValueJson },
                ct);
        }

        return Unit.Value;
    }
}
