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
using StoreOS.Application.Sales.VoidSale;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class VoidSaleTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    private Guid _nonImeiProductId;
    private Guid _imeiProductId;
    private Guid _giftProductId;
    private Guid _imeiRowId;
    private const string TestImei = "860123456789012";

    private Guid _saleId;
    private Guid _line1Id;
    private Guid _line2Id;
    private Guid _line3Id;

    public VoidSaleTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Void Test Tenant", subdomain: "void-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "VB1", "Void Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(_roleId, "sales:void"));

        var user = User.Create(_tenantId, $"manager@{_tenantId:N}.test", passwordHash, "Void Manager");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        db.UserBranches.Add(UserBranch.Create(_userId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchId));

        // 1. Non-IMEI product (Accessory)
        var p1 = Product.Create(_tenantId, "Protective Case", ProductCategory.Accessory, 1000m, 500m, 18m, "3926");
        db.Products.Add(p1);
        _nonImeiProductId = p1.Id;

        var inv1 = BranchInventory.Create(_tenantId, _branchId, _nonImeiProductId);
        inv1.IncreaseStock(5, 500m);
        db.BranchInventories.Add(inv1);

        // 2. IMEI product (NewPhone)
        var p2 = Product.Create(_tenantId, "Smartphone X", ProductCategory.NewPhone, 50000m, 40000m, 18m, "8517");
        db.Products.Add(p2);
        _imeiProductId = p2.Id;

        var inv2 = BranchInventory.Create(_tenantId, _branchId, _imeiProductId);
        db.BranchInventories.Add(inv2);

        // 3. Gift product (Accessory)
        var p3 = Product.Create(_tenantId, "Free Keyring", ProductCategory.Accessory, 0m, 50m, 18m, "7326");
        db.Products.Add(p3);
        _giftProductId = p3.Id;

        var inv3 = BranchInventory.Create(_tenantId, _branchId, _giftProductId);
        inv3.IncreaseStock(3, 50m);
        db.BranchInventories.Add(inv3);

        // Customer
        var customer = Customer.Create(_tenantId, "Void Customer", "9988776655", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        customer.AddToTotalPurchases(52000m);
        db.Customers.Add(customer);

        // Create Sale with 3 lines
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-VOID-001", DateTimeOffset.UtcNow,
            _customerId, "Void Customer", null,
            44067.80m, 3966.10m, 3966.10m, 52000m, 0m, 52000m,
            PaymentMode.Cash, null, null, null, null, null, "Sale to be voided", null, _userId);

        var item1 = SaleItem.Create(_tenantId, sale.Id, _nonImeiProductId, "Protective Case", "Accessory", 2, 1000m, 500m, "3926", false, false, 1694.92m, 152.54m, 152.54m, null);
        var item2 = SaleItem.Create(_tenantId, sale.Id, _imeiProductId, "Smartphone X", "NewPhone", 1, 50000m, 40000m, "8517", false, false, 42372.88m, 3813.56m, 3813.56m, TestImei);
        var item3 = SaleItem.Create(_tenantId, sale.Id, _giftProductId, "Free Keyring", "Accessory", 1, 0m, 50m, "7326", false, true, 0m, 0m, 0m, null);

        sale.AddItem(item1);
        sale.AddItem(item2);
        sale.AddItem(item3);
        db.Sales.Add(sale);
        _saleId = sale.Id;
        _line1Id = item1.Id;
        _line2Id = item2.Id;
        _line3Id = item3.Id;

        // IMEI row: initially sold to this sale
        var imei = InventoryImei.Create(_tenantId, _branchId, _imeiProductId, null, TestImei, DateOnly.FromDateTime(DateTime.UtcNow), 40000m);
        imei.MarkSold(sale.Id, item2.Id);
        db.InventoryImeis.Add(imei);
        _imeiRowId = imei.Id;

        // Customer ledger initial debit
        var ledgerDebit = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, sale.Id, CustomerLedgerEntryType.Invoice,
            52000m, _branchId, DateOnly.FromDateTime(DateTime.UtcNow), sale.Id.ToString());
        db.CustomerLedgerEntries.Add(ledgerDebit);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM customer_payments WHERE tenant_id = @t; " +
            "DELETE FROM customer_ledger_entries WHERE tenant_id = @t; " +
            "DELETE FROM sale_items WHERE tenant_id = @t; " +
            "DELETE FROM sales WHERE tenant_id = @t; " +
            "DELETE FROM idempotency_keys WHERE tenant_id = @t; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @t; " +
            "DELETE FROM inventory_imeis WHERE tenant_id = @t; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @t; " +
            "DELETE FROM products WHERE tenant_id = @t; " +
            "DELETE FROM customers WHERE tenant_id = @t; " +
            "DELETE FROM user_role_assignments WHERE user_id = @u; " +
            "DELETE FROM user_branches WHERE user_id = @u; " +
            "DELETE FROM users WHERE tenant_id = @t; " +
            "DELETE FROM role_permissions WHERE role_id = @r; " +
            "DELETE FROM roles WHERE tenant_id = @t; " +
            "DELETE FROM branches WHERE tenant_id = @t; " +
            "DELETE FROM tenants WHERE id = @t;", conn);
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(IEnumerable<string>? permissions = null)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: false, new[] { _branchId },
            permissions ?? new[] { "sales:void" }, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task VoidSale_ActiveSale_ReversesStockImeiAndLedger_PreservingOriginalSaleRow()
    {
        var token = IssueToken();

        // 1. Perform void via POST /api/sales/{id}/void
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{_saleId}/void")
        {
            Content = JsonContent.Create(new { reason = "Customer canceled in store" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Query DB to verify all assertions
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        // (a) Original Sale row preserved: only Status changed to Voided
        var sale = await db.Sales.Include(s => s.Items).IgnoreQueryFilters().FirstAsync(s => s.Id == _saleId);
        sale.Status.Should().Be(SaleStatus.Voided);
        sale.InvoiceNumber.Should().Be("INV-VOID-001");
        sale.GrandTotal.Should().Be(52000m);
        sale.TotalTaxable.Should().Be(44067.80m);
        sale.TotalCgst.Should().Be(3966.10m);
        sale.TotalSgst.Should().Be(3966.10m);
        sale.AmountPaid.Should().Be(52000m);
        sale.Items.Should().HaveCount(3);

        // (b) Stock restored for non-IMEI physical line (5 + 2 = 7)
        var inv1 = await db.BranchInventories.IgnoreQueryFilters()
            .FirstAsync(bi => bi.ProductId == _nonImeiProductId && bi.BranchId == _branchId);
        inv1.StockQty.Should().Be(7);

        // (c) IMEI-tracked line: stock restored (0 + 1 = 1) AND IMEI transitions to InStock
        var inv2 = await db.BranchInventories.IgnoreQueryFilters()
            .FirstAsync(bi => bi.ProductId == _imeiProductId && bi.BranchId == _branchId);
        inv2.StockQty.Should().Be(1);

        var imeiRow = await db.InventoryImeis.IgnoreQueryFilters().FirstAsync(i => i.Id == _imeiRowId);
        imeiRow.Status.Should().Be(ImeiStatus.InStock);
        imeiRow.SaleId.Should().BeNull();
        imeiRow.SaleItemId.Should().BeNull();

        // (d) Gift line: stock IS restored to stock on void (3 + 1 = 4)
        var inv3 = await db.BranchInventories.IgnoreQueryFilters()
            .FirstAsync(bi => bi.ProductId == _giftProductId && bi.BranchId == _branchId);
        inv3.StockQty.Should().Be(4);

        // (f) Customer ledger gets InvoiceVoid credit and total_purchases decreases
        var voidCredit = await db.CustomerLedgerEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.SaleId == _saleId && e.EntryType == CustomerLedgerEntryType.InvoiceVoid);
        voidCredit.Should().NotBeNull();
        voidCredit!.Credit.Should().Be(52000m);
        voidCredit.Debit.Should().Be(0m);

        var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Id == _customerId);
        customer.TotalPurchases.Should().Be(0m); // 52000 - 52000 = 0
    }

    [Fact]
    public async Task VoidSale_AlreadyVoided_Returns422SaleNotActive()
    {
        var token = IssueToken();

        // Void first time
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{_saleId}/void")
        {
            Content = JsonContent.Create(new { reason = "First void" })
        };
        req1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req1.Headers.Add("X-Branch-Id", _branchId.ToString());
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);

        // (e) Void second time -> 422 sale_not_active
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{_saleId}/void")
        {
            Content = JsonContent.Create(new { reason = "Second void" })
        };
        req2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        req2.Headers.Add("X-Branch-Id", _branchId.ToString());
        var res2 = await _client.SendAsync(req2);

        res2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await res2.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("sale_not_active");
    }

    [Fact]
    public async Task VoidSale_WithoutSalesVoidPermissionInDb_Returns403ViaLiveDbCheck()
    {
        // (g) 1. Issue JWT that has sales:void claim
        var token = IssueToken(new[] { "sales:void" });

        // 2. Revoke sales:void permission directly in the database
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM role_permissions WHERE role_id = @rId AND permission_code = 'sales:void';", conn))
        {
            cmd.Parameters.AddWithValue("rId", _roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Attempt void with stale token
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{_saleId}/void")
        {
            Content = JsonContent.Create(new { reason = "Attempted unauthorized void" })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);

        // LiveDbPermissionBehavior runs live query against DB -> immediately rejects with 403 Forbidden
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("forbidden");
    }
}
