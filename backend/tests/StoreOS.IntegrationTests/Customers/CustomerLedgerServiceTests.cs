using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Customers;
using StoreOS.Infrastructure.Persistence;
using StoreOS.IntegrationTests;

namespace StoreOS.IntegrationTests.Customers;

[Collection("postgres")]
public class CustomerLedgerServiceTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _customerId = Guid.NewGuid();
    private readonly Guid _saleId = Guid.NewGuid();

    public CustomerLedgerServiceTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Ledger Svc Tenant", subdomain: "cls-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);
        var branchA = Branch.Create(_tenantId, "LA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "LB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        var customer = Customer.Create(_tenantId, "Suresh Raina", "9876500000", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(customer, _customerId);
        db.Customers.Add(customer);

        var sale = Sale.CreateTaxInvoice(
            _tenantId, _branchAId, "INV-LEDGER-001", DateTimeOffset.UtcNow,
            _customerId, "Suresh Raina", null,
            5000m, 0m, 0m, 5000m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(sale, _saleId);
        db.Sales.Add(sale);

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
            DELETE FROM sales WHERE tenant_id = @t;
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

    private async Task<(StoreOsDbContext db, CustomerLedgerService service)> CreateContextAsync(Guid branchId)
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
        var service = new CustomerLedgerService(db, scope);
        return (db, service);
    }

    [Fact]
    public async Task RecordInvoiceDebit_FollowedBySaveChangesAsync_ProducesDebitRowWithRightAmountAndType()
    {
        var (db, service) = await CreateContextAsync(_branchAId);
        await using (db)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            await service.RecordInvoiceDebitAsync(_customerId, _saleId, 5000m, _branchAId, date, CancellationToken.None);
            await db.SaveChangesAsync();

            var entry = await db.CustomerLedgerEntries
                .FirstOrDefaultAsync(e => e.CustomerId == _customerId && e.EntryType == CustomerLedgerEntryType.Invoice);

            entry.Should().NotBeNull();
            entry!.Debit.Should().Be(5000m);
            entry.Credit.Should().Be(0m);
            entry.BranchId.Should().Be(_branchAId);
            entry.SaleId.Should().Be(_saleId);
            entry.EntryDate.Should().Be(date);
        }
    }

    [Fact]
    public async Task GetConsolidatedBalance_AfterOneDebitAndZeroPayments_ReturnsExactDebitAmount()
    {
        var (db, service) = await CreateContextAsync(_branchAId);
        await using (db)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            await service.RecordInvoiceDebitAsync(_customerId, _saleId, 5000m, _branchAId, date, CancellationToken.None);
            await db.SaveChangesAsync();

            var balance = await service.GetConsolidatedBalanceAsync(_customerId, CancellationToken.None);
            balance.Should().Be(5000m);
        }
    }

    [Fact]
    public async Task GetConsolidatedBalance_AfterDebitAndPaymentCredit_ReturnsNetBalance()
    {
        var (db, service) = await CreateContextAsync(_branchAId);
        await using (db)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            await service.RecordInvoiceDebitAsync(_customerId, _saleId, 5000m, _branchAId, date, CancellationToken.None);
            await service.RecordPaymentCreditAsync(_customerId, _saleId, 2000m, _branchAId, date, CancellationToken.None);
            await db.SaveChangesAsync();

            var balance = await service.GetConsolidatedBalanceAsync(_customerId, CancellationToken.None);
            balance.Should().Be(3000m);
        }
    }

    [Fact]
    public async Task GetConsolidatedBalance_AfterVoidReversalEqualDebit_ReturnsZero_NeverNegative()
    {
        var (db, service) = await CreateContextAsync(_branchAId);
        await using (db)
        {
            var date = DateOnly.FromDateTime(DateTime.UtcNow);
            await service.RecordInvoiceDebitAsync(_customerId, _saleId, 5000m, _branchAId, date, CancellationToken.None);
            await service.RecordVoidReversalAsync(_customerId, _saleId, 5000m, _branchAId, date, CancellationToken.None);
            await db.SaveChangesAsync();

            var balance = await service.GetConsolidatedBalanceAsync(_customerId, CancellationToken.None);
            balance.Should().Be(0m);

            // Also test over-credit guard (Math.Max(0m, ...))
            await service.RecordRefundCreditAsync(_customerId, _saleId, 1000m, _branchAId, date, CancellationToken.None);
            await db.SaveChangesAsync();

            var protectedBalance = await service.GetConsolidatedBalanceAsync(_customerId, CancellationToken.None);
            protectedBalance.Should().Be(0m);
        }
    }

    [Fact]
    public async Task GetConsolidatedBalance_CrossBranchConsolidation_SumsDebitsFromAllBranchesRegardlessOfCallingUserBranch()
    {
        var date = DateOnly.FromDateTime(DateTime.UtcNow);

        // User scoped to Branch A records a 3000 debit for Branch A
        var (dbA, serviceA) = await CreateContextAsync(_branchAId);
        await using (dbA)
        {
            await serviceA.RecordInvoiceDebitAsync(_customerId, _saleId, 3000m, _branchAId, date, CancellationToken.None);
            await dbA.SaveChangesAsync();
            await dbA.Database.CommitTransactionAsync();
        }

        // User scoped to Branch B records a 2000 debit for Branch B
        var (dbB, serviceB) = await CreateContextAsync(_branchBId);
        await using (dbB)
        {
            await serviceB.RecordInvoiceDebitAsync(_customerId, _saleId, 2000m, _branchBId, date, CancellationToken.None);
            await dbB.SaveChangesAsync();
            await dbB.Database.CommitTransactionAsync();
        }

        // Now, a user strictly scoped to Branch A calls GetConsolidatedBalanceAsync:
        var (dbVerifyA, serviceVerifyA) = await CreateContextAsync(_branchAId);
        await using (dbVerifyA)
        {
            var balanceSeenByBranchAUser = await serviceVerifyA.GetConsolidatedBalanceAsync(_customerId, CancellationToken.None);
            balanceSeenByBranchAUser.Should().Be(5000m); // 3000 (Branch A) + 2000 (Branch B)
        }

        // And a user strictly scoped to Branch B calls GetConsolidatedBalanceAsync:
        var (dbVerifyB, serviceVerifyB) = await CreateContextAsync(_branchBId);
        await using (dbVerifyB)
        {
            var balanceSeenByBranchBUser = await serviceVerifyB.GetConsolidatedBalanceAsync(_customerId, CancellationToken.None);
            balanceSeenByBranchBUser.Should().Be(5000m); // 3000 (Branch A) + 2000 (Branch B)
        }
    }
}
