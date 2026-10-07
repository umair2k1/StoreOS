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
using StoreOS.Application.Reporting.Dashboard;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Reporting;

[Collection("postgres")]
public class DashboardTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private Guid _prodAId;
    private Guid _prodBId;
    private Guid _prodCId;
    private Guid _prodDId;
    private Guid _prodGiftCableId;

    private DateOnly _localToday;
    private decimal _expectedMonthlyRevenue;
    private decimal _expectedMonthlyCogs;
    private decimal _expectedMonthlyExpenses;
    private decimal _expectedMonthlyProfit;

    public DashboardTests(PostgresTestFixture fixture)
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
        var tenant = Tenant.Create("Dashboard Test Tenant", timezone: "Asia/Kolkata", subdomain: "dsh-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "BR1", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var user = User.Create(_tenantId, $"dashboard-{_userId:N}@storeos.test", passwordHash, "Dashboard User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var role = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var userRole = UserRoleAssignment.Create(_userId, _roleId, _branchId);
        db.UserRoleAssignments.Add(userRole);

        var userBranch = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(userBranch);

        // 2. Resolve Asia/Kolkata dates
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
        _localToday = DateOnly.FromDateTime(localNow);

        DateTimeOffset ToUtc(DateOnly d, int hour, int minute)
        {
            var dt = d.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
            var offset = tz.GetUtcOffset(dt);
            return new DateTimeOffset(dt, offset).ToUniversalTime();
        }

        // 3. Products & Branch Inventory
        // Prod A: 10 in stock, threshold 3 -> Normal stock (10 > 3)
        var prodA = Product.Create(_tenantId, "Phone A Alpha", ProductCategory.NewPhone, 10000m, 7000m, 18m, "8517");
        _prodAId = prodA.Id;
        db.Products.Add(prodA);
        var invA = BranchInventory.Create(_tenantId, _branchId, prodA.Id);
        invA.IncreaseStock(10, 7000m);
        invA.SetLowStockThreshold(3);
        db.BranchInventories.Add(invA);

        // Prod B: 2 in stock, threshold 5 -> LOW stock (2 > 0 && 2 <= 5)
        var prodB = Product.Create(_tenantId, "Phone B Beta", ProductCategory.NewPhone, 20000m, 15000m, 18m, "8517");
        _prodBId = prodB.Id;
        db.Products.Add(prodB);
        var invB = BranchInventory.Create(_tenantId, _branchId, prodB.Id);
        invB.IncreaseStock(2, 15000m);
        invB.SetLowStockThreshold(5);
        db.BranchInventories.Add(invB);

        // Prod C: 0 in stock, threshold 5 -> Out of stock (0 <= 5 but NOT > 0, excluded from low stock)
        var prodC = Product.Create(_tenantId, "Phone C Gamma", ProductCategory.NewPhone, 5000m, 3000m, 18m, "8517");
        _prodCId = prodC.Id;
        db.Products.Add(prodC);
        var invC = BranchInventory.Create(_tenantId, _branchId, prodC.Id);
        invC.IncreaseStock(1, 3000m);
        invC.DecreaseStock(1); // Now 0
        invC.SetLowStockThreshold(5);
        db.BranchInventories.Add(invC);

        // Prod D: 6 in stock, threshold 5 -> Normal stock (6 > 5)
        var prodD = Product.Create(_tenantId, "Phone D Delta", ProductCategory.NewPhone, 8000m, 5000m, 18m, "8517");
        _prodDId = prodD.Id;
        db.Products.Add(prodD);
        var invD = BranchInventory.Create(_tenantId, _branchId, prodD.Id);
        invD.IncreaseStock(6, 5000m);
        invD.SetLowStockThreshold(5);
        db.BranchInventories.Add(invD);

        // Prod Gift Cable: 1 in stock, threshold 3 -> LOW stock (1 > 0 && 1 <= 3)
        var prodGift = Product.Create(_tenantId, "Gift Cable Special", ProductCategory.Accessory, 500m, 200m, 18m, "8544");
        _prodGiftCableId = prodGift.Id;
        db.Products.Add(prodGift);
        var invGift = BranchInventory.Create(_tenantId, _branchId, prodGift.Id);
        invGift.IncreaseStock(1, 200m);
        invGift.SetLowStockThreshold(3);
        db.BranchInventories.Add(invGift);

        // Prod Service: Repair service (never has inventory)
        var prodService = Product.Create(_tenantId, "Screen Replacement", ProductCategory.RepairService, 1500m, 0m, 18m, "998713");
        db.Products.Add(prodService);

        // 4. Sales
        // Sale 1: Today, Active TaxInvoice.
        // Lines:
        // - Phone A: Qty 2, Taxable 16000, CGST 2000, SGST 2000, GrandTotal 20000. Snapshot: 7000. COGS = 2 * 7000 = 14000. LineRevenue = 20000.
        // - Gift Cable: Qty 10, IsGift = true, Taxable 0, CGST 0, SGST 0. Snapshot: 200. Must be excluded from Revenue and COGS and Top Sellers!
        var sale1 = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSH-001", ToUtc(_localToday, 10, 0),
            customerId: null, customerName: "Customer 1", customerGstin: null,
            totalTaxable: 16000m, totalCgst: 2000m, totalSgst: 2000m, grandTotal: 20000m,
            discount: 0m, amountPaid: 20000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale 1",
            repairJobId: null, createdBy: _userId);
        sale1.AddItem(SaleItem.Create(_tenantId, sale1.Id, prodA.Id, "Phone A Alpha", ProductCategory.NewPhone, 2, 10000m, 7000m, "8517", false, false, 16000m, 2000m, 2000m, null));
        sale1.AddItem(SaleItem.Create(_tenantId, sale1.Id, prodGift.Id, "Gift Cable Special", ProductCategory.Accessory, 10, 0m, 200m, "8544", false, true, 0m, 0m, 0m, null));
        db.Sales.Add(sale1);

        // Sale 2: Today, Active TaxInvoice.
        // Line: Phone B: Qty 3, Taxable 50000, CGST 5000, SGST 5000, GrandTotal 60000. Snapshot: 15000. COGS = 3 * 15000 = 45000. LineRevenue = 60000.
        var sale2 = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSH-002", ToUtc(_localToday, 12, 0),
            customerId: null, customerName: "Customer 2", customerGstin: null,
            totalTaxable: 50000m, totalCgst: 5000m, totalSgst: 5000m, grandTotal: 60000m,
            discount: 0m, amountPaid: 60000m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale 2",
            repairJobId: null, createdBy: _userId);
        sale2.AddItem(SaleItem.Create(_tenantId, sale2.Id, prodB.Id, "Phone B Beta", ProductCategory.NewPhone, 3, 20000m, 15000m, "8517", false, false, 50000m, 5000m, 5000m, null));
        db.Sales.Add(sale2);

        // Sale 3: Today, VOIDED TaxInvoice. GrandTotal = 10000. MUST be excluded.
        var saleVoid = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSH-003", ToUtc(_localToday, 14, 0),
            customerId: null, customerName: "Customer Void", customerGstin: null,
            totalTaxable: 8000m, totalCgst: 1000m, totalSgst: 1000m, grandTotal: 10000m,
            discount: 0m, amountPaid: 10000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale Void",
            repairJobId: null, createdBy: _userId);
        saleVoid.AddItem(SaleItem.Create(_tenantId, saleVoid.Id, prodA.Id, "Phone A Alpha", ProductCategory.NewPhone, 1, 10000m, 7000m, "8517", false, false, 8000m, 1000m, 1000m, null));
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(saleVoid, SaleStatus.Voided);
        db.Sales.Add(saleVoid);

        // Sale 4: Today, REFUNDED TaxInvoice. GrandTotal = 20000. MUST be excluded.
        var saleRefund = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSH-004", ToUtc(_localToday, 15, 0),
            customerId: null, customerName: "Customer Refund", customerGstin: null,
            totalTaxable: 16000m, totalCgst: 2000m, totalSgst: 2000m, grandTotal: 20000m,
            discount: 0m, amountPaid: 20000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale Refund",
            repairJobId: null, createdBy: _userId);
        saleRefund.AddItem(SaleItem.Create(_tenantId, saleRefund.Id, prodB.Id, "Phone B Beta", ProductCategory.NewPhone, 1, 20000m, 15000m, "8517", false, false, 16000m, 2000m, 2000m, null));
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(saleRefund, SaleStatus.Refunded);
        db.Sales.Add(saleRefund);

        // Sale 5: Today, ESTIMATE. GrandTotal = 50000. MUST be excluded.
        var saleEstimate = Sale.CreateEstimate(
            _tenantId, _branchId, "EST-DSH-005", ToUtc(_localToday, 16, 0),
            customerId: null, customerName: "Customer Estimate", customerGstin: null,
            totalTaxable: 40000m, totalCgst: 5000m, totalSgst: 5000m, grandTotal: 50000m,
            discount: 0m, amountPaid: 0m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Estimate",
            repairJobId: null, createdBy: _userId);
        saleEstimate.AddItem(SaleItem.Create(_tenantId, saleEstimate.Id, prodB.Id, "Phone B Beta", ProductCategory.NewPhone, 2, 20000m, 15000m, "8517", false, false, 40000m, 5000m, 5000m, null));
        db.Sales.Add(saleEstimate);

        // Sale 6: 4 days ago, Active TaxInvoice (falls within 7 days).
        // Line: Phone D: Qty 4, Taxable 32000, GrandTotal 32000. Snapshot: 5000. COGS = 4 * 5000 = 20000. LineRevenue = 32000.
        var date4DaysAgo = _localToday.AddDays(-4);
        var sale6 = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-DSH-006", ToUtc(date4DaysAgo, 11, 0),
            customerId: null, customerName: "Customer 4 Days Ago", customerGstin: null,
            totalTaxable: 32000m, totalCgst: 0m, totalSgst: 0m, grandTotal: 32000m,
            discount: 0m, amountPaid: 32000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Sale 4 Days Ago",
            repairJobId: null, createdBy: _userId);
        sale6.AddItem(SaleItem.Create(_tenantId, sale6.Id, prodD.Id, "Phone D Delta", ProductCategory.NewPhone, 4, 8000m, 5000m, "8517", false, false, 32000m, 0m, 0m, null));
        db.Sales.Add(sale6);

        // 5. Operational Expense: 5000 for current month
        var expense = Expense.Create(
            _tenantId, _branchId, "Electricity Bill", 5000m, ExpenseCategory.Utilities,
            _localToday, "Office power", _userId);
        db.Expenses.Add(expense);

        // 6. Repair Jobs (by default repairs feature is disabled on tenant)
        var job1 = RepairJob.Intake(
            _tenantId, _branchId, "REP-DSH-001", null, "Cust Repair 1", "9000000001", null,
            "Apple", "iPhone 13", "Phone", null, null, null, null, null, "Broken glass", null);
        db.RepairJobs.Add(job1); // Status = Received (Active)

        var job2 = RepairJob.Intake(
            _tenantId, _branchId, "REP-DSH-002", null, "Cust Repair 2", "9000000002", null,
            "Samsung", "S22", "Phone", null, null, null, null, null, "Battery drain", null);
        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(job2, RepairStatus.ReadyForPickup);
        db.RepairJobs.Add(job2); // Status = ReadyForPickup (Active + Ready)

        var job3 = RepairJob.Intake(
            _tenantId, _branchId, "REP-DSH-003", null, "Cust Repair 3", "9000000003", null,
            "OnePlus", "9 Pro", "Phone", null, null, null, null, null, "Dead", null);
        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(job3, RepairStatus.Delivered);
        db.RepairJobs.Add(job3); // Status = Delivered (Not active)

        await db.SaveChangesAsync();

        // Calculate expected monthly profit based on whether Sale 6 falls in current calendar month
        _expectedMonthlyRevenue = 20000m + 60000m; // Sale 1 and Sale 2
        _expectedMonthlyCogs = 14000m + 45000m;

        if (date4DaysAgo.Month == _localToday.Month && date4DaysAgo.Year == _localToday.Year)
        {
            _expectedMonthlyRevenue += 32000m;
            _expectedMonthlyCogs += 20000m;
        }

        _expectedMonthlyExpenses = 5000m;
        _expectedMonthlyProfit = _expectedMonthlyRevenue - _expectedMonthlyCogs - _expectedMonthlyExpenses;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM repair_job_status_history WHERE repair_job_id IN (SELECT id FROM repair_jobs WHERE tenant_id = @tId); " +
            "DELETE FROM repair_job_parts WHERE tenant_id = @tId; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @tId; " +
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
            "DELETE FROM expenses WHERE tenant_id = @tId; " +
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

    private async Task<DashboardDto> GetDashboardAsync(string query = "")
    {
        var token = IssueToken();
        var url = string.IsNullOrWhiteSpace(query) ? "/api/reports/dashboard" : $"/api/reports/dashboard?{query}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var response = await _client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP {(int)response.StatusCode}: {err}");
        }

        var dto = await response.Content.ReadFromJsonAsync<DashboardDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    [Fact]
    public async Task Dashboard_TodayRevenue_ExcludesVoidedRefundedAndEstimate()
    {
        // (a) today's revenue correctly excludes a Voided sale, a Refunded sale, and an Estimate created today,
        // counting only Active TaxInvoices.
        var dashboard = await GetDashboardAsync();

        // Sale 1 (20,000) + Sale 2 (60,000) = 80,000.
        // Sale 3 (Voided: 10,000), Sale 4 (Refunded: 20,000), Sale 5 (Estimate: 50,000) are all excluded.
        dashboard.TodayRevenue.Should().Be(80000m);
        dashboard.TodayInvoiceCount.Should().Be(2);
    }

    [Fact]
    public async Task Dashboard_MonthlyProfit_UsesPurchasePriceSnapshot_UnaffectedByProductPriceChange()
    {
        // (b) monthly profit uses purchase_price_snapshot, proven by changing the PRODUCT's current purchase price
        // AFTER the sale and confirming the dashboard's profit figure is UNCHANGED (still reflects the snapshot).
        var dashboardBefore = await GetDashboardAsync();
        dashboardBefore.MonthlyProfit.Should().Be(_expectedMonthlyProfit);

        // Update Product A purchase price in DB to an extreme number
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var prodA = await db.Products.IgnoreQueryFilters().FirstAsync(p => p.Id == _prodAId);
            typeof(Product).GetProperty(nameof(Product.PurchasePrice))!.SetValue(prodA, 999999m);
            await db.SaveChangesAsync();
        }

        // Fetch dashboard again
        var dashboardAfter = await GetDashboardAsync();

        // Must remain exactly unchanged because it uses purchase_price_snapshot
        dashboardAfter.MonthlyProfit.Should().Be(_expectedMonthlyProfit);
    }

    [Fact]
    public async Task Dashboard_LowStockList_MatchesExactBoundary()
    {
        // (c) low-stock list matches IsLowStock's exact boundary (qty<=threshold, qty>0)
        var dashboard = await GetDashboardAsync();

        // Prod B: qty 2, threshold 5 -> low stock (included)
        dashboard.LowStockItems.Should().Contain(x => x.ProductId == _prodBId && x.StockQty == 2 && x.Threshold == 5);

        // Prod Gift: qty 1, threshold 3 -> low stock (included)
        dashboard.LowStockItems.Should().Contain(x => x.ProductId == _prodGiftCableId && x.StockQty == 1 && x.Threshold == 3);

        // Prod A: qty 10, threshold 3 -> normal (excluded)
        dashboard.LowStockItems.Should().NotContain(x => x.ProductId == _prodAId);

        // Prod D: qty 6, threshold 5 -> normal (excluded)
        dashboard.LowStockItems.Should().NotContain(x => x.ProductId == _prodDId);

        // Prod C: qty 0, threshold 5 -> out of stock (qty > 0 boundary fails, excluded from low stock)
        dashboard.LowStockItems.Should().NotContain(x => x.ProductId == _prodCId);
    }

    [Fact]
    public async Task Dashboard_TopSellingItems_ExcludesGiftLinesFromRanking()
    {
        // (d) top-5 sellers correctly excludes a gift line's quantity from the ranking
        var dashboard = await GetDashboardAsync();

        // Gift Cable had 10 qty sold as gift, but should NOT appear in top sellers
        dashboard.TopSellingItems.Should().NotContain(x => x.ItemName == "Gift Cable Special");

        // Top sellers should contain items with non-gift sales:
        // Phone D (4 sold if in current month), Phone B (3 sold), Phone A (2 sold)
        dashboard.TopSellingItems.Should().Contain(x => x.ItemName == "Phone B Beta" && x.TotalQtySold == 3);
        dashboard.TopSellingItems.Should().Contain(x => x.ItemName == "Phone A Alpha" && x.TotalQtySold == 2);
    }

    [Fact]
    public async Task Dashboard_SevenDayChart_IncludesZeroRevenueDaysExplicitly()
    {
        // (e) the 7-day chart includes a zero-revenue day explicitly, not omitted
        var dashboard = await GetDashboardAsync();

        dashboard.SevenDayRevenue.Should().HaveCount(7);

        // Days with no sales (e.g. today - 1, today - 2, today - 3) must be explicitly present with 0 revenue
        var twoDaysAgo = _localToday.AddDays(-2);
        dashboard.SevenDayRevenue.Should().Contain(x => x.Date == twoDaysAgo && x.Revenue == 0m);

        // Today must have 80,000 revenue
        dashboard.SevenDayRevenue.Should().Contain(x => x.Date == _localToday && x.Revenue == 80000m);
    }

    [Fact]
    public async Task Dashboard_RepairsFeatureGated_ReturnsZeroWhenDisabled_ReturnsCountsWhenEnabled()
    {
        // (f) active repairs / ready-for-pickup return 0 when the repairs feature is disabled, without erroring.
        var dashboardDisabled = await GetDashboardAsync();

        dashboardDisabled.ActiveRepairsCount.Should().Be(0);
        dashboardDisabled.ReadyForPickupCount.Should().Be(0);

        // Enable repairs feature for this tenant
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Id == _tenantId);
            tenant.SetFeatures("{\"repairs\": true}");
            await db.SaveChangesAsync();
        }

        var dashboardEnabled = await GetDashboardAsync();

        // When enabled:
        // job1 (Received) -> Active
        // job2 (ReadyForPickup) -> Active and Ready
        // job3 (Delivered) -> Not active
        dashboardEnabled.ActiveRepairsCount.Should().Be(2);
        dashboardEnabled.ReadyForPickupCount.Should().Be(1);
    }
}
