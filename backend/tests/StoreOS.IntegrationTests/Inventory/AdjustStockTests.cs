using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory.Adjustments;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Inventory;

[Collection("postgres")]
public class AdjustStockTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private Guid _productId;

    public AdjustStockTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var passwordHash = _hasher.Hash("Password123!");

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Adjust Test Tenant", subdomain: "adj-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "AB1", "Adjust Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var product = Product.Create(_tenantId, "Audit Item", ProductCategory.Accessory, 400m, 150m, 18m, "85444299");
        db.Products.Add(product);
        _productId = product.Id;

        // Seed initial stock of 10
        var inv = BranchInventory.Create(_tenantId, _branchId, _productId);
        inv.IncreaseStock(10, 150m);
        db.BranchInventories.Add(inv);

        var role = Role.Create(_tenantId, "InventoryAuditor");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "inventory:adjustments");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "auditor@adjtest.com", passwordHash, "Auditor User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, NULL);", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM users WHERE id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE id = @rId; " +
            "DELETE FROM audit_log WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), new[] { "inventory:adjustments" }, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task PositiveAdjustment_IncreasesStock_AndWritesMovementAndAuditLogWithReason()
    {
        var token = IssueToken();

        var command = new AdjustStockCommand(_productId, QtyDelta: 5, Reason: "Found 5 extra units during cycle count");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AdjustStockResult>();
        result.Should().NotBeNull();
        result!.NewStockQty.Should().Be(15);

        // Verify in database: stock, movement, and audit log
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var inv = await db.BranchInventories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _productId);
        inv!.StockQty.Should().Be(15);

        var movement = await db.InventoryMovements.IgnoreQueryFilters()
            .Where(m => m.TenantId == _tenantId && m.ProductId == _productId && m.MovementType == MovementType.Adjustment)
            .OrderByDescending(m => m.CreatedAt)
            .FirstOrDefaultAsync();

        movement.Should().NotBeNull();
        movement!.QtyDelta.Should().Be(5);
        movement.RefType.Should().Be("Adjustment");

        var audit = await db.AuditLogEntries.IgnoreQueryFilters()
            .Where(a => a.TenantId == _tenantId && a.Action == "inventory.adjusted")
            .OrderByDescending(a => a.OccurredAt)
            .FirstOrDefaultAsync();

        audit.Should().NotBeNull();
        audit!.ChangesJson.Should().Contain("Found 5 extra units during cycle count");
    }

    [Fact]
    public async Task NegativeAdjustment_DecreasesStockCorrectly()
    {
        var token = IssueToken();

        var command = new AdjustStockCommand(_productId, QtyDelta: -3, Reason: "Damaged box discarded");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<AdjustStockResult>();
        result.Should().NotBeNull();
        result!.NewStockQty.Should().Be(7); // initial was 10, -3 = 7
    }

    [Fact]
    public async Task NegativeAdjustment_ExceedingCurrentStock_Returns422_InsufficientStock()
    {
        var token = IssueToken();

        var command = new AdjustStockCommand(_productId, QtyDelta: -99, Reason: "Massive write-off exceeding stock");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("insufficient_stock");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MissingOrEmptyReason_Returns400(string reason)
    {
        var token = IssueToken();

        var command = new AdjustStockCommand(_productId, QtyDelta: 2, Reason: reason);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task CallerWithoutInventoryAdjustmentsPermission_Returns403_ImmediatelyViaLiveDbCheck()
    {
        var token = IssueToken(); // token has inventory:adjustments claim in JWT

        // Revoke the permission directly in DB mid-session
        await using (var conn = new NpgsqlConnection(_fixture.OwnerConnectionString))
        {
            await conn.OpenAsync();
            await using var cmd = new NpgsqlCommand(
                "DELETE FROM role_permissions WHERE role_id = @rId AND permission_code = 'inventory:adjustments';", conn);
            cmd.Parameters.AddWithValue("rId", _roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        var command = new AdjustStockCommand(_productId, QtyDelta: 1, Reason: "Should be blocked by live DB check");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/adjustments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
