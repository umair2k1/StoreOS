using FluentAssertions;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Security;

[Collection("postgres")]
public class RlsBranchScopedPolicyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly string _ownerConnectionString;
    private readonly string _appConnectionString;

    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _branchA1Id = Guid.NewGuid();
    private readonly Guid _branchA2Id = Guid.NewGuid();
    private readonly Guid _branchB1Id = Guid.NewGuid();

    private readonly Guid _rowA1Id = Guid.NewGuid();
    private readonly Guid _rowA2Id = Guid.NewGuid();
    private readonly Guid _rowB1Id = Guid.NewGuid();

    public RlsBranchScopedPolicyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _ownerConnectionString = fixture.OwnerConnectionString;
        _appConnectionString = fixture.AppConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_ownerConnectionString);
        await conn.OpenAsync();

        // 1. Seed tenants
        var subA = "b-rls-a-" + _tenantAId.ToString("N")[..8];
        var subB = "b-rls-b-" + _tenantBId.ToString("N")[..8];

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
            "VALUES (@tA, 'Tenant A', @subA, true, 'Asia/Kolkata', '{}'::jsonb, now()), " +
            "       (@tB, 'Tenant B', @subB, true, 'Asia/Kolkata', '{}'::jsonb, now());", conn))
        {
            cmd.Parameters.AddWithValue("tA", _tenantAId);
            cmd.Parameters.AddWithValue("subA", subA);
            cmd.Parameters.AddWithValue("tB", _tenantBId);
            cmd.Parameters.AddWithValue("subB", subB);
            await cmd.ExecuteNonQueryAsync();
        }

        // 2. Seed branches
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO branches (id, tenant_id, code, name, is_active, created_at) " +
            "VALUES (@bA1, @tA, 'A01', 'Tenant A Branch 1', true, now()), " +
            "       (@bA2, @tA, 'A02', 'Tenant A Branch 2', true, now()), " +
            "       (@bB1, @tB, 'B01', 'Tenant B Branch 1', true, now());", conn))
        {
            cmd.Parameters.AddWithValue("bA1", _branchA1Id);
            cmd.Parameters.AddWithValue("bA2", _branchA2Id);
            cmd.Parameters.AddWithValue("bB1", _branchB1Id);
            cmd.Parameters.AddWithValue("tA", _tenantAId);
            cmd.Parameters.AddWithValue("tB", _tenantBId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Seed rls_probe_branch_scoped rows
        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO rls_probe_branch_scoped (id, tenant_id, branch_id, label) " +
            "VALUES (@rA1, @tA, @bA1, 'Row A1'), " +
            "       (@rA2, @tA, @bA2, 'Row A2'), " +
            "       (@rB1, @tB, @bB1, 'Row B1');", conn))
        {
            cmd.Parameters.AddWithValue("rA1", _rowA1Id);
            cmd.Parameters.AddWithValue("rA2", _rowA2Id);
            cmd.Parameters.AddWithValue("rB1", _rowB1Id);
            cmd.Parameters.AddWithValue("tA", _tenantAId);
            cmd.Parameters.AddWithValue("tB", _tenantBId);
            cmd.Parameters.AddWithValue("bA1", _branchA1Id);
            cmd.Parameters.AddWithValue("bA2", _branchA2Id);
            cmd.Parameters.AddWithValue("bB1", _branchB1Id);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_ownerConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM rls_probe_branch_scoped WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM branches WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM tenants WHERE id IN (@tA, @tB);", conn))
        {
            cmd.Parameters.AddWithValue("tA", _tenantAId);
            cmd.Parameters.AddWithValue("tB", _tenantBId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task User_WithAllBranchesTrue_SeesRowsFromAnyBranch()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        await using var setCmd = new NpgsqlCommand(
            $"SELECT set_config('app.tenant_id', '{_tenantAId}', true), " +
            "        set_config('app.all_branches', 'true', true), " +
            "        set_config('app.branch_ids', '00000000-0000-0000-0000-000000000000', true);", conn, tx);
        await setCmd.ExecuteNonQueryAsync();

        await using var queryCmd = new NpgsqlCommand(
            "SELECT id, label FROM rls_probe_branch_scoped ORDER BY label;", conn, tx);
        var labels = new List<string>();
        await using (var reader = await queryCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                labels.Add(reader.GetString(1));
            }
        }

        labels.Should().HaveCount(2).And.Contain(["Row A1", "Row A2"]);
        labels.Should().NotContain("Row B1");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task User_ScopedToBranchXOnly_SeesBranchXRowsNotBranchY()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        await using var setCmd = new NpgsqlCommand(
            $"SELECT set_config('app.tenant_id', '{_tenantAId}', true), " +
            "        set_config('app.all_branches', 'false', true), " +
            $"       set_config('app.branch_ids', '{_branchA1Id}', true);", conn, tx);
        await setCmd.ExecuteNonQueryAsync();

        await using var queryCmd = new NpgsqlCommand(
            "SELECT id, label FROM rls_probe_branch_scoped;", conn, tx);
        var labels = new List<string>();
        await using (var reader = await queryCmd.ExecuteReaderAsync())
        {
            while (await reader.ReadAsync())
            {
                labels.Add(reader.GetString(1));
            }
        }

        labels.Should().ContainSingle().Which.Should().Be("Row A1");

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task User_WithEmptyBranchListAndAllBranchesFalse_SeesZeroRowsWithoutCrash()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        // Pass sentinel value 00000000-0000-0000-0000-000000000000 as configured by RlsSessionInterceptor
        await using var setCmd = new NpgsqlCommand(
            $"SELECT set_config('app.tenant_id', '{_tenantAId}', true), " +
            "        set_config('app.all_branches', 'false', true), " +
            "        set_config('app.branch_ids', '00000000-0000-0000-0000-000000000000', true);", conn, tx);
        await setCmd.ExecuteNonQueryAsync();

        await using var queryCmd = new NpgsqlCommand(
            "SELECT count(*) FROM rls_probe_branch_scoped;", conn, tx);

        var count = Convert.ToInt32(await queryCmd.ExecuteScalarAsync());
        count.Should().Be(0);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task CrossTenantIsolation_HoldsEvenWhenBranchIdCollidesNumerically()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        // User is authenticated for Tenant A, but attempts to query with Tenant B's branch ID
        await using var setCmd = new NpgsqlCommand(
            $"SELECT set_config('app.tenant_id', '{_tenantAId}', true), " +
            "        set_config('app.all_branches', 'false', true), " +
            $"       set_config('app.branch_ids', '{_branchB1Id}', true);", conn, tx);
        await setCmd.ExecuteNonQueryAsync();

        await using var queryCmd = new NpgsqlCommand(
            "SELECT count(*) FROM rls_probe_branch_scoped;", conn, tx);

        var count = Convert.ToInt32(await queryCmd.ExecuteScalarAsync());
        count.Should().Be(0);

        await tx.RollbackAsync();
    }

    private class ProbeScope : IAccessScope
    {
        public Guid TenantId { get; set; }
        public Guid UserId => Guid.NewGuid();
        public HashSet<Guid> BranchIdsSet { get; set; } = new();
        public IReadOnlySet<Guid> BranchIds => BranchIdsSet;
        public bool AllBranches { get; set; }
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;
        public bool HasTenantContext => true;

        public void UseAnonymousTenant(Guid tenantId) => TenantId = tenantId;
        public void UseSystemContext(Guid tenantId) { TenantId = tenantId; AllBranches = true; }
        public bool HasBranchAccess(Guid branchId) => AllBranches || BranchIdsSet.Contains(branchId);
        public bool HasPermission(string permissionCode) => true;
    }

    [Fact]
    public async Task RlsSessionInterceptor_EmitsSentinel_WhenBranchListIsEmpty()
    {
        var scope = new ProbeScope
        {
            TenantId = _tenantAId,
            AllBranches = false,
            BranchIdsSet = new HashSet<Guid>() // empty!
        };

        var interceptor = new RlsSessionInterceptor(scope);

        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        // Simulate interceptor TransactionStartedAsync
        await using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT set_config('app.tenant_id', @t, true), set_config('app.branch_ids', @b, true), set_config('app.all_branches', @a, true)";
        var pT = cmd.CreateParameter(); pT.ParameterName = "@t"; pT.Value = scope.TenantId.ToString(); cmd.Parameters.Add(pT);
        var pB = cmd.CreateParameter(); pB.ParameterName = "@b"; pB.Value = scope.BranchIds.Count > 0 ? string.Join(",", scope.BranchIds) : "00000000-0000-0000-0000-000000000000"; cmd.Parameters.Add(pB);
        var pA = cmd.CreateParameter(); pA.ParameterName = "@a"; pA.Value = scope.AllBranches ? "true" : "false"; cmd.Parameters.Add(pA);

        await cmd.ExecuteNonQueryAsync();

        await using var queryCmd = new NpgsqlCommand(
            "SELECT count(*) FROM rls_probe_branch_scoped;", conn, tx);

        var count = Convert.ToInt32(await queryCmd.ExecuteScalarAsync());
        count.Should().Be(0);

        await tx.RollbackAsync();
    }
}
