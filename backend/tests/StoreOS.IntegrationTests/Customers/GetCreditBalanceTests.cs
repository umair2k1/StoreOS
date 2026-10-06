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
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Customers;

[Collection("postgres")]
public class GetCreditBalanceTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private readonly Guid _customerUnlimitedId = Guid.NewGuid();
    private readonly Guid _customerUnderLimitId = Guid.NewGuid();
    private readonly Guid _customerOverLimitId = Guid.NewGuid();
    private readonly Guid _customerCrossBranchId = Guid.NewGuid();

    private readonly Guid _saleUnlimitedId = Guid.NewGuid();
    private readonly Guid _saleUnderLimitId = Guid.NewGuid();
    private readonly Guid _saleOverLimitId = Guid.NewGuid();
    private readonly Guid _saleCrossBranch1Id = Guid.NewGuid();
    private readonly Guid _saleCrossBranch2Id = Guid.NewGuid();

    private string _token = default!;

    public GetCreditBalanceTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Credit Balance Test Tenant", subdomain: "cb-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "CBA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "CBB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        var role = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);
        db.RolePermissions.Add(RolePermission.Create(_roleId, "pos:checkout"));

        var user = User.Create(_tenantId, $"user@{_tenantId:N}.test", passwordHash, "POS User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        db.UserBranches.Add(UserBranch.Create(_userId, _branchAId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_userId, _roleId, _branchAId));

        // Customer (a): No credit limit, outstanding balance
        var cUnlimited = Customer.Create(_tenantId, "Unlimited Customer", "9900000001", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(cUnlimited, _customerUnlimitedId);
        db.Customers.Add(cUnlimited);

        var saleUnlimited = Sale.CreateTaxInvoice(_tenantId, _branchAId, "INV-UNLIM-1", DateTimeOffset.UtcNow,
            _customerUnlimitedId, "Unlimited Customer", null, 4000m, 0m, 0m, 4000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleUnlimited, _saleUnlimitedId);
        db.Sales.Add(saleUnlimited);

        // Customer (b): Limit = 10,000, balance under limit (3,000)
        var cUnderLimit = Customer.Create(_tenantId, "Under Limit Customer", "9900000002", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(cUnderLimit, _customerUnderLimitId);
        cUnderLimit.SetCreditLimit(10000m);
        db.Customers.Add(cUnderLimit);

        var saleUnder = Sale.CreateTaxInvoice(_tenantId, _branchAId, "INV-UNDER-1", DateTimeOffset.UtcNow,
            _customerUnderLimitId, "Under Limit Customer", null, 3000m, 0m, 0m, 3000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleUnder, _saleUnderLimitId);
        db.Sales.Add(saleUnder);

        // Customer (c): Over limit (e.g., debt 5,000, limit lowered to 2,000)
        var cOverLimit = Customer.Create(_tenantId, "Over Limit Customer", "9900000003", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(cOverLimit, _customerOverLimitId);
        cOverLimit.SetCreditLimit(2000m);
        db.Customers.Add(cOverLimit);

        var saleOver = Sale.CreateTaxInvoice(_tenantId, _branchAId, "INV-OVER-1", DateTimeOffset.UtcNow,
            _customerOverLimitId, "Over Limit Customer", null, 5000m, 0m, 0m, 5000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleOver, _saleOverLimitId);
        db.Sales.Add(saleOver);

        // Customer (d): Cross-branch customer (debt across Branch A and Branch B)
        var cCrossBranch = Customer.Create(_tenantId, "Cross Branch Customer", "9900000004", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(cCrossBranch, _customerCrossBranchId);
        cCrossBranch.SetCreditLimit(10000m);
        db.Customers.Add(cCrossBranch);

        var saleCross1 = Sale.CreateTaxInvoice(_tenantId, _branchAId, "INV-CROSS-1", DateTimeOffset.UtcNow,
            _customerCrossBranchId, "Cross Branch Customer", null, 3000m, 0m, 0m, 3000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleCross1, _saleCrossBranch1Id);
        db.Sales.Add(saleCross1);

        var saleCross2 = Sale.CreateTaxInvoice(_tenantId, _branchBId, "INV-CROSS-2", DateTimeOffset.UtcNow,
            _customerCrossBranchId, "Cross Branch Customer", null, 2000m, 0m, 0m, 2000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(saleCross2, _saleCrossBranch2Id);
        db.Sales.Add(saleCross2);

        // Ledger entries:
        // cUnlimited: 4000 debit
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerUnlimitedId, _saleUnlimitedId, CustomerLedgerEntryType.Invoice, 4000m,
            _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-UNLIM-1"));

        // cUnderLimit: 3000 debit
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerUnderLimitId, _saleUnderLimitId, CustomerLedgerEntryType.Invoice, 3000m,
            _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-UNDER-1"));

        // cOverLimit: 5000 debit
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerOverLimitId, _saleOverLimitId, CustomerLedgerEntryType.Invoice, 5000m,
            _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-OVER-1"));

        // cCrossBranch: 3000 debit in Branch A, 2000 debit in Branch B
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerCrossBranchId, _saleCrossBranch1Id, CustomerLedgerEntryType.Invoice, 3000m,
            _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-CROSS-1"));
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerCrossBranchId, _saleCrossBranch2Id, CustomerLedgerEntryType.Invoice, 2000m,
            _branchBId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-CROSS-2"));

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: false, new[] { _branchAId }, new[] { "pos:checkout" }, tokenVersion: 1);
        _token = token;
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
        cmd.Parameters.AddWithValue("uid", _userId);
        cmd.Parameters.AddWithValue("rid", _roleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task CustomerWithNoCreditLimit_ReturnsNullLimitsAndNotOverLimit()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchAId.ToString());

        var response = await _client.GetAsync($"/api/customers/{_customerUnlimitedId}/credit-balance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<CreditBalanceDto>();
        dto.Should().NotBeNull();
        dto!.CustomerId.Should().Be(_customerUnlimitedId);
        dto.ConsolidatedBalance.Should().Be(4000m);
        dto.CreditLimit.Should().BeNull();
        dto.AvailableCredit.Should().BeNull();
        dto.IsOverLimit.Should().BeFalse();
    }

    [Fact]
    public async Task CustomerWithLimit_UnderLimit_ReturnsCorrectAvailableCredit()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchAId.ToString());

        var response = await _client.GetAsync($"/api/customers/{_customerUnderLimitId}/credit-balance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<CreditBalanceDto>();
        dto.Should().NotBeNull();
        dto!.CustomerId.Should().Be(_customerUnderLimitId);
        dto.ConsolidatedBalance.Should().Be(3000m);
        dto.CreditLimit.Should().Be(10000m);
        dto.AvailableCredit.Should().Be(7000m);
        dto.IsOverLimit.Should().BeFalse();
    }

    [Fact]
    public async Task CustomerOverLimit_ReturnsIsOverLimitTrue()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchAId.ToString());

        var response = await _client.GetAsync($"/api/customers/{_customerOverLimitId}/credit-balance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<CreditBalanceDto>();
        dto.Should().NotBeNull();
        dto!.CustomerId.Should().Be(_customerOverLimitId);
        dto.ConsolidatedBalance.Should().Be(5000m);
        dto.CreditLimit.Should().Be(2000m);
        dto.AvailableCredit.Should().Be(-3000m);
        dto.IsOverLimit.Should().BeTrue();
    }

    [Fact]
    public async Task CrossBranchCustomer_ConsolidatesBalanceAcrossAllBranches()
    {
        // Calling from user scoped strictly to Branch A
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _token);
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchAId.ToString());

        var response = await _client.GetAsync($"/api/customers/{_customerCrossBranchId}/credit-balance");
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await response.Content.ReadFromJsonAsync<CreditBalanceDto>();
        dto.Should().NotBeNull();
        dto!.CustomerId.Should().Be(_customerCrossBranchId);
        dto.ConsolidatedBalance.Should().Be(5000m); // 3000 in Branch A + 2000 in Branch B
        dto.CreditLimit.Should().Be(10000m);
        dto.AvailableCredit.Should().Be(5000m);
        dto.IsOverLimit.Should().BeFalse();
    }
}
