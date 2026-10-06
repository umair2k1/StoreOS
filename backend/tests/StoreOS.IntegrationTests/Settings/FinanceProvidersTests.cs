using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Application.Settings.FinanceProviders;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Settings;

[Collection("postgres")]
public class FinanceProvidersTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _adminUserId = Guid.NewGuid();
    private readonly Guid _adminRoleId = Guid.NewGuid();

    private Guid _phoneProductId;
    private string _adminToken = string.Empty;

    public FinanceProvidersTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("FP Tenant", subdomain: "fp-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "FP1", "Finance Prov Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Role with settings:manage and pos:checkout
        var adminRole = Role.Create(_tenantId, "AdminRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(adminRole, _adminRoleId);
        db.Roles.Add(adminRole);

        db.RolePermissions.Add(RolePermission.Create(_adminRoleId, "settings:manage"));
        db.RolePermissions.Add(RolePermission.Create(_adminRoleId, "pos:checkout"));

        var adminUser = User.Create(_tenantId, "admin@fptest.com", passwordHash, "Admin User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(adminUser, _adminUserId);
        db.Users.Add(adminUser);

        db.UserBranches.Add(UserBranch.Create(_adminUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_adminUserId, _adminRoleId, _branchId));

        var product = Product.Create(_tenantId, "Smartphone", ProductCategory.NewPhone, 20000m, 15000m, 18m, "85171300");
        db.Products.Add(product);
        _phoneProductId = product.Id;

        var inv = BranchInventory.Create(_tenantId, _branchId, _phoneProductId);
        inv.IncreaseStock(10, 15000m);
        db.BranchInventories.Add(inv);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _adminUserId, _tenantId, allBranches: false, new[] { _branchId },
            new[] { "settings:manage", "pos:checkout" }, tokenVersion: 1);
        _adminToken = token;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM customer_payments WHERE tenant_id = @t;
            DELETE FROM customer_ledger_entries WHERE tenant_id = @t;
            DELETE FROM sale_items WHERE tenant_id = @t;
            DELETE FROM sales WHERE tenant_id = @t;
            DELETE FROM idempotency_keys WHERE tenant_id = @t;
            DELETE FROM settings WHERE tenant_id = @t;
            DELETE FROM refresh_tokens WHERE user_id = @u;
            DELETE FROM user_role_assignments WHERE user_id = @u;
            DELETE FROM user_branches WHERE user_id = @u;
            DELETE FROM users WHERE tenant_id = @t;
            DELETE FROM role_permissions WHERE role_id = @r;
            DELETE FROM roles WHERE tenant_id = @t;
            DELETE FROM inventory_movements WHERE tenant_id = @t;
            DELETE FROM branch_inventory WHERE tenant_id = @t;
            DELETE FROM products WHERE tenant_id = @t;
            DELETE FROM invoice_sequences WHERE tenant_id = @t;
            DELETE FROM branches WHERE tenant_id = @t;
            DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("u", _adminUserId);
        cmd.Parameters.AddWithValue("r", _adminRoleId);
        await cmd.ExecuteNonQueryAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string url, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        if (body != null)
        {
            req.Content = JsonContent.Create(body);
        }
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _adminToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        return req;
    }

    [Fact]
    public async Task FinanceProviders_FullLifecycle_And_HistoricalSalesPreserved()
    {
        // 1. Initial GET is empty
        using (var req = CreateRequest(HttpMethod.Get, "/api/settings/finance-providers"))
        {
            var res = await _client.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
            var providers = await res.Content.ReadFromJsonAsync<List<FinanceProviderDto>>();
            providers.Should().BeEmpty();
        }

        // 2. Add "HDFC Bank" -> appears in subsequent GET
        using (var req = CreateRequest(HttpMethod.Post, "/api/settings/finance-providers", new { DisplayName = "HDFC Bank" }))
        {
            var res = await _client.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        }

        using (var req = CreateRequest(HttpMethod.Get, "/api/settings/finance-providers"))
        {
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var providers = await res.Content.ReadFromJsonAsync<List<FinanceProviderDto>>();
            providers.Should().HaveCount(1);
            providers![0].NormalizedName.Should().Be("hdfc bank");
            providers[0].DisplayName.Should().Be("HDFC Bank");
        }

        // 3. Add case-insensitive duplicate "hdfc bank" -> does NOT create second entry
        using (var req = CreateRequest(HttpMethod.Post, "/api/settings/finance-providers", new { DisplayName = "hdfc bank" }))
        {
            var res = await _client.SendAsync(req);
            var content = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.OK, because: content);
        }

        using (var req = CreateRequest(HttpMethod.Get, "/api/settings/finance-providers"))
        {
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var providers = await res.Content.ReadFromJsonAsync<List<FinanceProviderDto>>();
            providers.Should().HaveCount(1);
            // Original display name casing is preserved
            providers![0].DisplayName.Should().Be("HDFC Bank");
        }

        // 4. Create a checkout with FinanceProvider = "HDFC Bank" (historical sale)
        var checkoutKey = Guid.NewGuid().ToString();
        var checkoutCmd = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_phoneProductId, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Financed Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment>(),
            Finance: new CheckoutFinanceDetails("HDFC Bank", "HDFC Bank Retail", 5000m, 12, 10m),
            Description: "Financed sale to preserve"
        );

        using (var req = CreateRequest(HttpMethod.Post, "/api/pos/checkout", checkoutCmd))
        {
            req.Headers.Add("Idempotency-Key", checkoutKey);
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 5. Remove "HDFC Bank" from suggestion list
        using (var req = CreateRequest(HttpMethod.Delete, "/api/settings/finance-providers/hdfc%20bank"))
        {
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.NoContent);
        }

        // Confirm provider is removed from suggestion list
        using (var req = CreateRequest(HttpMethod.Get, "/api/settings/finance-providers"))
        {
            var res = await _client.SendAsync(req);
            res.StatusCode.Should().Be(HttpStatusCode.OK);
            var providers = await res.Content.ReadFromJsonAsync<List<FinanceProviderDto>>();
            providers.Should().BeEmpty();
        }

        // 6. CRITICAL TEST: Direct DB query against historical sales confirms sales.finance_provider STILL reads "HDFC Bank"
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var historicalSale = await db.Sales.IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.TenantId == _tenantId && s.Description == "Financed sale to preserve");

        historicalSale.Should().NotBeNull();
        historicalSale!.FinanceProvider.Should().Be("HDFC Bank");
        historicalSale.FinancerName.Should().Be("HDFC Bank Retail");
    }
}
