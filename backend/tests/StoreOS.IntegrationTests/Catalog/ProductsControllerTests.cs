using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Controllers;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Catalog.Products;
using StoreOS.Application.Catalog.Products.CreateProduct;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Catalog;

[Collection("postgres")]
public class ProductsControllerTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerRoleId = Guid.NewGuid();

    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    public ProductsControllerTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Product Test Tenant", subdomain: "pct-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);
        var branch = Branch.Create(_tenantId, "PB1", "Product HQ");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Manager Role & User (has inventory:manage)
        var managerRole = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(managerRole, _managerRoleId);
        db.Roles.Add(managerRole);

        var managerPerm = RolePermission.Create(_managerRoleId, "inventory:manage");
        db.RolePermissions.Add(managerPerm);

        var managerUser = User.Create(_tenantId, "manager@pctest.com", passwordHash, "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(managerUser, _managerUserId);
        db.Users.Add(managerUser);

        var managerUb = UserBranch.Create(_managerUserId, _branchId, isDefault: true);
        db.UserBranches.Add(managerUb);

        // Cashier Role & User (has pos:checkout, no inventory:manage)
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        var cashierPerm = RolePermission.Create(_cashierRoleId, "pos:checkout");
        db.RolePermissions.Add(cashierPerm);

        var cashierUser = User.Create(_tenantId, "cashier@pctest.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        var cashierUb = UserBranch.Create(_cashierUserId, _branchId, isDefault: true);
        db.UserBranches.Add(cashierUb);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES " +
            "(@mId, @mrId, NULL), " +
            "(@cId, @crId, @bId);", conn);
        cmd.Parameters.AddWithValue("mId", _managerUserId);
        cmd.Parameters.AddWithValue("mrId", _managerRoleId);
        cmd.Parameters.AddWithValue("cId", _cashierUserId);
        cmd.Parameters.AddWithValue("crId", _cashierRoleId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id IN (@mId, @cId); " +
            "DELETE FROM user_branches WHERE user_id IN (@mId, @cId); " +
            "DELETE FROM users WHERE id IN (@mId, @cId); " +
            "DELETE FROM role_permissions WHERE role_id IN (@mrId, @crId); " +
            "DELETE FROM roles WHERE id IN (@mrId, @crId); " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("mId", _managerUserId);
        cmd.Parameters.AddWithValue("cId", _cashierUserId);
        cmd.Parameters.AddWithValue("mrId", _managerRoleId);
        cmd.Parameters.AddWithValue("crId", _cashierRoleId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, bool allBranches, IEnumerable<Guid> branchIds, IEnumerable<string> perms)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches, branchIds, perms, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task CreateProducts_OfEachCategory_Returns201_AndRepairServiceGetsDefaults()
    {
        var token = IssueToken(_managerUserId, allBranches: true, Array.Empty<Guid>(), new[] { "inventory:manage" });

        var categories = new[]
        {
            (ProductCategory.NewPhone, "Galaxy S24", "85171300"),
            (ProductCategory.UsedPhone, "Used Pixel 7", "85171300"),
            (ProductCategory.Accessory, "Type-C Cable", "85444299"),
            (ProductCategory.RepairService, "Battery Replacement", (string?)null)
        };

        foreach (var (category, name, hsn) in categories)
        {
            var command = new CreateProductCommand(
                Name: name,
                Category: category,
                SellPrice: 1500m,
                PurchasePrice: 800m,
                GstRate: category == ProductCategory.RepairService ? 0m : 18m,
                HsnCode: hsn);

            var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
            request.Content = JsonContent.Create(command);

            var response = await _client.SendAsync(request);
            response.StatusCode.Should().Be(HttpStatusCode.Created);

            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            var newId = json.GetProperty("id").GetGuid();

            var getRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/products/{newId}");
            getRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            var getResponse = await _client.SendAsync(getRequest);
            getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

            var productDto = await getResponse.Content.ReadFromJsonAsync<ProductDto>();
            productDto.Should().NotBeNull();
            productDto!.Name.Should().Be(name);
            productDto.Category.Should().Be(category);

            if (category == ProductCategory.RepairService)
            {
                productDto.HsnCode.Should().Be("998713");
                productDto.GstRate.Should().Be(18m);
                productDto.EstimatedDurationMinutes.Should().Be(60);
                productDto.WarrantyDays.Should().Be(30);
            }
        }
    }

    [Fact]
    public async Task CreateProduct_WithoutInventoryManagePermission_Returns403()
    {
        var token = IssueToken(_cashierUserId, allBranches: false, new[] { _branchId }, new[] { "pos:checkout" });

        var command = new CreateProductCommand(
            Name: "Test Phone",
            Category: ProductCategory.NewPhone,
            SellPrice: 1000m,
            PurchasePrice: 500m,
            GstRate: 18m,
            HsnCode: "85171300");

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(command);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListProducts_FilterByCategory_ReturnsOnlyCategory()
    {
        var token = IssueToken(_managerUserId, allBranches: true, Array.Empty<Guid>(), new[] { "inventory:manage" });

        var usedCommand = new CreateProductCommand("Used iPhone 11", ProductCategory.UsedPhone, 20000m, 15000m, 18m, null);
        var accCommand = new CreateProductCommand("Case", ProductCategory.Accessory, 500m, 200m, 18m, null);

        foreach (var cmd in new[] { usedCommand, accCommand })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/products");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
            req.Content = JsonContent.Create(cmd);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var listRequest = new HttpRequestMessage(HttpMethod.Get, $"/api/products?category={ProductCategory.UsedPhone}");
        listRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var listResponse = await _client.SendAsync(listRequest);
        listResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await listResponse.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(p => p.Category == ProductCategory.UsedPhone);
    }

    [Fact]
    public async Task ListProducts_FilterBySearch_ReturnsMatchingName()
    {
        var token = IssueToken(_managerUserId, allBranches: true, Array.Empty<Guid>(), new[] { "inventory:manage" });

        var cmd1 = new CreateProductCommand("Apple iPhone 15", ProductCategory.NewPhone, 80000m, 70000m, 18m, null);
        var cmd2 = new CreateProductCommand("Samsung Galaxy S24", ProductCategory.NewPhone, 75000m, 65000m, 18m, null);

        foreach (var cmd in new[] { cmd1, cmd2 })
        {
            var req = new HttpRequestMessage(HttpMethod.Post, "/api/products");
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            req.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
            req.Content = JsonContent.Create(cmd);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        var searchReq = new HttpRequestMessage(HttpMethod.Get, "/api/products?search=iphone");
        searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var searchRes = await _client.SendAsync(searchReq);
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await searchRes.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        result.Should().NotBeNull();
        result!.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(p => p.Name.Contains("iPhone", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public async Task ListProducts_Pagination_ReturnsCorrectPageAndTotalCount()
    {
        var token = IssueToken(_managerUserId, allBranches: true, Array.Empty<Guid>(), new[] { "inventory:manage" });

        // Seed 60 items directly into DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            for (var i = 1; i <= 60; i++)
            {
                var prod = Product.Create(_tenantId, $"PagedItem {i:D3}", ProductCategory.Accessory, 100m, 50m, 18m, null);
                db.Products.Add(prod);
            }
            await db.SaveChangesAsync();
        }

        // Page 1 with pageSize 50
        var p1Req = new HttpRequestMessage(HttpMethod.Get, "/api/products?pageSize=50&page=1&search=PagedItem");
        p1Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var p1Res = await _client.SendAsync(p1Req);
        p1Res.StatusCode.Should().Be(HttpStatusCode.OK);

        var p1Result = await p1Res.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        p1Result.Should().NotBeNull();
        p1Result!.TotalCount.Should().Be(60);
        p1Result.Items.Count.Should().Be(50);
        p1Result.Page.Should().Be(1);
        p1Result.PageSize.Should().Be(50);

        // Page 2 with pageSize 50
        var p2Req = new HttpRequestMessage(HttpMethod.Get, "/api/products?pageSize=50&page=2&search=PagedItem");
        p2Req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var p2Res = await _client.SendAsync(p2Req);
        p2Res.StatusCode.Should().Be(HttpStatusCode.OK);

        var p2Result = await p2Res.Content.ReadFromJsonAsync<PagedResult<ProductDto>>();
        p2Result.Should().NotBeNull();
        p2Result!.TotalCount.Should().Be(60);
        p2Result.Items.Count.Should().Be(10);
        p2Result.Page.Should().Be(2);
        p2Result.PageSize.Should().Be(50);
    }

    [Fact]
    public async Task UpdateProduct_MarginSchemeOnNonUsedPhone_Returns422()
    {
        var token = IssueToken(_managerUserId, allBranches: true, Array.Empty<Guid>(), new[] { "inventory:manage" });

        var createCmd = new CreateProductCommand("Accessory Test", ProductCategory.Accessory, 500m, 200m, 18m, null);
        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/products");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        createReq.Content = JsonContent.Create(createCmd);
        var createRes = await _client.SendAsync(createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var prodId = json.GetProperty("id").GetGuid();

        var updateBody = new UpdateProductBody(IsMarginScheme: true);
        var patchReq = new HttpRequestMessage(HttpMethod.Patch, $"/api/products/{prodId}");
        patchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        patchReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        patchReq.Content = JsonContent.Create(updateBody);

        var patchRes = await _client.SendAsync(patchReq);
        patchRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var errorJson = await patchRes.Content.ReadFromJsonAsync<JsonElement>();
        errorJson.GetProperty("code").GetString().Should().Be("margin_scheme_invalid_category");
    }
}
