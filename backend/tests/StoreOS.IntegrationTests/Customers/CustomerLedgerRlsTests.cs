using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.IntegrationTests;

namespace StoreOS.IntegrationTests.Customers;

[Collection("postgres")]
public class CustomerLedgerRlsTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();

    public CustomerLedgerRlsTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("CustLedger Tenant", subdomain: "cl-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "CLA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "CLB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        var customer = Customer.Create(_tenantId, "Rohan Verma", "9123456780", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        db.Customers.Add(customer);

        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        // Entry 1 stamped with Branch A: Debit 1000
        var entryA = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, saleId: null,
            CustomerLedgerEntryType.Invoice, 1000m, _branchAId, today, "INV-A1");
        db.CustomerLedgerEntries.Add(entryA);

        // Entry 2 stamped with Branch B: Debit 2000
        var entryB = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerId, saleId: null,
            CustomerLedgerEntryType.Invoice, 2000m, _branchBId, today, "INV-B1");
        db.CustomerLedgerEntries.Add(entryB);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM customer_ledger_entries WHERE tenant_id = @t;
            DELETE FROM customers WHERE tenant_id = @t;
            DELETE FROM branches WHERE tenant_id = @t;
            DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; } = Guid.NewGuid();
        public IReadOnlySet<Guid> BranchIds { get; init; } = new HashSet<Guid>();
        public bool AllBranches => false;
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    private async Task<StoreOsDbContext> CreateAppDbContextAsync(Guid branchId)
    {
        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            BranchIds = new HashSet<Guid> { branchId }
        };
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope))
            .Options;
        var db = new StoreOsDbContext(options, scope);
        await db.Database.BeginTransactionAsync();
        return db;
    }

    [Fact]
    public async Task StandardFilteredQuery_ForBranchAScopedUser_OnlyReturnsBranchAEntries()
    {
        await using var db = await CreateAppDbContextAsync(_branchAId);

        // Standard EF query without IgnoreQueryFilters
        var entries = await db.CustomerLedgerEntries
            .Where(e => e.CustomerId == _customerId)
            .ToListAsync();

        entries.Should().HaveCount(1);
        entries[0].BranchId.Should().Be(_branchAId);
        entries[0].Debit.Should().Be(1000m);
    }

    [Fact]
    public async Task TenantGlobalBalanceAggregation_UsingIgnoreQueryFilters_SumsAllBranchesForCustomer()
    {
        await using var db = await CreateAppDbContextAsync(_branchAId);

        // Documented deliberate exception for tenant-global customer balance aggregation:
        // uses IgnoreQueryFilters() filtered strictly by tenant_id and customer_id
        var totalDebit = await db.CustomerLedgerEntries
            .IgnoreQueryFilters()
            .Where(e => e.TenantId == _tenantId && e.CustomerId == _customerId)
            .SumAsync(e => e.Debit);

        totalDebit.Should().Be(3000m); // 1000 from Branch A + 2000 from Branch B
    }
}
