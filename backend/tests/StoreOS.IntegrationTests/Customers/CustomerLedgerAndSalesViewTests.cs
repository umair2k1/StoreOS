using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers.GetCreditBalance;
using StoreOS.Application.Customers.GetCustomerLedger;
using StoreOS.Application.Sales.Search;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Customers;

[Collection("postgres")]
public class CustomerLedgerAndSalesViewTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _userBranchAId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _saleBranchAId = Guid.NewGuid();
    private readonly Guid _saleBranchBId = Guid.NewGuid();

    private string _branchAToken = default!;

    public CustomerLedgerAndSalesViewTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Ledger/Sales View Tenant", subdomain: "ls-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "BA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "BB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        var role = Role.Create(_tenantId, "Staff");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(_roleId, "pos:checkout"));

        var userA = User.Create(_tenantId, $"usera@{_tenantId:N}.test", passwordHash, "User Branch A");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userA, _userBranchAId);
        db.Users.Add(userA);
        db.UserBranches.Add(UserBranch.Create(_userBranchAId, _branchAId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userBranchAId, _roleId, _branchAId));

        var customer = Customer.Create(_tenantId, "Sourav Ganguly", "9830012345", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        customer.SetCreditLimit(50000m);
        db.Customers.Add(customer);

        var saleA = Sale.CreateTaxInvoice(_tenantId, _branchAId, "INV-A-101", DateTimeOffset.UtcNow,
            _customerId, "Sourav Ganguly", null, 6000m, 0m, 0m, 6000m, 0m, 1000m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleA, _saleBranchAId);
        db.Sales.Add(saleA);

        var saleB = Sale.CreateTaxInvoice(_tenantId, _branchBId, "INV-B-201", DateTimeOffset.UtcNow,
            _customerId, "Sourav Ganguly", null, 4000m, 0m, 0m, 4000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleB, _saleBranchBId);
        db.Sales.Add(saleB);

        // Ledger entries:
        // Entry in Branch A: debit 6000
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, _saleBranchAId, CustomerLedgerEntryType.Invoice, 6000m,
            _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-A-101"));

        // Entry in Branch A: payment credit 1000
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateCredit(
            _tenantId, _customerId, _saleBranchAId, CustomerLedgerEntryType.Payment, 1000m,
            _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "PAY-A-101"));

        // Entry in Branch B: debit 4000
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, _saleBranchBId, CustomerLedgerEntryType.Invoice, 4000m,
            _branchBId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-B-201"));

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        // User scoped strictly to Branch A
        var (token, _) = jwtService.IssueAccessToken(
            _userBranchAId, _tenantId, allBranches: false, new[] { _branchAId }, new[] { "pos:checkout" }, tokenVersion: 1);
        _branchAToken = token;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM customer_ledger_entries WHERE tenant_id = @tid;
            DELETE FROM sales WHERE tenant_id = @tid;
            DELETE FROM customers WHERE tenant_id = @tid;
            DELETE FROM user_role_assignments WHERE user_id = @uid;
            DELETE FROM user_branches WHERE user_id = @uid;
            DELETE FROM role_permissions WHERE role_id = @rid;
            DELETE FROM roles WHERE tenant_id = @tid;
            DELETE FROM users WHERE tenant_id = @tid;
            DELETE FROM branches WHERE tenant_id = @tid;
            DELETE FROM tenants WHERE id = @tid;
        ";
        cmd.Parameters.AddWithValue("tid", _tenantId);
        cmd.Parameters.AddWithValue("uid", _userBranchAId);
        cmd.Parameters.AddWithValue("rid", _roleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task LedgerView_BranchScopedUser_ShowsOnlyBranchAEntriesWithBranchAttribution_ContrastingWithTenantGlobalBalance()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _branchAToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchAId.ToString());

        // 1. Balance endpoint (P-601): sums ALL branches tenant-globally = (6000 - 1000) + 4000 = 9000
        var balanceResp = await _client.GetAsync($"/api/customers/{_customerId}/credit-balance");
        balanceResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var balanceDto = await balanceResp.Content.ReadFromJsonAsync<CreditBalanceDto>();
        balanceDto.Should().NotBeNull();
        balanceDto!.ConsolidatedBalance.Should().Be(9000m, "credit balance endpoint is tenant-global and consolidates across all branches");

        // 2. Ledger endpoint (P-603): DETAIL view is branch-filtered, so branch A user sees ONLY branch A entries
        var ledgerResp = await _client.GetAsync($"/api/customers/{_customerId}/ledger");
        ledgerResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var ledgerResult = await ledgerResp.Content.ReadFromJsonAsync<PagedResult<CustomerLedgerEntryDto>>();
        ledgerResult.Should().NotBeNull();
        ledgerResult!.TotalCount.Should().Be(2, "branch-A user only sees the 2 entries stamped with branch A, not the Branch B entry");
        ledgerResult.Items.Should().HaveCount(2);

        // Verify branch attribution
        foreach (var entry in ledgerResult.Items)
        {
            entry.BranchId.Should().Be(_branchAId);
            entry.BranchName.Should().Be("Branch Alpha");
        }

        ledgerResult.Items.Should().Contain(e => e.EntryType == CustomerLedgerEntryType.Invoice && e.Debit == 6000m);
        ledgerResult.Items.Should().Contain(e => e.EntryType == CustomerLedgerEntryType.Payment && e.Credit == 1000m);
    }

    [Fact]
    public async Task SalesView_ReturnsCustomerSalesHistory_BranchFiltered()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _branchAToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchAId.ToString());

        var salesResp = await _client.GetAsync($"/api/customers/{_customerId}/sales");
        salesResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var salesResult = await salesResp.Content.ReadFromJsonAsync<PagedResult<SaleSummaryDto>>();
        salesResult.Should().NotBeNull();

        // Branch-scoped user only sees sales in Branch A
        salesResult!.TotalCount.Should().Be(1);
        salesResult.Items.Should().ContainSingle(s => s.InvoiceNumber == "INV-A-101");
        salesResult.Items[0].CustomerName.Should().Be("Sourav Ganguly");
        salesResult.Items[0].GrandTotal.Should().Be(6000m);
    }
}
