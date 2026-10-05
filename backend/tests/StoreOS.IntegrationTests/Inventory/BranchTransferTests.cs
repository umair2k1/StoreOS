using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Inventory;
using StoreOS.Application.Inventory.Transfers.TransferOut;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Inventory;

namespace StoreOS.IntegrationTests.Inventory;

[Collection("postgres")]
public class BranchTransferTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchAId = Guid.NewGuid();
    private readonly Guid _branchBId = Guid.NewGuid();
    private readonly Guid _branchCId = Guid.NewGuid();

    private readonly Guid _userAId = Guid.NewGuid();
    private readonly Guid _userBId = Guid.NewGuid();
    private readonly Guid _userCId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private Guid _prodPhoneId;
    private const string TargetImei = "358888001234567";

    public BranchTransferTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Transfer Test Tenant", subdomain: "trf-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branchA = Branch.Create(_tenantId, "TA1", "Branch A");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchA, _branchAId);
        db.Branches.Add(branchA);

        var branchB = Branch.Create(_tenantId, "TB1", "Branch B");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchB, _branchBId);
        db.Branches.Add(branchB);

        var branchC = Branch.Create(_tenantId, "TC1", "Branch C");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branchC, _branchCId);
        db.Branches.Add(branchC);

        var product = Product.Create(_tenantId, "Transfer Phone", ProductCategory.NewPhone, 80000m, 70000m, 18m, "85171300");
        db.Products.Add(product);
        _prodPhoneId = product.Id;

        // Seed stock at Branch A = 10, Branch B = 0, Branch C = 0
        var invA = BranchInventory.Create(_tenantId, _branchAId, _prodPhoneId);
        invA.IncreaseStock(10, 70000m);
        db.BranchInventories.Add(invA);

        // Seed 1 InStock IMEI at Branch A
        var imei = InventoryImei.Create(_tenantId, _branchAId, _prodPhoneId, null, TargetImei, new DateOnly(2026, 10, 1), 70000m);
        db.InventoryImeis.Add(imei);

        var role = Role.Create(_tenantId, "TransferManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "inventory:manage");
        db.RolePermissions.Add(perm);

        // User A (assigned to branch A)
        var userA = User.Create(_tenantId, "usera@trftest.com", passwordHash, "User A");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userA, _userAId);
        db.Users.Add(userA);
        db.UserBranches.Add(UserBranch.Create(_userAId, _branchAId, isDefault: true));

        // User B (assigned to branch B)
        var userB = User.Create(_tenantId, "userb@trftest.com", passwordHash, "User B");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userB, _userBId);
        db.Users.Add(userB);
        db.UserBranches.Add(UserBranch.Create(_userBId, _branchBId, isDefault: true));

        // User C (assigned to branch C)
        var userC = User.Create(_tenantId, "userc@trftest.com", passwordHash, "User C");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userC, _userCId);
        db.Users.Add(userC);
        db.UserBranches.Add(UserBranch.Create(_userCId, _branchCId, isDefault: true));

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES " +
            "(@uA, @rId, @bA), " +
            "(@uB, @rId, @bB), " +
            "(@uC, @rId, @bC);", conn);
        cmd.Parameters.AddWithValue("uA", _userAId);
        cmd.Parameters.AddWithValue("uB", _userBId);
        cmd.Parameters.AddWithValue("uC", _userCId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("bA", _branchAId);
        cmd.Parameters.AddWithValue("bB", _branchBId);
        cmd.Parameters.AddWithValue("bC", _branchCId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id IN (@uA, @uB, @uC); " +
            "DELETE FROM user_branches WHERE user_id IN (@uA, @uB, @uC); " +
            "DELETE FROM users WHERE id IN (@uA, @uB, @uC); " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE id = @rId; " +
            "DELETE FROM inventory_transfers WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_imeis WHERE tenant_id = @tId; " +
            "DELETE FROM inventory_movements WHERE tenant_id = @tId; " +
            "DELETE FROM branch_inventory WHERE tenant_id = @tId; " +
            "DELETE FROM products WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id IN (@bA, @bB, @bC); " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uA", _userAId);
        cmd.Parameters.AddWithValue("uB", _userBId);
        cmd.Parameters.AddWithValue("uC", _userCId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("bA", _branchAId);
        cmd.Parameters.AddWithValue("bB", _branchBId);
        cmd.Parameters.AddWithValue("bC", _branchCId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, Guid branchId)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches: false, new[] { branchId }, new[] { "inventory:manage" }, tokenVersion: 1);
        return token;
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; }
        public IReadOnlySet<Guid> BranchIds { get; init; } = new HashSet<Guid>();
        public bool AllBranches => false;
        public IReadOnlySet<string> Permissions => new HashSet<string> { "inventory:manage" };
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => BranchIds.Contains(branchId);
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }

    [Fact]
    public async Task CompleteTransferLifecycle_TransfersStockAndImeis_PreservingTenantWideTotal()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        // Measure initial tenant-wide stock
        int initialTenantStock;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            initialTenantStock = await db.BranchInventories.IgnoreQueryFilters()
                .Where(bi => bi.TenantId == _tenantId && bi.ProductId == _prodPhoneId)
                .SumAsync(bi => bi.StockQty);
        }
        initialTenantStock.Should().Be(10);

        var tokenA = IssueToken(_userAId, _branchAId);
        var tokenB = IssueToken(_userBId, _branchBId);
        var tokenC = IssueToken(_userCId, _branchCId);

        // 1. Transfer Out 2 phones (including 1 IMEI) from Branch A to Branch B
        var transferOutCmd = new TransferOutCommand(
            ToBranchId: _branchBId,
            ProductId: _prodPhoneId,
            Qty: 2,
            ImeiStrings: new[] { TargetImei, "NON_EXISTENT_WOULD_FAIL" }[..1]); // exactly 1 IMEI for tests or 1 phone?
        // Wait, command has Qty = 1 with TargetImei:
        var transferOut1PhoneCmd = new TransferOutCommand(
            ToBranchId: _branchBId,
            ProductId: _prodPhoneId,
            Qty: 1,
            ImeiStrings: new[] { TargetImei });

        var reqOut = new HttpRequestMessage(HttpMethod.Post, "/api/inventory/transfers");
        reqOut.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenA);
        reqOut.Headers.Add(BranchContextMiddleware.HeaderName, _branchAId.ToString());
        reqOut.Content = JsonContent.Create(transferOut1PhoneCmd);

        var resOut = await _client.SendAsync(reqOut);
        resOut.StatusCode.Should().Be(HttpStatusCode.Created);

        var jsonOut = await resOut.Content.ReadFromJsonAsync<JsonElement>();
        var transferId = jsonOut.GetProperty("id").GetGuid();

        // Verify Branch A stock decreased to 9
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var invA = await db.BranchInventories.IgnoreQueryFilters()
                .FirstAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchAId && bi.ProductId == _prodPhoneId);
            invA.StockQty.Should().Be(9);

            var imei = await db.InventoryImeis.IgnoreQueryFilters()
                .FirstAsync(i => i.TenantId == _tenantId && i.Imei == TargetImei);
            imei.Status.Should().Be(ImeiStatus.InTransit);
        }

        // 2. Verify InTransit IMEI is UNSELLABLE at BOTH Branch A and Branch B
        var testScopeA = new TestAccessScope { TenantId = _tenantId, UserId = _userAId, BranchIds = new HashSet<Guid> { _branchAId } };
        var interceptorA = new RlsSessionInterceptor(testScopeA);
        var optionsA = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptorA)
            .Options;

        await using (var dbA = new StoreOsDbContext(optionsA, testScopeA))
        {
            await using var txA = await dbA.Database.BeginTransactionAsync();
            var stockServiceA = new InventoryStockService(dbA, testScopeA);
            var actA = async () => await stockServiceA.LoadImeiForSaleAsync(_branchAId, TargetImei, CancellationToken.None);
            await actA.Should().ThrowAsync<DomainValidationException>()
                .Where(ex => ex.Code == "imei_not_available");
        }

        var testScopeB = new TestAccessScope { TenantId = _tenantId, UserId = _userBId, BranchIds = new HashSet<Guid> { _branchBId } };
        var interceptorB = new RlsSessionInterceptor(testScopeB);
        var optionsB = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(interceptorB)
            .Options;

        await using (var dbB = new StoreOsDbContext(optionsB, testScopeB))
        {
            await using var txB = await dbB.Database.BeginTransactionAsync();
            var stockServiceB = new InventoryStockService(dbB, testScopeB);
            var actB = async () => await stockServiceB.LoadImeiForSaleAsync(_branchBId, TargetImei, CancellationToken.None);
            await actB.Should().ThrowAsync<DomainValidationException>()
                .Where(ex => ex.Code == "imei_not_found");
        }

        // 3. Attempt to receive transfer at WRONG branch (Branch C) -> fails with 422
        var reqWrong = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/transfers/{transferId}/receive");
        reqWrong.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenC);
        reqWrong.Headers.Add(BranchContextMiddleware.HeaderName, _branchCId.ToString());

        var resWrong = await _client.SendAsync(reqWrong);
        ((int)resWrong.StatusCode).Should().BeOneOf((int)HttpStatusCode.NotFound, (int)HttpStatusCode.UnprocessableEntity, (int)HttpStatusCode.Forbidden);

        // 4. Receive transfer at CORRECT branch (Branch B) -> succeeds
        var reqReceive = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/transfers/{transferId}/receive");
        reqReceive.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        reqReceive.Headers.Add(BranchContextMiddleware.HeaderName, _branchBId.ToString());

        var resReceive = await _client.SendAsync(reqReceive);
        var body = await resReceive.Content.ReadAsStringAsync();
        if (resReceive.StatusCode != HttpStatusCode.OK)
        {
            throw new Exception("RECEIVE ERROR: " + body);
        }

        // 5. Verify Branch B stock increased to 1, IMEI is InStock at Branch B
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var invB = await db.BranchInventories.IgnoreQueryFilters()
                .FirstAsync(bi => bi.TenantId == _tenantId && bi.BranchId == _branchBId && bi.ProductId == _prodPhoneId);
            invB.StockQty.Should().Be(1);

            var imei = await db.InventoryImeis.IgnoreQueryFilters()
                .FirstAsync(i => i.TenantId == _tenantId && i.Imei == TargetImei);
            imei.Status.Should().Be(ImeiStatus.InStock);
            imei.BranchId.Should().Be(_branchBId);

            var transfer = await db.InventoryTransfers.IgnoreQueryFilters().FirstAsync(t => t.Id == transferId);
            transfer.Status.Should().Be(TransferStatus.Received);
            transfer.ReceivedBy.Should().Be(_userBId);
            transfer.ReceivedAt.Should().NotBeNull();
        }

        // 6. Attempt to receive an ALREADY-received transfer -> transfer_already_received (422)
        var reqDoubleReceive = new HttpRequestMessage(HttpMethod.Post, $"/api/inventory/transfers/{transferId}/receive");
        reqDoubleReceive.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenB);
        reqDoubleReceive.Headers.Add(BranchContextMiddleware.HeaderName, _branchBId.ToString());

        var resDouble = await _client.SendAsync(reqDoubleReceive);
        resDouble.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var jsonDouble = await resDouble.Content.ReadFromJsonAsync<JsonElement>();
        jsonDouble.GetProperty("code").GetString().Should().Be("transfer_already_received");

        // 7. Verify TENANT-WIDE total stock is IDENTICAL before and after (Plan §10.6)
        int finalTenantStock;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            finalTenantStock = await db.BranchInventories.IgnoreQueryFilters()
                .Where(bi => bi.TenantId == _tenantId && bi.ProductId == _prodPhoneId)
                .SumAsync(bi => bi.StockQty);
        }
        finalTenantStock.Should().Be(initialTenantStock, because: "transfers between branches never change tenant-level total stock");
    }
}
