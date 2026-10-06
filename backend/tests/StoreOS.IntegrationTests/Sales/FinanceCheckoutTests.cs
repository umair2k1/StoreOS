using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Finance;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Customers;
using StoreOS.Infrastructure.Inventory;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Sales;
using StoreOS.Infrastructure.Security;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class FinanceCheckoutTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Guid _phoneProductId;
    private Guid _customerId;

    public FinanceCheckoutTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Finance Tenant", subdomain: "fin-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "FN1", "Finance Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Product priced at 14000
        var phone = Product.Create(_tenantId, "Budget Smartphone", ProductCategory.NewPhone, 14000m, 10000m, 18m, "85171300");
        db.Products.Add(phone);
        _phoneProductId = phone.Id;

        var phoneInv = BranchInventory.Create(_tenantId, _branchId, _phoneProductId);
        phoneInv.IncreaseStock(10, 10000m);
        db.BranchInventories.Add(phoneInv);

        // Customer with credit limit 50000
        var customer = Customer.Create(_tenantId, "Finance Customer", "9777788888", null);
        _customerId = customer.Id;
        customer.SetCreditLimit(50000m);
        db.Customers.Add(customer);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM customer_payments WHERE tenant_id = @t;
            DELETE FROM customer_ledger_entries WHERE tenant_id = @t;
            DELETE FROM sale_items WHERE tenant_id = @t;
            DELETE FROM sales WHERE tenant_id = @t;
            DELETE FROM customers WHERE tenant_id = @t;
            DELETE FROM idempotency_keys WHERE tenant_id = @t;
            DELETE FROM inventory_movements WHERE tenant_id = @t;
            DELETE FROM branch_inventory WHERE tenant_id = @t;
            DELETE FROM products WHERE tenant_id = @t;
            DELETE FROM invoice_sequences WHERE tenant_id = @t;
            DELETE FROM branches WHERE tenant_id = @t;
            DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        await cmd.ExecuteNonQueryAsync();
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; }
        public IReadOnlySet<Guid> BranchIds { get; init; } = new HashSet<Guid>();
        public bool AllBranches => false;
        public IReadOnlySet<string> Permissions { get; init; } = new HashSet<string> { "pos:checkout", "inventory:manage" };
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => Permissions.Contains(permissionCode);
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    private class NoOpAuditWriter : IAuditWriter
    {
        public Task WriteAsync(Guid? branchId, string action, string entityType, string entityId, object? changes, CancellationToken ct = default)
            => Task.CompletedTask;
    }

    private async Task<(StoreOsDbContext db, CheckoutHandler handler)> CreateHandlerAsync()
    {
        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            UserId = _userId,
            BranchIds = new HashSet<Guid> { _branchId }
        };

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope))
            .Options;

        var db = new StoreOsDbContext(options, scope);
        await db.Database.BeginTransactionAsync();

        var branchContext = new MutableBranchContext { CurrentBranchId = _branchId };
        var stockService = new InventoryStockService(db, scope);
        var pricingResolver = new PricingResolver(db, scope, stockService);
        var ledgerService = new CustomerLedgerService(db, scope);
        var audit = new NoOpAuditWriter();
        var creditChecker = new CreditLimitChecker(db, ledgerService, scope, audit);
        var allocator = new InvoiceSequenceAllocator(db);

        var handler = new CheckoutHandler(
            db, scope, branchContext, pricingResolver, stockService,
            ledgerService, creditChecker, allocator, audit);

        return (db, handler);
    }

    [Fact]
    public async Task ScenarioA_ZeroInterestFinance_CalculatesExactEmiAndRecordsFinanceDetails()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var finance = new CheckoutFinanceDetails(
                FinanceProvider: "Bajaj",
                FinancerName: "Bajaj Finserv",
                DownPayment: 2000m,
                TenureMonths: 12,
                AnnualInterestRate: 0m
            );

            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(_phoneProductId, 1, null, false, false, null) },
                CustomerId: null,
                WalkInCustomerName: "Bajaj Customer",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(), // Down payment picked up from finance
                Finance: finance,
                Description: "Zero interest test"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            result.GrandTotal.Should().Be(14000m);
            result.AmountPaid.Should().Be(2000m);
            result.Balance.Should().Be(12000m);

            var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == result.SaleId);
            sale.Should().NotBeNull();
            sale!.PaymentMode.Should().Be(PaymentMode.Finance);
            sale.FinanceProvider.Should().Be("Bajaj");
            sale.FinancerName.Should().Be("Bajaj Finserv");
            sale.DownPayment.Should().Be(2000m);
            sale.EmiDetailsJson.Should().NotBeNullOrWhiteSpace();

            var composed = JsonSerializer.Deserialize<ComposedFinance>(sale.EmiDetailsJson!);
            composed.Should().NotBeNull();
            composed!.Principal.Should().Be(12000m);
            composed.MonthlyEmi.Should().Be(1000.00m);
            composed.TotalPayable.Should().Be(14000.00m);
            composed.AnnualInterestRate.Should().Be(0m);
            composed.TenureMonths.Should().Be(12);

            var payment = await db.CustomerPayments.FirstOrDefaultAsync(p => p.SaleId == sale.Id);
            payment.Should().NotBeNull();
            payment!.Amount.Should().Be(2000m);
        }
    }

    [Fact]
    public async Task ScenarioB_PositiveInterestFinance_EmiMatchesFinanceEngineCalculation()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var finance = new CheckoutFinanceDetails(
                FinanceProvider: "HDB",
                FinancerName: "HDB Financial",
                DownPayment: 2000m,
                TenureMonths: 12,
                AnnualInterestRate: 12m
            );

            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(_phoneProductId, 1, null, false, false, null) },
                CustomerId: null,
                WalkInCustomerName: "HDB Customer",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(),
                Finance: finance,
                Description: "12% interest test"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == result.SaleId);
            var composed = JsonSerializer.Deserialize<ComposedFinance>(sale!.EmiDetailsJson!);

            // Verify with FinanceEngine directly
            var expectedPrincipal = FinanceEngine.CalculatePrincipal(14000m, 2000m);
            var expectedEmi = FinanceEngine.CalculateEmi(expectedPrincipal, 12m, 12);
            composed!.Principal.Should().Be(expectedPrincipal);
            composed.MonthlyEmi.Should().Be(expectedEmi);
            composed.MonthlyEmi.Should().BeGreaterThan(1000m);
        }
    }

    [Fact]
    public async Task ScenarioC_FinancedSaleForRegisteredCustomer_NeverPostsToCustomerLedger()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var finance = new CheckoutFinanceDetails(
                FinanceProvider: "Bajaj",
                FinancerName: "Bajaj Finserv",
                DownPayment: 2000m,
                TenureMonths: 6,
                AnnualInterestRate: 0m
            );

            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(_phoneProductId, 1, null, false, false, null) },
                CustomerId: _customerId,
                WalkInCustomerName: null,
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(),
                Finance: finance,
                Description: "Customer ledger bypass test"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            // Assert: strictly ZERO entries posted to customer_ledger_entries (neither debit nor credit)
            var ledgerCount = await db.CustomerLedgerEntries
                .CountAsync(cle => cle.CustomerId == _customerId && cle.SaleId == result.SaleId);
            ledgerCount.Should().Be(0);

            // Still records customer payment for the down payment
            var payment = await db.CustomerPayments.FirstOrDefaultAsync(cp => cp.SaleId == result.SaleId);
            payment.Should().NotBeNull();
            payment!.Amount.Should().Be(2000m);
        }
    }

    [Fact]
    public async Task ScenarioD_DownPaymentExceedsGrandTotal_ThrowsFinanceDownPaymentExceedsTotal()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var finance = new CheckoutFinanceDetails(
                FinanceProvider: "Bajaj",
                FinancerName: null,
                DownPayment: 20000m, // Exceeds GrandTotal (14000)
                TenureMonths: 12,
                AnnualInterestRate: 0m
            );

            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(_phoneProductId, 1, null, false, false, null) },
                CustomerId: null,
                WalkInCustomerName: "Overpayer",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(),
                Finance: finance,
                Description: "Excess down payment test"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var act = () => handler.Handle(command, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DomainValidationException>();
            ex.Which.Code.Should().Be("finance_down_payment_exceeds_total");
        }
    }
}
