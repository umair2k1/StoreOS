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
using StoreOS.Application.Sales.Search;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class GetSalesTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    // Known sales for deterministic assertions
    private Guid _saleTodayId;
    private Guid _saleYesterdayId;
    private Guid _saleThisWeekId;
    private Guid _saleThisMonthId;
    private Guid _saleLastMonthId;
    private Guid _saleThisYearId;
    private Guid _saleVoidedId;
    private Guid _saleRefundedId;
    private Guid _saleEstimateId;
    private Guid _saleCardId;
    private Guid _saleCreditId;

    public GetSalesTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Sales Search Tenant", timezone: "Asia/Kolkata", subdomain: "sst-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "BR1", "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var user = User.Create(_tenantId, $"cashier-{_userId:N}@storeos.test", passwordHash, "Sales Test User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var role = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var userRole = UserRoleAssignment.Create(_userId, _roleId, _branchId);
        db.UserRoleAssignments.Add(userRole);

        var userBranch = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(userBranch);

        // Resolve local time in Asia/Kolkata
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
        var localToday = DateOnly.FromDateTime(localNow);

        DateTimeOffset ToUtc(DateOnly d, int hour, int minute)
        {
            var dt = d.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
            var offset = tz.GetUtcOffset(dt);
            return new DateTimeOffset(dt, offset).ToUniversalTime();
        }

        // 1. Sale Today: Cash, Paid, TaxInvoice, Category Accessory
        var saleToday = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1001", ToUtc(localToday, 10, 0),
            customerId: null, customerName: "Alice Walker", customerGstin: "29AAAAA0000A1Z5",
            totalTaxable: 800m, totalCgst: 100m, totalSgst: 100m, grandTotal: 1000m,
            discount: 0m, amountPaid: 1000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Today Cash Sale",
            repairJobId: null, createdBy: _userId);
        saleToday.AddItem(SaleItem.Create(_tenantId, saleToday.Id, null, "USB-C Fast Cable", "Accessory", 2, 500m, 200m, "8544", false, false, 800m, 100m, 100m, null));
        db.Sales.Add(saleToday);
        _saleTodayId = saleToday.Id;

        // 2. Sale Yesterday: UPI, Due, TaxInvoice, Category Audio
        var yesterday = localToday.AddDays(-1);
        var saleYesterday = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1002", ToUtc(yesterday, 11, 0),
            customerId: null, customerName: "Bob Dylan", customerGstin: "29BBBBB0000B1Z5",
            totalTaxable: 4000m, totalCgst: 500m, totalSgst: 500m, grandTotal: 5000m,
            discount: 0m, amountPaid: 2000m, paymentMode: PaymentMode.UPI,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Yesterday UPI Sale",
            repairJobId: null, createdBy: _userId);
        saleYesterday.AddItem(SaleItem.Create(_tenantId, saleYesterday.Id, null, "Wireless Earbuds", "Audio", 1, 5000m, 3000m, "8518", false, false, 4000m, 500m, 500m, null));
        db.Sales.Add(saleYesterday);
        _saleYesterdayId = saleYesterday.Id;

        // 3. Sale This Week (start of week Monday)
        int diffMon = (7 + ((int)localToday.DayOfWeek - (int)DayOfWeek.Monday)) % 7;
        var monday = localToday.AddDays(-diffMon);
        var saleThisWeek = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1003", ToUtc(monday, 9, 30),
            customerId: null, customerName: "Charlie Brown", customerGstin: null,
            totalTaxable: 1800m, totalCgst: 100m, totalSgst: 100m, grandTotal: 2000m,
            discount: 0m, amountPaid: 2000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Monday Sale",
            repairJobId: null, createdBy: _userId);
        saleThisWeek.AddItem(SaleItem.Create(_tenantId, saleThisWeek.Id, null, "Tempered Glass", "Accessory", 1, 2000m, 500m, "3926", false, false, 1800m, 100m, 100m, null));
        db.Sales.Add(saleThisWeek);
        _saleThisWeekId = saleThisWeek.Id;

        // 4. Sale This Month (1st day of month)
        var firstOfMonth = new DateOnly(localToday.Year, localToday.Month, 1);
        var saleThisMonth = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1004", ToUtc(firstOfMonth, 12, 0),
            customerId: null, customerName: "David Miller", customerGstin: null,
            totalTaxable: 7000m, totalCgst: 500m, totalSgst: 500m, grandTotal: 8000m,
            discount: 0m, amountPaid: 4000m, paymentMode: PaymentMode.UPI,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "First of Month Sale",
            repairJobId: null, createdBy: _userId);
        saleThisMonth.AddItem(SaleItem.Create(_tenantId, saleThisMonth.Id, null, "Smart Watch", "Wearables", 1, 8000m, 5000m, "8517", false, false, 7000m, 500m, 500m, null));
        db.Sales.Add(saleThisMonth);
        _saleThisMonthId = saleThisMonth.Id;

        // 5. Sale Last Month: Finance, Category NewPhone with IMEI & Finance Provider
        var firstOfCurrent = new DateOnly(localToday.Year, localToday.Month, 1);
        var lastMonthDate = firstOfCurrent.AddMonths(-1).AddDays(10);
        var saleLastMonth = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1005", ToUtc(lastMonthDate, 15, 0),
            customerId: null, customerName: "Emma Finance", customerGstin: "29EEEEE0000E1Z5",
            totalTaxable: 45000m, totalCgst: 2500m, totalSgst: 2500m, grandTotal: 50000m,
            discount: 0m, amountPaid: 10000m, paymentMode: PaymentMode.Finance,
            financeProvider: "Bajaj Finserv", financerName: "Bajaj", downPayment: 10000m,
            emiDetailsJson: "{\"tenure\":6}", giftDetailsJson: null, description: "Financed Phone Sale",
            repairJobId: null, createdBy: _userId);
        saleLastMonth.AddItem(SaleItem.Create(_tenantId, saleLastMonth.Id, null, "Pixel 8 Pro", "NewPhone", 1, 50000m, 40000m, "8517", false, false, 45000m, 2500m, 2500m, "359876543210987"));
        db.Sales.Add(saleLastMonth);
        _saleLastMonthId = saleLastMonth.Id;

        // 6. Sale This Year (Jan 10 of current year)
        var thisYearDate = new DateOnly(localToday.Year, 1, 10);
        var saleThisYear = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1006", ToUtc(thisYearDate, 14, 0),
            customerId: null, customerName: "Frank Sinatra", customerGstin: null,
            totalTaxable: 3000m, totalCgst: 0m, totalSgst: 0m, grandTotal: 3000m,
            discount: 0m, amountPaid: 3000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Early Year Sale",
            repairJobId: null, createdBy: _userId);
        saleThisYear.AddItem(SaleItem.Create(_tenantId, saleThisYear.Id, null, "Leather Case", "Accessory", 1, 3000m, 1000m, "4202", false, false, 3000m, 0m, 0m, null));
        db.Sales.Add(saleThisYear);
        _saleThisYearId = saleThisYear.Id;

        // 7. Voided Sale
        var saleVoided = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1007-VOID", ToUtc(localToday, 16, 0),
            customerId: null, customerName: "Grace Hopper", customerGstin: null,
            totalTaxable: 2500m, totalCgst: 0m, totalSgst: 0m, grandTotal: 2500m,
            discount: 0m, amountPaid: 2500m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Voided Sale",
            repairJobId: null, createdBy: _userId);
        saleVoided.AddItem(SaleItem.Create(_tenantId, saleVoided.Id, null, "Screen Protector", "Accessory", 1, 2500m, 500m, "3926", false, false, 2500m, 0m, 0m, null));
        saleVoided.MarkVoided();
        db.Sales.Add(saleVoided);
        _saleVoidedId = saleVoided.Id;

        // 8. Refunded Sale
        var saleRefunded = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1008-REFUND", ToUtc(localToday, 17, 0),
            customerId: null, customerName: "Hannah Abbott", customerGstin: null,
            totalTaxable: 3500m, totalCgst: 0m, totalSgst: 0m, grandTotal: 3500m,
            discount: 0m, amountPaid: 3500m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Refunded Sale",
            repairJobId: null, createdBy: _userId);
        saleRefunded.AddItem(SaleItem.Create(_tenantId, saleRefunded.Id, null, "Bluetooth Speaker", "Audio", 1, 3500m, 1500m, "8518", false, false, 3500m, 0m, 0m, null));
        saleRefunded.MarkRefunded();
        db.Sales.Add(saleRefunded);
        _saleRefundedId = saleRefunded.Id;

        // 9. Estimate
        var saleEstimate = Sale.CreateEstimate(
            _tenantId, _branchId, "EST-1009", ToUtc(localToday, 18, 0),
            customerId: null, customerName: "Ian Malcolm", customerGstin: null,
            totalTaxable: 15000m, totalCgst: 0m, totalSgst: 0m, grandTotal: 15000m,
            discount: 0m, amountPaid: 0m, paymentMode: null,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Draft Estimate",
            repairJobId: null, createdBy: _userId);
        saleEstimate.AddItem(SaleItem.Create(_tenantId, saleEstimate.Id, null, "Tablet Pro", "Tablets", 1, 15000m, 10000m, "8471", false, false, 15000m, 0m, 0m, null));
        db.Sales.Add(saleEstimate);
        _saleEstimateId = saleEstimate.Id;

        // 10. Card Sale: Active
        var saleCard = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1010", ToUtc(localToday, 13, 0),
            customerId: null, customerName: "Julia Roberts", customerGstin: null,
            totalTaxable: 6000m, totalCgst: 0m, totalSgst: 0m, grandTotal: 6000m,
            discount: 0m, amountPaid: 6000m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Card Payment Sale",
            repairJobId: null, createdBy: _userId);
        saleCard.AddItem(SaleItem.Create(_tenantId, saleCard.Id, null, "Power Bank", "Accessory", 1, 6000m, 2000m, "8507", false, false, 6000m, 0m, 0m, null));
        db.Sales.Add(saleCard);
        _saleCardId = saleCard.Id;

        // 11. Credit Sale: Active, Unpaid
        var saleCredit = Sale.CreateTaxInvoice(
            _tenantId, _branchId, "INV-1011", ToUtc(localToday, 14, 0),
            customerId: null, customerName: "Kevin Durant", customerGstin: null,
            totalTaxable: 4500m, totalCgst: 0m, totalSgst: 0m, grandTotal: 4500m,
            discount: 0m, amountPaid: 0m, paymentMode: PaymentMode.Credit,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Store Credit Sale",
            repairJobId: null, createdBy: _userId);
        saleCredit.AddItem(SaleItem.Create(_tenantId, saleCredit.Id, null, "Gaming Mouse", "Accessory", 1, 4500m, 1500m, "8471", false, false, 4500m, 0m, 0m, null));
        db.Sales.Add(saleCredit);
        _saleCreditId = saleCredit.Id;

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
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

    private async Task<PagedResult<SaleSummaryDto>> ExecuteGetSalesAsync(string queryString = "")
    {
        var token = IssueToken();
        var url = string.IsNullOrWhiteSpace(queryString) ? "/api/sales" : $"/api/sales?{queryString}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await response.Content.ReadFromJsonAsync<PagedResult<SaleSummaryDto>>();
        result.Should().NotBeNull();
        return result!;
    }

    // --- Search Tests ---

    [Fact]
    public async Task Search_ByInvoiceNumber_ReturnsMatchingSale()
    {
        var result = await ExecuteGetSalesAsync("search=INV-1001");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleTodayId);
        result.Items[0].InvoiceNumber.Should().Be("INV-1001");
    }

    [Fact]
    public async Task Search_ByImei_ReturnsMatchingSale()
    {
        var result = await ExecuteGetSalesAsync("search=359876543210987");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleLastMonthId);
        result.Items[0].InvoiceNumber.Should().Be("INV-1005");
    }

    [Fact]
    public async Task Search_ByFinanceProvider_ReturnsMatchingSale()
    {
        var result = await ExecuteGetSalesAsync("search=Bajaj");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleLastMonthId);
        result.Items[0].PaymentMode.Should().Be(PaymentMode.Finance);
    }

    // --- Date Filter Tests (All 7 Options + Custom) ---

    [Fact]
    public async Task DateFilter_Today_ReturnsSalesFromTodayOnly()
    {
        var result = await ExecuteGetSalesAsync("dateFilter=Today");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleTodayId);
        result.Items.Should().NotContain(x => x.Id == _saleYesterdayId);
        result.Items.Should().NotContain(x => x.Id == _saleLastMonthId);
    }

    [Fact]
    public async Task DateFilter_Yesterday_ReturnsSalesFromYesterdayOnly()
    {
        var result = await ExecuteGetSalesAsync("dateFilter=Yesterday");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleYesterdayId);
        result.Items.Should().NotContain(x => x.Id == _saleTodayId);
        result.Items.Should().NotContain(x => x.Id == _saleLastMonthId);
    }

    [Fact]
    public async Task DateFilter_ThisWeek_IncludesSalesInCurrentWeek()
    {
        var result = await ExecuteGetSalesAsync("dateFilter=ThisWeek");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleThisWeekId);
        result.Items.Should().NotContain(x => x.Id == _saleLastMonthId);
    }

    [Fact]
    public async Task DateFilter_ThisMonth_IncludesSalesInCurrentMonth()
    {
        var result = await ExecuteGetSalesAsync("dateFilter=ThisMonth");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleThisMonthId);
        result.Items.Should().NotContain(x => x.Id == _saleLastMonthId);
    }

    [Fact]
    public async Task DateFilter_LastMonth_ReturnsSalesInLastMonthOnly()
    {
        var result = await ExecuteGetSalesAsync("dateFilter=LastMonth");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleLastMonthId);
        result.Items.Should().NotContain(x => x.Id == _saleTodayId);
    }

    [Fact]
    public async Task DateFilter_ThisYear_IncludesSalesInCurrentYear()
    {
        var result = await ExecuteGetSalesAsync("dateFilter=ThisYear");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleThisYearId);
    }

    [Fact]
    public async Task DateFilter_CustomRange_ReturnsSalesWithinRange()
    {
        var localToday = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(5.5));
        var from = localToday.AddDays(-1).ToString("yyyy-MM-dd");
        var to = localToday.AddDays(-1).ToString("yyyy-MM-dd");

        var result = await ExecuteGetSalesAsync($"dateFilter=Custom&customFrom={from}&customTo={to}");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().Contain(x => x.Id == _saleYesterdayId);
        result.Items.Should().NotContain(x => x.Id == _saleTodayId);
    }

    // --- Status Filter Tests ---

    [Fact]
    public async Task StatusFilter_Active_ReturnsOnlyActiveSales()
    {
        var result = await ExecuteGetSalesAsync("status=Active");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.Status == SaleStatus.Active);
        result.Items.Should().NotContain(x => x.Id == _saleVoidedId);
        result.Items.Should().NotContain(x => x.Id == _saleRefundedId);
    }

    [Fact]
    public async Task StatusFilter_Voided_ReturnsOnlyVoidedSales()
    {
        var result = await ExecuteGetSalesAsync("status=Voided");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleVoidedId);
        result.Items[0].Status.Should().Be(SaleStatus.Voided);
    }

    [Fact]
    public async Task StatusFilter_Refunded_ReturnsOnlyRefundedSales()
    {
        var result = await ExecuteGetSalesAsync("status=Refunded");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleRefundedId);
        result.Items[0].Status.Should().Be(SaleStatus.Refunded);
    }

    // --- Payment Mode Filter Tests ---

    [Theory]
    [InlineData(PaymentMode.Cash)]
    [InlineData(PaymentMode.UPI)]
    [InlineData(PaymentMode.Card)]
    [InlineData(PaymentMode.Credit)]
    [InlineData(PaymentMode.Finance)]
    public async Task PaymentModeFilter_ReturnsOnlySalesWithMatchingMode(string mode)
    {
        var result = await ExecuteGetSalesAsync($"paymentMode={mode}");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.PaymentMode == mode);
    }

    // --- Invoice Type Filter Tests ---

    [Fact]
    public async Task InvoiceTypeFilter_TaxInvoice_ReturnsOnlyTaxInvoices()
    {
        var result = await ExecuteGetSalesAsync($"invoiceType={InvoiceType.TaxInvoice}");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.InvoiceType == InvoiceType.TaxInvoice);
        result.Items.Should().NotContain(x => x.Id == _saleEstimateId);
    }

    [Fact]
    public async Task InvoiceTypeFilter_Estimate_ReturnsOnlyEstimates()
    {
        var result = await ExecuteGetSalesAsync($"invoiceType={InvoiceType.Estimate}");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleEstimateId);
        result.Items[0].InvoiceType.Should().Be(InvoiceType.Estimate);
    }

    // --- PaidOnly / DueOnly Tests ---

    [Fact]
    public async Task PaidOnlyFilter_ReturnsOnlyFullyPaidSales()
    {
        var result = await ExecuteGetSalesAsync("paidOnly=true");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.AmountPaid >= x.GrandTotal);
        result.Items.Should().Contain(x => x.Id == _saleTodayId);
        result.Items.Should().NotContain(x => x.Id == _saleYesterdayId);
    }

    [Fact]
    public async Task DueOnlyFilter_ReturnsOnlySalesWithBalanceDue()
    {
        var result = await ExecuteGetSalesAsync("dueOnly=true");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.AmountPaid < x.GrandTotal);
        result.Items.Should().Contain(x => x.Id == _saleYesterdayId);
        result.Items.Should().NotContain(x => x.Id == _saleTodayId);
    }

    // --- FinancedOnly Tests ---

    [Fact]
    public async Task FinancedOnlyFilter_True_ReturnsOnlyFinancedSales()
    {
        var result = await ExecuteGetSalesAsync("financedOnly=true");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleLastMonthId);
        result.Items[0].IsFinanced.Should().BeTrue();
    }

    [Fact]
    public async Task FinancedOnlyFilter_False_ReturnsOnlyNonFinancedSales()
    {
        var result = await ExecuteGetSalesAsync("financedOnly=false");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().NotContain(x => x.Id == _saleLastMonthId);
        result.Items.Should().OnlyContain(x => !x.IsFinanced);
    }

    // --- Category Filter Tests ---

    [Fact]
    public async Task CategoryFilter_ReturnsSalesContainingCategoryItems()
    {
        var result = await ExecuteGetSalesAsync("category=NewPhone");
        result.Items.Should().ContainSingle();
        result.Items[0].Id.Should().Be(_saleLastMonthId);
    }

    // --- MinAmount / MaxAmount Tests ---

    [Fact]
    public async Task MinMaxAmountFilter_ReturnsSalesWithinGrandTotalRange()
    {
        var result = await ExecuteGetSalesAsync("minAmount=4000&maxAmount=6000");
        result.Items.Should().NotBeEmpty();
        result.Items.Should().OnlyContain(x => x.GrandTotal >= 4000m && x.GrandTotal <= 6000m);
        result.Items.Should().Contain(x => x.Id == _saleYesterdayId); // 5000m
        result.Items.Should().Contain(x => x.Id == _saleCardId);      // 6000m
        result.Items.Should().Contain(x => x.Id == _saleCreditId);    // 4500m
        result.Items.Should().NotContain(x => x.Id == _saleTodayId);  // 1000m
        result.Items.Should().NotContain(x => x.Id == _saleLastMonthId); // 50000m
    }

    // --- Sort Tests (5 fields x 2 directions) ---

    [Theory]
    [InlineData("SaleDate", "asc")]
    [InlineData("SaleDate", "desc")]
    [InlineData("GrandTotal", "asc")]
    [InlineData("GrandTotal", "desc")]
    [InlineData("InvoiceNumber", "asc")]
    [InlineData("InvoiceNumber", "desc")]
    [InlineData("CustomerName", "asc")]
    [InlineData("CustomerName", "desc")]
    [InlineData("Balance", "asc")]
    [InlineData("Balance", "desc")]
    public async Task Sorting_AllFieldsAndDirections_OrderCorrectly(string sortBy, string sortDirection)
    {
        var result = await ExecuteGetSalesAsync($"sortBy={sortBy}&sortDirection={sortDirection}&pageSize=100");
        result.Items.Should().NotBeEmpty();

        var items = result.Items;
        for (int i = 0; i < items.Count - 1; i++)
        {
            var cur = items[i];
            var next = items[i + 1];

            if (sortBy.Equals("GrandTotal", StringComparison.OrdinalIgnoreCase))
            {
                if (sortDirection == "asc")
                    cur.GrandTotal.Should().BeLessThanOrEqualTo(next.GrandTotal);
                else
                    cur.GrandTotal.Should().BeGreaterThanOrEqualTo(next.GrandTotal);
            }
            else if (sortBy.Equals("Balance", StringComparison.OrdinalIgnoreCase))
            {
                if (sortDirection == "asc")
                    cur.Balance.Should().BeLessThanOrEqualTo(next.Balance);
                else
                    cur.Balance.Should().BeGreaterThanOrEqualTo(next.Balance);
            }
            else if (sortBy.Equals("InvoiceNumber", StringComparison.OrdinalIgnoreCase))
            {
                if (sortDirection == "asc")
                    string.Compare(cur.InvoiceNumber, next.InvoiceNumber, StringComparison.Ordinal).Should().BeLessThanOrEqualTo(0);
                else
                    string.Compare(cur.InvoiceNumber, next.InvoiceNumber, StringComparison.Ordinal).Should().BeGreaterThanOrEqualTo(0);
            }
            else if (sortBy.Equals("SaleDate", StringComparison.OrdinalIgnoreCase))
            {
                if (sortDirection == "asc")
                    cur.SaleDate.Should().BeOnOrBefore(next.SaleDate);
                else
                    cur.SaleDate.Should().BeOnOrAfter(next.SaleDate);
            }
        }
    }

    // --- Pagination Tests ---

    [Fact]
    public async Task Pagination_Boundaries_ReturnsExpectedPages()
    {
        var page1 = await ExecuteGetSalesAsync("page=1&pageSize=3");
        page1.Items.Count.Should().Be(3);
        page1.TotalCount.Should().BeGreaterOrEqualTo(11);
        page1.Page.Should().Be(1);
        page1.PageSize.Should().Be(3);

        var page2 = await ExecuteGetSalesAsync("page=2&pageSize=3");
        page2.Items.Count.Should().Be(3);
        page2.TotalCount.Should().Be(page1.TotalCount);
        page2.Page.Should().Be(2);

        // Disjoint sets
        var page1Ids = page1.Items.Select(x => x.Id).ToHashSet();
        page2.Items.Select(x => x.Id).Should().NotIntersectWith(page1Ids);
    }
}
