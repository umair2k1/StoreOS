using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using StoreOS.Application.Abstractions;

namespace StoreOS.Infrastructure.Persistence;

public sealed class RlsSessionInterceptor : DbTransactionInterceptor
{
    private readonly IAccessScope _scope;

    public RlsSessionInterceptor(IAccessScope scope) => _scope = scope;

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection, TransactionEndEventData eventData, DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        if (_scope.HasTenantContext)
        {
            var branchIdsCsv = _scope.BranchIds.Count > 0
                ? string.Join(",", _scope.BranchIds)
                : "00000000-0000-0000-0000-000000000000";

            await using var cmd = connection.CreateCommand();
            cmd.Transaction = result;
            cmd.CommandText = "SELECT set_config('app.tenant_id', @t, true), set_config('app.branch_ids', @b, true), set_config('app.all_branches', @a, true)";
            Add(cmd, "@t", _scope.TenantId.ToString());
            Add(cmd, "@b", branchIdsCsv);
            Add(cmd, "@a", _scope.AllBranches ? "true" : "false");
            await cmd.ExecuteNonQueryAsync(cancellationToken);
        }
        return result;
    }

    public override DbTransaction TransactionStarted(DbConnection connection, TransactionEndEventData eventData, DbTransaction result)
        => TransactionStartedAsync(connection, eventData, result).AsTask().GetAwaiter().GetResult();

    private static void Add(DbCommand cmd, string name, string value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
