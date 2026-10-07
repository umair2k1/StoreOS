using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Reporting.InventoryExport;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Reporting;

[Collection("postgres")]
public class InventoryExportTests : IAsyncLifetime
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

    public InventoryExportTests(PostgresTestFixture fixture)
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

        // 1. Tenant & Branch
        var tenant = Tenant.Create("Export Tenant", timezone: "Asia/Kolkata", subdomain: "exp-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "BR1", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // 2. Manager user (with inventory:cost:view permission)
        var managerUser = User.Create(_tenantId, $"mgr-{_managerUserId:N}@storeos.test", passwordHash, "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(managerUser, _managerUserId);
        db.Users.Add(managerUser);

        var managerRole = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(managerRole, _managerRoleId);
        db.Roles.Add(managerRole);
        db.RolePermissions.Add(RolePermission.Create(_managerRoleId, PermissionCatalog.InventoryCostView));

        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_managerUserId, _managerRoleId, _branchId));
        db.UserBranches.Add(UserBranch.Create(_managerUserId, _branchId, isDefault: true));

        // 3. Cashier user (without inventory:cost:view permission)
        var cashierUser = User.Create(_tenantId, $"cashier-{_cashierUserId:N}@storeos.test", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_cashierUserId, _cashierRoleId, _branchId));
        db.UserBranches.Add(UserBranch.Create(_cashierUserId, _branchId, isDefault: true));

        // 4. Products & Stock & IMEIs
        // Prod 1: New Phone with 12 in-stock IMEIs
        var prodNew = Product.Create(_tenantId, "Flagship Phone 16", ProductCategory.NewPhone, 100000m, 80000m, 18m, "8517");
        db.Products.Add(prodNew);

        var invNew = BranchInventory.Create(_tenantId, _branchId, prodNew.Id);
        invNew.IncreaseStock(15, 80000m);
        db.BranchInventories.Add(invNew);

        for (int i = 1; i <= 12; i++)
        {
            var imei = InventoryImei.Create(
                _tenantId, _branchId, prodNew.Id, null, $"IMEI{i:D4}", new DateOnly(2026, 10, 1), 80000m);
            db.InventoryImeis.Add(imei);
        }

        // Prod 2: Used Phone with 3 in-stock IMEIs
        var prodUsed = Product.Create(_tenantId, "Pre-owned Phone X", ProductCategory.UsedPhone, 30000m, 20000m, 18m, "8517");
        db.Products.Add(prodUsed);

        var invUsed = BranchInventory.Create(_tenantId, _branchId, prodUsed.Id);
        invUsed.IncreaseStock(3, 20000m);
        db.BranchInventories.Add(invUsed);

        for (int i = 1; i <= 3; i++)
        {
            var imei = InventoryImei.Create(
                _tenantId, _branchId, prodUsed.Id, null, $"USED{i:D3}", new DateOnly(2026, 10, 1), 20000m);
            db.InventoryImeis.Add(imei);
        }

        // Prod 3: Accessory with supplier
        var prodAcc = Product.Create(_tenantId, "Case Cover Armor", ProductCategory.Accessory, 1000m, 300m, 18m, "8544");
        db.Products.Add(prodAcc);

        var invAcc = BranchInventory.Create(_tenantId, _branchId, prodAcc.Id);
        invAcc.IncreaseStock(50, 300m);
        db.BranchInventories.Add(invAcc);

        var purchaseAcc = Purchase.Create(
            _tenantId, _branchId, prodAcc.Id, 50, 300m, null, "Global Acc Ltd", null, null,
            new DateOnly(2026, 10, 1), _managerUserId);
        db.Purchases.Add(purchaseAcc);

        // Prod 4: Repair Service
        var prodService = Product.Create(_tenantId, "Screen Glass Fix", ProductCategory.RepairService, 3500m, 0m, 18m, "998713");
        typeof(Product).GetProperty(nameof(Product.Description))!.SetValue(prodService, "Fix cracked front glass");
        db.Products.Add(prodService);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM purchases WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_imeis WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM user_role_assignments WHERE user_id IN (@uMgr, @uCsh); " +
            "DELETE FROM user_branches WHERE user_id IN (@uMgr, @uCsh); " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("uMgr", _managerUserId);
        cmd.Parameters.AddWithValue("uCsh", _cashierUserId);
        cmd.Parameters.AddWithValue("rId", _managerRoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches: true, branchIds: new[] { _branchId }, permissions, tokenVersion: 1);
        return token;
    }

    private async Task<InventoryExportDto> GetExportJsonAsync(string query = "", bool asManager = true)
    {
        var permissions = asManager ? new[] { PermissionCatalog.InventoryCostView } : Array.Empty<string>();
        var userId = asManager ? _managerUserId : _cashierUserId;
        var token = IssueToken(userId, permissions);

        var url = string.IsNullOrWhiteSpace(query)
            ? "/api/exports/inventory?format=json"
            : $"/api/exports/inventory?{query}&format=json";

        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var response = await _client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP {(int)response.StatusCode}: {err}");
        }

        var dto = await response.Content.ReadFromJsonAsync<InventoryExportDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    [Fact]
    public async Task InventoryExport_AllFourCategories_ReturnsPhysicalAndServicesSeparately()
    {
        // (a) requesting all 4 categories returns physical items in PhysicalItems and repair services in Services, never mixed
        var query = "categories=NewPhone&categories=UsedPhone&categories=Accessory&categories=RepairService";
        var export = await GetExportJsonAsync(query);

        // Physical items should contain NewPhone, UsedPhone, Accessory
        export.PhysicalItems.Should().NotBeEmpty();
        export.PhysicalItems.Should().OnlyContain(x => x.Category != ProductCategory.RepairService);
        export.PhysicalItems.Select(x => x.Item).Should().Contain(new[] { "Flagship Phone 16", "Pre-owned Phone X", "Case Cover Armor" });

        // Services should contain only RepairService
        export.Services.Should().NotBeEmpty();
        export.Services.Should().OnlyContain(x => x.Service == "Screen Glass Fix");
        export.Services.First().Description.Should().Be("Fix cracked front glass");
        export.Services.First().ServiceCharge.Should().Be(3500m);
    }

    [Fact]
    public async Task InventoryExport_OnlyRepairService_ReturnsEmptyPhysicalListAndPopulatedServicesList()
    {
        // (b) requesting only ["RepairService"] returns an EMPTY PhysicalItems list and a populated Services list
        var export = await GetExportJsonAsync("categories=RepairService");

        export.PhysicalItems.Should().BeEmpty();
        export.Services.Should().NotBeEmpty();
        export.Services.Should().ContainSingle(s => s.Service == "Screen Glass Fix");
    }

    [Fact]
    public async Task InventoryExport_ImeiSummary_TruncatesWithPlusMoreForOver10Imeis()
    {
        // (c) an IMEI-tracked item's ImeiSummary correctly summarizes/truncates for a product with more than 10 active IMEIs
        var export = await GetExportJsonAsync("categories=NewPhone&categories=UsedPhone");

        var flagship = export.PhysicalItems.First(p => p.Item == "Flagship Phone 16");
        flagship.ImeiSummary.Should().NotBeNull();
        flagship.ImeiSummary.Should().Be("IMEI0001, IMEI0002, IMEI0003, IMEI0004, IMEI0005, IMEI0006, IMEI0007, IMEI0008, IMEI0009, IMEI0010, +2 more");

        var used = export.PhysicalItems.First(p => p.Item == "Pre-owned Phone X");
        used.ImeiSummary.Should().Be("USED001, USED002, USED003");
    }

    [Fact]
    public async Task InventoryExport_SubsetOfCategories_ReturnsOnlyRequestedCategory()
    {
        // (d) requesting a subset of categories (e.g. just ["Accessory"]) returns only that category's physical items
        var export = await GetExportJsonAsync("categories=Accessory");

        export.PhysicalItems.Should().NotBeEmpty();
        export.PhysicalItems.Should().OnlyContain(p => p.Category == ProductCategory.Accessory);
        export.PhysicalItems.Should().Contain(p => p.Item == "Case Cover Armor" && p.Supplier == "Global Acc Ltd");
        export.Services.Should().BeEmpty();
    }

    [Fact]
    public async Task InventoryExport_CsvFormat_EmitsUtf8BomAndHeaders()
    {
        // CSV export: UTF-8 BOM, RFC 4180 columns, Content-Disposition filename
        var token = IssueToken(_managerUserId, new[] { PermissionCatalog.InventoryCostView });
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/exports/inventory?format=csv");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().StartWith("inventory-").And.EndWith(".csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // UTF-8 BOM: 0xEF, 0xBB, 0xBF
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var csvText = Encoding.UTF8.GetString(bytes[3..]);
        csvText.Should().Contain("Item,Category,Qty,PurchasePrice,SellPrice,GstRate,Hsn,Supplier,ImeiCount");
        csvText.Should().Contain("Service,Description,ServiceCharge,Taxable,GstRate,Sac");
        csvText.Should().Contain("Flagship Phone 16");
        csvText.Should().Contain("Screen Glass Fix");
    }

    [Fact]
    public async Task InventoryExport_CallerLackingPermission_HidesPurchasePrice()
    {
        // Hide PurchasePrice when caller lacks inventory:cost:view
        var export = await GetExportJsonAsync("categories=NewPhone", asManager: false);

        export.PhysicalItems.Should().NotBeEmpty();
        export.PhysicalItems.Should().OnlyContain(p => p.PurchasePrice == 0m);
    }
}
