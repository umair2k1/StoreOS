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
using StoreOS.Application.Reporting.Compare;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Reporting;

[Collection("postgres")]
public class BranchComparisonTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();
    private readonly Guid _branch2Id = Guid.NewGuid();

    private readonly Guid _allBranchesUserId = Guid.NewGuid();
    private readonly Guid _allBranchesRoleId = Guid.NewGuid();

    private readonly Guid _branch1UserId = Guid.NewGuid();
    private readonly Guid _branch1RoleId = Guid.NewGuid();

    private DateOnly _reportDate;

    public BranchComparisonTests(PostgresTestFixture fixture)
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

        // 1. Tenant with repairs enabled
        var tenant = Tenant.Create("Comparison Tenant", timezone: "Asia/Kolkata", subdomain: "cmp-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\": true}");
        db.Tenants.Add(tenant);

        // 2. Two Branches: Downtown and Uptown
        var branch1 = Branch.Create(_tenantId, "BR1", "Downtown Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch1, _branch1Id);
        db.Branches.Add(branch1);

        var branch2 = Branch.Create(_tenantId, "BR2", "Uptown Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch2, _branch2Id);
        db.Branches.Add(branch2);

        // 3. User with all branches access
        var userAll = User.Create(_tenantId, $"all-{_allBranchesUserId:N}@storeos.test", passwordHash, "All Branches User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userAll, _allBranchesUserId);
        db.Users.Add(userAll);

        var roleAll = Role.Create(_tenantId, "Admin");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(roleAll, _allBranchesRoleId);
        db.Roles.Add(roleAll);

        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_allBranchesUserId, _allBranchesRoleId, _branch1Id));
        db.UserBranches.Add(UserBranch.Create(_allBranchesUserId, _branch1Id, isDefault: true));
        db.UserBranches.Add(UserBranch.Create(_allBranchesUserId, _branch2Id, isDefault: false));

        // 4. User scoped to Branch 1 only
        var userB1 = User.Create(_tenantId, $"b1-{_branch1UserId:N}@storeos.test", passwordHash, "Branch 1 User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userB1, _branch1UserId);
        db.Users.Add(userB1);

        var roleB1 = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(roleB1, _branch1RoleId);
        db.Roles.Add(roleB1);

        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_branch1UserId, _branch1RoleId, _branch1Id));
        db.UserBranches.Add(UserBranch.Create(_branch1UserId, _branch1Id, isDefault: true));

        // 5. Timezone & Dates
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

        // 6. Products
        var prodA = Product.Create(_tenantId, "Gadget Alpha", ProductCategory.NewPhone, 50000m, 30000m, 18m, "8517");
        var prodB = Product.Create(_tenantId, "Gadget Beta", ProductCategory.NewPhone, 80000m, 60000m, 18m, "8517");
        var prodC = Product.Create(_tenantId, "Gadget Gamma", ProductCategory.NewPhone, 20000m, 10000m, 18m, "8517");
        db.Products.AddRange(prodA, prodB, prodC);

        // 7. Seed Sales
        // Branch 1: Sale 1 -> Revenue = 50,000, BuyingCost = 30,000, Profit = 20,000, Invoices = 1
        var saleB1 = Sale.CreateTaxInvoice(
            _tenantId, _branch1Id, "INV-CMP-001", ToUtc(_reportDate, 10, 0),
            customerId: null, customerName: "B1 Cust", customerGstin: null,
            totalTaxable: 42372.88m, totalCgst: 3813.56m, totalSgst: 3813.56m, grandTotal: 50000m,
            discount: 0m, amountPaid: 50000m, paymentMode: PaymentMode.Cash,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "B1 Sale",
            repairJobId: null, createdBy: _allBranchesUserId);
        saleB1.AddItem(SaleItem.Create(
            _tenantId, saleB1.Id, prodA.Id, "Gadget Alpha", ProductCategory.NewPhone,
            1, 50000m, 30000m, "8517", false, false, 42372.88m, 3813.56m, 3813.56m, null));
        db.Sales.Add(saleB1);

        // Branch 2: Sale 2 -> Revenue = 80,000, BuyingCost = 60,000, Profit = 20,000
        var saleB2_1 = Sale.CreateTaxInvoice(
            _tenantId, _branch2Id, "INV-CMP-002", ToUtc(_reportDate, 11, 0),
            customerId: null, customerName: "B2 Cust 1", customerGstin: null,
            totalTaxable: 67796.61m, totalCgst: 6101.695m, totalSgst: 6101.695m, grandTotal: 80000m,
            discount: 0m, amountPaid: 80000m, paymentMode: PaymentMode.UPI,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "B2 Sale 1",
            repairJobId: null, createdBy: _allBranchesUserId);
        saleB2_1.AddItem(SaleItem.Create(
            _tenantId, saleB2_1.Id, prodB.Id, "Gadget Beta", ProductCategory.NewPhone,
            1, 80000m, 60000m, "8517", false, false, 67796.61m, 6101.695m, 6101.695m, null));
        db.Sales.Add(saleB2_1);

        // Branch 2: Sale 3 -> Revenue = 20,000, BuyingCost = 10,000, Profit = 10,000
        // Branch 2 Totals: Revenue = 100,000, Profit = 30,000, Invoices = 2
        var saleB2_2 = Sale.CreateTaxInvoice(
            _tenantId, _branch2Id, "INV-CMP-003", ToUtc(_reportDate, 14, 0),
            customerId: null, customerName: "B2 Cust 2", customerGstin: null,
            totalTaxable: 16949.15m, totalCgst: 1525.425m, totalSgst: 1525.425m, grandTotal: 20000m,
            discount: 0m, amountPaid: 20000m, paymentMode: PaymentMode.Card,
            financeProvider: null, financerName: null, downPayment: null,
            emiDetailsJson: null, giftDetailsJson: null, description: "B2 Sale 2",
            repairJobId: null, createdBy: _allBranchesUserId);
        saleB2_2.AddItem(SaleItem.Create(
            _tenantId, saleB2_2.Id, prodC.Id, "Gadget Gamma", ProductCategory.NewPhone,
            1, 20000m, 10000m, "8517", false, false, 16949.15m, 1525.425m, 1525.425m, null));
        db.Sales.Add(saleB2_2);

        // 8. Repair Jobs
        // Branch 1: 2 Active repairs (Received and ReadyForPickup)
        var jobB1_1 = RepairJob.Intake(
            _tenantId, _branch1Id, "REP-CMP-001", null, "Rep B1 1", null, null,
            "Brand", "Model", "Phone", null, null, null, null, null, "Problem", null);
        var jobB1_2 = RepairJob.Intake(
            _tenantId, _branch1Id, "REP-CMP-002", null, "Rep B1 2", null, null,
            "Brand", "Model", "Phone", null, null, null, null, null, "Problem", null);
        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(jobB1_2, RepairStatus.ReadyForPickup);
        db.RepairJobs.AddRange(jobB1_1, jobB1_2);

        // Branch 2: 1 Active repair (Diagnosing) and 1 Inactive (Delivered)
        var jobB2_1 = RepairJob.Intake(
            _tenantId, _branch2Id, "REP-CMP-003", null, "Rep B2 1", null, null,
            "Brand", "Model", "Phone", null, null, null, null, null, "Problem", null);
        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(jobB2_1, RepairStatus.Diagnosing);

        var jobB2_2 = RepairJob.Intake(
            _tenantId, _branch2Id, "REP-CMP-004", null, "Rep B2 2", null, null,
            "Brand", "Model", "Phone", null, null, null, null, null, "Problem", null);
        typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(jobB2_2, RepairStatus.Delivered);
        db.RepairJobs.AddRange(jobB2_1, jobB2_2);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM repair_job_status_history WHERE repair_job_id IN (SELECT id FROM repair_jobs WHERE tenant_id = @tId); " +
            "DELETE FROM repair_job_parts WHERE tenant_id = @tId; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @tId; " +
            "DELETE FROM customer_payments WHERE tenant_id = @tId; " +
            "DELETE FROM sale_items WHERE tenant_id = @tId; " +
            "DELETE FROM sales WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM user_role_assignments WHERE user_id IN (@uAll, @uB1); " +
            "DELETE FROM user_branches WHERE user_id IN (@uAll, @uB1); " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("uAll", _allBranchesUserId);
        cmd.Parameters.AddWithValue("uB1", _branch1UserId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(bool allBranches, Guid[] branchIds, Guid userId)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches: allBranches, branchIds: branchIds, Array.Empty<string>(), tokenVersion: 1);
        return token;
    }

    private async Task<BranchComparisonDto> GetComparisonAsync(string token, Guid branchHeader)
    {
        var url = $"/api/reports/compare?date={_reportDate:yyyy-MM-dd}";
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, branchHeader.ToString());

        var response = await _client.SendAsync(request);
        if (response.StatusCode != HttpStatusCode.OK)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new Exception($"HTTP {(int)response.StatusCode}: {err}");
        }

        var dto = await response.Content.ReadFromJsonAsync<BranchComparisonDto>();
        dto.Should().NotBeNull();
        return dto!;
    }

    [Fact]
    public async Task BranchComparison_MultiBranch_IndividualRowsAreCorrect()
    {
        // (a) seed sales across TWO branches on the same date, confirm each branch's row is correct
        var token = IssueToken(allBranches: true, branchIds: new[] { _branch1Id, _branch2Id }, userId: _allBranchesUserId);
        var report = await GetComparisonAsync(token, _branch1Id);

        report.Branches.Should().HaveCount(2);

        var rowB1 = report.Branches.First(b => b.BranchId == _branch1Id);
        rowB1.BranchName.Should().Be("Downtown Branch");
        rowB1.Revenue.Should().Be(50000m);
        rowB1.InvoiceCount.Should().Be(1);
        rowB1.Profit.Should().Be(20000m);
        rowB1.ActiveRepairs.Should().Be(2);

        var rowB2 = report.Branches.First(b => b.BranchId == _branch2Id);
        rowB2.BranchName.Should().Be("Uptown Branch");
        rowB2.Revenue.Should().Be(100000m);
        rowB2.InvoiceCount.Should().Be(2);
        rowB2.Profit.Should().Be(30000m);
        rowB2.ActiveRepairs.Should().Be(1);
    }

    [Fact]
    public async Task BranchComparison_ConsolidatedRow_ExactlyEqualsSumOfIndividualBranches()
    {
        // (b) the Consolidated row EXACTLY equals the sum of the individual branch rows
        // (Plan's testing-strategy table explicitly calls out "Consolidated report == sum of branch reports"
        // as a required invariant test)
        var token = IssueToken(allBranches: true, branchIds: new[] { _branch1Id, _branch2Id }, userId: _allBranchesUserId);
        var report = await GetComparisonAsync(token, _branch1Id);

        var expectedRevenue = report.Branches.Sum(b => b.Revenue);
        var expectedInvoices = report.Branches.Sum(b => b.InvoiceCount);
        var expectedProfit = report.Branches.Sum(b => b.Profit);
        var expectedRepairs = report.Branches.Sum(b => b.ActiveRepairs);

        report.Consolidated.Revenue.Should().Be(expectedRevenue);
        report.Consolidated.InvoiceCount.Should().Be(expectedInvoices);
        report.Consolidated.Profit.Should().Be(expectedProfit);
        report.Consolidated.ActiveRepairs.Should().Be(expectedRepairs);

        // Exact hand-seeded numbers
        report.Consolidated.Revenue.Should().Be(150000m);
        report.Consolidated.InvoiceCount.Should().Be(3);
        report.Consolidated.Profit.Should().Be(50000m);
        report.Consolidated.ActiveRepairs.Should().Be(3);
    }

    [Fact]
    public async Task BranchComparison_SingleBranchUser_SeesOnlyTheirOwnBranch_ConsolidatedMatchesSingleBranch()
    {
        // (c) a user scoped to only ONE branch sees only that branch's row
        // (and a Consolidated row identical to their one visible branch)
        var token = IssueToken(allBranches: false, branchIds: new[] { _branch1Id }, userId: _branch1UserId);
        var report = await GetComparisonAsync(token, _branch1Id);

        report.Branches.Should().HaveCount(1);
        report.Branches[0].BranchId.Should().Be(_branch1Id);
        report.Branches[0].Revenue.Should().Be(50000m);
        report.Branches[0].InvoiceCount.Should().Be(1);
        report.Branches[0].Profit.Should().Be(20000m);
        report.Branches[0].ActiveRepairs.Should().Be(2);

        // Consolidated row is identical to their one visible branch
        report.Consolidated.Revenue.Should().Be(report.Branches[0].Revenue);
        report.Consolidated.InvoiceCount.Should().Be(report.Branches[0].InvoiceCount);
        report.Consolidated.Profit.Should().Be(report.Branches[0].Profit);
        report.Consolidated.ActiveRepairs.Should().Be(report.Branches[0].ActiveRepairs);
    }
}
