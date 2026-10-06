using System.Collections.Concurrent;
using FluentAssertions;
using MediatR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers;
using StoreOS.Application.Inventory;
using StoreOS.Application.Behaviors;
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
using StoreOS.IntegrationTests;

namespace StoreOS.ConcurrencyTests;

[Collection("postgres")]
public class EndToEndCheckoutConcurrencyTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();

    public EndToEndCheckoutConcurrencyTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("E2E Conc Tenant", subdomain: "e2e-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "EE1", "E2E Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

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
        public Guid UserId { get; init; } = Guid.NewGuid();
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

    private IServiceProvider BuildServiceProvider(Guid userId)
    {
        var services = new ServiceCollection();
        services.AddLogging(b => b.AddConsole().SetMinimumLevel(LogLevel.Warning));

        var scope = new TestAccessScope
        {
            TenantId = _tenantId,
            UserId = userId,
            BranchIds = new HashSet<Guid> { _branchId }
        };
        services.AddSingleton<IAccessScope>(scope);

        var branchContext = new MutableBranchContext { CurrentBranchId = _branchId };
        services.AddSingleton<IBranchContext>(branchContext);

        var interceptor = new RlsSessionInterceptor(scope);
        services.AddDbContext<StoreOsDbContext>(options =>
        {
            options.UseNpgsql(_fixture.AppConnectionString);
            options.AddInterceptors(interceptor);
        });

        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<StoreOsDbContext>());
        services.AddScoped<IInventoryStockService, InventoryStockService>();
        services.AddScoped<ICustomerLedgerService, CustomerLedgerService>();
        services.AddScoped<IInvoiceSequenceAllocator, InvoiceSequenceAllocator>();
        services.AddScoped<IAuditWriter, NoOpAuditWriter>();
        services.AddScoped<PricingResolver>();
        services.AddScoped<CreditLimitChecker>();
        services.AddScoped<IdempotencyReplayFlag>();
        services.AddScoped<IIdempotencyStore, IdempotencyStore>();

        services.AddMediatR(cfg =>
        {
            cfg.RegisterServicesFromAssembly(typeof(CheckoutHandler).Assembly);
            cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.ValidationBehavior<,>));
            cfg.AddOpenBehavior(typeof(SerializationRetryBehavior<,>));
            cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.AuthorizationBehavior<,>));
            cfg.AddOpenBehavior(typeof(TransactionBehavior<,>));
            cfg.AddOpenBehavior(typeof(StoreOS.Application.Behaviors.IdempotencyBehavior<,>));
        });

        return services.BuildServiceProvider();
    }

    [Fact]
    public async Task Scenario1_LastUnitRace_RunFiveIterations()
    {
        for (var iteration = 1; iteration <= 5; iteration++)
        {
            var product = Product.Create(_tenantId, $"Last Unit Item It{iteration}", ProductCategory.Accessory, 500m, 200m, 18m, "39269099");
            var options = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(_fixture.OwnerConnectionString)
                .Options;
            await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
            {
                db.Products.Add(product);
                var inv = BranchInventory.Create(_tenantId, _branchId, product.Id);
                inv.IncreaseStock(1, 200m);
                db.BranchInventories.Add(inv);
                await db.SaveChangesAsync();
            }

            var successBag = new ConcurrentBag<CheckoutResult>();
            var failureBag = new ConcurrentBag<Exception>();

            var tasks = Enumerable.Range(1, 5).Select(async i =>
            {
                var sp = BuildServiceProvider(Guid.NewGuid());
                using var serviceScope = sp.CreateScope();
                var mediator = serviceScope.ServiceProvider.GetRequiredService<IMediator>();

                var cmd = new CheckoutCommand(
                    Lines: new List<CheckoutCartLine> { new(product.Id, 1, null, false, false, null) },
                    CustomerId: null,
                    WalkInCustomerName: $"Racer {i}",
                    InvoiceDiscount: 0m,
                    InvoiceType: InvoiceType.TaxInvoice,
                    Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 500m) },
                    Finance: null,
                    Description: "Race last unit"
                )
                { IdempotencyKey = Guid.NewGuid().ToString() };

                try
                {
                    var result = await mediator.Send(cmd);
                    successBag.Add(result);
                }
                catch (Exception ex)
                {
                    failureBag.Add(ex);
                }
            });

            await Task.WhenAll(tasks);

            successBag.Should().HaveCount(1, $"Iteration {iteration}: exactly 1 checkout must succeed for last unit");
            failureBag.Should().HaveCount(4, $"Iteration {iteration}: exactly 4 checkouts must fail");

            foreach (var ex in failureBag)
            {
                ex.Should().BeOfType<DomainValidationException>();
                ((DomainValidationException)ex).Code.Should().Be("insufficient_stock");
            }

            await using (var dbVerify = new StoreOsDbContext(options, new NullAccessScope()))
            {
                var invVerify = await dbVerify.BranchInventories.IgnoreQueryFilters()
                    .FirstAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == product.Id);
                invVerify.StockQty.Should().Be(0);
            }
        }
    }

    [Fact]
    public async Task Scenario2_ImeiDoubleSell_RunFiveIterations()
    {
        for (var iteration = 1; iteration <= 5; iteration++)
        {
            var product = Product.Create(_tenantId, $"Exclusive Phone It{iteration}", ProductCategory.NewPhone, 80000m, 60000m, 18m, "85171300");
            var imeiNumber = $"35999999{iteration:D7}";

            var options = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(_fixture.OwnerConnectionString)
                .Options;
            await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
            {
                db.Products.Add(product);
                var inv = BranchInventory.Create(_tenantId, _branchId, product.Id);
                inv.IncreaseStock(1, 60000m);
                db.BranchInventories.Add(inv);

                var imei = InventoryImei.Create(
                    _tenantId, _branchId, product.Id, null,
                    imeiNumber, DateOnly.FromDateTime(DateTime.UtcNow), 60000m);
                db.InventoryImeis.Add(imei);
                await db.SaveChangesAsync();
            }

            var successBag = new ConcurrentBag<CheckoutResult>();
            var failureBag = new ConcurrentBag<Exception>();

            var tasks = Enumerable.Range(1, 3).Select(async i =>
            {
                var sp = BuildServiceProvider(Guid.NewGuid());
                using var serviceScope = sp.CreateScope();
                var mediator = serviceScope.ServiceProvider.GetRequiredService<IMediator>();

                var cmd = new CheckoutCommand(
                    Lines: new List<CheckoutCartLine> { new(product.Id, 1, imeiNumber, false, false, null) },
                    CustomerId: null,
                    WalkInCustomerName: $"IMEI Buyer {i}",
                    InvoiceDiscount: 0m,
                    InvoiceType: InvoiceType.TaxInvoice,
                    Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 80000m) },
                    Finance: null,
                    Description: "IMEI double sell test"
                )
                { IdempotencyKey = Guid.NewGuid().ToString() };

                try
                {
                    var result = await mediator.Send(cmd);
                    successBag.Add(result);
                }
                catch (Exception ex)
                {
                    failureBag.Add(ex);
                }
            });

            await Task.WhenAll(tasks);

            successBag.Should().HaveCount(1, $"Iteration {iteration}: exactly 1 checkout must succeed for exclusive IMEI");
            failureBag.Should().HaveCount(2, $"Iteration {iteration}: exactly 2 checkouts must fail");

            var acceptableCodes = new HashSet<string> { "imei_not_available", "insufficient_stock" };
            foreach (var ex in failureBag)
            {
                ex.Should().BeOfType<DomainValidationException>();
                acceptableCodes.Should().Contain(((DomainValidationException)ex).Code);
            }

            var winningSale = successBag.First();
            await using (var dbVerify = new StoreOsDbContext(options, new NullAccessScope()))
            {
                var imeiRow = await dbVerify.InventoryImeis.IgnoreQueryFilters()
                    .FirstAsync(i => i.TenantId == _tenantId && i.Imei == imeiNumber);
                imeiRow.Status.Should().Be(ImeiStatus.Sold);
                imeiRow.SaleId.Should().Be(winningSale.SaleId);
                imeiRow.SaleItemId.Should().NotBeNull();
                imeiRow.SaleItemId.Should().NotBe(Guid.Empty);

                var saleItem = await dbVerify.SaleItems.IgnoreQueryFilters()
                    .FirstAsync(si => si.Id == imeiRow.SaleItemId!.Value);
                saleItem.SaleId.Should().Be(winningSale.SaleId);
            }
        }
    }

    [Fact]
    public async Task Scenario3_ParallelInvoiceNumbering_RunFiveIterations()
    {
        for (var iteration = 1; iteration <= 5; iteration++)
        {
            var product = Product.Create(_tenantId, $"Bulk Item It{iteration}", ProductCategory.Accessory, 100m, 50m, 18m, "39269099");
            var options = new DbContextOptionsBuilder<StoreOsDbContext>()
                .UseNpgsql(_fixture.OwnerConnectionString)
                .Options;
            await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
            {
                db.Products.Add(product);
                var inv = BranchInventory.Create(_tenantId, _branchId, product.Id);
                inv.IncreaseStock(50, 50m);
                db.BranchInventories.Add(inv);
                await db.SaveChangesAsync();
            }

            var resultsBag = new ConcurrentBag<CheckoutResult>();

            var tasks = Enumerable.Range(1, 20).Select(async i =>
            {
                var sp = BuildServiceProvider(Guid.NewGuid());
                using var serviceScope = sp.CreateScope();
                var mediator = serviceScope.ServiceProvider.GetRequiredService<IMediator>();

                var cmd = new CheckoutCommand(
                    Lines: new List<CheckoutCartLine> { new(product.Id, 1, null, false, false, null) },
                    CustomerId: null,
                    WalkInCustomerName: $"Sequential Buyer {i}",
                    InvoiceDiscount: 0m,
                    InvoiceType: InvoiceType.TaxInvoice,
                    Payments: new List<CheckoutPayment> { new(PaymentMode.Cash, 100m) },
                    Finance: null,
                    Description: "Invoice numbering contention"
                )
                { IdempotencyKey = Guid.NewGuid().ToString() };

                CheckoutResult? res = null;
                for (var attempt = 1; attempt <= 10; attempt++)
                {
                    try
                    {
                        res = await mediator.Send(cmd);
                        break;
                    }
                    catch (DomainException dex) when (dex.Code == "serialization_conflict")
                    {
                        await Task.Delay(25 * attempt);
                    }
                }
                res.Should().NotBeNull();
                resultsBag.Add(res!);
            });

            await Task.WhenAll(tasks);

            resultsBag.Should().HaveCount(20, $"Iteration {iteration}: all 20 checkouts must succeed");
            var invoiceNumbers = resultsBag.Select(r => r.InvoiceNumber).ToList();
            invoiceNumbers.Distinct().Should().HaveCount(20, $"Iteration {iteration}: all 20 invoice numbers must be unique");

            await using (var dbVerify = new StoreOsDbContext(options, new NullAccessScope()))
            {
                var distinctDbInvoices = await dbVerify.Sales.IgnoreQueryFilters()
                    .Where(s => s.TenantId == _tenantId && invoiceNumbers.Contains(s.InvoiceNumber))
                    .Select(s => s.InvoiceNumber)
                    .Distinct()
                    .CountAsync();
                distinctDbInvoices.Should().Be(20);
            }
        }
    }
}
