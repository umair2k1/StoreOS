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
using StoreOS.Application.Sales.RefundSale;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class RefundSaleTests : IAsyncLifetime
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

    private Guid _prodAccessoryId;
    private Guid _prodPhoneId;

    private const string TestImei = "860987654321098";

    public RefundSaleTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Refund Test Tenant", subdomain: "ref-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "RB1", "Refund Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(_roleId, "sales:refund"));

        var user = User.Create(_tenantId, $"refund-mgr@{_tenantId:N}.test", passwordHash, "Refund Manager");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        db.UserBranches.Add(UserBranch.Create(_userId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchId));

        var customer = Customer.Create(_tenantId, "Refund Customer", "9977886655", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        customer.AddToTotalPurchases(100000m);
        db.Customers.Add(customer);

        // Products
        var p1 = Product.Create(_tenantId, "Wireless Mouse", ProductCategory.Accessory, 1000m, 500m, 18m, "8471");
        db.Products.Add(p1);
        _prodAccessoryId = p1.Id;

        var inv1 = BranchInventory.Create(_tenantId, _branchId, _prodAccessoryId);
        inv1.IncreaseStock(10, 500m);
        db.BranchInventories.Add(inv1);

        var p2 = Product.Create(_tenantId, "Flagship Phone", ProductCategory.NewPhone, 50000m, 40000m, 18m, "8517");
        db.Products.Add(p2);
        _prodPhoneId = p2.Id;

        var inv2 = BranchInventory.Create(_tenantId, _branchId, _prodPhoneId);
        db.BranchInventories.Add(inv2); // 0 in stock initially

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
            permissions ?? new[] { "sales:refund" }, tokenVersion: 1);
        return token;
    }

    private async Task<(Guid saleId, Guid item1Id, Guid item2Id, Guid imeiId)> SeedSaleAsync(string invoiceNumber, decimal item1Price = 2000m, decimal item2Price = 50000m)
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var total = item1Price + item2Price;
        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchId, invoiceNumber, DateTimeOffset.UtcNow,
            _customerId, "Refund Customer", null,
            total * 0.85m, total * 0.075m, total * 0.075m, total, 0m, total,
            PaymentMode.Cash, null, null, null, null, null, "Sale for refund test", null, _userId);

        var item1 = SaleItem.Create(_tenantId, sale.Id, _prodAccessoryId, "Wireless Mouse", "Accessory", 2, item1Price / 2, 250m, "8471", false, false, item1Price * 0.85m, item1Price * 0.075m, item1Price * 0.075m, null);
        var item2 = SaleItem.Create(_tenantId, sale.Id, _prodPhoneId, "Flagship Phone", "NewPhone", 1, item2Price, 40000m, "8517", false, false, item2Price * 0.85m, item2Price * 0.075m, item2Price * 0.075m, TestImei);

        sale.AddItem(item1);
        sale.AddItem(item2);
        db.Sales.Add(sale);

        var imei = InventoryImei.Create(_tenantId, _branchId, _prodPhoneId, null, TestImei + invoiceNumber[..3], DateOnly.FromDateTime(DateTime.UtcNow), 40000m);
        imei.MarkSold(sale.Id, item2.Id);
        db.InventoryImeis.Add(imei);

        var ledgerDebit = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, sale.Id, CustomerLedgerEntryType.Invoice,
            total, _branchId, DateOnly.FromDateTime(DateTime.UtcNow), sale.Id.ToString());
        db.CustomerLedgerEntries.Add(ledgerDebit);

        await db.SaveChangesAsync();
        return (sale.Id, item1.Id, item2.Id, imei.Id);
    }

    [Fact]
    public async Task WholeSaleRefund_SetsStatusRefunded_RestoresAllStock_PreservesOriginalSaleRow()
    {
        var (saleId, _, _, imeiId) = await SeedSaleAsync("INV-REF-WHOLE", 2000m, 50000m);
        var token = IssueToken();

        // (a) Whole-sale refund with SaleItemIds = null
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund")
        {
            Content = JsonContent.Create(new
            {
                saleItemIds = (Guid[]?)null,
                reason = "Customer returned everything under warranty",
                refundAmount = 52000m
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var sale = await db.Sales.Include(s => s.Items).IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
        sale.Status.Should().Be(SaleStatus.Refunded);
        sale.GrandTotal.Should().Be(52000m); // preserved!
        sale.Items.Should().HaveCount(2);

        // Stock restored for both items
        var inv1 = await db.BranchInventories.IgnoreQueryFilters().FirstAsync(bi => bi.ProductId == _prodAccessoryId && bi.BranchId == _branchId);
        inv1.StockQty.Should().Be(12); // 10 + 2

        var inv2 = await db.BranchInventories.IgnoreQueryFilters().FirstAsync(bi => bi.ProductId == _prodPhoneId && bi.BranchId == _branchId);
        inv2.StockQty.Should().Be(1); // 0 + 1

        var imeiRow = await db.InventoryImeis.IgnoreQueryFilters().FirstAsync(i => i.Id == imeiId);
        imeiRow.Status.Should().Be(ImeiStatus.InStock);

        // Customer ledger has refund credit
        var refundCredit = await db.CustomerLedgerEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.SaleId == saleId && e.EntryType == CustomerLedgerEntryType.Refund);
        refundCredit.Should().NotBeNull();
        refundCredit!.Credit.Should().Be(52000m);

        var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Id == _customerId);
        customer.TotalPurchases.Should().Be(48000m); // 100000 - 52000
    }

    [Fact]
    public async Task PartialRefund_SaleRemainsActive_OnlySpecifiedLineRestored_AuditRecordsLines()
    {
        var (saleId, item1Id, item2Id, imeiId) = await SeedSaleAsync("INV-REF-PARTIAL", 2000m, 50000m);
        var token = IssueToken();

        // (b) Partial refund of line 1 only (Accessory)
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund")
        {
            Content = JsonContent.Create(new
            {
                saleItemIds = new[] { item1Id },
                reason = "Customer returned only the mouse",
                refundAmount = 2000m
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
        sale.Status.Should().Be(SaleStatus.Active); // REMAINS ACTIVE per deliberate design!

        // ONLY line 1 restored
        var inv1 = await db.BranchInventories.IgnoreQueryFilters().FirstAsync(bi => bi.ProductId == _prodAccessoryId && bi.BranchId == _branchId);
        inv1.StockQty.Should().Be(12); // 10 + 2

        var inv2 = await db.BranchInventories.IgnoreQueryFilters().FirstAsync(bi => bi.ProductId == _prodPhoneId && bi.BranchId == _branchId);
        inv2.StockQty.Should().Be(0); // UNTOUCHED

        var imeiRow = await db.InventoryImeis.IgnoreQueryFilters().FirstAsync(i => i.Id == imeiId);
        imeiRow.Status.Should().Be(ImeiStatus.Sold); // UNTOUCHED

        // Audit log verified
        var audit = await db.AuditLogEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(a => a.EntityId == saleId.ToString() && a.Action == "sale.refunded");
        audit.Should().NotBeNull();
        audit!.ChangesJson.Should().Contain(item1Id.ToString());
        audit.ChangesJson.Should().Contain("false"); // IsWholeSaleRefund: false
    }

    [Fact]
    public async Task RefundAmountExceedingGrandTotal_Returns422InvalidRefundAmount()
    {
        var (saleId, _, _, _) = await SeedSaleAsync("INV-REF-EXCEED", 2000m, 3000m);
        var token = IssueToken();

        // (c) RefundAmount > GrandTotal (5000)
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund")
        {
            Content = JsonContent.Create(new
            {
                saleItemIds = (Guid[]?)null,
                reason = "Invalid amount refund",
                refundAmount = 6000m
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("invalid_refund_amount");
    }

    [Fact]
    public async Task RefundingVoidedSale_Returns422SaleNotActive()
    {
        var (saleId, _, _, _) = await SeedSaleAsync("INV-REF-VOIDED", 1000m, 2000m);

        // Void the sale in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var s = await db.Sales.IgnoreQueryFilters().FirstAsync(x => x.Id == saleId);
            s.MarkVoided();
            await db.SaveChangesAsync();
        }

        var token = IssueToken();

        // (d) Refunding already-voided sale
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund")
        {
            Content = JsonContent.Create(new
            {
                saleItemIds = (Guid[]?)null,
                reason = "Refund after void",
                refundAmount = 3000m
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("sale_not_active");
    }

    [Fact]
    public async Task RestockingFeeRefund_LedgerGetsExactRefundAmountAndTotalPurchasesDecreasesAccurately()
    {
        var (saleId, item1Id, _, _) = await SeedSaleAsync("INV-REF-FEE", 10000m, 0m);
        var token = IssueToken();

        // (e) Sale is 10000m, but restocking fee applies: refund only 9000m
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund")
        {
            Content = JsonContent.Create(new
            {
                saleItemIds = new[] { item1Id },
                reason = "Returned with 10% restocking fee deduction",
                refundAmount = 9000m
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var ledgerEntry = await db.CustomerLedgerEntries.IgnoreQueryFilters()
            .FirstOrDefaultAsync(e => e.SaleId == saleId && e.EntryType == CustomerLedgerEntryType.Refund);
        ledgerEntry.Should().NotBeNull();
        ledgerEntry!.Credit.Should().Be(9000m); // exactly 9000m credited!

        var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Id == _customerId);
        customer.TotalPurchases.Should().Be(91000m); // 100000 - 9000 = 91000 (not 90000)
    }

    [Fact]
    public async Task CallerWithoutSalesRefundInDb_Returns403ViaLiveDbCheck()
    {
        var (saleId, _, _, _) = await SeedSaleAsync("INV-REF-PERM", 1000m, 1000m);

        // (f) Issue JWT with sales:refund claim
        var token = IssueToken(new[] { "sales:refund" });

        // Revoke sales:refund directly from database
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM role_permissions WHERE role_id = @rId AND permission_code = 'sales:refund';", conn))
        {
            cmd.Parameters.AddWithValue("rId", _roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/sales/{saleId}/refund")
        {
            Content = JsonContent.Create(new
            {
                saleItemIds = (Guid[]?)null,
                reason = "Unauthorized refund attempt",
                refundAmount = 2000m
            })
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("forbidden");
    }
}
