using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Customers;
using StoreOS.Infrastructure.Inventory;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Sales;
using StoreOS.Infrastructure.Security;
using StoreOS.IntegrationTests;

namespace StoreOS.IntegrationTests.Sales;

[Collection("postgres")]
public class CheckoutHandlerTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Guid _phoneProductId;
    private Guid _accessoryProductId;
    private Guid _customerId;
    private const string PhoneImei = "860555555555555";

    public CheckoutHandlerTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Checkout Tenant", subdomain: "chk-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "CK1", "Checkout Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // 1. Phone product with 1 stock and 1 IMEI
        var phone = Product.Create(_tenantId, "iPhone 15 Pro", ProductCategory.NewPhone, 100000m, 80000m, 18m, "85171300");
        db.Products.Add(phone);
        _phoneProductId = phone.Id;

        var phoneInv = BranchInventory.Create(_tenantId, _branchId, _phoneProductId);
        phoneInv.IncreaseStock(1, 80000m);
        db.BranchInventories.Add(phoneInv);

        var imei = InventoryImei.Create(
            _tenantId, _branchId, _phoneProductId, purchaseId: null,
            imei: PhoneImei, purchaseDate: DateOnly.FromDateTime(DateTime.UtcNow), purchasePrice: 80000m);
        db.InventoryImeis.Add(imei);

        // 2. Accessory product with 10 stock
        var acc = Product.Create(_tenantId, "Silicone Case", ProductCategory.Accessory, 2000m, 800m, 18m, "39269099");
        db.Products.Add(acc);
        _accessoryProductId = acc.Id;

        var accInv = BranchInventory.Create(_tenantId, _branchId, _accessoryProductId);
        accInv.IncreaseStock(10, 800m);
        db.BranchInventories.Add(accInv);

        // 3. Customer with credit limit 50000
        var customer = Customer.Create(_tenantId, "Vikas Gupta", "9811122233", "29AAAAA1111A1Z1");
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

    private async Task<(StoreOsDbContext db, CheckoutHandler handler)> CreateHandlerAsync(
        bool hasCreditOverride = false)
    {
        var permissions = new HashSet<string> { "pos:checkout", "inventory:manage" };
        if (hasCreditOverride) permissions.Add("sales:credit:override");

        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            UserId = _userId,
            BranchIds = new HashSet<Guid> { _branchId },
            Permissions = permissions
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
    public async Task HappyPath_SingleLineCashSaleWithImei_ShouldSucceedAndThreadSaleItemId()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new CheckoutCartLine(_phoneProductId, 1, PhoneImei, false, false, null)
                },
                CustomerId: null,
                WalkInCustomerName: "Aman",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>
                {
                    new CheckoutPayment(PaymentMode.Cash, 100000m)
                },
                Finance: null,
                Description: "Phone sale test"
            );

            var result = await handler.Handle(command, CancellationToken.None);

            result.Should().NotBeNull();
            result.GrandTotal.Should().Be(100000m);
            result.AmountPaid.Should().Be(100000m);
            result.Balance.Should().Be(0m);
            result.InvoiceNumber.Should().StartWith("CK1/");

            // Verify DB state
            var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == result.SaleId);
            sale.Should().NotBeNull();
            sale!.AmountPaid.Should().Be(100000m);
            sale.Items.Should().HaveCount(1);
            var saleItem = sale.Items[0];
            saleItem.PricePerUnit.Should().Be(100000m);
            saleItem.Imei.Should().Be(PhoneImei);

            // Verify IMEI sold and has non-empty sale_item_id matching the sale item
            var imeiRow = await db.InventoryImeis.FirstOrDefaultAsync(i => i.Imei == PhoneImei);
            imeiRow.Should().NotBeNull();
            imeiRow!.Status.Should().Be(ImeiStatus.Sold);
            imeiRow.SaleId.Should().Be(sale.Id);
            imeiRow.SaleItemId.Should().Be(saleItem.Id);
            imeiRow.SaleItemId.Should().NotBe(Guid.Empty);

            // Verify stock decremented to 0
            var inv = await db.BranchInventories.AsNoTracking().FirstOrDefaultAsync(bi => bi.ProductId == _phoneProductId && bi.BranchId == _branchId);
            inv!.StockQty.Should().Be(0);

            // Verify inventory movements
            var movement = await db.InventoryMovements.FirstOrDefaultAsync(m => m.RefId == sale.Id.ToString());
            movement.Should().NotBeNull();
            movement!.MovementType.Should().Be(MovementType.Sale);
            movement.QtyDelta.Should().Be(-1);
        }
    }

    [Fact]
    public async Task CreditSale_ForCustomer_CreatesLedgerDebitAndUpdatesCustomerTotalPurchases()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new CheckoutCartLine(_accessoryProductId, 2, null, false, false, null) // 2 * 2000 = 4000
                },
                CustomerId: _customerId,
                WalkInCustomerName: null,
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(), // Zero payment = 100% credit
                Finance: null,
                Description: "Credit sale test"
            );

            var result = await handler.Handle(command, CancellationToken.None);

            result.GrandTotal.Should().Be(4000m);
            result.AmountPaid.Should().Be(0m);
            result.Balance.Should().Be(4000m);

            var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == _customerId);
            customer!.TotalPurchases.Should().Be(4000m);

            var ledgerEntry = await db.CustomerLedgerEntries
                .FirstOrDefaultAsync(e => e.CustomerId == _customerId && e.SaleId == result.SaleId);
            ledgerEntry.Should().NotBeNull();
            ledgerEntry!.Debit.Should().Be(4000m);
            ledgerEntry.Credit.Should().Be(0m);
            ledgerEntry.EntryType.Should().Be(CustomerLedgerEntryType.Invoice);
        }
    }

    [Fact]
    public async Task SaleExceedingCreditLimit_WithoutOverride_FailsAndNothingIsPersisted()
    {
        var (db, handler) = await CreateHandlerAsync(hasCreditOverride: false);
        await using (db)
        {
            // Customer credit limit is 50000. We try to bill 30 items at 2000 = 60000 on credit
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new CheckoutCartLine(_accessoryProductId, 30, null, false, false, null)
                },
                CustomerId: _customerId,
                WalkInCustomerName: null,
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(),
                Finance: null,
                Description: "Over limit test"
            );

            var act = () => handler.Handle(command, CancellationToken.None);
            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "credit_limit_exceeded");

            // Verify atomic non-persistence: no sale created
            var salesCount = await db.Sales.CountAsync();
            salesCount.Should().Be(0);
        }
    }

    [Fact]
    public async Task InvoiceDiscount_CorrectlyReducesGrandTotalAndProportionallyShrinksTaxes()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // 1 accessory at 2000. 18% GST built-in: Subtotal = 2000, Taxable = 1694.92, CGST = 152.54, SGST = 152.54
            // Discount 200 -> GrandTotal = 1800
            // Proportional tax after 10% discount: Taxable = 1525.42, CGST = 137.29, SGST = 137.29
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new CheckoutCartLine(_accessoryProductId, 1, null, false, false, null)
                },
                CustomerId: null,
                WalkInCustomerName: "Discount Buyer",
                InvoiceDiscount: 200m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>
                {
                    new CheckoutPayment(PaymentMode.UPI, 1800m)
                },
                Finance: null,
                Description: "Discount test"
            );

            var result = await handler.Handle(command, CancellationToken.None);

            result.GrandTotal.Should().Be(1800m);
            result.AmountPaid.Should().Be(1800m);

            var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == result.SaleId);
            sale!.Discount.Should().Be(200m);
            sale.GrandTotal.Should().Be(1800m);
            sale.TotalTaxable.Should().Be(1525.43m);
            sale.TotalCgst.Should().Be(137.29m);
            sale.TotalSgst.Should().Be(137.29m);
        }
    }

    [Fact]
    public async Task OutOfStockLine_FailsAtomicallyWithNothingPersisted()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // Accessory has stock 10. Request 99 units.
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new CheckoutCartLine(_accessoryProductId, 99, null, false, false, null)
                },
                CustomerId: null,
                WalkInCustomerName: "OOS Buyer",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>
                {
                    new CheckoutPayment(PaymentMode.Cash, 198000m)
                },
                Finance: null,
                Description: "OOS test"
            );

            var act = () => handler.Handle(command, CancellationToken.None);
            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(e => e.Code == "insufficient_stock");

            var salesCount = await db.Sales.CountAsync();
            salesCount.Should().Be(0);

            var stock = await db.BranchInventories.FirstOrDefaultAsync(bi => bi.ProductId == _accessoryProductId && bi.BranchId == _branchId);
            stock!.StockQty.Should().Be(10); // Untouched
        }
    }
}
