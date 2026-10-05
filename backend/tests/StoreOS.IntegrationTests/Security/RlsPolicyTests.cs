using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Npgsql;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Security;

[Collection("postgres")]
public class RlsPolicyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly string _ownerConnectionString;
    private readonly string _appConnectionString;

    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();

    public RlsPolicyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _ownerConnectionString = fixture.OwnerConnectionString;
        _appConnectionString = fixture.AppConnectionString;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_ownerConnectionString);
        await conn.OpenAsync();

        var subA = "rls-a-" + _tenantAId.ToString("N")[..8];
        var subB = "rls-b-" + _tenantBId.ToString("N")[..8];

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
            "VALUES (@idA, 'RLS Tenant A', @subA, true, 'Asia/Kolkata', '{}'::jsonb, now()), " +
            "       (@idB, 'RLS Tenant B', @subB, true, 'Asia/Kolkata', '{}'::jsonb, now());", conn))
        {
            cmd.Parameters.AddWithValue("idA", _tenantAId);
            cmd.Parameters.AddWithValue("subA", subA);
            cmd.Parameters.AddWithValue("idB", _tenantBId);
            cmd.Parameters.AddWithValue("subB", subB);
            await cmd.ExecuteNonQueryAsync();
        }

        await using (var cmd = new NpgsqlCommand(
            "INSERT INTO branches (id, tenant_id, code, name, is_active, created_at) " +
            "VALUES (@bId, @tId, 'R01', 'RLS Branch A', true, now());", conn))
        {
            cmd.Parameters.AddWithValue("bId", _branchAId);
            cmd.Parameters.AddWithValue("tId", _tenantAId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_ownerConnectionString);
        await conn.OpenAsync();

        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id IN (@tIdA, @tIdB);", conn))
        {
            cmd.Parameters.AddWithValue("bId", _branchAId);
            cmd.Parameters.AddWithValue("tIdA", _tenantAId);
            cmd.Parameters.AddWithValue("tIdB", _tenantBId);
            await cmd.ExecuteNonQueryAsync();
        }
    }

    [Fact]
    public async Task AppRole_CannotBypassRls()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT rolsuper, rolbypassrls FROM pg_roles WHERE rolname = current_user;", conn);
        await using var reader = await cmd.ExecuteReaderAsync();

        (await reader.ReadAsync()).Should().BeTrue();
        var isSuper = reader.GetBoolean(0);
        var bypassRls = reader.GetBoolean(1);

        isSuper.Should().BeFalse();
        bypassRls.Should().BeFalse();
    }

    [Fact]
    public async Task RlsRoleGuard_OwnerConnection_ThrowsInvalidOperationException()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _ownerConnectionString
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var guard = new RlsRoleGuard(config);

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Refusing to start: the database role bypasses row level security.");
    }

    [Fact]
    public async Task RlsRoleGuard_AppConnection_StartsSuccessfully()
    {
        var inMemorySettings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _appConnectionString
        };
        var config = new ConfigurationBuilder().AddInMemoryCollection(inMemorySettings).Build();
        var guard = new RlsRoleGuard(config);

        var act = () => guard.StartAsync(CancellationToken.None);

        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RlsPolicy_FailClosed_WhenNoTenantContext()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        await using var cmd = new NpgsqlCommand(
            "SELECT count(*) FROM branches WHERE id = @id;", conn, tx);
        cmd.Parameters.AddWithValue("id", _branchAId);

        var count = Convert.ToInt32(await cmd.ExecuteScalarAsync());
        count.Should().Be(0);

        await tx.RollbackAsync();
    }

    [Fact]
    public async Task RlsPolicy_TenantContext_ReturnsOnlyOwnRows()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        // 1. In transaction with Tenant A scope: should see branch A
        await using (var txA = await conn.BeginTransactionAsync())
        {
            await using var setCmd = new NpgsqlCommand(
                $"SET LOCAL app.tenant_id = '{_tenantAId}';", conn, txA);
            await setCmd.ExecuteNonQueryAsync();

            await using var checkCmd = new NpgsqlCommand(
                "SELECT count(*) FROM branches WHERE id = @id;", conn, txA);
            checkCmd.Parameters.AddWithValue("id", _branchAId);

            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            count.Should().Be(1);

            await txA.RollbackAsync();
        }

        // 2. In transaction with Tenant B scope: should NOT see branch A
        await using (var txB = await conn.BeginTransactionAsync())
        {
            await using var setCmd = new NpgsqlCommand(
                $"SET LOCAL app.tenant_id = '{_tenantBId}';", conn, txB);
            await setCmd.ExecuteNonQueryAsync();

            await using var checkCmd = new NpgsqlCommand(
                "SELECT count(*) FROM branches WHERE id = @id;", conn, txB);
            checkCmd.Parameters.AddWithValue("id", _branchAId);

            var count = Convert.ToInt32(await checkCmd.ExecuteScalarAsync());
            count.Should().Be(0);

            await txB.RollbackAsync();
        }
    }

    [Fact]
    public async Task RlsPolicy_CrossTenantInsert_ThrowsPostgresException()
    {
        await using var conn = new NpgsqlConnection(_appConnectionString);
        await conn.OpenAsync();

        await using var tx = await conn.BeginTransactionAsync();

        // Set session to Tenant A
        await using var setCmd = new NpgsqlCommand(
            $"SET LOCAL app.tenant_id = '{_tenantAId}';", conn, tx);
        await setCmd.ExecuteNonQueryAsync();

        // Attempt to insert a branch for Tenant B
        await using var insertCmd = new NpgsqlCommand(
            "INSERT INTO branches (id, tenant_id, code, name, is_active, created_at) " +
            "VALUES (@id, @tenantId, 'R02', 'Cross-tenant branch', true, now());", conn, tx);
        insertCmd.Parameters.AddWithValue("id", Guid.NewGuid());
        insertCmd.Parameters.AddWithValue("tenantId", _tenantBId);

        var act = () => insertCmd.ExecuteNonQueryAsync();

        await act.Should().ThrowAsync<PostgresException>()
            .Where(e => e.SqlState == "42501");

        await tx.RollbackAsync();
    }
}
