using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Application.Suppliers;
using StoreOS.Application.Suppliers.CreateReturn;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Suppliers;

[Collection("postgres")]
public class CreateSupplierReturnTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();
    private Guid _prodAccessoryId;
    private Guid _prodHandsetId;
    private const string TestImei = "IMEI-RET-TEST-001";

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public CreateSupplierReturnTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Return Test Tenant", subdomain: "ret-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "R1", "Returns Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "ReturnManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);
        var perm = RolePermission.Create(_roleId, "purchases:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "retmgr@test.com", passwordHash, "Return Mgr");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        var supplier = Supplier.Create(_tenantId, "Nexus Components", "9876543210", "Industrial Area", "Spares", null);
        typeof(Supplier).GetProperty(nameof(Supplier.Id))!.SetValue(supplier, _supplierId);
        db.Suppliers.Add(supplier);

        // Product 1: Accessory (Non-IMEI, stock = 10)
        var prodAcc = Product.Create(_tenantId, "USB-C Fast Cable", ProductCategory.Accessory, 400m, 200m, 18m, "85444299");
        _prodAccessoryId = prodAcc.Id;
        db.Products.Add(prodAcc);
        var invAcc = BranchInventory.Create(_tenantId, _branchId, _prodAccessoryId);
        invAcc.IncreaseStock(10, 200m);
        db.BranchInventories.Add(invAcc);

        // Product 2: Handset (IMEI, stock = 2)
        var prodHandset = Product.Create(_tenantId, "Pixel 9 Pro", ProductCategory.NewPhone, 80000m, 60000m, 18m, "85171300");
        _prodHandsetId = prodHandset.Id;
        db.Products.Add(prodHandset);
        var invHandset = BranchInventory.Create(_tenantId, _branchId, _prodHandsetId);
        invHandset.IncreaseStock(2, 60000m);
        db.BranchInventories.Add(invHandset);

        // Inventory IMEI
        var imeiRecord = InventoryImei.Create(_tenantId, _branchId, _prodHandsetId, null, TestImei,
            DateOnly.FromDateTime(DateTime.UtcNow), 60000m);
        db.InventoryImeis.Add(imeiRecord);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, NULL);", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM user_branches WHERE user_id = @uId; " +
            "DELETE FROM users WHERE id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE id = @rId; " +
            "DELETE FROM inventory_imeis WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM supplier_ledger_entries WHERE tenant_id = @tId; " +
            "DELETE FROM supplier_transactions WHERE tenant_id = @tId; " +
            "DELETE FROM supplier_return_items WHERE tenant_id = @tId; " +
            "DELETE FROM supplier_returns WHERE tenant_id = @tId; " +
            "DELETE FROM invoice_sequences WHERE tenant_id = @tId; " +
            "DELETE FROM suppliers WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), new[] { "purchases:manage" }, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task Return_NonImeiLine_DecrementsStock_AndCreatesSupplierReturnMovement()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var cmd = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: _prodAccessoryId,
                    ItemName: "USB-C Fast Cable",
                    Qty: 3,
                    PurchasePrice: 200m,
                    ReturnRate: 190m)
            },
            SettlementType: SettlementType.PayableDeduction,
            Reason: "Faulty connector pins");

        var resp = await client.PostAsJsonAsync("/api/supplier-returns", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<CreateSupplierReturnResult>(JsonOpts);
        result.Should().NotBeNull();
        result!.TotalAmount.Should().Be(570m);

        // Check stock decremented from 10 to 7
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var inv = await db.BranchInventories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _prodAccessoryId);
        inv.Should().NotBeNull();
        inv!.StockQty.Should().Be(7);

        // Check movement created
        var movement = await db.InventoryMovements.IgnoreQueryFilters()
            .FirstOrDefaultAsync(m => m.TenantId == _tenantId && m.BranchId == _branchId && m.ProductId == _prodAccessoryId && m.MovementType == MovementType.SupplierReturn);
        movement!.QtyDelta.Should().Be(-3);
        movement.RefType.Should().Be("SupplierReturn");
        movement.RefId.Should().Be(result.ReturnId.ToString());
    }

    [Fact]
    public async Task Return_ImeiLine_TransitionsImeiToReturnedToSupplier_AndUnavailableForSale()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var cmd = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: _prodHandsetId,
                    ItemName: "Pixel 9 Pro",
                    Qty: 1,
                    PurchasePrice: 60000m,
                    ReturnRate: 60000m,
                    ImeiNumbers: new[] { TestImei })
            },
            SettlementType: SettlementType.PendingCredit,
            Reason: "Display defect");

        var resp = await client.PostAsJsonAsync("/api/supplier-returns", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var result = await resp.Content.ReadFromJsonAsync<CreateSupplierReturnResult>(JsonOpts);

        // Verify IMEI status in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var imei = await db.InventoryImeis.IgnoreQueryFilters()
                .FirstOrDefaultAsync(i => i.TenantId == _tenantId && i.Imei == TestImei);
            imei.Should().NotBeNull();
            imei!.Status.Should().Be(ImeiStatus.ReturnedToSupplier);
            imei.ReturnId.Should().Be(result!.ReturnId);
        }

        // Verify Invariant 8: LoadImeiForSaleAsync rejects ReturnedToSupplier IMEI
        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var stockService = scope.ServiceProvider.GetRequiredService<IInventoryStockService>();
            var act = () => stockService.LoadImeiForSaleAsync(_branchId, TestImei, CancellationToken.None);
            await act.Should().ThrowAsync<DomainValidationException>()
                .Where(ex => ex.Code == "imei_not_available");
        }
    }

    [Fact]
    public async Task Return_PayableDeduction_ReducesPayable()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // Setup opening payable of 10000
        await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 10000m,
            TxnDate = new DateOnly(2026, 10, 1)
        });

        // Return 3 cables at 1000 each = 3000 total deduction
        var cmd = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: null,
                    ItemName: "Misc Cable Lot",
                    Qty: 3,
                    PurchasePrice: 1000m,
                    ReturnRate: 1000m)
            },
            SettlementType: SettlementType.PayableDeduction);

        var resp = await client.PostAsJsonAsync("/api/supplier-returns", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify payable becomes 10000 - 3000 = 7000
        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISupplierLedgerService>();
            var payable = await ledgerService.GetPayableAsync(_supplierId, CancellationToken.None);
            payable.Should().Be(7000m);
        }
    }

    [Fact]
    public async Task Return_CashRefund_DoesNotAffectPayable()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // Setup opening payable of 10000
        await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 10000m,
            TxnDate = new DateOnly(2026, 10, 1)
        });

        // Return with CashRefund = 2000
        var cmd = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: null,
                    ItemName: "Cash Refund Items",
                    Qty: 2,
                    PurchasePrice: 1000m,
                    ReturnRate: 1000m)
            },
            SettlementType: SettlementType.CashRefund);

        var resp = await client.PostAsJsonAsync("/api/supplier-returns", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify payable remains 10000 (CashRefund does NOT reduce payable)
        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISupplierLedgerService>();
            var payable = await ledgerService.GetPayableAsync(_supplierId, CancellationToken.None);
            payable.Should().Be(10000m);
        }
    }

    [Fact]
    public async Task Return_PendingCredit_ReducesPayable()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // Setup opening payable of 10000
        await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 10000m,
            TxnDate = new DateOnly(2026, 10, 1)
        });

        // Return with PendingCredit = 2500
        var cmd = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: null,
                    ItemName: "Pending Credit Items",
                    Qty: 1,
                    PurchasePrice: 2500m,
                    ReturnRate: 2500m)
            },
            SettlementType: SettlementType.PendingCredit);

        var resp = await client.PostAsJsonAsync("/api/supplier-returns", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.OK);

        // Verify payable is reduced by 2500 -> 7500
        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISupplierLedgerService>();
            var payable = await ledgerService.GetPayableAsync(_supplierId, CancellationToken.None);
            payable.Should().Be(7500m);
        }
    }

    [Fact]
    public async Task Return_MoreThanAvailableStock_ThrowsInsufficientStock_RollsBackEverything()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // Accessory stock is 10. Attempt to return 25
        var cmd = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: _prodAccessoryId,
                    ItemName: "USB-C Fast Cable",
                    Qty: 25,
                    PurchasePrice: 200m,
                    ReturnRate: 200m)
            },
            SettlementType: SettlementType.PayableDeduction);

        var resp = await client.PostAsJsonAsync("/api/supplier-returns", cmd);
        resp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Verify stock remains 10 and no return or movement was committed
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var inv = await db.BranchInventories.IgnoreQueryFilters()
            .FirstOrDefaultAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchId && bi.ProductId == _prodAccessoryId);
        inv!.StockQty.Should().Be(10);

        var returnCount = await db.SupplierReturns.IgnoreQueryFilters()
            .CountAsync(r => r.TenantId == _tenantId);
        returnCount.Should().Be(0);
    }

    [Fact]
    public async Task Return_AllocatesUniqueFormattedReturnNumber()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var cmd1 = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: null,
                    ItemName: "Item A",
                    Qty: 1,
                    PurchasePrice: 100m,
                    ReturnRate: 100m)
            },
            SettlementType: SettlementType.PayableDeduction);

        var resp1 = await client.PostAsJsonAsync("/api/supplier-returns", cmd1);
        resp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var res1 = await resp1.Content.ReadFromJsonAsync<CreateSupplierReturnResult>(JsonOpts);

        var cmd2 = new CreateSupplierReturnCommand(
            SupplierId: _supplierId,
            ReturnDate: new DateOnly(2026, 10, 7),
            Lines: new[]
            {
                new ReturnLineInput(
                    ProductId: null,
                    ItemName: "Item B",
                    Qty: 1,
                    PurchasePrice: 200m,
                    ReturnRate: 200m)
            },
            SettlementType: SettlementType.PayableDeduction);

        var resp2 = await client.PostAsJsonAsync("/api/supplier-returns", cmd2);
        resp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var res2 = await resp2.Content.ReadFromJsonAsync<CreateSupplierReturnResult>(JsonOpts);

        res1!.ReturnNumber.Should().Be("RET-R1-20261007-001");
        res2!.ReturnNumber.Should().Be("RET-R1-20261007-002");
        res1.ReturnNumber.Should().NotBe(res2.ReturnNumber);
    }
}
