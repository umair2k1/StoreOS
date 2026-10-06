using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Customers.Crud.CreateCustomer;
using StoreOS.Application.Customers.Crud.GetCustomerById;
using StoreOS.Application.Customers.Crud.GetCustomers;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Customers;

[Collection("postgres")]
public class CustomersCrudTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _posUserId = Guid.NewGuid();
    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _posRoleId = Guid.NewGuid();
    private readonly Guid _managerRoleId = Guid.NewGuid();

    private string _posToken = default!;
    private string _managerToken = default!;

    public CustomersCrudTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Customers CRUD Tenant", subdomain: "cc-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "CRD", "CRUD Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // POS role with pos:checkout only
        var posRole = Role.Create(_tenantId, "Cashier");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(posRole, _posRoleId);
        db.Roles.Add(posRole);
        db.RolePermissions.Add(RolePermission.Create(_posRoleId, "pos:checkout"));

        // Manager role with settings:manage AND pos:checkout
        var managerRole = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(managerRole, _managerRoleId);
        db.Roles.Add(managerRole);
        db.RolePermissions.Add(RolePermission.Create(_managerRoleId, "pos:checkout"));
        db.RolePermissions.Add(RolePermission.Create(_managerRoleId, "settings:manage"));

        var posUser = User.Create(_tenantId, $"pos@{_tenantId:N}.test", passwordHash, "POS User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(posUser, _posUserId);
        db.Users.Add(posUser);
        db.UserBranches.Add(UserBranch.Create(_posUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_posUserId, _posRoleId, _branchId));

        var managerUser = User.Create(_tenantId, $"manager@{_tenantId:N}.test", passwordHash, "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(managerUser, _managerUserId);
        db.Users.Add(managerUser);
        db.UserBranches.Add(UserBranch.Create(_managerUserId, _branchId, isDefault: true));
        db.UserRoleAssignments.Add(UserRoleAssignment.Create(_managerUserId, _managerRoleId, _branchId));

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();

        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();

        var (posTok, _) = jwtService.IssueAccessToken(
            _posUserId, _tenantId, allBranches: false, new[] { _branchId }, new[] { "pos:checkout" }, tokenVersion: 1);
        _posToken = posTok;

        var (mgrTok, _) = jwtService.IssueAccessToken(
            _managerUserId, _tenantId, allBranches: false, new[] { _branchId }, new[] { "pos:checkout", "settings:manage" }, tokenVersion: 1);
        _managerToken = mgrTok;
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            DELETE FROM customer_ledger_entries WHERE tenant_id = @tid;
            DELETE FROM sales WHERE tenant_id = @tid;
            DELETE FROM customers WHERE tenant_id = @tid;
            DELETE FROM user_role_assignments WHERE user_id = @uid1 OR user_id = @uid2;
            DELETE FROM user_branches WHERE user_id = @uid1 OR user_id = @uid2;
            DELETE FROM role_permissions WHERE role_id = @rid1 OR role_id = @rid2;
            DELETE FROM roles WHERE tenant_id = @tid;
            DELETE FROM users WHERE tenant_id = @tid;
            DELETE FROM branches WHERE tenant_id = @tid;
            DELETE FROM tenants WHERE id = @tid;
        ";
        cmd.Parameters.AddWithValue("tid", _tenantId);
        cmd.Parameters.AddWithValue("uid1", _posUserId);
        cmd.Parameters.AddWithValue("uid2", _managerUserId);
        cmd.Parameters.AddWithValue("rid1", _posRoleId);
        cmd.Parameters.AddWithValue("rid2", _managerRoleId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task Create_Get_UpdateProfile_WorkCorrectly()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _posToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // Create
        var createCmd = new CreateCustomerCommand("Rahul Dravid", "9845012345", "29ABCDE1234F1Z5");
        var createResp = await _client.PostAsJsonAsync("/api/customers", createCmd);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var location = createResp.Headers.Location;
        location.Should().NotBeNull();

        // Get by Id
        var getResp = await _client.GetAsync(location);
        getResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var detail = await getResp.Content.ReadFromJsonAsync<CustomerDetailDto>();
        detail.Should().NotBeNull();
        detail!.Name.Should().Be("Rahul Dravid");
        detail.Phone.Should().Be("9845012345");
        detail.Gstin.Should().Be("29ABCDE1234F1Z5");
        detail.TotalPurchases.Should().Be(0m);
        detail.CreditLimit.Should().BeNull();
        detail.CurrentCreditBalance.Should().Be(0m);

        // Update profile
        var updateResp = await _client.PatchAsJsonAsync(
            $"/api/customers/{detail.Id}",
            new { Name = "Rahul Sharad Dravid", Phone = "9845099999", Gstin = (string?)null });
        updateResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify updated get
        var getUpdatedResp = await _client.GetAsync($"/api/customers/{detail.Id}");
        getUpdatedResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var updatedDetail = await getUpdatedResp.Content.ReadFromJsonAsync<CustomerDetailDto>();
        updatedDetail!.Name.Should().Be("Rahul Sharad Dravid");
        updatedDetail.Phone.Should().Be("9845099999");
        updatedDetail.Gstin.Should().BeNull();
    }

    [Fact]
    public async Task PatchCreditLimit_RequiresSettingsManage_Returns403ForPosUser_SucceedsForManager()
    {
        // 1. Create a customer using POS user
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _posToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var createCmd = new CreateCustomerCommand("VVS Laxman", "9845054321", null);
        var createResp = await _client.PostAsJsonAsync("/api/customers", createCmd);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Name == "VVS Laxman" && c.TenantId == _tenantId);

        // 2. POS user (only pos:checkout) attempts to update credit limit -> 403 Forbidden
        var posPatchResp = await _client.PatchAsJsonAsync(
            $"/api/customers/{customer.Id}/credit-limit",
            new { CreditLimit = 50000m });
        posPatchResp.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. Manager user (has settings:manage) updates credit limit -> 204 NoContent
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _managerToken);
        var mgrPatchResp = await _client.PatchAsJsonAsync(
            $"/api/customers/{customer.Id}/credit-limit",
            new { CreditLimit = 50000m });
        mgrPatchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify credit limit updated
        var getResp = await _client.GetAsync($"/api/customers/{customer.Id}");
        var detail = await getResp.Content.ReadFromJsonAsync<CustomerDetailDto>();
        detail!.CreditLimit.Should().Be(50000m);
    }

    [Fact]
    public async Task Search_ByPartialPhoneAndName_WorksCorrectly()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _posToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var c1 = new CreateCustomerCommand("Anil Kumble", "9111222333", null);
        var c2 = new CreateCustomerCommand("Javagal Srinath", "9444555666", null);
        var c3 = new CreateCustomerCommand("Venkatesh Prasad", "9777888999", null);

        await _client.PostAsJsonAsync("/api/customers", c1);
        await _client.PostAsJsonAsync("/api/customers", c2);
        await _client.PostAsJsonAsync("/api/customers", c3);

        // Search by partial name: "Kumble"
        var searchNameResp = await _client.GetAsync("/api/customers?search=kumb");
        searchNameResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var nameResult = await searchNameResp.Content.ReadFromJsonAsync<PagedResult<CustomerListItemDto>>();
        nameResult!.Items.Should().ContainSingle(c => c.Name == "Anil Kumble");

        // Search by partial phone: "5556"
        var searchPhoneResp = await _client.GetAsync("/api/customers?search=5556");
        searchPhoneResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var phoneResult = await searchPhoneResp.Content.ReadFromJsonAsync<PagedResult<CustomerListItemDto>>();
        phoneResult!.Items.Should().ContainSingle(c => c.Name == "Javagal Srinath");
    }

    [Fact]
    public async Task CustomerListItemDto_CurrentCreditBalance_MatchesConsolidatedBalance()
    {
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _posToken);
        _client.DefaultRequestHeaders.Remove("X-Branch-Id");
        _client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var createCmd = new CreateCustomerCommand("Zaheer Khan", "9888877777", null);
        var createResp = await _client.PostAsJsonAsync("/api/customers", createCmd);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var customer = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.Name == "Zaheer Khan" && c.TenantId == _tenantId);

        var saleId = Guid.NewGuid();
        var sale = Sale.CreateTaxInvoice(_tenantId, _branchId, "INV-ZK-01", DateTimeOffset.UtcNow,
            customer.Id, "Zaheer Khan", null, 8500m, 0m, 0m, 8500m, 0m, 0m,
            null, null, null, null, null, null, null, null, Guid.NewGuid());
        typeof(Sale).GetProperty(nameof(Sale.Id))!.SetValue(sale, saleId);
        db.Sales.Add(sale);

        // Add 8500 debit and 2500 credit -> balance = 6000
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateDebit(
            _tenantId, customer.Id, saleId, CustomerLedgerEntryType.Invoice, 8500m,
            _branchId, DateOnly.FromDateTime(DateTime.UtcNow), "INV-ZK-01"));
        db.CustomerLedgerEntries.Add(CustomerLedgerEntry.CreateCredit(
            _tenantId, customer.Id, saleId, CustomerLedgerEntryType.Payment, 2500m,
            _branchId, DateOnly.FromDateTime(DateTime.UtcNow), "REC-ZK-01"));

        await db.SaveChangesAsync();

        var listResp = await _client.GetAsync($"/api/customers?search=Zaheer");
        listResp.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged = await listResp.Content.ReadFromJsonAsync<PagedResult<CustomerListItemDto>>();
        paged!.Items.Should().ContainSingle(c => c.Id == customer.Id);
        var item = paged.Items.First(c => c.Id == customer.Id);
        item.CurrentCreditBalance.Should().Be(6000m);
    }
}
