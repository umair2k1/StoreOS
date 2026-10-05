using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Customers;
using StoreOS.Infrastructure.Persistence;
using StoreOS.IntegrationTests;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class CreditLimitCheckerTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _customerUnlimitedId = Guid.NewGuid();
    private readonly Guid _customerLimitedId = Guid.NewGuid();

    public CreditLimitCheckerTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Credit Tenant", subdomain: "crd-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "CA", "Branch Alpha");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "CB", "Branch Beta");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        // Unlimited customer (CreditLimit = null)
        var custUnlimited = Customer.Create(_tenantId, "Unlimited Customer", "9999900001", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(custUnlimited, _customerUnlimitedId);
        db.Customers.Add(custUnlimited);

        // Limited customer (CreditLimit = 10000)
        var custLimited = Customer.Create(_tenantId, "Limited Customer", "9999900002", null);
        typeof(Customer).GetProperty(nameof(Customer.Id))!.SetValue(custLimited, _customerLimitedId);
        custLimited.SetCreditLimit(10000m);
        db.Customers.Add(custLimited);

        // Seed 8000 balance split across two branches:
        // 5000 debit at Branch A
        var entryA = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerLimitedId, saleId: null,
            CustomerLedgerEntryType.Invoice, 5000m, _branchAId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-A");
        db.CustomerLedgerEntries.Add(entryA);

        // 3000 debit at Branch B
        var entryB = CustomerLedgerEntry.CreateDebit(
            _tenantId, _customerLimitedId, saleId: null,
            CustomerLedgerEntryType.Invoice, 3000m, _branchBId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-B");
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
        public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    private class SpyAuditWriter : IAuditWriter
    {
        public List<(string action, string entityId)> WrittenEntries { get; } = new();

        public Task WriteAsync(Guid? branchId, string action, string entityType, string entityId, object? changes, CancellationToken ct = default)
        {
            WrittenEntries.Add((action, entityId));
            return Task.CompletedTask;
        }
    }

    private async Task<(StoreOsDbContext db, CreditLimitChecker checker, SpyAuditWriter auditWriter)> CreateCheckerAsync(
        Guid branchId, bool hasOverridePermission = false)
    {
        var permissions = hasOverridePermission
            ? new HashSet<string> { "sales:credit:override" }
            : new HashSet<string>();

        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            BranchIds = new HashSet<Guid> { branchId },
            Permissions = permissions
        };

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope))
            .Options;

        var db = new StoreOsDbContext(options, scope);
        await db.Database.BeginTransactionAsync();

        var ledgerService = new CustomerLedgerService(db, scope);
        var auditWriter = new SpyAuditWriter();
        var checker = new CreditLimitChecker(db, ledgerService, scope, auditWriter);

        return (db, checker, auditWriter);
    }

    [Fact]
    public async Task CheckAsync_WhenCreditAmountIsZeroOrNegative_EarlyReturnsWithoutLoadingCustomer()
    {
        var (db, checker, _) = await CreateCheckerAsync(_branchAId);
        await using (db)
        {
            var nonExistentCustomerId = Guid.NewGuid();

            // Zero credit amount -> passes even for nonexistent customer id
            var actZero = () => checker.CheckAsync(nonExistentCustomerId, 0m, CancellationToken.None);
            await actZero.Should().NotThrowAsync();

            // Negative credit amount -> also early returns
            var actNegative = () => checker.CheckAsync(nonExistentCustomerId, -500m, CancellationToken.None);
            await actNegative.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task CheckAsync_WhenCustomerHasNoCreditLimit_AlwaysPassesRegardlessOfBalance()
    {
        var (db, checker, _) = await CreateCheckerAsync(_branchAId);
        await using (db)
        {
            // Unlimited customer requesting massive credit amount
            var act = () => checker.CheckAsync(_customerUnlimitedId, 500000m, CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task CheckAsync_WhenProjectedBalanceWithinCreditLimit_Passes()
    {
        // Limit: 10000, Existing: 8000. New credit: 1500 -> Projected: 9500 <= 10000
        var (db, checker, _) = await CreateCheckerAsync(_branchAId);
        await using (db)
        {
            var act = () => checker.CheckAsync(_customerLimitedId, 1500m, CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task CheckAsync_WhenProjectedBalanceExceedsCreditLimit_WithoutOverridePermission_ThrowsCreditLimitExceeded()
    {
        // Limit: 10000, Existing: 8000. New credit: 3000 -> Projected: 11000 > 10000
        var (db, checker, _) = await CreateCheckerAsync(_branchAId, hasOverridePermission: false);
        await using (db)
        {
            var act = () => checker.CheckAsync(_customerLimitedId, 3000m, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DomainValidationException>();
            ex.Which.Code.Should().Be("credit_limit_exceeded");
            ex.Which.Message.Should().Contain("exceeding their credit limit of 10000.00");
        }
    }

    [Fact]
    public async Task CheckAsync_WhenProjectedBalanceExceedsCreditLimit_WithOverridePermission_PassesAndAudits()
    {
        // Limit: 10000, Existing: 8000. New credit: 3000 -> Projected: 11000 > 10000
        var (db, checker, auditWriter) = await CreateCheckerAsync(_branchAId, hasOverridePermission: true);
        await using (db)
        {
            var act = () => checker.CheckAsync(_customerLimitedId, 3000m, CancellationToken.None);
            await act.Should().NotThrowAsync();

            auditWriter.WrittenEntries.Should().ContainSingle();
            var audit = auditWriter.WrittenEntries[0];
            audit.action.Should().Be("sales.credit_limit_overridden");
            audit.entityId.Should().Be(_customerLimitedId.ToString());
        }
    }

    [Fact]
    public async Task CheckAsync_CrossBranchBalance_ConsolidatesExistingDebitsFromAllBranchesRegardlessOfCurrentCheckoutBranch()
    {
        // Even though existing 8000 balance is split (5000 at Branch A, 3000 at Branch B),
        // checkout occurring at Branch B still sums both to 8000 and blocks a 2500 credit (projected 10500 > 10000).
        var (db, checker, _) = await CreateCheckerAsync(_branchBId, hasOverridePermission: false);
        await using (db)
        {
            var act = () => checker.CheckAsync(_customerLimitedId, 2500m, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DomainValidationException>();
            ex.Which.Code.Should().Be("credit_limit_exceeded");
            ex.Which.Message.Should().Contain("10500.00");
        }
    }
}
