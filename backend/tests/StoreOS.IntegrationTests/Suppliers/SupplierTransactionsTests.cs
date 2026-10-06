using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Controllers;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Suppliers;
using StoreOS.Application.Suppliers.GetSupplierPayable;
using StoreOS.Application.Suppliers.GetSupplierTransactions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Suppliers;

[Collection("postgres")]
public class SupplierTransactionsTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _unprivilegedUserId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private readonly Guid _unprivilegedRoleId = Guid.NewGuid();
    private readonly Guid _supplierId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public SupplierTransactionsTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Supplier Txn Tenant", subdomain: "stx-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "ST1", "Supplier Txn Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "SupplierPurchaser");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var unprivRole = Role.Create(_tenantId, "PosCashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(unprivRole, _unprivilegedRoleId);
        db.Roles.Add(unprivRole);

        var perm = RolePermission.Create(_roleId, "purchases:manage");
        db.RolePermissions.Add(perm);

        var unprivPerm = RolePermission.Create(_unprivilegedRoleId, "pos:checkout");
        db.RolePermissions.Add(unprivPerm);

        var user = User.Create(_tenantId, "purchaser@test.com", passwordHash, "Purchaser");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var unprivUser = User.Create(_tenantId, "cashier@test.com", passwordHash, "Cashier");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(unprivUser, _unprivilegedUserId);
        db.Users.Add(unprivUser);

        var ub1 = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub1);

        var ub2 = UserBranch.Create(_unprivilegedUserId, _branchId, isDefault: true);
        db.UserBranches.Add(ub2);

        var supplier = Supplier.Create(_tenantId, "Alpha Hardware Ltd", "9876543210", "Tech Park, Bangalore", "Hardware", "Primary Supplier");
        typeof(Supplier).GetProperty(nameof(Supplier.Id))!.SetValue(supplier, _supplierId);
        db.Suppliers.Add(supplier);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId1, @rId1, NULL);" +
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId2, @rId2, NULL);", conn);
        cmd.Parameters.AddWithValue("uId1", _userId);
        cmd.Parameters.AddWithValue("rId1", _roleId);
        cmd.Parameters.AddWithValue("uId2", _unprivilegedUserId);
        cmd.Parameters.AddWithValue("rId2", _unprivilegedRoleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @uId1 OR user_id = @uId2; " +
            "DELETE FROM user_branches WHERE user_id = @uId1 OR user_id = @uId2; " +
            "DELETE FROM users WHERE id = @uId1 OR id = @uId2; " +
            "DELETE FROM role_permissions WHERE role_id = @rId1 OR role_id = @rId2; " +
            "DELETE FROM roles WHERE id = @rId1 OR id = @rId2; " +
            "DELETE FROM supplier_ledger_entries WHERE tenant_id = @tId; " +
            "DELETE FROM supplier_transactions WHERE tenant_id = @tId; " +
            "DELETE FROM suppliers WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE id = @bId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId1", _userId);
        cmd.Parameters.AddWithValue("uId2", _unprivilegedUserId);
        cmd.Parameters.AddWithValue("rId1", _roleId);
        cmd.Parameters.AddWithValue("rId2", _unprivilegedRoleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        cmd.Parameters.AddWithValue("bId", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(bool privileged = true)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            privileged ? _userId : _unprivilegedUserId,
            _tenantId,
            allBranches: true,
            Array.Empty<Guid>(),
            privileged ? new[] { "purchases:manage" } : new[] { "pos:checkout" },
            tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task SupplierPayable_OpeningBillPaymentSequence_MatchesPolarityAndCalculation()
    {
        var token = IssueToken(privileged: true);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // (a) Recording an OpeningBalance of 10000 -> GetPayableAsync returns 10000
        var openResp = await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 10000m,
            TxnDate = new DateOnly(2026, 3, 1),
            Notes = "Opening vendor balance"
        });
        openResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payable1Resp = await client.GetAsync($"/api/suppliers/{_supplierId}/ledger");
        payable1Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var p1 = await payable1Resp.Content.ReadFromJsonAsync<SupplierPayableDto>(JsonOpts);
        p1.Should().NotBeNull();
        p1!.SupplierId.Should().Be(_supplierId);
        p1.Balance.Should().Be(10000m);

        // Verify via ISupplierLedgerService directly
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
        // (b) Recording a Bill of 5000 -> payable becomes 15000
        var billResp = await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "Bill",
            Amount = 5000m,
            TxnDate = new DateOnly(2026, 3, 5),
            ReferenceNo = "BILL-001"
        });
        billResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payable2Resp = await client.GetAsync($"/api/suppliers/{_supplierId}/ledger");
        payable2Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var p2 = await payable2Resp.Content.ReadFromJsonAsync<SupplierPayableDto>(JsonOpts);
        p2!.Balance.Should().Be(15000m);

        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISupplierLedgerService>();
            var payable = await ledgerService.GetPayableAsync(_supplierId, CancellationToken.None);
            payable.Should().Be(15000m);
        }
        // (c) Recording a Payment of 8000 -> payable becomes 7000
        var payResp = await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "Payment",
            Amount = 8000m,
            PaymentMode = "BankTransfer",
            ReferenceNo = "TXN-BANK-001",
            TxnDate = new DateOnly(2026, 3, 10)
        });
        payResp.StatusCode.Should().Be(HttpStatusCode.OK);

        var payable3Resp = await client.GetAsync($"/api/suppliers/{_supplierId}/ledger");
        payable3Resp.StatusCode.Should().Be(HttpStatusCode.OK);
        var p3 = await payable3Resp.Content.ReadFromJsonAsync<SupplierPayableDto>(JsonOpts);
        p3!.Balance.Should().Be(7000m);

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
    public async Task PayableFormula_ExactlyMatchesRequirementsSection13_4()
    {
        // Formula: Payable = Opening + Bills - Payments - Credits
        // Create dedicated supplier for this test
        var customSupplierId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var s = Supplier.Create(_tenantId, "Beta Tech Supplies", "9876543211", "Delhi", "Components", null);
            typeof(Supplier).GetProperty(nameof(Supplier.Id))!.SetValue(s, customSupplierId);
            db.Suppliers.Add(s);
            await db.SaveChangesAsync();
        }

        var token = IssueToken(privileged: true);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // Opening balance = 10000
        await client.PostAsJsonAsync($"/api/suppliers/{customSupplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 10000m,
            TxnDate = new DateOnly(2026, 2, 1)
        });

        // Bills: 4000 + 6000 = 10000
        await client.PostAsJsonAsync($"/api/suppliers/{customSupplierId}/transactions", new
        {
            Type = "Bill",
            Amount = 4000m,
            TxnDate = new DateOnly(2026, 2, 5)
        });
        await client.PostAsJsonAsync($"/api/suppliers/{customSupplierId}/transactions", new
        {
            Type = "Bill",
            Amount = 6000m,
            TxnDate = new DateOnly(2026, 2, 8)
        });

        // Payments: 5000 + 3000 = 8000
        await client.PostAsJsonAsync($"/api/suppliers/{customSupplierId}/transactions", new
        {
            Type = "Payment",
            Amount = 5000m,
            PaymentMode = "Cash",
            TxnDate = new DateOnly(2026, 2, 12)
        });
        await client.PostAsJsonAsync($"/api/suppliers/{customSupplierId}/transactions", new
        {
            Type = "Payment",
            Amount = 3000m,
            PaymentMode = "UPI",
            TxnDate = new DateOnly(2026, 2, 15)
        });

        // Credit / ReturnCredit = 2500
        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISupplierLedgerService>();
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            await ledgerService.RecordReturnCreditAsync(customSupplierId, 2500m, _branchId, new DateOnly(2026, 2, 18), "RET-001", CancellationToken.None);
            await db.SaveChangesAsync();
            await tx.CommitAsync();
        }
        // Expected Payable = 10000 (Opening) + 10000 (Bills) - 8000 (Payments) - 2500 (Credits) = 9500
        var payableResp = await client.GetAsync($"/api/suppliers/{customSupplierId}/ledger");
        payableResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var p = await payableResp.Content.ReadFromJsonAsync<SupplierPayableDto>(JsonOpts);
        p!.Balance.Should().Be(9500m);

        using (var scope = _factory.Services.CreateScope())
        {
            var accessScope = scope.ServiceProvider.GetRequiredService<IAccessScope>();
            accessScope.UseSystemContext(_tenantId);
            var db = scope.ServiceProvider.GetRequiredService<StoreOsDbContext>();
            await using var tx = await db.Database.BeginTransactionAsync();
            var ledgerService = scope.ServiceProvider.GetRequiredService<ISupplierLedgerService>();
            var payable = await ledgerService.GetPayableAsync(customSupplierId, CancellationToken.None);
            payable.Should().Be(9500m);
        }
    }

    [Fact]
    public async Task CallerWithoutPurchasesManage_Returns403Forbidden()
    {
        var token = IssueToken(privileged: false);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var postResp = await client.PostAsJsonAsync($"/api/suppliers/{_supplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 1000m,
            TxnDate = new DateOnly(2026, 1, 1)
        });
        postResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var ledgerResp = await client.GetAsync($"/api/suppliers/{_supplierId}/ledger");
        ledgerResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var txnResp = await client.GetAsync($"/api/suppliers/{_supplierId}/transactions");
        txnResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task ListSupplierTransactions_DateRangeFilter_ReturnsFilteredSubset()
    {
        var filterSupplierId = Guid.NewGuid();
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var s = Supplier.Create(_tenantId, "Gamma Filter Supplies", "9876543212", "Chennai", "Displays", null);
            typeof(Supplier).GetProperty(nameof(Supplier.Id))!.SetValue(s, filterSupplierId);
            db.Suppliers.Add(s);
            await db.SaveChangesAsync();
        }

        var token = IssueToken(privileged: true);
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        await client.PostAsJsonAsync($"/api/suppliers/{filterSupplierId}/transactions", new
        {
            Type = "OpeningBalance",
            Amount = 1000m,
            TxnDate = new DateOnly(2026, 3, 1)
        });

        await client.PostAsJsonAsync($"/api/suppliers/{filterSupplierId}/transactions", new
        {
            Type = "Bill",
            Amount = 2000m,
            ReferenceNo = "BILL-MAR-05",
            TxnDate = new DateOnly(2026, 3, 5)
        });

        await client.PostAsJsonAsync($"/api/suppliers/{filterSupplierId}/transactions", new
        {
            Type = "Payment",
            Amount = 500m,
            PaymentMode = "Cash",
            TxnDate = new DateOnly(2026, 3, 10)
        });

        await client.PostAsJsonAsync($"/api/suppliers/{filterSupplierId}/transactions", new
        {
            Type = "Bill",
            Amount = 3000m,
            ReferenceNo = "BILL-MAR-15",
            TxnDate = new DateOnly(2026, 3, 15)
        });

        // Filter date range [2026-03-04, 2026-03-11] -> should return the two transactions: 2026-03-05 and 2026-03-10
        var filterResp = await client.GetAsync($"/api/suppliers/{filterSupplierId}/transactions?fromDate=2026-03-04&toDate=2026-03-11");
        filterResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var page = await filterResp.Content.ReadFromJsonAsync<PagedResult<SupplierTransactionDto>>(JsonOpts);
        page.Should().NotBeNull();
        page!.TotalCount.Should().Be(2);
        page.Items.Should().HaveCount(2);
        page.Items.Select(t => t.TxnDate).Should().BeEquivalentTo(new[]
        {
            new DateOnly(2026, 3, 5),
            new DateOnly(2026, 3, 10)
        });

        // Filter with type = Bill in that date range -> only 1
        var billOnlyResp = await client.GetAsync($"/api/suppliers/{filterSupplierId}/transactions?fromDate=2026-03-04&toDate=2026-03-11&type=Bill");
        billOnlyResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var billPage = await billOnlyResp.Content.ReadFromJsonAsync<PagedResult<SupplierTransactionDto>>(JsonOpts);
        billPage.Should().NotBeNull();
        billPage!.TotalCount.Should().Be(1);
        billPage.Items.Single().Amount.Should().Be(2000m);
    }
}
