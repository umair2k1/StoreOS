using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Customers;
using StoreOS.Infrastructure.Inventory;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Sales;
using StoreOS.Infrastructure.Security;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class EstimateCheckoutTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Guid _phoneProductId;
    private Guid _accessoryProductId;
    private Guid _customerId;
    private const string PhoneImei = "860888888888888";

    public EstimateCheckoutTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Estimate Tenant", subdomain: "est-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "ET1", "Estimate Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // 1. Phone with 1 InStock unit and 1 IMEI
        var phone = Product.Create(_tenantId, "Flagship Phone", ProductCategory.NewPhone, 80000m, 60000m, 18m, "85171300");
        db.Products.Add(phone);
        _phoneProductId = phone.Id;

        var phoneInv = BranchInventory.Create(_tenantId, _branchId, _phoneProductId);
        phoneInv.IncreaseStock(1, 60000m);
        db.BranchInventories.Add(phoneInv);

        var imei = InventoryImei.Create(
            _tenantId, _branchId, _phoneProductId, null,
            PhoneImei, DateOnly.FromDateTime(DateTime.UtcNow), 60000m);
        db.InventoryImeis.Add(imei);

        // 2. Accessory with 10 InStock units
        var accessory = Product.Create(_tenantId, "Wireless Mouse", ProductCategory.Accessory, 1500m, 600m, 18m, "84716070");
        db.Products.Add(accessory);
        _accessoryProductId = accessory.Id;

        var accInv = BranchInventory.Create(_tenantId, _branchId, _accessoryProductId);
        accInv.IncreaseStock(10, 600m);
        db.BranchInventories.Add(accInv);

        // 3. Customer with a tight credit limit of 2000 INR
        var customer = Customer.Create(_tenantId, "Estimate Customer", "9111122222", null);
        _customerId = customer.Id;
        customer.SetCreditLimit(2000m);
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
            DELETE FROM inventory_imeis WHERE tenant_id = @t;
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
    public async Task Estimate_PhysicalStockAndImei_StockAndImeiRemainUntouched()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // Estimate referencing both accessory and serialized phone
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_accessoryProductId, 3, null, false, false, null),
                    new(_phoneProductId, 1, PhoneImei, false, false, null)
                },
                CustomerId: null,
                WalkInCustomerName: "Estimate Prospect",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.Estimate,
                Payments: new List<CheckoutPayment>(),
                Finance: null,
                Description: "Quotation only"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            result.Should().NotBeNull();
            // Estimate formatted with /E/
            result.InvoiceNumber.Should().Contain("/E/");

            // (a) Physical stock quantity UNCHANGED (accessory remains at 10)
            var accStock = await db.BranchInventories.IgnoreQueryFilters()
                .FirstAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _accessoryProductId);
            accStock.StockQty.Should().Be(10);

            // Zero inventory movements created for this sale
            var movementsCount = await db.InventoryMovements.IgnoreQueryFilters()
                .CountAsync(m => m.TenantId == _tenantId && m.RefId == result.SaleId.ToString());
            movementsCount.Should().Be(0);

            // (b) IMEI remains InStock (not marked Sold)
            var imeiRow = await db.InventoryImeis.IgnoreQueryFilters()
                .FirstAsync(i => i.TenantId == _tenantId && i.Imei == PhoneImei);
            imeiRow.Status.Should().Be(ImeiStatus.InStock);
            imeiRow.SaleId.Should().BeNull();
            imeiRow.SaleItemId.Should().BeNull();
        }
    }

    [Fact]
    public async Task Estimate_ForCustomer_NeverPostsToCustomerLedgerAndDoesNotUpdateTotalPurchases()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_accessoryProductId, 2, null, false, false, null) // 3000 INR
                },
                CustomerId: _customerId,
                WalkInCustomerName: null,
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.Estimate,
                Payments: new List<CheckoutPayment>(),
                Finance: null,
                Description: "Customer quote"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            // (c) Zero new customer_ledger_entries rows
            var ledgerRows = await db.CustomerLedgerEntries.IgnoreQueryFilters()
                .CountAsync(cle => cle.TenantId == _tenantId && cle.CustomerId == _customerId && cle.SaleId == result.SaleId);
            ledgerRows.Should().Be(0);

            // customers.total_purchases unchanged (0)
            var customer = await db.Customers.IgnoreQueryFilters()
                .FirstAsync(c => c.TenantId == _tenantId && c.Id == _customerId);
            customer.TotalPurchases.Should().Be(0m);
        }
    }

    [Fact]
    public async Task Estimate_ExceedingCustomerCreditLimit_SucceedsWithoutCreditCheckBlock()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // Customer has limit 2000 INR. We generate an estimate for 80000 INR phone.
            // If this were a TaxInvoice, CreditLimitChecker would reject with credit_limit_exceeded.
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_phoneProductId, 1, PhoneImei, false, false, null) // 80000 INR
                },
                CustomerId: _customerId,
                WalkInCustomerName: null,
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.Estimate,
                Payments: new List<CheckoutPayment>(),
                Finance: null,
                Description: "Over-limit quotation"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var act = () => handler.Handle(command, CancellationToken.None);
            await act.Should().NotThrowAsync();
        }
    }

    [Fact]
    public async Task Estimate_UsesSeparateSequenceSeries_DoesNotConsumeTaxInvoiceSequence()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // 1. Create an Estimate
            var estCommand = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(_accessoryProductId, 1, null, false, false, null) },
                CustomerId: null,
                WalkInCustomerName: "Estimate First",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.Estimate,
                Payments: new List<CheckoutPayment>(),
                Finance: null,
                Description: "Est 1"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var estResult = await handler.Handle(estCommand, CancellationToken.None);
            estResult.InvoiceNumber.Should().Contain("/E/");

            // 2. Create a Tax Invoice
            var taxCommand = new CheckoutCommand(
                Lines: new List<CheckoutCartLine> { new(_accessoryProductId, 1, null, false, false, null) },
                CustomerId: null,
                WalkInCustomerName: "Tax Second",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 1500m) },
                Finance: null,
                Description: "Tax 1"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var taxResult = await handler.Handle(taxCommand, CancellationToken.None);

            // The Tax Invoice must start from sequence 1 (not sequence 2)
            // e.g. "ET1/26-27/000001"
            taxResult.InvoiceNumber.Should().EndWith("/000001");
            taxResult.InvoiceNumber.Should().NotContain("/E/");

            // Confirm queryable distinction for reports (P-801 filter pattern)
            var taxSalesSum = await db.Sales.IgnoreQueryFilters()
                .Where(s => s.TenantId == _tenantId && s.InvoiceType == InvoiceType.TaxInvoice && s.Status == SaleStatus.Active)
                .SumAsync(s => s.GrandTotal);

            var estimateSalesSum = await db.Sales.IgnoreQueryFilters()
                .Where(s => s.TenantId == _tenantId && s.InvoiceType == InvoiceType.Estimate && s.Status == SaleStatus.Active)
                .SumAsync(s => s.GrandTotal);

            taxSalesSum.Should().Be(1500m);
            estimateSalesSum.Should().Be(1500m);
        }
    }
}
