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
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class CreateEstimateEndpointTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    private Guid _prod1Id;
    private Guid _prod2Id;
    private string _cashierToken = string.Empty;

    public CreateEstimateEndpointTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Estimate Ep Tenant", subdomain: "eep-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "EE1", "Estimate Ep Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Role with pos:checkout
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        var cashierPerm = RolePermission.Create(_cashierRoleId, "pos:checkout");
        db.RolePermissions.Add(cashierPerm);

        var cashierUser = User.Create(_tenantId, "cashier@eep.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        db.UserBranches.Add(UserBranch.Create(_cashierUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_cashierUserId, _cashierRoleId, _branchId));

        var p1 = Product.Create(_tenantId, "Mechanical Keyboard", ProductCategory.Accessory, 4000m, 2000m, 18m, "84716060");
        db.Products.Add(p1);
        _prod1Id = p1.Id;

        var inv1 = BranchInventory.Create(_tenantId, _branchId, _prod1Id);
        inv1.IncreaseStock(10, 2000m);
        db.BranchInventories.Add(inv1);

        var p2 = Product.Create(_tenantId, "Gaming Headset", ProductCategory.Accessory, 2500m, 1000m, 18m, "85183000");
        db.Products.Add(p2);
        _prod2Id = p2.Id;

        var inv2 = BranchInventory.Create(_tenantId, _branchId, _prod2Id);
        inv2.IncreaseStock(10, 1000m);
        db.BranchInventories.Add(inv2);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _cashierUserId, _tenantId, allBranches: false, new[] { _branchId }, new[] { "pos:checkout" }, tokenVersion: 1);
        _cashierToken = token;
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
        cmd.Parameters.AddWithValue("u", _cashierUserId);
        cmd.Parameters.AddWithValue("r", _cashierRoleId);
        await cmd.ExecuteNonQueryAsync();
        _client.Dispose();
        _factory.Dispose();
    }

    private HttpRequestMessage CreateRequest(object body, string? idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/pos/estimate")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        if (idempotencyKey != null)
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return req;
    }

    [Fact]
    public async Task PostEstimate_WithoutIdempotencyKey_Succeeds201_StockUnaffected_AndDatabaseShowsEstimateType()
    {
        var body = new
        {
            lines = new[] { new { productId = _prod1Id, qty = 2, imei = (string?)null, useMarginScheme = false, isGift = false, overridePricePerUnit = (decimal?)null } },
            customerId = (Guid?)null,
            walkInCustomerName = "Casual Shopper",
            invoiceDiscount = 0m,
            payments = Array.Empty<object>(),
            description = "No key estimate"
        };

        // (a) POST without Idempotency-Key header succeeds with 201 Created (optional here!)
        using var req = CreateRequest(body, idempotencyKey: null);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        res.Headers.Location.Should().NotBeNull();

        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = json.GetProperty("saleId").GetGuid();
        var invoiceNumber = json.GetProperty("invoiceNumber").GetString();
        json.GetProperty("grandTotal").GetDecimal().Should().Be(8000m);

        invoiceNumber.Should().Contain("/E/");

        // (b) Verify DB row has invoice_type = 'Estimate'
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == saleId);
        sale.InvoiceType.Should().Be(InvoiceType.Estimate);
        sale.Status.Should().Be(SaleStatus.Active);
        sale.GrandTotal.Should().Be(8000m);

        // (c) Stock is unaffected (remains 10)
        var inv = await db.BranchInventories.IgnoreQueryFilters().FirstAsync(bi => bi.ProductId == _prod1Id && bi.BranchId == _branchId);
        inv.StockQty.Should().Be(10);
    }

    [Fact]
    public async Task PostEstimate_WithIdempotencyKey_SupportsReplayAndDetectsPayloadMismatch()
    {
        var key = Guid.NewGuid().ToString();

        var body1 = new
        {
            lines = new[] { new { productId = _prod1Id, qty = 1, imei = (string?)null, useMarginScheme = false, isGift = false, overridePricePerUnit = (decimal?)null } },
            customerId = (Guid?)null,
            walkInCustomerName = "Shopper 1",
            invoiceDiscount = 0m,
            payments = Array.Empty<object>(),
            description = "Initial Payload"
        };

        var body2 = new
        {
            lines = new[] { new { productId = _prod2Id, qty = 1, imei = (string?)null, useMarginScheme = false, isGift = false, overridePricePerUnit = (decimal?)null } },
            customerId = (Guid?)null,
            walkInCustomerName = "Shopper 2",
            invoiceDiscount = 0m,
            payments = Array.Empty<object>(),
            description = "Conflicting Payload"
        };

        // First attempt with key -> 201 Created
        using (var req1 = CreateRequest(body1, key))
        {
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // Replay same key and same body -> 201 Created with Idempotent-Replayed: true
        using (var reqReplay = CreateRequest(body1, key))
        {
            var resReplay = await _client.SendAsync(reqReplay);
            resReplay.StatusCode.Should().Be(HttpStatusCode.Created);
            resReplay.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
            resReplay.Headers.GetValues("Idempotent-Replayed").First().Should().Be("true");
        }

        // Replay same key with conflicting body -> 409 Conflict (idempotency_key_reuse)
        using (var reqConflict = CreateRequest(body2, key))
        {
            var resConflict = await _client.SendAsync(reqConflict);
            resConflict.StatusCode.Should().Be(HttpStatusCode.Conflict);
            var errJson = await resConflict.Content.ReadFromJsonAsync<JsonElement>();
            errJson.GetProperty("code").GetString().Should().Be("idempotency_key_reuse");
        }
    }
}
