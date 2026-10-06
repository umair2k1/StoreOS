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
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class ConvertEstimateTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _cashierUserId = Guid.NewGuid();
    private readonly Guid _cashierRoleId = Guid.NewGuid();

    private Guid _phoneProductId;
    private Guid _accessoryProductId;
    private Guid _customerId;
    private const string PhoneImei = "860777777777777";
    private string _cashierToken = string.Empty;

    public ConvertEstimateTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Convert Est Tenant", subdomain: "cet-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "CE1", "Convert Est Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Role with pos:checkout
        var cashierRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(cashierRole, _cashierRoleId);
        db.Roles.Add(cashierRole);

        db.RolePermissions.Add(RolePermission.Create(_cashierRoleId, "pos:checkout"));

        var cashierUser = User.Create(_tenantId, "cashier@cet.com", passwordHash, "Cashier User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(cashierUser, _cashierUserId);
        db.Users.Add(cashierUser);

        db.UserBranches.Add(UserBranch.Create(_cashierUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_cashierUserId, _cashierRoleId, _branchId));

        // Phone product with 1 InStock unit and 1 IMEI
        var phone = Product.Create(_tenantId, "Convertible Phone", ProductCategory.NewPhone, 60000m, 45000m, 18m, "85171300");
        db.Products.Add(phone);
        _phoneProductId = phone.Id;

        var phoneInv = BranchInventory.Create(_tenantId, _branchId, _phoneProductId);
        phoneInv.IncreaseStock(1, 45000m);
        db.BranchInventories.Add(phoneInv);

        var imei = InventoryImei.Create(
            _tenantId, _branchId, _phoneProductId, null,
            PhoneImei, DateOnly.FromDateTime(DateTime.UtcNow), 45000m);
        db.InventoryImeis.Add(imei);

        // Accessory with 5 InStock units
        var acc = Product.Create(_tenantId, "Phone Case", ProductCategory.Accessory, 1000m, 400m, 18m, "39269099");
        db.Products.Add(acc);
        _accessoryProductId = acc.Id;

        var accInv = BranchInventory.Create(_tenantId, _branchId, _accessoryProductId);
        accInv.IncreaseStock(5, 400m);
        db.BranchInventories.Add(accInv);

        // Customer with credit limit 100000
        var customer = Customer.Create(_tenantId, "Convert Customer", "9222233333", null);
        _customerId = customer.Id;
        customer.SetCreditLimit(100000m);
        db.Customers.Add(customer);

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
            DELETE FROM customers WHERE tenant_id = @t;
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

    private HttpRequestMessage CreateConvertRequest(Guid estimateId, object? body, string? idempotencyKey)
    {
        var req = new HttpRequestMessage(HttpMethod.Post, $"/api/pos/estimate/{estimateId}/convert")
        {
            Content = JsonContent.Create(body ?? new { payments = Array.Empty<object>() })
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());
        if (idempotencyKey != null)
        {
            req.Headers.Add("Idempotency-Key", idempotencyKey);
        }
        return req;
    }

    private async Task<Guid> CreateEstimateAsync(params CheckoutCartLine[] lines)
    {
        var body = new
        {
            lines = lines.Select(l => new
            {
                productId = l.ProductId,
                qty = l.Qty,
                imei = l.Imei,
                useMarginScheme = l.UseMarginScheme,
                isGift = l.IsGift,
                overridePricePerUnit = l.OverridePricePerUnit
            }).ToArray(),
            customerId = (Guid?)_customerId,
            walkInCustomerName = (string?)null,
            invoiceDiscount = 0m,
            payments = Array.Empty<object>(),
            description = "Test estimate to convert"
        };

        var req = new HttpRequestMessage(HttpMethod.Post, "/api/pos/estimate")
        {
            Content = JsonContent.Create(body)
        };
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        req.Headers.Add("X-Branch-Id", _branchId.ToString());

        var res = await _client.SendAsync(req);
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("saleId").GetGuid();
    }

    [Fact]
    public async Task HappyPath_ConvertEstimate_CreatesTaxInvoice_DecrementsStockAndImei_SetsBidirectionalLinks()
    {
        var estimateId = await CreateEstimateAsync(
            new CheckoutCartLine(_accessoryProductId, 2, null, false, false, null),
            new CheckoutCartLine(_phoneProductId, 1, PhoneImei, false, false, null));

        var key = Guid.NewGuid().ToString();
        var convertBody = new
        {
            payments = new[]
            {
                new { paymentMode = PaymentMode.Cash, amount = 62000m }
            }
        };

        using var req = CreateConvertRequest(estimateId, convertBody, key);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        var newSaleId = json.GetProperty("saleId").GetGuid();
        var invoiceNumber = json.GetProperty("invoiceNumber").GetString();

        invoiceNumber.Should().NotContain("/E/");
        json.GetProperty("grandTotal").GetDecimal().Should().Be(62000m);
        json.GetProperty("amountPaid").GetDecimal().Should().Be(62000m);

        // Verify Database
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var originalEstimate = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == estimateId);
        var newInvoice = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == newSaleId);

        // Bidirectional links
        originalEstimate.LinkedTaxInvoiceId.Should().Be(newSaleId);
        newInvoice.ConvertedFromEstimateId.Should().Be(estimateId);
        newInvoice.InvoiceType.Should().Be(InvoiceType.TaxInvoice);

        // Stock correctly decremented NOW
        var accStock = await db.BranchInventories.IgnoreQueryFilters()
            .FirstAsync(bi => bi.ProductId == _accessoryProductId && bi.BranchId == _branchId);
        accStock.StockQty.Should().Be(3); // 5 - 2 = 3

        var phoneStock = await db.BranchInventories.IgnoreQueryFilters()
            .FirstAsync(bi => bi.ProductId == _phoneProductId && bi.BranchId == _branchId);
        phoneStock.StockQty.Should().Be(0); // 1 - 1 = 0

        // IMEI marked Sold NOW
        var imeiRow = await db.InventoryImeis.IgnoreQueryFilters().FirstAsync(i => i.Imei == PhoneImei);
        imeiRow.Status.Should().Be(ImeiStatus.Sold);
        imeiRow.SaleId.Should().Be(newSaleId);
        imeiRow.SaleItemId.Should().NotBeNull();
        imeiRow.SaleItemId.Should().NotBe(Guid.Empty);
    }

    [Fact]
    public async Task ConvertingSameEstimateTwice_ThrowsEstimateAlreadyConverted()
    {
        var estimateId = await CreateEstimateAsync(
            new CheckoutCartLine(_accessoryProductId, 1, null, false, false, null));

        var key1 = Guid.NewGuid().ToString();
        var key2 = Guid.NewGuid().ToString();

        // 1st conversion succeeds
        using (var req1 = CreateConvertRequest(estimateId, null, key1))
        {
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 2nd conversion with different key fails with estimate_already_converted (422)
        using (var req2 = CreateConvertRequest(estimateId, null, key2))
        {
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
            var err = await res2.Content.ReadFromJsonAsync<JsonElement>();
            err.GetProperty("code").GetString().Should().Be("estimate_already_converted");
        }
    }

    [Fact]
    public async Task StockSoldOutBeforeConversion_ThrowsInsufficientStock_AndEstimateRemainsUnconverted()
    {
        var estimateId = await CreateEstimateAsync(
            new CheckoutCartLine(_accessoryProductId, 5, null, false, false, null)); // wants all 5

        // Deplete stock in another checkout
        var checkoutBody = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_accessoryProductId, 5, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Quick Buyer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 5000m) },
            Finance: null,
            Description: "Deplete stock"
        );
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        checkoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        checkoutReq.Headers.Add("X-Branch-Id", _branchId.ToString());
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var checkoutRes = await _client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Now attempt to convert estimate -> must fail with insufficient_stock
        var convertKey = Guid.NewGuid().ToString();
        using var req = CreateConvertRequest(estimateId, null, convertKey);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var err = await res.Content.ReadFromJsonAsync<JsonElement>();
        err.GetProperty("code").GetString().Should().Be("insufficient_stock");

        // Estimate remains unconverted
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var est = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == estimateId);
        est.LinkedTaxInvoiceId.Should().BeNull();
    }

    [Fact]
    public async Task ImeiSoldBeforeConversion_FailsCleanly()
    {
        var estimateId = await CreateEstimateAsync(
            new CheckoutCartLine(_phoneProductId, 1, PhoneImei, false, false, null));

        // Sell IMEI in another checkout
        var checkoutBody = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_phoneProductId, 1, PhoneImei, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "IMEI Grabber",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 60000m) },
            Finance: null,
            Description: "Sell IMEI"
        );
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        checkoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        checkoutReq.Headers.Add("X-Branch-Id", _branchId.ToString());
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var checkoutRes = await _client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // Attempt to convert estimate referencing now-sold IMEI -> fails cleanly
        var convertKey = Guid.NewGuid().ToString();
        using var req = CreateConvertRequest(estimateId, null, convertKey);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var err = await res.Content.ReadFromJsonAsync<JsonElement>();
        err.GetProperty("code").GetString().Should().BeOneOf("imei_not_available", "insufficient_stock");
    }

    [Fact]
    public async Task Idempotency_SameKeyReplaysWithoutDoubleConverting()
    {
        var estimateId = await CreateEstimateAsync(
            new CheckoutCartLine(_accessoryProductId, 1, null, false, false, null));

        var key = Guid.NewGuid().ToString();

        // 1st conversion with key -> 201 Created
        using (var req1 = CreateConvertRequest(estimateId, null, key))
        {
            var res1 = await _client.SendAsync(req1);
            res1.StatusCode.Should().Be(HttpStatusCode.Created);
        }

        // 2nd conversion with exact same key -> 201 Created with Idempotent-Replayed: true
        using (var req2 = CreateConvertRequest(estimateId, null, key))
        {
            var res2 = await _client.SendAsync(req2);
            res2.StatusCode.Should().Be(HttpStatusCode.Created);
            res2.Headers.Contains("Idempotent-Replayed").Should().BeTrue();
            res2.Headers.GetValues("Idempotent-Replayed").First().Should().Be("true");
        }
    }

    [Fact]
    public async Task AttemptingToConvertRegularTaxInvoice_ThrowsNotAnEstimate()
    {
        // Create regular tax invoice
        var checkoutBody = new CheckoutCommand(
            Lines: new List<CheckoutCartLine> { new(_accessoryProductId, 1, null, false, false, null) },
            CustomerId: null,
            WalkInCustomerName: "Regular Tax Customer",
            InvoiceDiscount: 0m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 1000m) },
            Finance: null,
            Description: "Tax invoice test"
        );
        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(checkoutBody)
        };
        checkoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierToken);
        checkoutReq.Headers.Add("X-Branch-Id", _branchId.ToString());
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var checkoutRes = await _client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await checkoutRes.Content.ReadFromJsonAsync<JsonElement>();
        var taxInvoiceId = json.GetProperty("saleId").GetGuid();

        // Attempt to convert this tax invoice ID
        using var req = CreateConvertRequest(taxInvoiceId, null, Guid.NewGuid().ToString());
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var err = await res.Content.ReadFromJsonAsync<JsonElement>();
        err.GetProperty("code").GetString().Should().Be("not_an_estimate");
    }
}
