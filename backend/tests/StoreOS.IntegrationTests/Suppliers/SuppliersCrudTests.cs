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
using StoreOS.Application.Purchasing.StockIn;
using StoreOS.Application.Suppliers;
using StoreOS.Application.Suppliers.CreateSupplier;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Suppliers;

[Collection("postgres")]
public class SuppliersCrudTests : IAsyncLifetime
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

    public SuppliersCrudTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Suppliers Test Tenant", subdomain: "sup-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "SP1", "Suppliers Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var product = Product.Create(_tenantId, "Supplier Test Cable", ProductCategory.Accessory, 500m, 200m, 18m, "85444299");
        db.Products.Add(product);
        _prodAccessoryId = product.Id;

        var role = Role.Create(_tenantId, "SupplierManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "purchases:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "supmgr@test.com", passwordHash, "Supplier Mgr");
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
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM purchases WHERE tenant_id = @tId; " +
            "DELETE FROM suppliers WHERE tenant_id = @tId; " +
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
    public async Task CreateListSearchUpdate_Supplier_LifecycleSucceeds()
    {
        var token = IssueToken();

        // 1. Create Supplier
        var createCmd = new CreateSupplierCommand(
            Name: "Apex Electronics",
            Phone: "9876543210",
            Address: "123 Market St",
            BrandOrCategory: "Cables & Adapters",
            Notes: "Preferred vendor");

        var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/suppliers");
        createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        createReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        createReq.Content = JsonContent.Create(createCmd);

        var createRes = await _client.SendAsync(createReq);
        createRes.StatusCode.Should().Be(HttpStatusCode.Created);

        var json = await createRes.Content.ReadFromJsonAsync<JsonElement>();
        var supplierId = json.GetProperty("id").GetGuid();

        // 2. GetById
        var getReq = new HttpRequestMessage(HttpMethod.Get, $"/api/suppliers/{supplierId}");
        getReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        getReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var getRes = await _client.SendAsync(getReq);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await getRes.Content.ReadFromJsonAsync<SupplierDto>();
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Apex Electronics");
        dto.Phone.Should().Be("9876543210");
        dto.BrandOrCategory.Should().Be("Cables & Adapters");

        // 3. Search List
        var searchReq = new HttpRequestMessage(HttpMethod.Get, "/api/suppliers?search=apex");
        searchReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        searchReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var searchRes = await _client.SendAsync(searchReq);
        searchRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var paged = await searchRes.Content.ReadFromJsonAsync<PagedResult<SupplierDto>>();
        paged.Should().NotBeNull();
        paged!.Items.Should().Contain(s => s.Id == supplierId);

        // 4. Update
        var updateBody = new UpdateSupplierBody(
            Name: "Apex Electronics Global",
            Phone: "9876543211",
            Address: "123 Market St Suite 400",
            BrandOrCategory: "Mobile Accessories",
            Notes: "Updated terms");

        var updateReq = new HttpRequestMessage(HttpMethod.Put, $"/api/suppliers/{supplierId}");
        updateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        updateReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        updateReq.Content = JsonContent.Create(updateBody);

        var updateRes = await _client.SendAsync(updateReq);
        updateRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 5. Verify Update
        var verifyReq = new HttpRequestMessage(HttpMethod.Get, $"/api/suppliers/{supplierId}");
        verifyReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        verifyReq.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var verifyRes = await _client.SendAsync(verifyReq);
        var updatedDto = await verifyRes.Content.ReadFromJsonAsync<SupplierDto>();
        updatedDto!.Name.Should().Be("Apex Electronics Global");
        updatedDto.Phone.Should().Be("9876543211");
        updatedDto.Notes.Should().Be("Updated terms");
    }

    [Fact]
    public async Task StockIn_ReferencingNonExistentSupplier_Returns422_ForeignConstraintEnforced()
    {
        var token = IssueToken();

        var nonExistentSupplierId = Guid.NewGuid();

        var stockInCmd = new StockInCommand(
            ProductId: _prodAccessoryId,
            Qty: 5,
            PurchaseRate: 200m,
            SupplierId: nonExistentSupplierId,
            SupplierName: "Ghost Supplier",
            SupplierPhone: null,
            SupplierAddress: null,
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-GHOST",
            Imeis: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(stockInCmd);

        var response = await _client.SendAsync(request);
        ((int)response.StatusCode).Should().BeOneOf((int)HttpStatusCode.NotFound, (int)HttpStatusCode.UnprocessableEntity);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("supplier_not_found");
    }

    [Fact]
    public async Task StockIn_ReferencingValidSupplier_Succeeds()
    {
        var token = IssueToken();

        // 1. Create real supplier in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        Guid validSupplierId;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var supplier = Supplier.Create(_tenantId, "Real Distributor", "1112223334", "Warehouse Road", "Phones", null);
            db.Suppliers.Add(supplier);
            await db.SaveChangesAsync();
            validSupplierId = supplier.Id;
        }

        // 2. Stock in referencing real supplier
        var stockInCmd = new StockInCommand(
            ProductId: _prodAccessoryId,
            Qty: 10,
            PurchaseRate: 180m,
            SupplierId: validSupplierId,
            SupplierName: "Real Distributor",
            SupplierPhone: "1112223334",
            SupplierAddress: "Warehouse Road",
            PurchaseDate: new DateOnly(2026, 10, 5),
            SupplierInvoiceNo: "INV-REAL-01",
            Imeis: null);

        var request = new HttpRequestMessage(HttpMethod.Post, "/api/purchases");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());
        request.Content = JsonContent.Create(stockInCmd);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<StockInResult>();
        result.Should().NotBeNull();
        result!.PurchaseId.Should().NotBeEmpty();

        // Verify purchase row in DB has foreign key value set
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var p = await db.Purchases.IgnoreQueryFilters().FirstOrDefaultAsync(x => x.Id == result.PurchaseId);
            p.Should().NotBeNull();
            p!.SupplierId.Should().Be(validSupplierId);
        }
    }
}
