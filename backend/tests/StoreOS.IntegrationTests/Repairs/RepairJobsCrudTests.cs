using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Controllers;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class RepairJobsCrudTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private const string BranchCode = "B1";

    private readonly Guid _managerUserId = Guid.NewGuid();
    private readonly Guid _managerRoleId = Guid.NewGuid();

    private readonly Guid _unprivilegedUserId = Guid.NewGuid();
    private readonly Guid _unprivilegedRoleId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public RepairJobsCrudTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Repair Job Test Tenant", subdomain: "rjt-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\":true}");
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Branch 1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        // Manager role with repairs:manage
        var mgrRole = Role.Create(_tenantId, "RepairMgrRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(mgrRole, _managerRoleId);
        db.Roles.Add(mgrRole);

        var perm = RolePermission.Create(_managerRoleId, "repairs:manage");
        db.RolePermissions.Add(perm);

        var mgrUser = User.Create(_tenantId, "mgroup@test.com", passwordHash, "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(mgrUser, _managerUserId);
        db.Users.Add(mgrUser);

        var ub1 = UserBranch.Create(_managerUserId, _branchId, isDefault: true);
        db.UserBranches.Add(ub1);

        // Unprivileged role with NO repairs:manage
        var unprivRole = Role.Create(_tenantId, "UnprivRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(unprivRole, _unprivilegedRoleId);
        db.Roles.Add(unprivRole);

        var unprivUser = User.Create(_tenantId, "unpriv@test.com", passwordHash, "Unpriv User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(unprivUser, _unprivilegedUserId);
        db.Users.Add(unprivUser);

        var ub2 = UserBranch.Create(_unprivilegedUserId, _branchId, isDefault: true);
        db.UserBranches.Add(ub2);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@u1, @r1, NULL);" +
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@u2, @r2, NULL);", conn);
        cmd.Parameters.AddWithValue("u1", _managerUserId);
        cmd.Parameters.AddWithValue("r1", _managerRoleId);
        cmd.Parameters.AddWithValue("u2", _unprivilegedUserId);
        cmd.Parameters.AddWithValue("r2", _unprivilegedRoleId);
        await cmd.ExecuteNonQueryAsync();

        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id IN (@u1, @u2); " +
            "DELETE FROM user_branches WHERE user_id IN (@u1, @u2); " +
            "DELETE FROM users WHERE id IN (@u1, @u2); " +
            "DELETE FROM role_permissions WHERE role_id IN (@r1, @r2); " +
            "DELETE FROM roles WHERE id IN (@r1, @r2); " +
            "DELETE FROM repair_jobs WHERE tenant_id = @t; " +
            "DELETE FROM invoice_sequences WHERE tenant_id = @t; " +
            "DELETE FROM branches WHERE id = @b; " +
            "DELETE FROM tenants WHERE id = @t;", conn);
        cmd.Parameters.AddWithValue("u1", _managerUserId);
        cmd.Parameters.AddWithValue("u2", _unprivilegedUserId);
        cmd.Parameters.AddWithValue("r1", _managerRoleId);
        cmd.Parameters.AddWithValue("r2", _unprivilegedRoleId);
        cmd.Parameters.AddWithValue("t", _tenantId);
        cmd.Parameters.AddWithValue("b", _branchId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, string[]? permissions = null)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId,
            _tenantId,
            allBranches: true,
            Array.Empty<Guid>(),
            permissions ?? new[] { "repairs:manage" },
            tokenVersion: 1);
        return token;
    }

    private HttpClient CreateManagerClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(_managerUserId, new[] { "repairs:manage" }));
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());
        return client;
    }

    private HttpClient CreateUnprivilegedClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", IssueToken(_unprivilegedUserId, Array.Empty<string>()));
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());
        return client;
    }

    [Fact]
    public async Task CreateRepairJob_AllocatesCorrectlyFormattedJobNumber_AndSetsStatusReceived()
    {
        // (a) creating a job allocates a correctly-formatted, unique job number and sets Status=Received
        using var client = CreateManagerClient();

        var command = new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "John Smith",
            CustomerPhone: "9876543210",
            CustomerAddress: "Flat 101, Bangalore",
            DeviceBrand: "OnePlus",
            DeviceModel: "12R",
            DeviceType: "SmartPhone",
            SerialNumber: "OP123456",
            Imei1: "860123456789012",
            Imei2: null,
            DeviceCondition: "Mint condition",
            AccessoriesReceived: "Original charger and box",
            ProblemDescription: "Charging port loose",
            CustomerNotes: "Call after 5 PM",
            EstimatedCost: 1500m,
            AdvancePaid: 500m);

        var response = await client.PostAsJsonAsync("/api/repair-jobs", command);
        response.StatusCode.Should().Be(HttpStatusCode.Created);

        var result = await response.Content.ReadFromJsonAsync<JsonElement>();
        var id = result.GetProperty("id").GetGuid();

        var getRes = await client.GetAsync($"/api/repair-jobs/{id}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await getRes.Content.ReadFromJsonAsync<RepairJobDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Status.Should().Be(RepairStatus.Received);
        dto.CustomerName.Should().Be("John Smith");
        dto.DeviceModel.Should().Be("12R");
        dto.EstimatedCost.Should().Be(1500m);
        dto.AdvancePaid.Should().Be(500m);
        dto.BalanceDue.Should().Be(1000m);

        // Job number format: REP-B1-YYYYMMDD-XXXX
        var dateStr = DateTime.UtcNow.ToString("yyyyMMdd");
        Regex.IsMatch(dto.JobNumber, $@"^REP-B1-{dateStr}-\d{{4}}$").Should().BeTrue();
    }

    [Fact]
    public async Task SearchRepairJobs_ByDeviceModel_ByImei1_ByPhone_AllWork()
    {
        // (b) search by device model, by IMEI1, by customer phone all work
        using var client = CreateManagerClient();

        // 1. Create job 1 with distinct device model
        var j1Res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Alice Model",
            CustomerPhone: "9111111111",
            CustomerAddress: null,
            DeviceBrand: "Apple",
            DeviceModel: "iPhone 15 Pro Max",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: "111122223333444",
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Screen crack",
            CustomerNotes: null));
        var j1Id = (await j1Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2. Create job 2 with distinct IMEI1
        var j2Res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Bob Imei",
            CustomerPhone: "9222222222",
            CustomerAddress: null,
            DeviceBrand: "Samsung",
            DeviceModel: "Galaxy Fold 5",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: "998877665544332",
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Hinge stiff",
            CustomerNotes: null));
        var j2Id = (await j2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 3. Create job 3 with distinct customer phone
        var j3Res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Charlie Phone",
            CustomerPhone: "9333444555",
            CustomerAddress: null,
            DeviceBrand: "Xiaomi",
            DeviceModel: "Redmi Note 13",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: "555566667777888",
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Speaker issue",
            CustomerNotes: null));
        var j3Id = (await j3Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Search by device model
        var searchModel = await client.GetFromJsonAsync<PagedResult<RepairJobDto>>("/api/repair-jobs?search=Pro Max", JsonOpts);
        searchModel.Should().NotBeNull();
        searchModel!.Items.Should().Contain(j => j.Id == j1Id);
        searchModel.Items.Should().NotContain(j => j.Id == j2Id || j.Id == j3Id);

        // Search by IMEI1
        var searchImei = await client.GetFromJsonAsync<PagedResult<RepairJobDto>>("/api/repair-jobs?search=998877665544", JsonOpts);
        searchImei.Should().NotBeNull();
        searchImei!.Items.Should().Contain(j => j.Id == j2Id);
        searchImei.Items.Should().NotContain(j => j.Id == j1Id || j.Id == j3Id);

        // Search by customer phone
        var searchPhone = await client.GetFromJsonAsync<PagedResult<RepairJobDto>>("/api/repair-jobs?search=9333444555", JsonOpts);
        searchPhone.Should().NotBeNull();
        searchPhone!.Items.Should().Contain(j => j.Id == j3Id);
        searchPhone.Items.Should().NotContain(j => j.Id == j1Id || j.Id == j2Id);
    }

    [Fact]
    public async Task StatusFilter_Active_ReturnsJobsNotInDeliveredOrCancelled()
    {
        // (c) status filter Active returns jobs NOT in Delivered/Cancelled (reusing RepairStatus.IsActive)
        using var client = CreateManagerClient();

        // 1. Create active job (Received)
        var j1Res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Active Job Cust",
            CustomerPhone: "9000000001",
            CustomerAddress: null,
            DeviceBrand: "Vivo",
            DeviceModel: "V29",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Problem 1",
            CustomerNotes: null));
        var j1Id = (await j1Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2. Create job and set to Delivered directly in DB
        var j2Res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Delivered Job Cust",
            CustomerPhone: "9000000002",
            CustomerAddress: null,
            DeviceBrand: "Oppo",
            DeviceModel: "Reno 10",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Problem 2",
            CustomerNotes: null));
        var j2Id = (await j2Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 3. Create job and set to Cancelled directly in DB
        var j3Res = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Cancelled Job Cust",
            CustomerPhone: "9000000003",
            CustomerAddress: null,
            DeviceBrand: "Realme",
            DeviceModel: "11 Pro",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Problem 3",
            CustomerNotes: null));
        var j3Id = (await j3Res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Update j2 and j3 in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var job2 = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == j2Id);
            typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(job2, RepairStatus.Delivered);

            var job3 = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == j3Id);
            typeof(RepairJob).GetProperty(nameof(RepairJob.Status))!.SetValue(job3, RepairStatus.Cancelled);

            await db.SaveChangesAsync();
        }

        // Query status=Active
        var activeJobs = await client.GetFromJsonAsync<PagedResult<RepairJobDto>>("/api/repair-jobs?status=Active", JsonOpts);
        activeJobs.Should().NotBeNull();
        activeJobs!.Items.Should().Contain(j => j.Id == j1Id);
        activeJobs.Items.Should().NotContain(j => j.Id == j2Id || j.Id == j3Id);
    }

    [Fact]
    public async Task UpdateDiagnosisAndCommercials_PersistsCorrectly_AndRecomputesBalanceDue()
    {
        // (d) updating diagnosis/commercials persists correctly and BalanceDue recomputes
        using var client = CreateManagerClient();

        var createRes = await client.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Test Commercials Cust",
            CustomerPhone: "9444444444",
            CustomerAddress: null,
            DeviceBrand: "Google",
            DeviceModel: "Pixel 7a",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "No sound from speaker",
            CustomerNotes: null,
            EstimatedCost: 2000m,
            AdvancePaid: 500m));
        var jobId = (await createRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // Update diagnosis
        var diagBody = new UpdateRepairJobDiagnosisBody(
            Diagnosis: "Blown ear speaker coil",
            TechnicianNotes: "Needs speaker module replacement",
            InternalNotes: "Part in stock at central warehouse",
            Technician: "Ramesh Tech");
        var diagRes = await client.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/diagnosis", diagBody);
        diagRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Update commercials
        var commBody = new UpdateRepairJobCommercialsBody(
            EstimatedCost: 2000m,
            ApprovedCost: 2500m,
            PartsCost: 1500m,
            LaborCharge: 1000m,
            Discount: 200m,
            Tax: 414m,
            FinalTotal: 2714m,
            AdvancePaid: 500m);
        var commRes = await client.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/commercials", commBody);
        commRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Re-fetch detail
        var getRes = await client.GetAsync($"/api/repair-jobs/{jobId}");
        getRes.StatusCode.Should().Be(HttpStatusCode.OK);

        var dto = await getRes.Content.ReadFromJsonAsync<RepairJobDto>(JsonOpts);
        dto.Should().NotBeNull();
        dto!.Diagnosis.Should().Be("Blown ear speaker coil");
        dto.TechnicianNotes.Should().Be("Needs speaker module replacement");
        dto.InternalNotes.Should().Be("Part in stock at central warehouse");
        dto.Technician.Should().Be("Ramesh Tech");
        dto.ApprovedCost.Should().Be(2500m);
        dto.PartsCost.Should().Be(1500m);
        dto.LaborCharge.Should().Be(1000m);
        dto.Discount.Should().Be(200m);
        dto.Tax.Should().Be(414m);
        dto.FinalTotal.Should().Be(2714m);
        dto.AdvancePaid.Should().Be(500m);
        // BalanceDue = 2714 - 500 = 2214
        dto.BalanceDue.Should().Be(2214m);
    }

    [Fact]
    public async Task UnprivilegedUser_Receives403OnMutations_CanStillReadListAndDetail()
    {
        // (e) a caller without repairs:manage -> 403 on mutations, but CAN still read via the list/detail queries
        using var mgrClient = CreateManagerClient();
        using var unprivClient = CreateUnprivilegedClient();

        // 1. Create a job using manager
        var createRes = await mgrClient.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Read Only Test",
            CustomerPhone: "9555555555",
            CustomerAddress: null,
            DeviceBrand: "Apple",
            DeviceModel: "iPhone 13",
            DeviceType: "Phone",
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: "Water damage",
            CustomerNotes: null));
        var jobId = (await createRes.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("id").GetGuid();

        // 2. Unprivileged user can read list
        var listRes = await unprivClient.GetAsync("/api/repair-jobs");
        listRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 3. Unprivileged user can read detail
        var detailRes = await unprivClient.GetAsync($"/api/repair-jobs/{jobId}");
        detailRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // 4. Unprivileged user CANNOT create job -> 403
        var unprivCreate = await unprivClient.PostAsJsonAsync("/api/repair-jobs", new CreateRepairJobCommand(
            CustomerId: null,
            CustomerName: "Hacker",
            CustomerPhone: null,
            CustomerAddress: null,
            DeviceBrand: null,
            DeviceModel: null,
            DeviceType: null,
            SerialNumber: null,
            Imei1: null,
            Imei2: null,
            DeviceCondition: null,
            AccessoriesReceived: null,
            ProblemDescription: null,
            CustomerNotes: null));
        unprivCreate.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. Unprivileged user CANNOT patch diagnosis -> 403
        var unprivDiag = await unprivClient.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/diagnosis", new UpdateRepairJobDiagnosisBody("Hacked"));
        unprivDiag.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 6. Unprivileged user CANNOT patch commercials -> 403
        var unprivComm = await unprivClient.PatchAsJsonAsync($"/api/repair-jobs/{jobId}/commercials", new UpdateRepairJobCommercialsBody(FinalTotal: 0m));
        unprivComm.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 7. Unprivileged user CANNOT patch general details -> 403
        var unprivGeneral = await unprivClient.PatchAsJsonAsync($"/api/repair-jobs/{jobId}", new UpdateRepairJobBody("Notes"));
        unprivGeneral.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
