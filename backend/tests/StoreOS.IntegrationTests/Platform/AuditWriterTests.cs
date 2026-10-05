using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Platform;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class AuditWriterTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private const string CorrelationId = "corr-audit-test-123";

    public AuditWriterTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        var sub = "audit-" + _tenantId.ToString("N")[..8];
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO tenants (id, name, subdomain, is_active, timezone, features, created_at) " +
            "VALUES (@tId, 'Audit Tenant', @sub, true, 'Asia/Kolkata', '{}'::jsonb, now()); " +
            "INSERT INTO branches (id, tenant_id, code, name, is_active, created_at) " +
            "VALUES (@bId, @tId, 'A01', 'Audit Branch', true, now());", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("sub", sub);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "DELETE FROM audit_log WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public TestAccessScope(Guid tenantId, Guid userId, Guid branchId)
        {
            TenantId = tenantId;
            UserId = userId;
            BranchIds = new HashSet<Guid> { branchId };
            Permissions = new HashSet<string>();
        }

        public Guid TenantId { get; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; }
        public IReadOnlySet<Guid> BranchIds { get; }
        public bool AllBranches => false;
        public IReadOnlySet<string> Permissions { get; }
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);
        public void UseAnonymousTenant(Guid tenantId) => throw new InvalidOperationException("Not supported in test");
        public void UseSystemContext(Guid tenantId) => throw new InvalidOperationException("Not supported in test");
    }

    private class TestCorrelationContext : ICorrelationContextAccessor
    {
        public string CorrelationId { get; set; } = AuditWriterTests.CorrelationId;
    }

    [Fact]
    public async Task WriteAsync_StagesEntry_AndPersistsOnSaveChanges()
    {
        var scope = new TestAccessScope(_tenantId, _userId, _branchId);
        var correlation = new TestCorrelationContext();

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, scope);
        var writer = new AuditWriter(db, scope, correlation);

        var changes = new { Price = 100.50m, OldPrice = 80.00m, Note = "Price updated" };

        await writer.WriteAsync(_branchId, "product:price_update", "product", "prod-999", changes);

        // Before SaveChanges, query from DB directly to verify WriteAsync only staged
        await using (var checkConn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await checkConn.OpenAsync();
            await using var cmd = new NpgsqlCommand("SELECT COUNT(*) FROM audit_log WHERE entity_id = 'prod-999';", checkConn);
            var initialCount = (long)(await cmd.ExecuteScalarAsync())!;
            initialCount.Should().Be(0, "WriteAsync must only stage the entity and not call SaveChangesAsync");
        }

        // SaveChanges persists the staged entry
        await db.SaveChangesAsync();

        var persisted = await db.AuditLogEntries.FirstOrDefaultAsync(a => a.EntityId == "prod-999");
        persisted.Should().NotBeNull();
        persisted!.TenantId.Should().Be(_tenantId);
        persisted.BranchId.Should().Be(_branchId);
        persisted.CorrelationId.Should().Be(CorrelationId);
        persisted.UserId.Should().Be(_userId);
        persisted.Action.Should().Be("product:price_update");
        persisted.EntityType.Should().Be("product");
        persisted.ChangesJson.Should().NotBeNull();

        using var jsonDoc = JsonDocument.Parse(persisted.ChangesJson!);
        jsonDoc.RootElement.GetProperty("Price").GetDecimal().Should().Be(100.50m);
        jsonDoc.RootElement.GetProperty("OldPrice").GetDecimal().Should().Be(80.00m);
        jsonDoc.RootElement.GetProperty("Note").GetString().Should().Be("Price updated");
    }
}
