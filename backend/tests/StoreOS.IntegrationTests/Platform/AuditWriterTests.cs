using System.Security.Claims;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Platform;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Platform;
using StoreOS.IntegrationTests.Fixtures;
using StoreOS.IntegrationTests.Security;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class AuditWriterTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly StoreOsWebApplicationFactory _factory;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private const string CorrelationId = "corr-audit-test-123";

    public AuditWriterTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _factory = new StoreOsWebApplicationFactory();
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

        _factory.Dispose();
    }

    private class TestCorrelationContext : ICorrelationContextAccessor
    {
        public string CorrelationId { get; set; } = AuditWriterTests.CorrelationId;
    }

    [Fact]
    public async Task WriteAsync_WithTestAuthHandler_StagesAndPersistsUnderAppRoleRls()
    {
        // 1. Configure authenticated claims via TestAuthHandler
        var claims = new List<Claim>
        {
            new("tenant_id", _tenantId.ToString()),
            new("sub", _userId.ToString()),
            new("all_branches", "false"),
            new("branch_id", _branchId.ToString()),
            new("perm", "settings:manage"),
            new("token_version", "1")
        };
        var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, TestAuthHandler.SchemeName));
        TestAuthHandler.CurrentPrincipal.Value = principal;

        // 2. Set up DI scope with HttpContext and TestAuthHandler
        using var serviceScope = _factory.Services.CreateScope();
        var httpContextAccessor = serviceScope.ServiceProvider.GetRequiredService<IHttpContextAccessor>();
        var authService = serviceScope.ServiceProvider.GetRequiredService<IAuthenticationService>();

        var httpContext = new DefaultHttpContext
        {
            RequestServices = serviceScope.ServiceProvider
        };
        httpContextAccessor.HttpContext = httpContext;

        var authResult = await authService.AuthenticateAsync(httpContext, TestAuthHandler.SchemeName);
        authResult.Succeeded.Should().BeTrue();
        httpContext.User = authResult.Principal!;

        var accessScope = serviceScope.ServiceProvider.GetRequiredService<IAccessScope>();
        accessScope.IsAuthenticated.Should().BeTrue();
        accessScope.TenantId.Should().Be(_tenantId);
        accessScope.UserId.Should().Be(_userId);

        // 3. Connect using AppConnectionString under storeos_app role with RlsSessionInterceptor
        var interceptor = new RlsSessionInterceptor(accessScope);
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptor)
            .Options;

        var correlation = new TestCorrelationContext();
        await using var appDb = new StoreOsDbContext(options, accessScope);
        var writer = new AuditWriter(appDb, accessScope, correlation);

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

        // 4. SaveChanges persists staged entry inside RLS transaction (interceptor sets session variables)
        await using var tx = await appDb.Database.BeginTransactionAsync();
        await appDb.SaveChangesAsync();
        await tx.CommitAsync();

        // 5. Query AuditLogEntries through appDb under RLS transaction
        await using var queryTx = await appDb.Database.BeginTransactionAsync();
        var persisted = await appDb.AuditLogEntries.FirstOrDefaultAsync(a => a.EntityId == "prod-999");
        persisted.Should().NotBeNull();
        persisted!.TenantId.Should().Be(_tenantId);
        persisted.BranchId.Should().Be(_branchId);
        persisted.CorrelationId.Should().Be(CorrelationId);
        persisted.UserId.Should().Be(_userId);
        persisted.Action.Should().Be("product:price_update");
        persisted.EntityType.Should().Be("product");
        persisted.ChangesJson.Should().NotBeNull();
        await queryTx.CommitAsync();

        using var jsonDoc = JsonDocument.Parse(persisted.ChangesJson!);
        jsonDoc.RootElement.GetProperty("Price").GetDecimal().Should().Be(100.50m);
        jsonDoc.RootElement.GetProperty("OldPrice").GetDecimal().Should().Be(80.00m);
        jsonDoc.RootElement.GetProperty("Note").GetString().Should().Be("Price updated");
    }
}
