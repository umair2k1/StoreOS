using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
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
public class GiftAndDiscountCheckoutTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    private Guid _paidProductId;
    private Guid _giftProductId;

    public GiftAndDiscountCheckoutTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Gift Discount Tenant", subdomain: "gdt-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "GD1", "Gift Discount Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Paid product: 1000 INR, 18% GST (Taxable ~847.46, CGST ~76.27, SGST ~76.27)
        var paidProduct = Product.Create(_tenantId, "Paid Phone Case", ProductCategory.Accessory, 1000m, 400m, 18m, "39269099");
        db.Products.Add(paidProduct);
        _paidProductId = paidProduct.Id;

        var paidInv = BranchInventory.Create(_tenantId, _branchId, _paidProductId);
        paidInv.IncreaseStock(10, 400m);
        db.BranchInventories.Add(paidInv);

        // Gift product: nominal 500 INR, 18% GST rate
        var giftProduct = Product.Create(_tenantId, "Free Screen Guard", ProductCategory.Accessory, 500m, 100m, 18m, "39269099");
        db.Products.Add(giftProduct);
        _giftProductId = giftProduct.Id;

        var giftInv = BranchInventory.Create(_tenantId, _branchId, _giftProductId);
        giftInv.IncreaseStock(10, 100m);
        db.BranchInventories.Add(giftInv);

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
    public async Task ScenarioA_PaidLineAndGiftLine_GrandTotalReflectsOnlyPaidLineAndBothStocksDecrement()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_paidProductId, 1, null, false, false, null), // 1000 INR
                    new(_giftProductId, 1, null, false, true, null)   // Free nominal 500 INR
                },
                CustomerId: null,
                WalkInCustomerName: "Gift Buyer",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>
                {
                    new(PaymentMode.Cash, 1000m)
                },
                Finance: null,
                Description: "Scenario A"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            result.GrandTotal.Should().Be(1000m);
            result.AmountPaid.Should().Be(1000m);
            result.Balance.Should().Be(0m);

            var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == result.SaleId);
            sale.Should().NotBeNull();
            sale!.GrandTotal.Should().Be(1000m);
            sale.Items.Should().HaveCount(2);

            var paidItem = sale.Items.First(i => i.ProductId == _paidProductId);
            paidItem.PricePerUnit.Should().Be(1000m);
            paidItem.IsGift.Should().BeFalse();
            paidItem.Taxable.Should().Be(847.46m);
            paidItem.Cgst.Should().Be(76.27m);
            paidItem.Sgst.Should().Be(76.27m);

            var giftItem = sale.Items.First(i => i.ProductId == _giftProductId);
            giftItem.PricePerUnit.Should().Be(500m); // Nominal price preserved for display
            giftItem.IsGift.Should().BeTrue();
            giftItem.Taxable.Should().Be(0m);
            giftItem.Cgst.Should().Be(0m);
            giftItem.Sgst.Should().Be(0m);

            // Both stocks decremented from 10 to 9
            var paidStock = await db.BranchInventories.AsNoTracking()
                .FirstAsync(bi => bi.ProductId == _paidProductId && bi.BranchId == _branchId);
            paidStock.StockQty.Should().Be(9);

            var giftStock = await db.BranchInventories.AsNoTracking()
                .FirstAsync(bi => bi.ProductId == _giftProductId && bi.BranchId == _branchId);
            giftStock.StockQty.Should().Be(9);
        }
    }

    [Fact]
    public async Task ScenarioB_InvoiceDiscountOnCartWithPaidAndGiftLines_DiscountComputedAgainstPaidSubtotalOnly()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // Paid line: 1000 INR
            // Gift line: nominal 500 INR
            // Discount: 100 INR
            // Paid subtotal is 1000 (not 1500).
            // GrandTotal = 1000 - 100 = 900 INR.
            // Discount factor is 100 / 1000 = 10% (not 100 / 1500 = 6.67%).
            // Pre-discount tax on 1000: Taxable = 847.46, CGST = 76.27, SGST = 76.27
            // 10% reduction: Taxable = 847.46 * 0.9 = 762.71, CGST = 76.27 * 0.9 = 68.64, SGST = 68.64
            var command = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_paidProductId, 1, null, false, false, null),
                    new(_giftProductId, 1, null, false, true, null)
                },
                CustomerId: null,
                WalkInCustomerName: "Discount Gift Buyer",
                InvoiceDiscount: 100m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>
                {
                    new(PaymentMode.UPI, 900m)
                },
                Finance: null,
                Description: "Scenario B"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(command, CancellationToken.None);

            result.GrandTotal.Should().Be(900m);
            result.AmountPaid.Should().Be(900m);
            result.Balance.Should().Be(0m);

            var sale = await db.Sales.FirstOrDefaultAsync(s => s.Id == result.SaleId);
            sale!.Discount.Should().Be(100m);
            sale.GrandTotal.Should().Be(900m);
            sale.TotalTaxable.Should().Be(762.71m);
            sale.TotalCgst.Should().Be(68.64m);
            sale.TotalSgst.Should().Be(68.64m);
        }
    }

    [Fact]
    public async Task ScenarioC_PureGiveawayOnlyGiftLines_GrandTotalIsZero_AndNonZeroPaymentRejected()
    {
        var (db, handler) = await CreateHandlerAsync();
        await using (db)
        {
            // Pure giveaway: only gift line
            // Attempting to checkout with payment > 0 must fail with payment_exceeds_balance
            var commandWithPayment = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_giftProductId, 1, null, false, true, null)
                },
                CustomerId: null,
                WalkInCustomerName: "Giveaway Receiver",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>
                {
                    new(PaymentMode.Cash, 50m) // Non-zero payment for 0 total
                },
                Finance: null,
                Description: "Scenario C Invalid"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var act = () => handler.Handle(commandWithPayment, CancellationToken.None);
            var ex = await act.Should().ThrowAsync<DomainValidationException>();
            ex.Which.Code.Should().Be("payment_exceeds_balance");

            // Successful pure giveaway with 0 payment
            var commandZeroPayment = new CheckoutCommand(
                Lines: new List<CheckoutCartLine>
                {
                    new(_giftProductId, 1, null, false, true, null)
                },
                CustomerId: null,
                WalkInCustomerName: "Giveaway Receiver",
                InvoiceDiscount: 0m,
                InvoiceType: InvoiceType.TaxInvoice,
                Payments: new List<CheckoutPayment>(), // No payments
                Finance: null,
                Description: "Scenario C Valid"
            )
            { IdempotencyKey = Guid.NewGuid().ToString() };

            var result = await handler.Handle(commandZeroPayment, CancellationToken.None);
            result.GrandTotal.Should().Be(0m);
            result.AmountPaid.Should().Be(0m);
            result.Balance.Should().Be(0m);

            var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == result.SaleId);
            sale!.GrandTotal.Should().Be(0m);
            sale.AmountPaid.Should().Be(0m);
            sale.Items.Should().ContainSingle();
            sale.Items[0].IsGift.Should().BeTrue();
            sale.Items[0].PricePerUnit.Should().Be(500m);
        }
    }
}
