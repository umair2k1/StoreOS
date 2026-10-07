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
using StoreOS.Application.Repairs.BillRepair;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class BillRepairTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private const string BranchCode = "B1";

    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private readonly Guid _noPermUserId = Guid.NewGuid();
    private readonly Guid _noPermRoleId = Guid.NewGuid();

    public BillRepairTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Bill Repair Shop", subdomain: "bill-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\":true}");
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Role with repairs:manage
        var role = Role.Create(_tenantId, "RepairBillAdminRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "repairs:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "billadmin@test.com", passwordHash, "Bill Admin");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        // Role WITHOUT repairs:manage
        var noPermRole = Role.Create(_tenantId, "NoPermRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(noPermRole, _noPermRoleId);
        db.Roles.Add(noPermRole);

        var noPermUser = User.Create(_tenantId, "noperm@test.com", passwordHash, "No Perm");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(noPermUser, _noPermUserId);
        db.Users.Add(noPermUser);

        var ub2 = UserBranch.Create(_noPermUserId, _branchId, isDefault: true);
        db.UserBranches.Add(ub2);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@u, @r, NULL), (@nu, @nr, NULL);", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        cmd.Parameters.AddWithValue("nu", _noPermUserId);
        cmd.Parameters.AddWithValue("nr", _noPermRoleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @u OR user_id = @nu; " +
            "DELETE FROM user_branches WHERE user_id = @u OR user_id = @nu; " +
            "DELETE FROM users WHERE id = @u OR id = @nu; " +
            "DELETE FROM role_permissions WHERE role_id = @r OR role_id = @nr; " +
            "DELETE FROM roles WHERE id = @r OR id = @nr; " +
            "DELETE FROM idempotency_keys WHERE tenant_id = @t; " +
            "DELETE FROM customer_payments WHERE tenant_id = @t; " +
            "DELETE FROM sale_items WHERE tenant_id = @t; " +
            "DELETE FROM sales WHERE tenant_id = @t; " +
            "DELETE FROM repair_job_parts WHERE tenant_id = @t; " +
            "DELETE FROM repair_job_status_history WHERE tenant_id = @t; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @t; " +
            "DELETE FROM invoice_sequences WHERE tenant_id = @t; " +
            "DELETE FROM branches WHERE id = @b; " +
            "DELETE FROM tenants WHERE id = @t;", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("nu", _noPermUserId);
        cmd.Parameters.AddWithValue("r", _roleId);
        cmd.Parameters.AddWithValue("nr", _noPermRoleId);
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("b", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, string[] permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId,
            _tenantId,
            allBranches: true,
            Array.Empty<Guid>(),
            permissions,
            tokenVersion: 1);
        return token;
    }

    private HttpClient CreateAuthenticatedClient(bool withManagePerm = true)
    {
        var client = _factory.CreateClient();
        var token = withManagePerm
            ? IssueToken(_userId, new[] { "repairs:manage" })
            : IssueToken(_noPermUserId, Array.Empty<string>());
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());
        return client;
    }

    private async Task<Guid> CreateJobAsync(HttpClient client, string name = "Customer")
    {
        var res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: name,
            CustomerPhone: "9876543210",
            CustomerAddress: null,
            DeviceBrand: "Apple",
            DeviceModel: "iPhone 14",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: "354123456789012",
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Diagnostic needed",
            CustomerNotes: null));
        res.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await res.Content.ReadFromJsonAsync<JsonElement>();
        return json.GetProperty("id").GetGuid();
    }

    [Fact]
    public async Task BillRepair_WithCommercials_CreatesSaleAndBidirectionalLink_MatchesTotals()
    {
        // (a) billing a repair job with commercials set creates a Sale with RepairJobId correctly populated,
        // AND the RepairJob.SaleId/InvoiceNumber are correctly set (bidirectional link verified both directions);
        // (c) the created sale's GrandTotal exactly matches job.FinalTotal
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        // Set commercials
        var commRes = await client.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/commercials", new UpdateRepairJobCommercialsBody(
            PartsCost: 1000m,
            LaborCharge: 1500m,
            Discount: 0m,
            Tax: 450m,
            FinalTotal: 2950m));
        commRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Bill repair
        var idemKey = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{jobId}/bill")
        {
            Content = JsonContent.Create(new BillRepairBody(new[] { new CheckoutPayment(PaymentMode.Cash, 2950m) }))
        };
        request.Headers.Add("Idempotency-Key", idemKey);

        var billRes = await client.SendAsync(request);
        billRes.StatusCode.Should().Be(HttpStatusCode.OK);
        var billResult = await billRes.Content.ReadFromJsonAsync<BillRepairResult>();
        billResult.Should().NotBeNull();
        billResult!.GrandTotal.Should().Be(2950m);
        billResult.AmountPaid.Should().Be(2950m);
        billResult.Balance.Should().Be(0m);
        billResult.InvoiceNumber.Should().NotBeNullOrWhiteSpace();

        // Verify in DB - bidirectional link
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == billResult.SaleId);
        sale.RepairJobId.Should().Be(jobId);
        sale.GrandTotal.Should().Be(2950m);
        sale.InvoiceNumber.Should().Be(billResult.InvoiceNumber);

        var job = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.SaleId.Should().Be(sale.Id);
        job.InvoiceNumber.Should().Be(sale.InvoiceNumber);
    }

    [Fact]
    public async Task BillRepair_WithoutCommercials_Returns422_RepairCommercialsNotSet()
    {
        // (b) billing a job with NO commercials set -> repair_commercials_not_set
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        var idemKey = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{jobId}/bill")
        {
            Content = JsonContent.Create(new BillRepairBody())
        };
        request.Headers.Add("Idempotency-Key", idemKey);

        var billRes = await client.SendAsync(request);
        billRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await billRes.Content.ReadAsStringAsync();
        body.Should().Contain("repair_commercials_not_set");
    }

    [Fact]
    public async Task BillRepair_WithoutPermission_Returns403()
    {
        // (d) a caller without repairs:manage -> 403
        using var client = CreateAuthenticatedClient(withManagePerm: false);

        var jobId = Guid.NewGuid();
        var idemKey = Guid.NewGuid().ToString();
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{jobId}/bill")
        {
            Content = JsonContent.Create(new BillRepairBody())
        };
        request.Headers.Add("Idempotency-Key", idemKey);

        var billRes = await client.SendAsync(request);
        billRes.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task BillRepair_Idempotency_ReplayingSameKey_DoesNotDoubleBill()
    {
        // (e) idempotency: replaying the same key does not double-bill
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        await client.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/commercials", new UpdateRepairJobCommercialsBody(
            PartsCost: 500m,
            LaborCharge: 500m,
            Tax: 180m,
            FinalTotal: 1180m));

        var idemKey = Guid.NewGuid().ToString();

        // First call
        var req1 = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{jobId}/bill")
        {
            Content = JsonContent.Create(new BillRepairBody(new[] { new CheckoutPayment(PaymentMode.Cash, 1180m) }))
        };
        req1.Headers.Add("Idempotency-Key", idemKey);
        var res1 = await client.SendAsync(req1);
        res1.StatusCode.Should().Be(HttpStatusCode.OK);
        var result1 = await res1.Content.ReadFromJsonAsync<BillRepairResult>();

        // Second call with same key
        var req2 = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{jobId}/bill")
        {
            Content = JsonContent.Create(new BillRepairBody(new[] { new CheckoutPayment(PaymentMode.Cash, 1180m) }))
        };
        req2.Headers.Add("Idempotency-Key", idemKey);
        var res2 = await client.SendAsync(req2);
        res2.StatusCode.Should().Be(HttpStatusCode.OK);
        res2.Headers.Should().Contain(h => h.Key == "Idempotent-Replayed");
        var result2 = await res2.Content.ReadFromJsonAsync<BillRepairResult>();

        result2!.SaleId.Should().Be(result1!.SaleId);

        // Verify only 1 sale exists in DB for this job
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var sales = await db.Sales.IgnoreQueryFilters().Where(s => s.RepairJobId == jobId).ToListAsync();
        sales.Should().HaveCount(1);
    }

    [Fact]
    public async Task BillRepair_TenIterationsLoop_NoDeadlockOrFkFailure()
    {
        // (f) run this test at least 10 times in a loop asserting no deadlock or FK-constraint failure ever occurs
        using var client = CreateAuthenticatedClient();

        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;

        for (int i = 0; i < 10; i++)
        {
            var jobId = await CreateJobAsync(client, $"Customer {i}");

            decimal finalTotal = 1000m + (i * 50m);
            var commRes = await client.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/commercials", new UpdateRepairJobCommercialsBody(
                PartsCost: 200m,
                LaborCharge: finalTotal - 200m,
                Tax: 0m,
                FinalTotal: finalTotal));
            commRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

            var idemKey = Guid.NewGuid().ToString();
            var req = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{jobId}/bill")
            {
                Content = JsonContent.Create(new BillRepairBody(new[] { new CheckoutPayment(PaymentMode.Cash, finalTotal) }))
            };
            req.Headers.Add("Idempotency-Key", idemKey);

            var billRes = await client.SendAsync(req);
            billRes.StatusCode.Should().Be(HttpStatusCode.OK, because: $"Iteration {i} should succeed without FK or deadlock error");

            var result = await billRes.Content.ReadFromJsonAsync<BillRepairResult>();
            result.Should().NotBeNull();
            result!.GrandTotal.Should().Be(finalTotal);

            // Verify both directions of FK
            await using var db = new StoreOsDbContext(options, new NullAccessScope());
            var sale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == result.SaleId);
            sale.RepairJobId.Should().Be(jobId);

            var job = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
            job.SaleId.Should().Be(sale.Id);
            job.InvoiceNumber.Should().Be(sale.InvoiceNumber);
        }
    }
}
