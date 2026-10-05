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
using StoreOS.Application.Purchasing.StockIn;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Purchasing;

[Collection("postgres")]
public class StockInTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private Guid _prodAccessoryId;
    private Guid _prodPhoneId;

    public StockInTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Purchase Test Tenant", subdomain: "pur-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "PB1", "Purchasing HQ");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var pAccessory = Product.Create(_tenantId, "Fast Charger", ProductCategory.Accessory, 1200m, 600m, 18m, "85044030");
        db.Products.Add(pAccessory);
        _prodAccessoryId = pAccessory.Id;

        var pPhone = Product.Create(_tenantId, "Pixel 8", ProductCategory.NewPhone, 75000m, 65000m, 18m, "85171300");
        db.Products.Add(pPhone);
        _prodPhoneId = pPhone.Id;

        var role = Role.Create(_tenantId, "PurchasingManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "purchases:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "buyer@purtest.com", passwordHash, "Buyer User");
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
            "DELETE FROM inventory_imeis WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM purchases WHERE tenant_id = @tId; " +
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
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), new[] { "purchases:manage" }, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task StockIn_WithoutImeis_IncreasesStockAndCreatesInventoryMovement()
    {
        var token = IssueToken();

        var command = new StockInCommand(
            ProductId: _prodAccessoryId,
            Qty: 20,
            PurchaseRate: 500m,
            SupplierId: null,
            SupplierName: "Global Accessories",
            SupplierPhone: "9876543210",
            SupplierAddress: "Market Road",
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-ACC-01",
            Imeis: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<StockInResult>();
        result.Should().NotBeNull();
        result!.NewStockQty.Should().Be(20);
        result.CreatedImeiIds.Should().BeEmpty();

        // Verify movement
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var movements = await db.InventoryMovements
            .IgnoreQueryFilters()
            .Where(m => m.TenantId == _tenantId && m.ProductId == _prodAccessoryId)
            .ToListAsync();

        movements.Should().HaveCount(1);
        movements[0].MovementType.Should().Be(MovementType.Purchase);
        movements[0].QtyDelta.Should().Be(20);
        movements[0].UnitCost.Should().Be(500m);
    }

    [Fact]
    public async Task StockIn_WithImeisCountMismatch_Returns400()
    {
        var token = IssueToken();

        var command = new StockInCommand(
            ProductId: _prodPhoneId,
            Qty: 3,
            PurchaseRate: 60000m,
            SupplierId: null,
            SupplierName: "Phone Dist",
            SupplierPhone: null,
            SupplierAddress: null,
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-P01",
            Imeis: new[] { "111111111111111", "222222222222222" }); // only 2 IMEIs for Qty = 3

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StockIn_WithMatchingImeis_CreatesImeisLinkedToPurchase()
    {
        var token = IssueToken();

        var imeis = new[] { "350000000000001", "350000000000002" };
        var command = new StockInCommand(
            ProductId: _prodPhoneId,
            Qty: 2,
            PurchaseRate: 60000m,
            SupplierId: null,
            SupplierName: "Phone Dist",
            SupplierPhone: null,
            SupplierAddress: null,
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-P02",
            Imeis: imeis);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<StockInResult>();
        result.Should().NotBeNull();
        result!.CreatedImeiIds.Should().HaveCount(2);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var createdImeis = await db.InventoryImeis
            .IgnoreQueryFilters()
            .Where(i => i.TenantId == _tenantId && i.PurchaseId == result.PurchaseId)
            .ToListAsync();

        createdImeis.Should().HaveCount(2);
        createdImeis.Should().OnlyContain(i => i.Status == ImeiStatus.InStock);
        createdImeis.Select(i => i.Imei).Should().BeEquivalentTo(imeis);
    }

    [Fact]
    public async Task StockIn_DuplicateImeiInSameRequest_Returns400()
    {
        var token = IssueToken();

        var command = new StockInCommand(
            ProductId: _prodPhoneId,
            Qty: 2,
            PurchaseRate: 60000m,
            SupplierId: null,
            SupplierName: "Phone Dist",
            SupplierPhone: null,
            SupplierAddress: null,
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-P03",
            Imeis: new[] { "350000000000099", "350000000000099" }); // duplicate in same request

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task StockIn_ImeiAlreadyActiveInDb_Returns409()
    {
        var token = IssueToken();

        const string existingImei = "350000000000555";

        // Seed active IMEI directly in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var imei = InventoryImei.Create(_tenantId, _branchId, _prodPhoneId, null, existingImei, new DateOnly(2026, 10, 1), 60000m);
            db.InventoryImeis.Add(imei);
            await db.SaveChangesAsync();
        }

        var command = new StockInCommand(
            ProductId: _prodPhoneId,
            Qty: 1,
            PurchaseRate: 62000m,
            SupplierId: null,
            SupplierName: "Phone Dist",
            SupplierPhone: null,
            SupplierAddress: null,
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-P04",
            Imeis: new[] { existingImei });

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("imei_already_active");
    }
}
