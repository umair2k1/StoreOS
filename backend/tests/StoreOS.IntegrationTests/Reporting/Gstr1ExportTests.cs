using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Reporting.Gstr1Export;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Reporting;

[Collection("postgres")]
public class Gstr1ExportTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _branchCId = Guid.NewGuid();

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private const string GstinState29 = "29ABCDE1234F1Z5";
    private const string GstinState27 = "27VWXYZ9876G2Z4";

    private DateOnly _fromDate;
    private DateOnly _toDate;
    private DateOnly _inRangeDate;
    private DateOnly _outOfRangeDate;

    public Gstr1ExportTests(PostgresTestFixture fixture)
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

        // 1. Tenant & User & Role
        var tenant = Tenant.Create("GSTR1 Tenant", timezone: "Asia/Kolkata", subdomain: "gst-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var user = User.Create(_tenantId, $"gstr1-{_userId:N}@storeos.test", passwordHash, "Gstr1 User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var role = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        // 2. Three Branches:
        // Branch A (Karnataka GSTIN 29)
        var branchA = Branch.Create(_tenantId, "BA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        branchA.UpdateProfile("Branch Alpha", null, null, null, GstinState29);
        db.Branches.Add(branchA);

        // Branch B (Maharashtra GSTIN 27 - DIFFERENT)
        var branchB = Branch.Create(_tenantId, "BB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        branchB.UpdateProfile("Branch Beta", null, null, null, GstinState27);
        db.Branches.Add(branchB);

        // Branch C (Karnataka GSTIN 29 - SAME as Branch A!)
        var branchC = Branch.Create(_tenantId, "BC", "Branch Gamma");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchC, _branchCId);
        branchC.UpdateProfile("Branch Gamma", null, null, null, GstinState29);
        db.Branches.Add(branchC);

        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchAId));
        db.UserBranches.Add(UserBranch.Create(_userId, _branchAId, isDefault: true));
        db.UserBranches.Add(UserBranch.Create(_userId, _branchBId, isDefault: false));
        db.UserBranches.Add(UserBranch.Create(_userId, _branchCId, isDefault: false));

        // 3. Dates
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
        _inRangeDate = DateOnly.FromDateTime(localNow);
        _fromDate = _inRangeDate.AddDays(-2);
        _toDate = _inRangeDate.AddDays(2);
        _outOfRangeDate = _fromDate.AddDays(-10);

        DateTimeOffset ToUtc(DateOnly d, int hour, int minute)
        {
            var dt = d.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Unspecified);
            var offset = tz.GetUtcOffset(dt);
            return new DateTimeOffset(dt, offset).ToUniversalTime();
        }

        // 4. Products
        var prodA = Product.Create(_tenantId, "Laptop A", ProductCategory.NewPhone, 10000m, 8000m, 18m, "8471");
        var prodB = Product.Create(_tenantId, "Monitor B", ProductCategory.Accessory, 20000m, 15000m, 18m, "8528");
        var prodC = Product.Create(_tenantId, "Keyboard C", ProductCategory.Accessory, 30000m, 22000m, 18m, "8471");
        db.Products.AddRange(prodA, prodB, prodC);

        // 5. Seed Sales
        // Sale 1: Branch A (GSTIN 29), In Range, Active TaxInvoice
        // Taxable: 8474.58, CGST: 762.71, SGST: 762.71, GrandTotal: 10000
        var saleA = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-GST-001", ToUtc(_inRangeDate, 10, 0),
            customerId: null, customerName: "Cust Alpha", customerGstin: "29AABCU9603R1ZM",
            totalTaxable: 8474.58m, totalCgst: 762.71m, totalSgst: 762.71m, grandTotal: 10000m,
            discount: 0m, amountPaid: 10000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Branch A Sale",
            repairJobId: null, createdBy: _userId);
        saleA.AddItem(SaleItem.Create(
            _tenantId, saleA.Id, prodA.Id, "Laptop A", ProductCategory.NewPhone,
            1, 10000m, 8000m, "8471", false, false, 8474.58m, 762.71m, 762.71m, null));
        db.Sales.Add(saleA);

        // Sale 2: Branch B (GSTIN 27), In Range, Active TaxInvoice
        // Taxable: 16949.15, CGST: 1525.425, SGST: 1525.425, GrandTotal: 20000
        var saleB = Sale.CreateTaxInvoice(
            _tenantId, _branchBId, "INV-GST-002", ToUtc(_inRangeDate, 11, 0),
            customerId: null, customerName: "Cust Beta", customerGstin: "27AABCU9603R1ZL",
            totalTaxable: 16949.15m, totalCgst: 1525.425m, totalSgst: 1525.425m, grandTotal: 20000m,
            discount: 0m, amountPaid: 20000m, paymentMode: PaymentMode.UPI,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Branch B Sale",
            repairJobId: null, createdBy: _userId);
        saleB.AddItem(SaleItem.Create(
            _tenantId, saleB.Id, prodB.Id, "Monitor B", ProductCategory.Accessory,
            1, 20000m, 15000m, "8528", false, false, 16949.15m, 1525.425m, 1525.425m, null));
        db.Sales.Add(saleB);

        // Sale 3: Branch C (GSTIN 29 - same as Branch A!), In Range, Active TaxInvoice
        // Taxable: 25423.73, CGST: 2288.135, SGST: 2288.135, GrandTotal: 30000
        var saleC = Sale.CreateTaxInvoice(
            _tenantId, _branchCId, "INV-GST-003", ToUtc(_inRangeDate, 12, 0),
            customerId: null, customerName: "Cust Gamma", customerGstin: null,
            totalTaxable: 25423.73m, totalCgst: 2288.135m, totalSgst: 2288.135m, grandTotal: 30000m,
            discount: 0m, amountPaid: 30000m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Branch C Sale",
            repairJobId: null, createdBy: _userId);
        saleC.AddItem(SaleItem.Create(
            _tenantId, saleC.Id, prodC.Id, "Keyboard C", ProductCategory.Accessory,
            1, 30000m, 22000m, "8471", false, false, 25423.73m, 2288.135m, 2288.135m, null));
        db.Sales.Add(saleC);

        // Sale 4 (Estimate on Branch A, In Range): MUST be excluded
        var saleEstimate = Sale.CreateEstimate(
            _tenantId, _branchAId, "EST-GST-004", ToUtc(_inRangeDate, 13, 0),
            customerId: null, customerName: "Cust Est", customerGstin: null,
            totalTaxable: 42372.88m, totalCgst: 3813.56m, totalSgst: 3813.56m, grandTotal: 50000m,
            discount: 0m, amountPaid: 0m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Estimate",
            repairJobId: null, createdBy: _userId);
        saleEstimate.AddItem(SaleItem.Create(
            _tenantId, saleEstimate.Id, prodA.Id, "Laptop A", ProductCategory.NewPhone,
            1, 50000m, 8000m, "8471", false, false, 42372.88m, 3813.56m, 3813.56m, null));
        db.Sales.Add(saleEstimate);

        // Sale 5 (Voided on Branch A, In Range): MUST be excluded
        var saleVoid = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-GST-VOID", ToUtc(_inRangeDate, 14, 0),
            customerId: null, customerName: "Cust Void", customerGstin: null,
            totalTaxable: 33898.31m, totalCgst: 3050.845m, totalSgst: 3050.845m, grandTotal: 40000m,
            discount: 0m, amountPaid: 40000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Void Sale",
            repairJobId: null, createdBy: _userId);
        saleVoid.AddItem(SaleItem.Create(
            _tenantId, saleVoid.Id, prodA.Id, "Laptop A", ProductCategory.NewPhone,
            1, 40000m, 8000m, "8471", false, false, 33898.31m, 3050.845m, 3050.845m, null));
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(saleVoid, SaleStatus.Voided);
        db.Sales.Add(saleVoid);

        // Sale 6 (Refunded on Branch B, In Range): MUST be excluded
        var saleRefund = Sale.CreateTaxInvoice(
            _tenantId, _branchBId, "INV-GST-REF", ToUtc(_inRangeDate, 15, 0),
            customerId: null, customerName: "Cust Refund", customerGstin: null,
            totalTaxable: 21186.44m, totalCgst: 1906.78m, totalSgst: 1906.78m, grandTotal: 25000m,
            discount: 0m, amountPaid: 25000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Refund Sale",
            repairJobId: null, createdBy: _userId);
        saleRefund.AddItem(SaleItem.Create(
            _tenantId, saleRefund.Id, prodB.Id, "Monitor B", ProductCategory.Accessory,
            1, 25000m, 15000m, "8528", false, false, 21186.44m, 1906.78m, 1906.78m, null));
        typeof(Sale).GetProperty(nameof(Sale.Status))!.SetValue(saleRefund, SaleStatus.Refunded);
        db.Sales.Add(saleRefund);

        // Sale 7 (Outside Date Range on Branch A): MUST be excluded
        var saleOutside = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-GST-OUT", ToUtc(_outOfRangeDate, 10, 0),
            customerId: null, customerName: "Cust Out", customerGstin: null,
            totalTaxable: 8474.58m, totalCgst: 762.71m, totalSgst: 762.71m, grandTotal: 10000m,
            discount: 0m, amountPaid: 10000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "Out of range Sale",
            repairJobId: null, createdBy: _userId);
        saleOutside.AddItem(SaleItem.Create(
            _tenantId, saleOutside.Id, prodA.Id, "Laptop A", ProductCategory.NewPhone,
            1, 10000m, 8000m, "8471", false, false, 8474.58m, 762.71m, 762.71m, null));
        db.Sales.Add(saleOutside);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
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
            _userId, _tenantId, allBranches: true, branchIds: new[] { _branchAId, _branchBId, _branchCId }, Array.Empty<string>(), tokenVersion: 1);
        return token;
    }

    private async Task<Gstr1ExportDto> GetGstr1JsonAsync(DateOnly from, DateOnly to)
    {
        var token = IssueToken();
        var url = $"/api/exports/gstr1?from={from:yyyy-MM-dd}&to={to:yyyy-MM-dd}&format=json";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var response = await _client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP {(int)response.StatusCode}: {err}");
        }

        var dto = await response.Content.ReadFromJsonAsync<Gstr1ExportDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    [Fact]
    public async Task Gstr1Export_GroupsBySellingBranchGstin_CombinesBranchesSharingGstin()
    {
        // (a) sales across two branches with DIFFERENT GSTINs -> two separate groups in the export, each with correct subtotals
        // (b) seed two branches SHARING the same GSTIN -> their sales are combined into ONE group
        var export = await GetGstr1JsonAsync(_fromDate, _toDate);

        // Exactly 2 GSTIN groups: 27VWXYZ9876G2Z4 (Branch B) and 29ABCDE1234F1Z5 (Branch A + Branch C)
        export.ByGstin.Should().HaveCount(2);

        // Group 1: 27 (Branch B only)
        var group27 = export.ByGstin.FirstOrDefault(g => g.Gstin == GstinState27);
        group27.Should().NotBeNull();
        group27!.Rows.Should().HaveCount(1);
        group27.Rows[0].InvoiceNumber.Should().Be("INV-GST-002");
        group27.Rows[0].CustomerGstin.Should().Be("27AABCU9603R1ZL");
        group27.Rows[0].TotalValue.Should().Be(20000m);

        // Group 2: 29 (Branch A AND Branch C combined)
        var group29 = export.ByGstin.FirstOrDefault(g => g.Gstin == GstinState29);
        group29.Should().NotBeNull();
        group29!.Rows.Should().HaveCount(2);
        group29.Rows.Select(r => r.InvoiceNumber).Should().Equal(new[] { "INV-GST-001", "INV-GST-003" });
    }

    [Fact]
    public async Task Gstr1Export_ExcludesEstimatesVoidedAndRefundedSales()
    {
        // (c) an Estimate, a Voided sale, and a Refunded sale within the date range are all correctly EXCLUDED
        var export = await GetGstr1JsonAsync(_fromDate, _toDate);

        var allRows = export.ByGstin.SelectMany(g => g.Rows).ToList();
        allRows.Should().NotContain(r => r.InvoiceNumber == "EST-GST-004");
        allRows.Should().NotContain(r => r.InvoiceNumber == "INV-GST-VOID");
        allRows.Should().NotContain(r => r.InvoiceNumber == "INV-GST-REF");
    }

    [Fact]
    public async Task Gstr1Export_ExcludesSalesOutsideDateRange()
    {
        // (d) a sale outside the [From, To] range is excluded
        var export = await GetGstr1JsonAsync(_fromDate, _toDate);

        var allRows = export.ByGstin.SelectMany(g => g.Rows).ToList();
        allRows.Should().NotContain(r => r.InvoiceNumber == "INV-GST-OUT");
    }

    [Fact]
    public async Task Gstr1Export_GroupTotalsExactlyMatchSumOfOwnRows()
    {
        // (e) each group's TotalTaxable/TotalCgst/TotalSgst/GrandTotal exactly match the sum of its own rows
        var export = await GetGstr1JsonAsync(_fromDate, _toDate);

        foreach (var group in export.ByGstin)
        {
            var sumTaxable = group.Rows.Sum(r => r.TaxableValue);
            var sumCgst = group.Rows.Sum(r => r.Cgst);
            var sumSgst = group.Rows.Sum(r => r.Sgst);
            var sumTotal = group.Rows.Sum(r => r.TotalValue);

            group.TotalTaxable.Should().Be(sumTaxable);
            group.TotalCgst.Should().Be(sumCgst);
            group.TotalSgst.Should().Be(sumSgst);
            group.GrandTotal.Should().Be(sumTotal);
        }
    }

    [Fact]
    public async Task Gstr1Export_CsvFormat_EmitsUtf8BomAndHeadersAndTotalRows()
    {
        // CSV export: UTF-8 BOM, headers, TOTAL rows
        var token = IssueToken();
        var url = $"/api/exports/gstr1?from={_fromDate:yyyy-MM-dd}&to={_toDate:yyyy-MM-dd}&format=csv";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType!.MediaType.Should().Be("text/csv");
        response.Content.Headers.ContentDisposition.Should().NotBeNull();
        response.Content.Headers.ContentDisposition!.FileName.Should().StartWith("gstr1-").And.EndWith(".csv");

        var bytes = await response.Content.ReadAsByteArrayAsync();
        // UTF-8 BOM check
        bytes.Length.Should().BeGreaterThan(3);
        bytes[0].Should().Be(0xEF);
        bytes[1].Should().Be(0xBB);
        bytes[2].Should().Be(0xBF);

        var csvText = Encoding.UTF8.GetString(bytes[3..]);
        csvText.Should().Contain("Gstin,InvoiceNumber,Date,CustomerName,CustomerGstin,Hsn,Category,TaxableValue,Cgst,Sgst,TotalValue");
        csvText.Should().Contain(GstinState29);
        csvText.Should().Contain(GstinState27);
        csvText.Should().Contain("TOTAL");
        csvText.Should().Contain("INV-GST-001");
        csvText.Should().Contain("INV-GST-002");
        csvText.Should().Contain("INV-GST-003");
    }
}
