using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Repairs.Services;
using StoreOS.Application.Repairs.Services.CreateRepairService;
using StoreOS.Application.Repairs.Services.UpdateRepairService;
using StoreOS.Application.Sales.GetSaleById;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class RepairServicesTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RepairServicesTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Repair Test Tenant", subdomain: "rep-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "RP1", "Repairs Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "RepairManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "repairs:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "repmgr@test.com", passwordHash, "Repair Mgr");
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
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
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

    private string IssueToken(string[]? permissions = null)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId,
            _tenantId,
            allBranches: true,
            Array.Empty<Guid>(),
            permissions ?? new[] { "repairs:manage" },
            tokenVersion: 1);
        return token;
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken());
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());
        return client;
    }

    [Fact]
    public async Task CreateRepairService_MinimalInput_GetsAllDocumentedDefaults()
    {
        // (a) creating a repair service with minimal input (name + charge only) gets ALL
        // the documented defaults (SAC 998713, GST 18%, duration 60, warranty 30)
        using var client = CreateAuthenticatedClient();

        var command = new CreateRepairServiceCommand(
            Name: "Screen Replacement Basic",
            Description: null,
            StandardCharge: 1500m,
            Taxable: true,
            GstRate: null,
            SacCode: null,
            EstimatedDurationMinutes: null,
            WarrantyDays: null);

        var response = await client.PostAsJsonAsync("/api/repair-services", command);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = result.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync($"/api/repair-services/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await getResponse.Content.ReadFromJsonAsync<RepairServiceDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Screen Replacement Basic");
        dto.StandardCharge.Should().Be(1500m);
        dto.SacCode.Should().Be("998713");
        dto.GstRate.Should().Be(18m);
        dto.EstimatedDurationMinutes.Should().Be(60);
        dto.WarrantyDays.Should().Be(30);
        dto.Taxable.Should().BeTrue();
        dto.IsActive.Should().BeTrue();
    }

    [Fact]
    public async Task CreateRepairService_ExplicitOverrides_RespectsOverrides()
    {
        // (b) creating with explicit overrides for each field respects them instead of the defaults
        using var client = CreateAuthenticatedClient();

        var command = new CreateRepairServiceCommand(
            Name: "Motherboard BGA Repair",
            Description: "Complex chipset re-balling",
            StandardCharge: 4500m,
            Taxable: true,
            GstRate: 12m,
            SacCode: "998719",
            EstimatedDurationMinutes: 180,
            WarrantyDays: 90);

        var response = await client.PostAsJsonAsync("/api/repair-services", command);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = result.GetProperty("id").GetGuid();

        var getResponse = await client.GetAsync($"/api/repair-services/{id}");
        getResponse.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await getResponse.Content.ReadFromJsonAsync<RepairServiceDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Name.Should().Be("Motherboard BGA Repair");
        dto.Description.Should().Be("Complex chipset re-balling");
        dto.StandardCharge.Should().Be(4500m);
        dto.SacCode.Should().Be("998719");
        dto.GstRate.Should().Be(12m);
        dto.EstimatedDurationMinutes.Should().Be(180);
        dto.WarrantyDays.Should().Be(90);
    }

    [Fact]
    public async Task GetRepairServices_ByDefaultExcludesInactive_IsActiveFalseIncludesThem()
    {
        // (c) GET /api/repair-services by default excludes inactive services; passing isActive=false explicitly includes them
        using var client = CreateAuthenticatedClient();

        // 1. Create service 1 (active)
        var s1Res = await client.PostAsJsonAsync("/api/repair-services", new CreateRepairServiceCommand(
            Name: "Battery Replacement Active",
            Description: null,
            StandardCharge: 1200m,
            Taxable: true,
            GstRate: null,
            SacCode: null,
            EstimatedDurationMinutes: null,
            WarrantyDays: null));
        var s1Id = (await s1Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2. Create service 2 and deactivate it
        var s2Res = await client.PostAsJsonAsync("/api/repair-services", new CreateRepairServiceCommand(
            Name: "Charging Port Fix Inactive",
            Description: null,
            StandardCharge: 800m,
            Taxable: true,
            GstRate: null,
            SacCode: null,
            EstimatedDurationMinutes: null,
            WarrantyDays: null));
        var s2Id = (await s2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        var deactRes = await client.PostAsync($"/api/repair-services/{s2Id}/deactivate", null);
        deactRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Query without isActive parameter (default should exclude inactive)
        var defaultList = await client.GetFromJsonAsync<PagedResult<RepairServiceDto>>("/api/repair-services", JsonOpts);
        defaultList.Should().NotBeNull();
        defaultList!.Items.Should().Contain(s => s.Id == s1Id);
        defaultList.Items.Should().NotContain(s => s.Id == s2Id);
        var allList = await client.GetFromJsonAsync<PagedResult<RepairServiceDto>>("/api/repair-services?isActive=false", JsonOpts);
        allList.Should().NotBeNull();
        allList!.Items.Should().Contain(s => s.Id == s1Id);
        allList.Items.Should().Contain(s => s.Id == s2Id);
    }

    [Fact]
    public async Task DeactivateService_UsedInHistoricalSale_PreservesSaleSnapshot()
    {
        // (d) deactivating a service that has ALREADY been used in a historical sale line does not
        // affect that sale line's snapshot data (seed a sale_item referencing this service product first,
        // then deactivate, then re-fetch the sale detail and confirm its snapshot fields are unchanged)
        using var client = CreateAuthenticatedClient();

        // 1. Create repair service
        var sRes = await client.PostAsJsonAsync("/api/repair-services", new CreateRepairServiceCommand(
            Name: "Camera Sensor Repair",
            Description: "Repair back camera",
            StandardCharge: 2200m,
            Taxable: true,
            GstRate: 18m,
            SacCode: "998713",
            EstimatedDurationMinutes: 60,
            WarrantyDays: 30));
        var serviceId = (await sRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2. Seed a historical sale referencing this service product directly in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        var saleId = Guid.NewGuid();
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var sale = Sale.CreateTaxInvoice(
                _tenantId,
                _branchId,
                "INV-REP-001",
                DateTimeOffset.UtcNow,
                customerId: null,
                customerName: "Walk-in Customer",
                customerGstin: null,
                totalTaxable: 1864.41m,
                totalCgst: 167.80m,
                totalSgst: 167.79m,
                grandTotal: 2200m,
                discount: 0m,
                amountPaid: 2200m,
                paymentMode: PaymentMode.Cash,
                financeProvider: null,
                financerName: null,
                downPayment: null,
                emiDetailsJson: null,
                giftDetailsJson: null,
                description: "Historical sale with repair service",
                repairJobId: null,
                createdBy: _userId);

            typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(sale, saleId);

            var saleItem = SaleItem.Create(
                _tenantId,
                saleId,
                productId: serviceId,
                itemName: "Camera Sensor Repair",
                category: ProductCategory.RepairService,
                qty: 1,
                pricePerUnit: 2200m,
                purchasePriceSnapshot: 0m,
                hsnSnapshot: "998713",
                isMarginScheme: false,
                isGift: false,
                taxable: 1864.41m,
                cgst: 167.80m,
                sgst: 167.79m,
                imei: null);

            sale.AddItem(saleItem);
            db.Sales.Add(sale);
            await db.SaveChangesAsync();
        }

        // 3. Deactivate the repair service via API
        var deactRes = await client.PostAsync($"/api/repair-services/{serviceId}/deactivate", null);
        deactRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 4. Re-fetch the sale detail and confirm its snapshot fields are unchanged
        var saleDetailRes = await client.GetAsync($"/api/sales/{saleId}");
        saleDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var saleDetail = await saleDetailRes.Content.ReadFromJsonAsync<SaleDetailDto>(JsonOpts);
        saleDetail.Should().NotBeNull();
        saleDetail!.Items.Should().HaveCount(1);

        var item = saleDetail.Items[0];
        item.ItemName.Should().Be("Camera Sensor Repair");
        item.Category.Should().Be(ProductCategory.RepairService);
        item.PricePerUnit.Should().Be(2200m);
        item.HsnSnapshot.Should().Be("998713");
        item.Taxable.Should().Be(1864.41m);
        item.Cgst.Should().Be(167.80m);
        item.Sgst.Should().Be(167.79m);
    }

    [Fact]
    public async Task CreateRepairService_NeverCreatesBranchInventory()
    {
        // (e) this endpoint NEVER creates a branch_inventory row for the created service (Invariant 3)
        using var client = CreateAuthenticatedClient();

        var command = new CreateRepairServiceCommand(
            Name: "Software Flash Service",
            Description: "OS reinstallation",
            StandardCharge: 500m,
            Taxable: true,
            GstRate: 18m,
            SacCode: "998713",
            EstimatedDurationMinutes: 30,
            WarrantyDays: 7);

        var response = await client.PostAsJsonAsync("/api/repair-services", command);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = result.GetProperty("id").GetGuid();

        // Verify direct DB query: NO branch_inventory rows exist for this product ID
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var inventoryRows = await db.Set<BranchInventory>()
            .Where(bi => bi.ProductId == id)
            .ToListAsync();

        inventoryRows.Should().BeEmpty();
    }
}
