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
using StoreOS.Application.Sales.GetSaleById;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class GetSaleByIdTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    // Tenant A
    private readonly Guid _tenantAId = Guid.NewGuid();
    private readonly Guid _branchA1Id = Guid.NewGuid();
    private readonly Guid _branchA2Id = Guid.NewGuid();
    private readonly Guid _cashierAUserId = Guid.NewGuid();
    private readonly Guid _cashierARoleId = Guid.NewGuid();
    private string _cashierAToken = string.Empty;

    // Tenant B (for cross-tenant anti-enumeration test)
    private readonly Guid _tenantBId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private Guid _saleTenantBId;

    // Sale in Branch A2 (for cross-branch anti-enumeration test)
    private Guid _saleBranchA2Id;

    private Guid _prod1Id;
    private Guid _prod2Id;

    public GetSaleByIdTests(PostgresTestFixture fixture)
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

        // 1. Setup Tenant A
        var tenantA = Tenant.Create("Tenant A", subdomain: "ta-" + _tenantAId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenantA, _tenantAId);
        db.Tenants.Add(tenantA);

        var branchA1 = Branch.Create(_tenantAId, "BA1", "Branch A1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA1, _branchA1Id);
        db.Branches.Add(branchA1);

        var branchA2 = Branch.Create(_tenantAId, "BA2", "Branch A2");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA2, _branchA2Id);
        db.Branches.Add(branchA2);

        var roleA = Role.Create(_tenantAId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(roleA, _cashierARoleId);
        db.Roles.Add(roleA);
        db.RolePermissions.Add(RolePermission.Create(_cashierARoleId, "pos:checkout"));

        var userA = User.Create(_tenantAId, $"cashier-a@{_tenantAId:N}.test", passwordHash, "Cashier A");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userA, _cashierAUserId);
        db.Users.Add(userA);

        // User A only has access to Branch A1 (allBranches: false)
        db.UserBranches.Add(UserBranch.Create(_cashierAUserId, _branchA1Id, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_cashierAUserId, _cashierARoleId, _branchA1Id));

        // Products for Tenant A
        var prod1 = Product.Create(_tenantAId, "USB Cable", ProductCategory.Accessory, 500m, 200m, 18m, "8544");
        db.Products.Add(prod1);
        _prod1Id = prod1.Id;

        var prod2 = Product.Create(_tenantAId, "Power Adapter", ProductCategory.Accessory, 1000m, 500m, 18m, "8504");
        db.Products.Add(prod2);
        _prod2Id = prod2.Id;

        // Stock in Branch A1
        var inv1 = BranchInventory.Create(_tenantAId, _branchA1Id, _prod1Id);
        inv1.IncreaseStock(20, 200m);
        db.BranchInventories.Add(inv1);

        var inv2 = BranchInventory.Create(_tenantAId, _branchA1Id, _prod2Id);
        inv2.IncreaseStock(20, 500m);
        db.BranchInventories.Add(inv2);

        // A Sale seeded in Branch A2 (inaccessible to User A)
        var saleA2 = Sale.CreateTaxInvoice(
            _tenantAId, _branchA2Id, "INV-A2-001", DateTimeOffset.UtcNow,
            null, "Branch 2 Customer", null,
            1000m, 90m, 90m, 1180m, 0m, 1180m,
            PaymentMode.Cash, null, null, null, null, null, "Branch A2 Sale", null, _cashierAUserId);
        saleA2.AddItem(SaleItem.Create(_tenantAId, saleA2.Id, _prod1Id, "USB Cable", "Accessory", 2, 500m, 200m, "8544", false, false, 1000m, 90m, 90m, null));
        db.Sales.Add(saleA2);
        _saleBranchA2Id = saleA2.Id;

        // 2. Setup Tenant B
        var tenantB = Tenant.Create("Tenant B", subdomain: "tb-" + _tenantBId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenantB, _tenantBId);
        db.Tenants.Add(tenantB);

        var branchB = Branch.Create(_tenantBId, "BB1", "Branch B1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        var saleB = Sale.CreateTaxInvoice(
            _tenantBId, _branchBId, "INV-B-001", DateTimeOffset.UtcNow,
            null, "Tenant B Customer", null,
            500m, 45m, 45m, 590m, 0m, 590m,
            PaymentMode.Cash, null, null, null, null, null, "Tenant B Sale", null, Guid.NewGuid());
        saleB.AddItem(SaleItem.Create(_tenantBId, saleB.Id, null, "Other Item", "Other", 1, 500m, 250m, "9999", false, false, 500m, 45m, 45m, null));
        db.Sales.Add(saleB);
        _saleTenantBId = saleB.Id;

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        // Issue token for Cashier A (scoped strictly to Branch A1, allBranches = false)
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _cashierAUserId, _tenantAId, allBranches: false, new[] { _branchA1Id }, new[] { "pos:checkout" }, tokenVersion: 1);
        _cashierAToken = token;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM customer_payments WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM customer_ledger_entries WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM sale_items WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM sales WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM idempotency_keys WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM inventory_movements WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM branch_inventory WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM products WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM user_role_assignments WHERE user_id = @uA; " +
            "DELETE FROM user_branches WHERE user_id = @uA; " +
            "DELETE FROM users WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM role_permissions WHERE role_id = @rA; " +
            "DELETE FROM roles WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM invoice_sequences WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM branches WHERE tenant_id IN (@tA, @tB); " +
            "DELETE FROM tenants WHERE id IN (@tA, @tB);", conn);
        cmd.Parameters.AddWithValue("tA", _tenantAId);
        cmd.Parameters.AddWithValue("tB", _tenantBId);
        cmd.Parameters.AddWithValue("uA", _cashierAUserId);
        cmd.Parameters.AddWithValue("rA", _cashierARoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private HttpRequestMessage CreateGetRequest(Guid saleId)
    {
        var req = new HttpRequestMessage(HttpMethod.Get, $"/api/sales/{saleId}");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierAToken);
        req.Headers.Add("X-Branch-Id", _branchA1Id.ToString());
        return req;
    }

    [Fact]
    public async Task GetSaleById_FullCheckoutSale_ReturnsAllFieldsPopulated()
    {
        // 1. Perform checkout
        var checkoutCommand = new CheckoutCommand(
            Lines: new List<CheckoutCartLine>
            {
                new(_prod1Id, 2, null, false, false, null),
                new(_prod2Id, 1, null, false, false, null)
            },
            CustomerId: null,
            WalkInCustomerName: "Dr. Gregory House",
            InvoiceDiscount: 100m,
            InvoiceType: InvoiceType.TaxInvoice,
            Payments: new List<CheckoutPayment>
            {
                new(PaymentMode.Cash, 1000m),
                new(PaymentMode.UPI, 900m)
            },
            Finance: null,
            Description: "Walk-in accessory bundle"
        );

        var checkoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/pos/checkout")
        {
            Content = JsonContent.Create(checkoutCommand)
        };
        checkoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierAToken);
        checkoutReq.Headers.Add("X-Branch-Id", _branchA1Id.ToString());
        checkoutReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var checkoutRes = await _client.SendAsync(checkoutReq);
        checkoutRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var checkoutJson = await checkoutRes.Content.ReadFromJsonAsync<JsonElement>();
        var saleId = checkoutJson.GetProperty("saleId").GetGuid();

        // 2. Fetch sale detail via GET /api/sales/{id}
        using var getReq = CreateGetRequest(saleId);
        var getRes = await _client.SendAsync(getReq);
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await getRes.Content.ReadFromJsonAsync<SaleDetailDto>();
        dto.Should().NotBeNull();
        dto!.Id.Should().Be(saleId);
        dto.InvoiceNumber.Should().StartWith("BA1");
        dto.InvoiceNumber.Should().Contain("26-27");
        dto.InvoiceType.Should().Be(InvoiceType.TaxInvoice);
        dto.CustomerName.Should().Be("Dr. Gregory House");
        dto.Discount.Should().Be(100m);
        dto.AmountPaid.Should().Be(1900m);
        dto.GrandTotal.Should().Be(1900m);
        dto.Balance.Should().Be(0m);
        dto.Status.Should().Be(SaleStatus.Active);
        dto.Description.Should().Be("Walk-in accessory bundle");

        // Items verification
        dto.Items.Should().HaveCount(2);
        dto.Items.Should().Contain(i => i.ItemName == "USB Cable" && i.Qty == 2);
        dto.Items.Should().Contain(i => i.ItemName == "Power Adapter" && i.Qty == 1);

        // Payments verification
        dto.Payments.Should().HaveCount(2);
        dto.Payments.Should().Contain(p => p.PaymentMode == PaymentMode.Cash && p.Amount == 1000m);
        dto.Payments.Should().Contain(p => p.PaymentMode == PaymentMode.UPI && p.Amount == 900m);
    }

    [Fact]
    public async Task GetSaleById_NonexistentId_Returns404SaleNotFound()
    {
        var nonexistentId = Guid.NewGuid();
        using var req = CreateGetRequest(nonexistentId);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("sale_not_found");
    }

    [Fact]
    public async Task GetSaleById_DifferentTenantSale_Returns404AntiEnumeration()
    {
        // Must return 404, NEVER 403, preventing cross-tenant existence probing
        using var req = CreateGetRequest(_saleTenantBId);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("sale_not_found");
    }

    [Fact]
    public async Task GetSaleById_InaccessibleBranchSale_Returns404AntiEnumeration()
    {
        // Cashier A has access only to Branch A1; Branch A2 sale must return 404
        using var req = CreateGetRequest(_saleBranchA2Id);
        var res = await _client.SendAsync(req);

        res.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var problem = await res.Content.ReadFromJsonAsync<JsonElement>();
        problem.GetProperty("code").GetString().Should().Be("sale_not_found");
    }

    [Fact]
    public async Task GetSaleById_ConvertedEstimate_ShowsBidirectionalLinks()
    {
        // 1. Create estimate
        var estimateBody = new
        {
            lines = new[]
            {
                new { productId = _prod1Id, qty = 1, imei = (string?)null, useMarginScheme = false, isGift = false, overridePricePerUnit = (decimal?)null }
            },
            customerId = (Guid?)null,
            walkInCustomerName = "Estimate Buyer",
            invoiceDiscount = 0m,
            payments = Array.Empty<object>(),
            description = "Estimate for conversion test"
        };

        var estimateReq = new HttpRequestMessage(HttpMethod.Post, "/api/pos/estimate")
        {
            Content = JsonContent.Create(estimateBody)
        };
        estimateReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierAToken);
        estimateReq.Headers.Add("X-Branch-Id", _branchA1Id.ToString());

        var estimateRes = await _client.SendAsync(estimateReq);
        estimateRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var estimateJson = await estimateRes.Content.ReadFromJsonAsync<JsonElement>();
        var estimateId = estimateJson.GetProperty("saleId").GetGuid();

        // 2. Convert estimate
        var convertBody = new
        {
            payments = new[]
            {
                new { paymentMode = PaymentMode.Cash, amount = 500m }
            }
        };

        var convertReq = new HttpRequestMessage(HttpMethod.Post, $"/api/pos/estimate/{estimateId}/convert")
        {
            Content = JsonContent.Create(convertBody)
        };
        convertReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _cashierAToken);
        convertReq.Headers.Add("X-Branch-Id", _branchA1Id.ToString());
        convertReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

        var convertRes = await _client.SendAsync(convertReq);
        convertRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var convertJson = await convertRes.Content.ReadFromJsonAsync<JsonElement>();
        var newSaleId = convertJson.GetProperty("saleId").GetGuid();

        // 3. Fetch tax invoice: ConvertedFromEstimateId must match estimateId
        using var getInvoiceReq = CreateGetRequest(newSaleId);
        var invoiceRes = await _client.SendAsync(getInvoiceReq);
        invoiceRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var invoiceDto = await invoiceRes.Content.ReadFromJsonAsync<SaleDetailDto>();
        invoiceDto.Should().NotBeNull();
        invoiceDto!.ConvertedFromEstimateId.Should().Be(estimateId);
        invoiceDto.LinkedTaxInvoiceId.Should().BeNull();

        // 4. Fetch estimate: LinkedTaxInvoiceId must match newSaleId
        using var getEstimateReq = CreateGetRequest(estimateId);
        var estimateDetailRes = await _client.SendAsync(getEstimateReq);
        estimateDetailRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var estimateDto = await estimateDetailRes.Content.ReadFromJsonAsync<SaleDetailDto>();
        estimateDto.Should().NotBeNull();
        estimateDto!.LinkedTaxInvoiceId.Should().Be(newSaleId);
        estimateDto.ConvertedFromEstimateId.Should().BeNull();
    }
}
