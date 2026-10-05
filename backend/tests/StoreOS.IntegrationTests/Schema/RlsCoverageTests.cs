using FluentAssertions;
using Npgsql;

namespace StoreOS.IntegrationTests.Schema;

[Collection("postgres")]
public class RlsCoverageTests : IClassFixture<PostgresTestFixture>
{
    private readonly PostgresTestFixture _fixture;
    public RlsCoverageTests(PostgresTestFixture fixture) => _fixture = fixture;

    private static readonly HashSet<string> DocumentedExceptions = new() { "tenants" };

    [Fact]
    public async Task Every_table_with_tenant_id_column_has_rls_enabled_and_forced()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT c.relname
            FROM pg_class c
            JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind = 'r'
              AND EXISTS (
                SELECT 1 FROM information_schema.columns col
                WHERE col.table_schema = 'public' AND col.table_name = c.relname
                  AND col.column_name = 'tenant_id'
              )
              AND (c.relrowsecurity = false OR c.relforcerowsecurity = false);";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync()) offenders.Add(reader.GetString(0));
        offenders = offenders.Where(o => !DocumentedExceptions.Contains(o)).ToList();
        offenders.Should().BeEmpty("every table with a tenant_id column must have RLS enabled AND forced, except documented exceptions: {0}", string.Join(",", DocumentedExceptions));
    }

    [Fact]
    public async Task No_policy_contains_forbidden_null_bypass()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = "SELECT policyname, tablename, qual FROM pg_policies WHERE schemaname = 'public';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync())
        {
            var qual = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            if (qual.Contains("IS NULL", StringComparison.OrdinalIgnoreCase) &&
                qual.Contains("current_setting", StringComparison.OrdinalIgnoreCase))
                offenders.Add($"{reader.GetString(1)}.{reader.GetString(0)}");
        }
        offenders.Should().BeEmpty("no RLS policy may bypass isolation via 'current_setting(...) IS NULL': {0}", string.Join(",", offenders));
    }

    [Fact]
    public async Task Every_table_with_tenant_id_has_at_least_one_policy()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = @"
            SELECT col.table_name
            FROM information_schema.columns col
            WHERE col.table_schema = 'public' AND col.column_name = 'tenant_id'
              AND NOT EXISTS (
                SELECT 1 FROM pg_policies p
                WHERE p.schemaname = 'public' AND p.tablename = col.table_name
              );";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync()) offenders.Add(reader.GetString(0));
        offenders = offenders.Where(o => !DocumentedExceptions.Contains(o)).ToList();
        offenders.Should().BeEmpty("tables with tenant_id must have at least one RLS policy: {0}", string.Join(",", DocumentedExceptions));
    }

    [Fact]
    public async Task Every_policy_using_current_setting_contains_nullif()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        const string sql = "SELECT policyname, tablename, qual, with_check FROM pg_policies WHERE schemaname = 'public';";
        await using var cmd = new NpgsqlCommand(sql, conn);
        await using var reader = await cmd.ExecuteReaderAsync();
        var offenders = new List<string>();
        while (await reader.ReadAsync())
        {
            var policy = $"{reader.GetString(1)}.{reader.GetString(0)}";
            var qual = reader.IsDBNull(2) ? string.Empty : reader.GetString(2);
            var withCheck = reader.IsDBNull(3) ? string.Empty : reader.GetString(3);

            if (qual.Contains("current_setting", StringComparison.OrdinalIgnoreCase) &&
                !qual.Contains("NULLIF(", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add($"{policy} (qual)");
            }

            if (withCheck.Contains("current_setting", StringComparison.OrdinalIgnoreCase) &&
                !withCheck.Contains("NULLIF(", StringComparison.OrdinalIgnoreCase))
            {
                offenders.Add($"{policy} (with_check)");
            }
        }
        offenders.Should().BeEmpty("every policy reading current_setting must sanitize via NULLIF(...): {0}", string.Join(",", offenders));
    }

    [Fact]
    public async Task Consecutive_transactions_on_single_connection_do_not_leak_tenant_context()
    {
        var tenantId = Guid.NewGuid();
        var branchId = Guid.NewGuid();
        var sub = "rls-leak-" + tenantId.ToString("N")[..8];

        await using (var ownerConn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await ownerConn.OpenAsync();
            await using (var cmd = new NpgsqlCommand(
                "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
                "VALUES (@tId, 'RLS Leak Test Tenant', @sub, true, 'Asia/Kolkata', '{}'::jsonb, now()); " +
                "INSERT INTO branches (id, tenant_id, code, name, is_active, created_at) " +
                "VALUES (@bId, @tId, 'L01', 'RLS Leak Branch', true, now());", ownerConn))
            {
                cmd.Parameters.AddWithValue("tId", tenantId);
                cmd.Parameters.AddWithValue("sub", sub);
                cmd.Parameters.AddWithValue("bId", branchId);
                await cmd.ExecuteNonQueryAsync();
            }
        }

        try
        {
            await using var appConn = new NpgsqlConnection(_fixture.AppConnectionString);
            await appConn.OpenAsync();

            // Transaction 1: with SET LOCAL app.tenant_id
            await using (var tx1 = await appConn.BeginTransactionAsync())
            {
                await using (var cmd = new NpgsqlCommand("SELECT set_config('app.tenant_id', @tId, true);", appConn, tx1))
                {
                    cmd.Parameters.AddWithValue("tId", tenantId.ToString());
                    await cmd.ExecuteScalarAsync();
                }

                await using (var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM branches;", appConn, tx1))
                {
                    var count = (long)(await cmd.ExecuteScalarAsync())!;
                    count.Should().Be(1, "transaction 1 with tenant set must see its branch");
                }

                await tx1.CommitAsync();
            }

            // Transaction 2: on the SAME raw connection, with NO set_config
            await using (var tx2 = await appConn.BeginTransactionAsync())
            {
                await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM branches;", appConn, tx2);
                var count = (long)(await cmd.ExecuteScalarAsync())!;
                count.Should().Be(0, "transaction 2 without tenant context on reused connection must see 0 rows under RLS");
                await tx2.CommitAsync();
            }
        }
        finally
        {
            await using var cleanupConn = new NpgsqlConnection(_fixture.OwnerConnectionString);
            await cleanupConn.OpenAsync();
            await using var cleanupCmd = new NpgsqlCommand(
                "DELETE FROM branches WHERE id = @bId; " +
                "DELETE FROM tenants WHERE id = @tId;", cleanupConn);
            cleanupCmd.Parameters.AddWithValue("bId", branchId);
            cleanupCmd.Parameters.AddWithValue("tId", tenantId);
            await cleanupCmd.ExecuteNonQueryAsync();
        }
    }
}
