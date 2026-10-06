using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Npgsql;
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
public class CheckoutContentionTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    private Guid _lastUnitProductId;
    private string _cashierToken = string.Empty;

    public CheckoutContentionTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Contention Tenant", subdomain: "cnt-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "CT1", "Contention Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Role with pos:checkout
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        var cashierPerm = RolePermission.Create(_cashierRoleId, "pos:checkout");
        db.RolePermissions.Add(cashierPerm);

        var cashierUser = User.Create(_tenantId, "cashier@contention.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        var cashierUb = UserBranch.Create(_cashierUserId, _branchId, isDefault: true);
        db.UserBranches.Add(cashierUb);

        var ura = UserRoleAssignment.Create(_cashierUserId, _cashierRoleId, _branchId);
        db.UserRoleAssignments.Add(ura);

        // Product with stock = 1 (last unit)
        var product = Product.Create(_tenantId, "Limited Edition Phone", ProductCategory.NewPhone, 50000m, 40000m, 18m, "85171300");
        db.Products.Add(product);
        _lastUnitProductId = product.Id;

        var inv = BranchInventory.Create(_tenantId, _branchId, _lastUnitProductId);
        inv.IncreaseStock(1, 40000m);
        db.BranchInventories.Add(inv);

        // 20 products for sequence contention test
        for (var i = 1; i <= 20; i++)
        {
            var p = Product.Create(_tenantId, $"Bulk Item {i}", ProductCategory.Accessory, 100m * i, 50m, 18m, "39269099");
            db.Products.Add(p);

            var bi = BranchInventory.Create(_tenantId, _branchId, p.Id);
            bi.IncreaseStock(10, 50m);
            db.BranchInventories.Add(bi);
        }

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        // Issue JWT token directly with pos:checkout permission
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<StoreOS.Application.Abstractions.IJwtTokenService>();
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

    private HttpRequestMessage CreateCheckoutRequest(string idempotencyKey, CheckoutCommand command)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(command)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        req.Headers.Add("Idempotency-Key", idempotencyKey);
        return req;
    }

    [Fact]
    public async Task TwoParallelCheckouts_OfLastUnitOfProduct_ExactlyOne201AndOne422()
    {
        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        var cmd1 = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_lastUnitProductId, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Customer 1",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 50000m) },
            Finance: null,
            Description: "Checkout 1"
        )
        { IdempotencyKey = key1 };

        var cmd2 = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_lastUnitProductId, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Customer 2",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 50000m) },
            Finance: null,
            Description: "Checkout 2"
        )
        { IdempotencyKey = key2 };

        var req1 = CreateCheckoutRequest(key1, cmd1);
        var req2 = CreateCheckoutRequest(key2, cmd2);

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);
        var statuses = responses.Select(r => r.StatusCode).ToList();

        statuses.Should().Contain(HttpStatusCode.Created);
        statuses.Should().Contain(HttpStatusCode.UnprocessableEntity);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var inv = await db.BranchInventories.IgnoreQueryFilters().FirstAsync(bi => bi.ProductId == _lastUnitProductId && bi.BranchId == _branchId);
        inv.StockQty.Should().Be(0);
    }

    [Fact]
    public async Task TwentyParallelCheckouts_OfDifferentProducts_All201WithUniqueConsecutiveInvoices()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var products = await db.Products.IgnoreQueryFilters()
            .Where(p => p.TenantId == _tenantId && p.Name.StartsWith("Bulk Item "))
            .ToListAsync();

        products.Should().HaveCount(20);

        var invoiceNumbers = new ConcurrentBag<string>();

        var tasks = products.Select(async p =>
        {
            var key = Guid.NewGuid().ToString();
            var cmd = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(p.Id, 1, null, false, false, null) },
                CustomerId: null,
                WalkInCustomerName: "Parallel Buyer",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, p.SellPrice) },
                Finance: null,
                Description: "Bulk Parallel"
            )
            { IdempotencyKey = key };

            HttpResponseMessage res;
            while (true)
            {
                using var req = CreateCheckoutRequest(key, cmd);
                res = await _client.SendAsync(req);
                if (res.StatusCode != HttpStatusCode.Conflict) break;
                await Task.Delay(50);
            }

            var content = await res.Content.ReadAsStringAsync();
            res.StatusCode.Should().Be(HttpStatusCode.Created, because: content);

            var body = await res.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
            var invNum = body.GetProperty("invoiceNumber").GetString()!;
            invoiceNumbers.Add(invNum);
        });

        await Task.WhenAll(tasks);

        invoiceNumbers.Should().HaveCount(20);
        invoiceNumbers.Distinct().Should().HaveCount(20);
    }

    [Fact]
    public async Task SameIdempotencyKey_SentTwiceInParallel_ExactlyOneSaleRowAndSameInvoiceNumber()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var product = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.TenantId == _tenantId && p.Name == "Bulk Item 1");

        var sharedKey = Guid.NewGuid().ToString();
        var cmd = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(product.Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Parallel Same Key",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, product.SellPrice) },
            Finance: null,
            Description: "Same Key Test"
        )
        { IdempotencyKey = sharedKey };

        using var req1 = CreateCheckoutRequest(sharedKey, cmd);
        using var req2 = CreateCheckoutRequest(sharedKey, cmd);

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);
        responses[0].StatusCode.Should().Be(HttpStatusCode.Created);
        responses[1].StatusCode.Should().Be(HttpStatusCode.Created);

        var body1 = await responses[0].Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        var body2 = await responses[1].Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();

        var inv1 = body1.GetProperty("invoiceNumber").GetString();
        var inv2 = body2.GetProperty("invoiceNumber").GetString();
        inv1.Should().Be(inv2);

        var salesCount = await db.Sales.IgnoreQueryFilters().CountAsync(s => s.InvoiceNumber == inv1);
        salesCount.Should().Be(1);
    }

    [Fact]
    public async Task SameKey_WithDifferentCart_Returns409IdempotencyKeyReuse()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var p1 = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.TenantId == _tenantId && p.Name == "Bulk Item 1");
        var p2 = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.TenantId == _tenantId && p.Name == "Bulk Item 2");

        var key = Guid.NewGuid().ToString();
        var cmd1 = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(p1.Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Buyer 1",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, p1.SellPrice) },
            Finance: null,
            Description: "First Request"
        )
        { IdempotencyKey = key };

        var cmd2 = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(p2.Id, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Buyer 2",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, p2.SellPrice) },
            Finance: null,
            Description: "Different Payload Request"
        )
        { IdempotencyKey = key };

        using var req1 = CreateCheckoutRequest(key, cmd1);
        var res1 = await _client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.Created);

        using var req2 = CreateCheckoutRequest(key, cmd2);
        var res2 = await _client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.Conflict);

        var errorBody = await res2.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
        errorBody.GetProperty("code").GetString().Should().Be("idempotency_key_reuse");
    }
}
