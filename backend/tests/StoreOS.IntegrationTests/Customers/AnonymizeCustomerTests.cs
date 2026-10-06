using System.Net;
using System.Net.Http.Headers;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Customers;

[Collection("postgres")]
public class AnonymizeCustomerTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _staffUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _staffRoleId = Guid.NewGuid();
    private readonly Guid _managerRoleId = Guid.NewGuid();

    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _productId = Guid.NewGuid();
    private readonly Guid _saleId = Guid.NewGuid();
    private readonly Guid _saleItemId = Guid.NewGuid();

    private string _staffToken = default!;
    private string _managerToken = default!;

    public AnonymizeCustomerTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("DPDP Tenant", subdomain: "dp-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "BRD", "DPDP Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Staff role: pos:checkout only (no settings:manage)
        var staffRole = Role.Create(_tenantId, "Staff");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(staffRole, _staffRoleId);
        db.Roles.Add(staffRole);
        db.RolePermissions.Add(RolePermission.Create(_staffRoleId, "pos:checkout"));

        // Manager role: settings:manage
        var managerRole = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(managerRole, _managerRoleId);
        db.Roles.Add(managerRole);
        db.RolePermissions.Add(RolePermission.Create(_managerRoleId, "settings:manage"));

        var staffUser = User.Create(_tenantId, $"staff@{_tenantId:N}.test", passwordHash, "Staff User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(staffUser, _staffUserId);
        db.Users.Add(staffUser);
        db.UserBranches.Add(UserBranch.Create(_staffUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_staffUserId, _staffRoleId, _branchId));

        var managerUser = User.Create(_tenantId, $"manager@{_tenantId:N}.test", passwordHash, "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(managerUser, _managerUserId);
        db.Users.Add(managerUser);
        db.UserBranches.Add(UserBranch.Create(_managerUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_managerUserId, _managerRoleId, _branchId));

        var product = Product.Create(
            _tenantId, "OnePlus 12", ProductCategory.NewPhone, 60000m, 50000m, 18m, "85171300");
        typeof(Product).GetProperty(nameof(Product.Id))!.SetValue(product, _productId);
        db.Products.Add(product);

        var customer = Customer.Create(_tenantId, "John Doe", "9876543210", "29AAAAA0000A1Z5");
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        customer.SetCreditLimit(100000m);
        db.Customers.Add(customer);

        var saleDate = DateTimeOffset.UtcNow;
        var sale = Sale.CreateTaxInvoice(_tenantId, _branchId, "INV-DPDP-001", saleDate,
            _customerId, "John Doe", null, 60000m, 5400m, 5400m, 70800m, 0m, 70800m,
            PaymentMode.Cash, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(sale, _saleId);
        db.Sales.Add(sale);

        var saleItem = SaleItem.Create(
            _tenantId, _saleId, _productId, "OnePlus 12", ProductCategory.NewPhone, 1, 60000m,
            50000m, "85171300", false, false, 60000m, 5400m, 5400m, null);
        typeof(SaleItem).GetProperty(nameof(SaleItem.Id))!.SetValue(saleItem, _saleItemId);
        db.SaleItems.Add(saleItem);

        // Ledger entry
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, _saleId, CustomerLedgerEntryType.Invoice, 70800m,
            _branchId, DateOnly.FromDateTime(saleDate.DateTime), "INV-DPDP-001"));

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var (sTok, _) = jwtService.IssueAccessToken(
            _staffUserId, _tenantId, allBranches: false, new[] { _branchId }, new[] { "pos:checkout" }, tokenVersion: 1);
        _staffToken = sTok;

        var (mTok, _) = jwtService.IssueAccessToken(
            _managerUserId, _tenantId, allBranches: false, new[] { _branchId }, new[] { "settings:manage" }, tokenVersion: 1);
        _managerToken = mTok;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM customer_ledger_entries WHERE tenant_id = @tid;
            DELETE FROM sale_items WHERE tenant_id = @tid;
            DELETE FROM sales WHERE tenant_id = @tid;
            DELETE FROM customers WHERE tenant_id = @tid;
            DELETE FROM products WHERE tenant_id = @tid;
            DELETE FROM user_role_assignments WHERE user_id = @uid1 OR user_id = @uid2;
            DELETE FROM user_branches WHERE user_id = @uid1 OR user_id = @uid2;
            DELETE FROM role_permissions WHERE role_id = @rid1 OR role_id = @rid2;
            DELETE FROM roles WHERE tenant_id = @tid;
            DELETE FROM users WHERE tenant_id = @tid;
            DELETE FROM branches WHERE tenant_id = @tid;
            DELETE FROM tenants WHERE id = @tid;
        ";
        cmd.Parameters.AddWithValue("tid", _tenantId);
        cmd.Parameters.AddWithValue("uid1", _staffUserId);
        cmd.Parameters.AddWithValue("uid2", _managerUserId);
        cmd.Parameters.AddWithValue("rid1", _staffRoleId);
        cmd.Parameters.AddWithValue("rid2", _managerRoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Anonymize_CallerWithoutSettingsManage_Returns403ViaLiveDbCheck()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _staffToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.PostAsync($"/api/customers/{_customerId}/anonymize", null);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Anonymize_ScrubsPiiAndSaleCustomerName_PreservesFiscalDataAndLedgerEntries()
    {
        // 1. Take snapshot of Sale, SaleItems, and CustomerLedger pre-anonymization
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var dbPre = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var preSale = await dbPre.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == _saleId);
            var preItem = await dbPre.SaleItems.IgnoreQueryFilters().FirstAsync(i => i.Id == _saleItemId);
            var preLedger = await dbPre.CustomerLedgerEntries.IgnoreQueryFilters().FirstAsync(e => e.CustomerId == _customerId);

            preSale.CustomerName.Should().Be("John Doe");
        }

        // 2. Perform anonymization via Manager
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _managerToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var response = await _client.PostAsync($"/api/customers/{_customerId}/anonymize", null);
        response.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Verify Customer record: PII scrubbed
        await using (var dbPost = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var customer = await dbPost.Customers.IgnoreQueryFilters().FirstAsync(c => c.Id == _customerId);
            customer.Name.Should().Be("Anonymized Customer");
            customer.Phone.Should().BeNull();
            customer.Gstin.Should().BeNull();

            // 4. Verify Sale: customer_name scrubbed, all fiscal fields byte-for-byte unchanged
            var sale = await dbPost.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == _saleId);
            sale.CustomerName.Should().Be("Anonymized Customer");

            // Fiscal fields byte-for-byte unchanged:
            sale.InvoiceNumber.Should().Be("INV-DPDP-001");
            sale.GrandTotal.Should().Be(70800m);
            sale.TotalTaxable.Should().Be(60000m);
            sale.TotalCgst.Should().Be(5400m);
            sale.TotalSgst.Should().Be(5400m);
            sale.AmountPaid.Should().Be(70800m);
            (sale.GrandTotal - sale.AmountPaid).Should().Be(0m);
            sale.Discount.Should().Be(0m);
            sale.PaymentMode.Should().Be(PaymentMode.Cash);
            sale.SaleDate.Should().Be(sale.SaleDate);

            // 5. Verify SaleItem: completely unchanged
            var item = await dbPost.SaleItems.IgnoreQueryFilters().FirstAsync(i => i.Id == _saleItemId);
            item.ItemName.Should().Be("OnePlus 12");
            item.Category.Should().Be(ProductCategory.NewPhone);
            item.Qty.Should().Be(1);
            item.PricePerUnit.Should().Be(60000m);
            item.PurchasePriceSnapshot.Should().Be(50000m);
            item.HsnSnapshot.Should().Be("85171300");
            item.IsMarginScheme.Should().BeFalse();
            item.IsGift.Should().BeFalse();
            item.Taxable.Should().Be(60000m);
            item.Cgst.Should().Be(5400m);
            item.Sgst.Should().Be(5400m);

            // 6. Verify customer_ledger_entries: rows untouched
            var ledger = await dbPost.CustomerLedgerEntries.IgnoreQueryFilters().FirstAsync(e => e.CustomerId == _customerId);
            ledger.Debit.Should().Be(70800m);
            ledger.Credit.Should().Be(0m);
            ledger.EntryType.Should().Be(CustomerLedgerEntryType.Invoice);
            ledger.RefId.Should().Be("INV-DPDP-001");
        }
    }
}
