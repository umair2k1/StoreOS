using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Reporting.DailySales;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Reporting;

[Collection("postgres")]
public class DailySalesReportTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private Guid _prodNewPhoneId;
    private Guid _prodUsedPhoneId;
    private Guid _prodAccessoryId;
    private Guid _prodServiceId;

    private DateOnly _reportDate;

    public DailySalesReportTests(PostgresTestFixture fixture)
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

        // 1. Tenant & Branch & User & Role
        var tenant = Tenant.Create("Daily Sales Tenant", timezone: "Asia/Kolkata", subdomain: "dsr-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "BR1", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var user = User.Create(_tenantId, $"daily-sales-{_userId:N}@storeos.test", passwordHash, "Daily Sales User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var role = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var userRole = UserRoleAssignment.Create(_userId, _roleId, _branchId);
        db.UserRoleAssignments.Add(userRole);

        var userBranch = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(userBranch);

        // 2. Resolve Asia/Kolkata timezone
        TimeZoneInfo tz;
        try
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("Asia/Kolkata");
        }
        catch (TimeZoneNotFoundException)
        {
            tz = TimeZoneInfo.FindSystemTimeZoneById("India Standard Time");
        }

        var localNow = TimeZoneInfo.ConvertTimeFromUtc(DateTimeOffset.UtcNow.UtcDateTime, tz);
        _reportDate = DateOnly.FromDateTime(localNow);

        DateTimeOffset ToUtc(DateOnly d, int hour, int minute)
        {
            var dt = d.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
            var offset = tz.GetUtcOffset(dt);
            return new DateTimeOffset(dt, offset).ToUniversalTime();
        }

        // 3. Catalog Products & Branch Inventory
        // New Phone: Stock 5, Threshold 2, Cost 100k, Sell 120k
        var prodNew = Product.Create(_tenantId, "iPhone 15 Pro", ProductCategory.NewPhone, 120000m, 100000m, 18m, "8517");
        _prodNewPhoneId = prodNew.Id;
        db.Products.Add(prodNew);
        var invNew = BranchInventory.Create(_tenantId, _branchId, prodNew.Id);
        invNew.IncreaseStock(5, 100000m);
        invNew.SetLowStockThreshold(2);
        db.BranchInventories.Add(invNew);

        // Used Phone: Stock 1, Threshold 2 -> LOW STOCK (1 > 0 && 1 <= 2), Cost 18k, Sell 25k
        var prodUsed = Product.Create(_tenantId, "Used Galaxy S21", ProductCategory.UsedPhone, 25000m, 18000m, 18m, "8517");
        _prodUsedPhoneId = prodUsed.Id;
        db.Products.Add(prodUsed);
        var invUsed = BranchInventory.Create(_tenantId, _branchId, prodUsed.Id);
        invUsed.IncreaseStock(1, 18000m);
        invUsed.SetLowStockThreshold(2);
        db.BranchInventories.Add(invUsed);

        // Accessory: Stock 0, Threshold 5 -> OUT OF STOCK (0 <= 0), Cost 500, Sell 1500
        var prodAccessory = Product.Create(_tenantId, "USB-C Adapter", ProductCategory.Accessory, 1500m, 500m, 18m, "8544");
        _prodAccessoryId = prodAccessory.Id;
        db.Products.Add(prodAccessory);
        var invAcc = BranchInventory.Create(_tenantId, _branchId, prodAccessory.Id);
        invAcc.IncreaseStock(1, 500m);
        invAcc.DecreaseStock(1);
        invAcc.SetLowStockThreshold(5);
        db.BranchInventories.Add(invAcc);

        // Gift Product (Accessory): Cost 50, Sell 300
        var prodGift = Product.Create(_tenantId, "Screen Guard Gift", ProductCategory.Accessory, 300m, 50m, 18m, "8544");
        db.Products.Add(prodGift);

        // Service Product: Repair Service (MUST be excluded from inventory summary)
        var prodService = Product.Create(_tenantId, "Battery Service", ProductCategory.RepairService, 2000m, 0m, 18m, "998713");
        _prodServiceId = prodService.Id;
        db.Products.Add(prodService);

        // 4. Sales & Payments on _reportDate
        // Sale 1: Active TaxInvoice, Cash payment (120,000)
        var sale1 = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSR-001", ToUtc(_reportDate, 10, 0),
            customerId: null, customerName: "John Doe", customerGstin: null,
            totalTaxable: 101694.92m, totalCgst: 9152.54m, totalSgst: 9152.54m, grandTotal: 120000m,
            discount: 0m, amountPaid: 120000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale 1 Full Cash",
            repairJobId: null, createdBy: _userId);
        sale1.AddItem(SaleItem.Create(
            _tenantId, sale1.Id, prodNew.Id, "iPhone 15 Pro", ProductCategory.NewPhone,
            1, 120000m, 95000m, "8517", false, false, 101694.92m, 9152.54m, 9152.54m, "111222333444555"));
        db.Sales.Add(sale1);

        var payment1 = CustomerPayment.Create(
            _tenantId, sale1.Id, 120000m, PaymentMode.Cash, _reportDate, _userId);
        db.CustomerPayments.Add(payment1);

        // Sale 2: Active TaxInvoice, Partial payments (10,000 UPI + 5,000 Card), Gift line (Screen Guard Gift)
        var sale2 = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSR-002", ToUtc(_reportDate, 12, 0),
            customerId: null, customerName: "Jane Smith", customerGstin: null,
            totalTaxable: 21186.44m, totalCgst: 1906.78m, totalSgst: 1906.78m, grandTotal: 25000m,
            discount: 0m, amountPaid: 15000m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale 2 Partial Card/UPI",
            repairJobId: null, createdBy: _userId);
        sale2.AddItem(SaleItem.Create(
            _tenantId, sale2.Id, prodUsed.Id, "Used Galaxy S21", ProductCategory.UsedPhone,
            1, 25000m, 17000m, "8517", false, false, 21186.44m, 1906.78m, 1906.78m, "999888777666555"));
        sale2.AddItem(SaleItem.Create(
            _tenantId, sale2.Id, prodGift.Id, "Screen Guard Gift", ProductCategory.Accessory,
            1, 0m, 50m, "8544", false, true, 0m, 0m, 0m, null));
        db.Sales.Add(sale2);

        var payment2Upi = CustomerPayment.Create(
            _tenantId, sale2.Id, 10000m, PaymentMode.UPI, _reportDate, _userId);
        var payment2Card = CustomerPayment.Create(
            _tenantId, sale2.Id, 5000m, PaymentMode.Card, _reportDate, _userId);
        db.CustomerPayments.Add(payment2Upi);
        db.CustomerPayments.Add(payment2Card);

        // Sale 3: Older Sale (5 days ago). Today customer pays 8,000 via Finance.
        var saleOld = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSR-OLD", ToUtc(_reportDate.AddDays(-5), 10, 0),
            customerId: null, customerName: "Old Cust", customerGstin: null,
            totalTaxable: 101694.92m, totalCgst: 9152.54m, totalSgst: 9152.54m, grandTotal: 120000m,
            discount: 0m, amountPaid: 8000m, paymentMode: PaymentMode.Finance,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Older Sale",
            repairJobId: null, createdBy: _userId);
        saleOld.AddItem(SaleItem.Create(
            _tenantId, saleOld.Id, prodNew.Id, "iPhone 15 Pro", ProductCategory.NewPhone,
            1, 120000m, 95000m, "8517", false, false, 101694.92m, 9152.54m, 9152.54m, null));
        db.Sales.Add(saleOld);

        var paymentOldFinance = CustomerPayment.Create(
            _tenantId, saleOld.Id, 8000m, PaymentMode.Finance, _reportDate, _userId);
        db.CustomerPayments.Add(paymentOldFinance);

        // Sale 4: Voided TaxInvoice created today. Must be excluded.
        var saleVoid = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSR-VOID", ToUtc(_reportDate, 14, 0),
            customerId: null, customerName: "Void Cust", customerGstin: null,
            totalTaxable: 42372.88m, totalCgst: 3813.56m, totalSgst: 3813.56m, grandTotal: 50000m,
            discount: 0m, amountPaid: 50000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Void Sale",
            repairJobId: null, createdBy: _userId);
        saleVoid.AddItem(SaleItem.Create(
            _tenantId, saleVoid.Id, prodNew.Id, "iPhone 15 Pro", ProductCategory.NewPhone,
            1, 50000m, 95000m, "8517", false, false, 42372.88m, 3813.56m, 3813.56m, null));
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(saleVoid, SaleStatus.Voided);
        db.Sales.Add(saleVoid);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM customer_payments WHERE tenant_id = @tId; " +
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("uId", _userId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, branchIds: new[] { _branchId }, Array.Empty<string>(), tokenVersion: 1);
        return token;
    }

    private async Task<DailySalesReportDto> GetReportAsync(DateOnly? date = null)
    {
        var token = IssueToken();
        var targetDate = date ?? _reportDate;
        var url = $"/api/reports/daily-sales?date={targetDate:yyyy-MM-dd}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var response = await _client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP {(int)response.StatusCode}: {err}");
        }

        var dto = await response.Content.ReadFromJsonAsync<DailySalesReportDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    [Fact]
    public async Task DailySalesReport_SalesSummary_ProfitUsesSnapshotCost_ExcludesGiftFromItemsSold()
    {
        // (a) sales summary profit uses snapshot cost: proven by changing the PRODUCT's current purchase price
        // AFTER the sale and confirming the report's profit figure is UNCHANGED (still reflects the snapshot).
        var report = await GetReportAsync();

        // Revenue: Sale 1 (120,000) + Sale 2 (25,000) = 145,000. Voided sale (50,000) excluded.
        report.Sales.Revenue.Should().Be(145000m);

        // Buying cost uses snapshot: 95,000 (iPhone) + 17,000 (Galaxy) = 112,000.
        report.Sales.BuyingCost.Should().Be(112000m);

        // Profit: 145,000 - 112,000 = 33,000.
        report.Sales.Profit.Should().Be(33000m);

        // Gst collected: 18,305.08 + 3,813.56 = 22,118.64
        report.Sales.GstCollected.Should().Be(22118.64m);

        // Invoice count: 2
        report.Sales.InvoiceCount.Should().Be(2);

        // Items sold excludes gift line: 1 (iPhone) + 1 (Galaxy) = 2.
        report.Sales.ItemsSold.Should().Be(2);

        // Update Product purchase price in DB to an extreme number
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var prodNew = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == _prodNewPhoneId);
            typeof(Product).GetProperty(nameof(Product.PurchasePrice))!.SetValue(prodNew, 999999m);
            await db.SaveChangesAsync();
        }

        // Fetch report again; profit must remain exactly 33,000
        var reportAfter = await GetReportAsync();
        reportAfter.Sales.BuyingCost.Should().Be(112000m);
        reportAfter.Sales.Profit.Should().Be(33000m);
    }

    [Fact]
    public async Task DailySalesReport_InventorySummary_ExcludesRepairService()
    {
        // (b) inventory summary excludes RepairService
        var report = await GetReportAsync();

        // iPhone: 5 units @ 100k cost / 120k sell
        // Used Galaxy: 1 unit @ 18k cost / 25k sell (Low Stock)
        // USB-C Adapter: 0 units @ 500 cost / 1500 sell (Out of Stock)
        // Battery Service: Repair Service (excluded)
        report.Inventory.TotalStockVolume.Should().Be(6); // 5 + 1
        report.Inventory.StockWorthAtCost.Should().Be(518000m); // 5*100k + 1*18k
        report.Inventory.RetailValuation.Should().Be(625000m); // 5*120k + 1*25k
        report.Inventory.LowStockCount.Should().Be(1); // Used Galaxy
        report.Inventory.OutOfStockCount.Should().Be(1); // USB-C Adapter
    }

    [Fact]
    public async Task DailySalesReport_PaymentBreakdown_BucketsCardAndCreditIntoOther()
    {
        // (c) payment breakdown correctly buckets Card/Credit into Other
        var report = await GetReportAsync();

        // Cash: Sale 1 (120,000)
        report.Payments.Cash.Should().Be(120000m);

        // UPI: Sale 2 (10,000)
        report.Payments.Upi.Should().Be(10000m);

        // Finance: Sale Old (8,000 payment recorded today)
        report.Payments.Finance.Should().Be(8000m);

        // Other: Card payment on Sale 2 (5,000)
        report.Payments.Other.Should().Be(5000m);

        // Total: 120,000 + 10,000 + 8,000 + 5,000 = 143,000
        report.Payments.Total.Should().Be(143000m);
    }

    [Fact]
    public async Task DailySalesReport_SaleValue_Collected_Outstanding_DualInterpretationVerified()
    {
        // (d) the sale-value vs. collected vs. outstanding distinction is correct per the documented dual interpretation:
        // - Sale value = Today's Active TaxInvoice GrandTotal sum (145,000)
        // - Collected amount = ALL payments recorded on Date (143,000, including 8k on older sale)
        // - Outstanding amount = Today's Revenue (145,000) - Payments collected for TODAY's sales (135,000) = 10,000.
        var report = await GetReportAsync();

        report.Sales.Revenue.Should().Be(145000m);
        report.CollectedAmount.Should().Be(143000m);
        report.OutstandingAmount.Should().Be(10000m);
    }

    [Fact]
    public async Task DailySalesReport_CategoryBreakdown_ZeroFillsCategoriesWithNoSales()
    {
        // (e) category breakdown zero-fills a category with no sales
        var report = await GetReportAsync();

        var newPhone = report.ByCategory.FirstOrDefault(c => c.Category == ProductCategory.NewPhone);
        newPhone.Should().NotBeNull();
        newPhone!.Revenue.Should().Be(120000m);
        newPhone.ItemsSold.Should().Be(1);

        var usedPhone = report.ByCategory.FirstOrDefault(c => c.Category == ProductCategory.UsedPhone);
        usedPhone.Should().NotBeNull();
        usedPhone!.Revenue.Should().Be(25000m);
        usedPhone.ItemsSold.Should().Be(1);

        // Zero-filled categories:
        var accessory = report.ByCategory.FirstOrDefault(c => c.Category == ProductCategory.Accessory);
        accessory.Should().NotBeNull();
        accessory!.Revenue.Should().Be(0m);
        accessory.ItemsSold.Should().Be(0);

        var service = report.ByCategory.FirstOrDefault(c => c.Category == ProductCategory.RepairService);
        service.Should().NotBeNull();
        service!.Revenue.Should().Be(0m);
        service.ItemsSold.Should().Be(0);
    }

    [Fact]
    public async Task DailySalesReport_ItemizedLog_IncludesEveryLineWithImeiAndPaymentMode()
    {
        // (f) itemized log includes every line with correct IMEI/payment-mode detail
        var report = await GetReportAsync();

        report.ItemizedLog.Should().HaveCount(3);

        // Line 1: iPhone
        var iphoneLine = report.ItemizedLog.First(l => l.ItemName == "iPhone 15 Pro");
        iphoneLine.InvoiceNumber.Should().Be("INV-DSR-001");
        iphoneLine.CustomerName.Should().Be("John Doe");
        iphoneLine.Qty.Should().Be(1);
        iphoneLine.SellingValue.Should().Be(120000m);
        iphoneLine.CostValue.Should().Be(95000m);
        iphoneLine.Gst.Should().Be(18305.08m);
        iphoneLine.Imei.Should().Be("111222333444555");
        iphoneLine.PaymentMode.Should().Be(PaymentMode.Cash);

        // Line 2: Used Galaxy
        var galaxyLine = report.ItemizedLog.First(l => l.ItemName == "Used Galaxy S21");
        galaxyLine.InvoiceNumber.Should().Be("INV-DSR-002");
        galaxyLine.CustomerName.Should().Be("Jane Smith");
        galaxyLine.Qty.Should().Be(1);
        galaxyLine.SellingValue.Should().Be(25000m);
        galaxyLine.CostValue.Should().Be(17000m);
        galaxyLine.Gst.Should().Be(3813.56m);
        galaxyLine.Imei.Should().Be("999888777666555");
        galaxyLine.PaymentMode.Should().Be(PaymentMode.Card);

        // Line 3: Gift Screen Guard
        var giftLine = report.ItemizedLog.First(l => l.ItemName == "Screen Guard Gift");
        giftLine.InvoiceNumber.Should().Be("INV-DSR-002");
        giftLine.Qty.Should().Be(1);
        giftLine.SellingValue.Should().Be(0m);
        giftLine.CostValue.Should().Be(50m);
        giftLine.Gst.Should().Be(0m);
        giftLine.Imei.Should().BeNull();
        giftLine.PaymentMode.Should().Be(PaymentMode.Card);
    }
}
