using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.Infrastructure.Sales;

public class InvoiceSequenceAllocator : IInvoiceSequenceAllocator
{
    private readonly StoreOsDbContext _db;
    public InvoiceSequenceAllocator(StoreOsDbContext db) => _db = db;

    public async Task<int> AllocateNextAsync(Guid tenantId, Guid branchId, string seriesKey, CancellationToken ct)
    {
        var connection = _db.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open) await connection.OpenAsync(ct);
        var transaction = _db.Database.CurrentTransaction?.GetDbTransaction();

        await using var cmd = connection.CreateCommand();
        cmd.Transaction = transaction;
        cmd.CommandText = @"
            WITH max_sales AS (
                SELECT COALESCE(
                    MAX(
                        NULLIF(
                            regexp_replace(invoice_number, '^.*\/', ''),
                            ''
                        )::int
                    ),
                    0
                ) AS max_num
                FROM sales
                WHERE tenant_id = @t AND branch_id = @b
                  AND ((@s LIKE 'EST-%' AND invoice_type = 'Estimate') OR (@s NOT LIKE 'EST-%' AND invoice_type = 'TaxInvoice'))
            )
            INSERT INTO invoice_sequences (tenant_id, branch_id, series_key, last_seq)
            SELECT @t, @b, @s, (SELECT max_num FROM max_sales) + 1
            ON CONFLICT (tenant_id, branch_id, series_key)
            DO UPDATE SET last_seq = GREATEST(invoice_sequences.last_seq, (SELECT max_num FROM max_sales)) + 1
            RETURNING last_seq;";
        AddParam(cmd, "t", tenantId);
        AddParam(cmd, "b", branchId);
        AddParam(cmd, "s", seriesKey);
        var result = await cmd.ExecuteScalarAsync(ct);
        return (int)result!;
    }

    private static void AddParam(System.Data.Common.DbCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }
}
