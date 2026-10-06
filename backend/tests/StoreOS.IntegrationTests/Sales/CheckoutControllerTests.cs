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
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class CheckoutControllerTests : IAsyncLifetime
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

    public CheckoutControllerTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Checkout Ctrl Tenant", subdomain: "cct-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "CC1", "Checkout Ctrl Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Role with pos:checkout
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        var cashierPerm = RolePermission.Create(_cashierRoleId, "pos:checkout");
        db.RolePermissions.Add(cashierPerm);

        var cashierUser = User.Create(_tenantId, "cashier@ctrltest.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        var cashierUb = UserBranch.Create(_cashierUserId, _branchId, isDefault: true);
        db.UserBranches.Add(cashierUb);

        var ura = UserRoleAssignment.Create(_cashierUserId, _cashierRoleId, _branchId);
        db.UserRoleAssignments.Add(ura);

        // Products with stock
        var p1 = Product.Create(_tenantId, "Test Product 1", ProductCategory.Accessory, 500m, 200m, 18m, "39269099");
        db.Products.Add(p1);
        _prod1Id = p1.Id;

        var inv1 = BranchInventory.Create(_tenantId, _branchId, _prod1Id);
        inv1.IncreaseStock(10, 200m);
        db.BranchInventories.Add(inv1);

        var p2 = Product.Create(_tenantId, "Test Product 2", ProductCategory.Accessory, 1000m, 400m, 18m, "39269099");
        db.Products.Add(p2);
        _prod2Id = p2.Id;

        var inv2 = BranchInventory.Create(_tenantId, _branchId, _prod2Id);
        inv2.IncreaseStock(10, 400m);
        db.BranchInventories.Add(inv2);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        // Issue cashier token
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
            DELETE FROM inventory_imeis WHERE tenant_id = @t;
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

    private HttpRequestMessage CreateRequest(CheckoutCommand command, string? idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(command)
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
    public async Task Post_WithoutIdempotencyKeyHeader_Returns400IdempotencyKeyRequired()
    {
        var command = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_prod1Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "No Key Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 500m) },
            Finance: null,
            Description: "No key test"
        );

        using var req = CreateRequest(command, idempotencyKey: null);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("code").GetString().Should().Be("idempotency_key_required");
        body.GetProperty("message").GetString().Should().Be("POST /pos/checkout requires an Idempotency-Key header.");
    }

    [Fact]
    public async Task Post_WithValidKeyAndWellFormedCart_Returns201WithLocationAndMatchingJsonShape()
    {
        var key = Guid.NewGuid().ToString();
        var command = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_prod1Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Valid Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 500m) },
            Finance: null,
            Description: "Valid checkout test"
        );

        using var req = CreateRequest(command, key);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        res.Headers.Location.Should().NotBeNull();

        var body = await res.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = body.GetProperty("saleId").GetGuid();
        res.Headers.Location!.ToString().Should().Be($"/api/sales/{saleId}");

        body.GetProperty("invoiceNumber").GetString().Should().NotBeNullOrWhiteSpace();
        body.GetProperty("grandTotal").GetDecimal().Should().Be(500m);
        body.GetProperty("amountPaid").GetDecimal().Should().Be(500m);
        body.GetProperty("balance").GetDecimal().Should().Be(0m);
        body.TryGetProperty("saleDate", out _).Should().BeTrue();
    }

    [Fact]
    public async Task RepeatingSameRequest_WithSameIdempotencyKey_ReplaysCached201WithoutCreatingSecondSale()
    {
        var key = Guid.NewGuid().ToString();
        var command = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_prod1Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Replay Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 500m) },
            Finance: null,
            Description: "Replay test"
        );

        using var req1 = CreateRequest(command, key);
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);
        var body1 = await res1.Content.ReadFromJsonAsync<JsonElement>();
        var inv1 = body1.GetProperty("invoiceNumber").GetString();

        using var req2 = CreateRequest(command, key);
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Created);
        res2.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
        res2.Headers.GetValues("Idempotent-Replayed").First().Should().Be("true");

        var body2 = await res2.Content.ReadFromJsonAsync<JsonElement>();
        var inv2 = body2.GetProperty("invoiceNumber").GetString();
        inv2.Should().Be(inv1);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var salesCount = await db.Sales.IgnoreQueryFilters().CountAsync(s => s.InvoiceNumber == inv1);
        salesCount.Should().Be(1);
    }

    [Fact]
    public async Task RepeatingWithSameKey_ButDifferentCartPayload_Returns409Conflict()
    {
        var key = Guid.NewGuid().ToString();
        var command1 = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_prod1Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Initial Payload Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 500m) },
            Finance: null,
            Description: "Payload 1"
        );

        var command2 = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_prod2Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Conflicting Payload Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 1000m) },
            Finance: null,
            Description: "Payload 2"
        );

        using var req1 = CreateRequest(command1, key);
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        using var req2 = CreateRequest(command2, key);
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorBody = await res2.Content.ReadFromJsonAsync<JsonElement>();
        errorBody.GetProperty("code").GetString().Should().Be("idempotency_key_reuse");
    }
}
