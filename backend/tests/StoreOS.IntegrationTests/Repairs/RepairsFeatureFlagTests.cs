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
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Application.Repairs.Services;
using StoreOS.Application.Sales.Checkout;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class RepairsFeatureFlagTests : IAsyncLifetime
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

    public RepairsFeatureFlagTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Flag Test Shop", subdomain: "flag-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "FlagAdminRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm1 = RolePermission.Create(_roleId, "repairs:manage");
        var perm2 = RolePermission.Create(_roleId, "settings:manage");
        db.RolePermissions.Add(perm1);
        db.RolePermissions.Add(perm2);

        var user = User.Create(_tenantId, "flagadmin@test.com", passwordHash, "Flag Admin");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@u, @r, NULL);", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @u; " +
            "DELETE FROM user_branches WHERE user_id = @u; " +
            "DELETE FROM users WHERE id = @u; " +
            "DELETE FROM role_permissions WHERE role_id = @r; " +
            "DELETE FROM roles WHERE id = @r; " +
            "DELETE FROM idempotency_keys WHERE tenant_id = @t; " +
            "DELETE FROM customer_payments WHERE tenant_id = @t; " +
            "DELETE FROM sale_items WHERE tenant_id = @t; " +
            "DELETE FROM sales WHERE tenant_id = @t; " +
            "DELETE FROM repair_job_parts WHERE tenant_id = @t; " +
            "DELETE FROM repair_job_status_history WHERE tenant_id = @t; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @t; " +
            "DELETE FROM products WHERE tenant_id = @t; " +
            "DELETE FROM invoice_sequences WHERE tenant_id = @t; " +
            "DELETE FROM branches WHERE id = @b; " +
            "DELETE FROM tenants WHERE id = @t;", conn);
        cmd.Parameters.AddWithValue("u", _userId);
        cmd.Parameters.AddWithValue("r", _roleId);
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("b", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken()
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId,
            _tenantId,
            allBranches: true,
            Array.Empty<Guid>(),
            new[] { "repairs:manage", "settings:manage" },
            tokenVersion: 1);
        return token;
    }

    private HttpClient CreateAuthenticatedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken());
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());
        return client;
    }

    [Fact]
    public async Task DisabledByDefault_AllRepairEndpointsReturn422_RepairsModuleDisabled()
    {
        // (a) with the feature DISABLED (tenant default), repair endpoints return repairs_module_disabled (422) instead of executing
        using var client = CreateAuthenticatedClient();
        var dummyId = Guid.NewGuid();

        // 1. POST /api/repair-services
        var res1 = await client.PostAsJsonAsync("/api/repair-services", new { Name = "Test Service", StandardCharge = 100m, Taxable = true });
        res1.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res1.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 2. GET /api/repair-services
        var res2 = await client.GetAsync("/api/repair-services");
        res2.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res2.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 3. GET /api/repair-services/{id}
        var res3 = await client.GetAsync($"/api/repair-services/{dummyId}");
        res3.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res3.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 4. PATCH /api/repair-services/{id}
        var res4 = await client.PatchAsJsonAsync($"/api/repair-services/{dummyId}", new { Name = "Updated" });
        res4.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res4.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 5. POST /api/repair-services/{id}/deactivate
        var res5 = await client.PostAsync($"/api/repair-services/{dummyId}/deactivate", null);
        res5.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res5.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 6. POST /api/repair-jobs
        var res6 = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null, CustomerName: "Cust", CustomerPhone: null, CustomerAddress: null,
            DeviceBrand: "Apple", DeviceModel: "iPhone", DeviceType: "Phone",
            SerialNumber: null, Imei1: null, Imei2: null, DeviceCondition: null,
            AccessoriesReceived: null, ProblemDescription: "Fix", CustomerNotes: null));
        res6.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res6.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 7. GET /api/repair-jobs
        var res7 = await client.GetAsync("/api/repair-jobs");
        res7.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res7.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 8. GET /api/repair-jobs/{id}
        var res8 = await client.GetAsync($"/api/repair-jobs/{dummyId}");
        res8.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res8.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 9. PATCH /api/repair-jobs/{id}
        var res9 = await client.PatchAsJsonAsync($"/api/repair-jobs/{dummyId}", new UpdateRepairJobBody());
        res9.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res9.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 10. PATCH /api/repair-jobs/{id}/diagnosis
        var res10 = await client.PatchAsJsonAsync($"/api/repair-jobs/{dummyId}/diagnosis", new UpdateRepairJobDiagnosisBody());
        res10.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res10.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 11. PATCH /api/repair-jobs/{id}/commercials
        var res11 = await client.PatchAsJsonAsync($"/api/repair-jobs/{dummyId}/commercials", new UpdateRepairJobCommercialsBody());
        res11.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res11.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 12. POST /api/repair-jobs/{id}/status
        var res12 = await client.PostAsJsonAsync($"/api/repair-jobs/{dummyId}/status", new ChangeRepairStatusBody(RepairStatus.Diagnosing));
        res12.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res12.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 13. GET /api/repair-jobs/{id}/status-history
        var res13 = await client.GetAsync($"/api/repair-jobs/{dummyId}/status-history");
        res13.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res13.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 14. POST /api/repair-jobs/{id}/parts
        var res14 = await client.PostAsJsonAsync($"/api/repair-jobs/{dummyId}/parts", new ReserveRepairPartBody(null, "Screen", 1, 100m, 200m));
        res14.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res14.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 15. GET /api/repair-jobs/{id}/parts
        var res15 = await client.GetAsync($"/api/repair-jobs/{dummyId}/parts");
        res15.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res15.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 16. POST /api/repair-jobs/{id}/parts/{partId}/consume
        var res16 = await client.PostAsync($"/api/repair-jobs/{dummyId}/parts/{dummyId}/consume", null);
        res16.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res16.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 17. POST /api/repair-jobs/{id}/parts/{partId}/return
        var res17 = await client.PostAsync($"/api/repair-jobs/{dummyId}/parts/{dummyId}/return", null);
        res17.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res17.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 18. POST /api/repair-jobs/{id}/approval
        var res18 = await client.PostAsJsonAsync($"/api/repair-jobs/{dummyId}/approval", new RecordApprovalBody(ApprovalState.Approved));
        res18.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res18.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 19. POST /api/repair-jobs/{id}/deliver
        var res19 = await client.PostAsJsonAsync($"/api/repair-jobs/{dummyId}/deliver", new DeliverRepairJobBody());
        res19.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res19.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");

        // 20. POST /api/repair-jobs/{id}/bill
        var billReq = new HttpRequestMessage(HttpMethod.Post, $"/api/repair-jobs/{dummyId}/bill")
        {
            Content = JsonContent.Create(new BillRepairBody())
        };
        billReq.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());
        var res20 = await client.SendAsync(billReq);
        res20.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        (await res20.Content.ReadAsStringAsync()).Should().Contain("repairs_module_disabled");
    }

    [Fact]
    public async Task EnableFeature_ViaSettings_AllowsNormalOperation()
    {
        // (b) enabling the feature via PATCH /api/settings/repairs-enabled immediately allows endpoints to function normally
        using var client = CreateAuthenticatedClient();

        // 1. Check feature is disabled initially
        var getRes1 = await client.GetAsync("/api/settings/repairs-enabled");
        getRes1.StatusCode.Should().Be(HttpStatusCode.OK);
        var json1 = await getRes1.Content.ReadFromJsonAsync<JsonElement>();
        json1.GetProperty("enabled").GetBoolean().Should().BeFalse();

        // 2. Enable feature
        var patchRes = await client.PatchAsJsonAsync("/api/settings/repairs-enabled", new SetRepairsEnabledBody(true));
        patchRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 3. Verify feature is enabled
        var getRes2 = await client.GetAsync("/api/settings/repairs-enabled");
        getRes2.StatusCode.Should().Be(HttpStatusCode.OK);
        var json2 = await getRes2.Content.ReadFromJsonAsync<JsonElement>();
        json2.GetProperty("enabled").GetBoolean().Should().BeTrue();

        // 4. Create repair job succeeds
        var jobRes = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null, CustomerName: "John Doe", CustomerPhone: "9876543210", CustomerAddress: null,
            DeviceBrand: "Apple", DeviceModel: "iPhone 14", DeviceType: "Phone",
            SerialNumber: null, Imei1: null, Imei2: null, DeviceCondition: null,
            AccessoriesReceived: null, ProblemDescription: "Broken glass", CustomerNotes: null));
        jobRes.StatusCode.Should().Be(HttpStatusCode.Created);

        // 5. Query repair jobs succeeds
        var queryRes = await client.GetAsync("/api/repair-jobs");
        queryRes.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task DisablingFeature_DoesNotDeleteOrHideExistingDataInDatabase()
    {
        // (c) disabling it again does NOT delete or hide any EXISTING repair job/history/parts data -
        // directly query the database and confirm all rows are still present even while the feature flag is off
        using var client = CreateAuthenticatedClient();

        // 1. Enable feature
        await client.PatchAsJsonAsync("/api/settings/repairs-enabled", new SetRepairsEnabledBody(true));

        // 2. Create job
        var jobRes = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null, CustomerName: "Jane Doe", CustomerPhone: "9876543210", CustomerAddress: null,
            DeviceBrand: "Samsung", DeviceModel: "Galaxy S23", DeviceType: "Phone",
            SerialNumber: null, Imei1: null, Imei2: null, DeviceCondition: null,
            AccessoriesReceived: null, ProblemDescription: "Battery draining", CustomerNotes: null));
        jobRes.StatusCode.Should().Be(HttpStatusCode.Created);
        var json = await jobRes.Content.ReadFromJsonAsync<JsonElement>();
        var jobId = json.GetProperty("id").GetGuid();

        // 3. Add a part
        var partRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            null, "OEM Battery", 1, 800m, 1500m));
        partRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Change status
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.Diagnosing));

        // 5. Disable feature
        var disableRes = await client.PatchAsJsonAsync("/api/settings/repairs-enabled", new SetRepairsEnabledBody(false));
        disableRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // 6. Verify API access is blocked
        var apiRes = await client.GetAsync($"/api/repair-jobs/{jobId}");
        apiRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // 7. Verify directly in database that data remains completely intact
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var job = await db.RepairJobs.IgnoreQueryFilters().FirstOrDefaultAsync(j => j.Id == jobId);
        job.Should().NotBeNull();
        job!.CustomerName.Should().Be("Jane Doe");
        job.Status.Should().Be(RepairStatus.Diagnosing);

        var parts = await db.RepairJobParts.IgnoreQueryFilters().Where(p => p.RepairJobId == jobId).ToListAsync();
        parts.Should().HaveCount(1);
        parts[0].PartName.Should().Be("OEM Battery");

        var history = await db.RepairJobStatusHistories.IgnoreQueryFilters().Where(h => h.RepairJobId == jobId).ToListAsync();
        history.Should().HaveCount(1);
        history[0].ToStatus.Should().Be(RepairStatus.Diagnosing);
    }
}
