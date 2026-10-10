using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory.Queries;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Inventory;

[Collection("postgres")]
public class InventoryQueriesTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();

    private readonly Guid _allBranchesUserId = Guid.NewGuid();
    private readonly Guid _allBranchesRoleId = Guid.NewGuid();

    private readonly Guid _branchAUserId = Guid.NewGuid();
    private readonly Guid _branchARoleId = Guid.NewGuid();

    private Guid _prodNewPhoneId;
    private Guid _prodUsedPhoneId;
    private Guid _prodAccessoryId;
    private Guid _prodRepairServiceId;

    public InventoryQueriesTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Inventory Test Tenant", subdomain: "inv-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "IA1", "Branch A");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "IB1", "Branch B");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        // Products
        var pNewPhone = Product.Create(_tenantId, "Galaxy S23", ProductCategory.NewPhone, 50000m, 40000m, 18m, "85171300");
        db.Products.Add(pNewPhone);
        _prodNewPhoneId = pNewPhone.Id;

        var pUsedPhone = Product.Create(_tenantId, "Used iPhone 11", ProductCategory.UsedPhone, 20000m, 15000m, 18m, "85171300");
        pUsedPhone.SetMarginScheme(true);
        db.Products.Add(pUsedPhone);
        _prodUsedPhoneId = pUsedPhone.Id;

        var pAccessory = Product.Create(_tenantId, "USB Cable", ProductCategory.Accessory, 500m, 200m, 18m, "85444299");
        db.Products.Add(pAccessory);
        _prodAccessoryId = pAccessory.Id;

        var pRepairService = Product.Create(_tenantId, "Screen Repair", ProductCategory.RepairService, 2000m, 500m, 18m, null);
        db.Products.Add(pRepairService);
        _prodRepairServiceId = pRepairService.Id;

        // Branch A Inventory:
        // 1. Galaxy S23: Qty = 10 (InStock), cost = 40000, sell = 50000
        var biA1 = BranchInventory.Create(_tenantId, _branchAId, _prodNewPhoneId);
        biA1.IncreaseStock(10, 40000m);
        db.BranchInventories.Add(biA1);

        // 2. Used iPhone 11: Qty = 2 (LowStock, threshold 3), cost = 15000, sell override = 19000
        var biA2 = BranchInventory.Create(_tenantId, _branchAId, _prodUsedPhoneId);
        biA2.IncreaseStock(2, 15000m);
        biA2.SetSellPriceOverride(19000m);
        db.BranchInventories.Add(biA2);

        // 3. USB Cable: Qty = 0 (OutOfStock), cost = 200, sell = 500
        var biA3 = BranchInventory.Create(_tenantId, _branchAId, _prodAccessoryId);
        db.BranchInventories.Add(biA3);

        // Branch B Inventory:
        // 4. Galaxy S23 in Branch B: Qty = 5 (InStock), cost = 40000, sell = 50000
        var biB1 = BranchInventory.Create(_tenantId, _branchBId, _prodNewPhoneId);
        biB1.IncreaseStock(5, 40000m);
        db.BranchInventories.Add(biB1);

        // Users & Roles
        var allBranchesRole = Role.Create(_tenantId, "OwnerRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(allBranchesRole, _allBranchesRoleId);
        db.Roles.Add(allBranchesRole);

        var allBranchesUser = User.Create(_tenantId, "owner@invtest.com", passwordHash, "Owner User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(allBranchesUser, _allBranchesUserId);
        db.Users.Add(allBranchesUser);

        var ubAll = UserBranch.Create(_allBranchesUserId, _branchAId, isDefault: true);
        db.UserBranches.Add(ubAll);

        var branchARole = Role.Create(_tenantId, "BranchARole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(branchARole, _branchARoleId);
        db.Roles.Add(branchARole);

        var branchAUser = User.Create(_tenantId, "brancha@invtest.com", passwordHash, "Branch A User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(branchAUser, _branchAUserId);
        db.Users.Add(branchAUser);

        var ubA = UserBranch.Create(_branchAUserId, _branchAId, isDefault: true);
        db.UserBranches.Add(ubA);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES " +
            "(@uAll, @rAll, NULL), " +
            "(@uA, @rA, @bA);", conn);
        cmd.Parameters.AddWithValue("uAll", _allBranchesUserId);
        cmd.Parameters.AddWithValue("rAll", _allBranchesRoleId);
        cmd.Parameters.AddWithValue("uA", _branchAUserId);
        cmd.Parameters.AddWithValue("rA", _branchARoleId);
        cmd.Parameters.AddWithValue("bA", _branchAId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id IN (@uAll, @uA); " +
            "DELETE FROM user_branches WHERE user_id IN (@uAll, @uA); " +
            "DELETE FROM users WHERE id IN (@uAll, @uA); " +
            "DELETE FROM role_permissions WHERE role_id IN (@rAll, @rA); " +
            "DELETE FROM roles WHERE id IN (@rAll, @rA); " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id IN (@bA, @bB); " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uAll", _allBranchesUserId);
        cmd.Parameters.AddWithValue("uA", _branchAUserId);
        cmd.Parameters.AddWithValue("rAll", _allBranchesRoleId);
        cmd.Parameters.AddWithValue("rA", _branchARoleId);
        cmd.Parameters.AddWithValue("bA", _branchAId);
        cmd.Parameters.AddWithValue("bB", _branchBId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, bool allBranches, IEnumerable<Guid> branchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches, branchIds, Array.Empty<string>(), tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task GetInventory_StockStatusLowStock_ReturnsOnlyLowStockRows()
    {
        var token = IssueToken(_allBranchesUserId, allBranches: true, Array.Empty<Guid>());

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/inventory?stockStatus=LowStock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<InventoryLineDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.StockStatus == "LowStock");
        result.Items.Should().Contain(x => x.ProductId == _prodUsedPhoneId && x.StockQty == 2);
    }

    [Fact]
    public async Task GetLowStock_ReturnsLowAndOutOfStockItems_ExcludingRepairService()
    {
        var token = IssueToken(_allBranchesUserId, allBranches: true, Array.Empty<Guid>());

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/low-stock");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var items = await response.Content.ReadFromJsonAsync<List<InventoryLineDto>>();
        items.Should().NotBeNull();
        items!.Should().OnlyContain(x => x.StockQty <= 3);
        items.Should().NotContain(x => x.Category == ProductCategory.RepairService);

        items.Should().Contain(x => x.ProductId == _prodUsedPhoneId && x.StockStatus == "LowStock");
        items.Should().Contain(x => x.ProductId == _prodAccessoryId && x.StockStatus == "OutOfStock");
    }

    [Fact]
    public async Task GetValuation_MatchesValuationCalculator_ExcludingRepairService()
    {
        var token = IssueToken(_allBranchesUserId, allBranches: true, Array.Empty<Guid>());

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/valuation");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var valuation = await response.Content.ReadFromJsonAsync<ValuationDto>();
        valuation.Should().NotBeNull();

        // Hand-computed expected across all branches:
        // Branch A:
        // - Galaxy S23: 10 * 40000 = 400000 cost, 10 * 50000 = 500000 retail
        // - Used iPhone 11: 2 * 15000 = 30000 cost, 2 * 19000 (override) = 38000 retail
        // - USB Cable: 0 * 200 = 0 cost, 0 * 500 = 0 retail
        // Branch B:
        // - Galaxy S23: 5 * 40000 = 200000 cost, 5 * 50000 = 250000 retail
        // Total Cost: 400000 + 30000 + 200000 = 630000.00
        // Total Retail: 500000 + 38000 + 250000 = 788000.00
        // Potential Gross Value: 788000 - 630000 = 158000.00
        valuation!.CostValuation.Should().Be(630000.00m);
        valuation.RetailValuation.Should().Be(788000.00m);
        valuation.PotentialGrossValue.Should().Be(158000.00m);
    }

    [Fact]
    public async Task UserScopedToBranchA_DoesNotSeeBranchBRowsOrValuation()
    {
        var token = IssueToken(_branchAUserId, allBranches: false, new[] { _branchAId });

        // Inventory Query:
        var invReq = new HttpRequestMessage(HttpMethod.Get, "/api/inventory");
        invReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        invReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var invRes = await _client.SendAsync(invReq);
        invRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var invResult = await invRes.Content.ReadFromJsonAsync<PagedResult<InventoryLineDto>>();
        invResult.Should().NotBeNull();
        invResult!.Items.Should().OnlyContain(x => x.BranchId == _branchAId);
        invResult.Items.Should().NotContain(x => x.BranchId == _branchBId);

        // Valuation Query:
        var valReq = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/valuation");
        valReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        valReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var valRes = await _client.SendAsync(valReq);
        valRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var valuation = await valRes.Content.ReadFromJsonAsync<ValuationDto>();
        valuation.Should().NotBeNull();

        // Hand-computed expected for Branch A only:
        // - Galaxy S23: 10 * 40000 = 400000 cost, 10 * 50000 = 500000 retail
        // - Used iPhone 11: 2 * 15000 = 30000 cost, 2 * 19000 = 38000 retail
        // - USB Cable: 0
        // Total Cost: 430000.00
        // Total Retail: 538000.00
        // Potential: 108000.00
        valuation!.CostValuation.Should().Be(430000.00m);
        valuation.RetailValuation.Should().Be(538000.00m);
        valuation.PotentialGrossValue.Should().Be(108000.00m);
    }

    [Fact]
    public async Task GetCategories_ReturnsExistingCategories()
    {
        var token = IssueToken(_allBranchesUserId, allBranches: true, new[] { _branchAId, _branchBId });

        var req = new HttpRequestMessage(HttpMethod.Get, "/api/inventory/categories");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.OK);

        var categories = await res.Content.ReadFromJsonAsync<List<string>>();
        categories.Should().NotBeNull();
        categories.Should().Contain(new[] { "Accessory", "NewPhone", "UsedPhone" });
    }
}
