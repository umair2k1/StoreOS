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
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Application.Repairs.Jobs.GetStatusHistory;
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class ChangeRepairStatusTests : IAsyncLifetime
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

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public ChangeRepairStatusTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Repair Status Test Tenant", subdomain: "rst-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\":true}");
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Branch 1");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "RepairStatusMgrRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "repairs:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "statusmgr@test.com", passwordHash, "Status Mgr");
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
            "DELETE FROM repair_job_status_history WHERE tenant_id = @t; " +
            "DELETE FROM repair_jobs WHERE tenant_id = @t; " +
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
            new[] { "repairs:manage" },
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
            Imei1: null,
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
    public async Task LegalTransition_Succeeds_AndCreatesExactlyOneHistoryRow()
    {
        // (a) a legal transition succeeds, creates exactly one history row with correct from/to values
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // Transition: Received -> Diagnosing
        var changeRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(
            Status: RepairStatus.Diagnosing,
            Notes: "Technician began triage"));
        changeRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify status changed on job
        var jobDto = await client.GetFromJsonAsync<RepairJobDto>($"/api/repair-jobs/{jobId}", JsonOpts);
        jobDto.Should().NotBeNull();
        jobDto!.Status.Should().Be(RepairStatus.Diagnosing);

        // Verify exactly 1 history row
        var history = await client.GetFromJsonAsync<IReadOnlyList<RepairStatusHistoryDto>>($"/api/repair-jobs/{jobId}/status-history", JsonOpts);
        history.Should().NotBeNull();
        history!.Should().HaveCount(1);

        var record = history[0];
        record.RepairJobId.Should().Be(jobId);
        record.FromStatus.Should().Be(RepairStatus.Received);
        record.ToStatus.Should().Be(RepairStatus.Diagnosing);
        record.Notes.Should().Be("Technician began triage");
        record.ChangedBy.Should().Be(_userId);
    }

    [Fact]
    public async Task IllegalTransition_Fails422_AndCreatesNoHistoryRow()
    {
        // (b) an illegal transition -> illegal_status_transition, NO history row created (atomic failure)
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // Transition: Received -> Delivered (illegal: Delivered only from ReadyForPickup)
        var changeRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(
            Status: RepairStatus.Delivered,
            Notes: "Attempt illegal bypass"));
        changeRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        // Verify job status remains Received
        var jobDto = await client.GetFromJsonAsync<RepairJobDto>($"/api/repair-jobs/{jobId}", JsonOpts);
        jobDto.Should().NotBeNull();
        jobDto!.Status.Should().Be(RepairStatus.Received);

        // Verify zero history rows created
        var history = await client.GetFromJsonAsync<IReadOnlyList<RepairStatusHistoryDto>>($"/api/repair-jobs/{jobId}/status-history", JsonOpts);
        history.Should().NotBeNull();
        history!.Should().BeEmpty();
    }

    [Fact]
    public async Task JobReachesDelivered_CorrectlySetsDeliveredAt()
    {
        // (c) a job that reaches Delivered correctly sets DeliveredAt
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // Transition chain: Received -> Diagnosing -> InRepair -> ReadyForPickup -> Delivered
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.Diagnosing));
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.InRepair));
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.ReadyForPickup));

        var beforeDeliver = DateTimeOffset.UtcNow;
        var deliverRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(
            Status: RepairStatus.Delivered,
            Notes: "Customer collected phone and signed"));
        deliverRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDeliver = DateTimeOffset.UtcNow;

        var jobDto = await client.GetFromJsonAsync<RepairJobDto>($"/api/repair-jobs/{jobId}", JsonOpts);
        jobDto.Should().NotBeNull();
        jobDto!.Status.Should().Be(RepairStatus.Delivered);
        jobDto.DeliveredAt.Should().NotBeNull();
        jobDto.DeliveredAt.Should().BeOnOrAfter(beforeDeliver).And.BeOnOrBefore(afterDeliver);
    }

    [Fact]
    public async Task MultipleSequentialTransitions_CreateOrderedCompleteHistoryTrail()
    {
        // (d) multiple sequential legal transitions create a correctly ordered, complete history trail
        using var client = CreateAuthenticatedClient();

        var jobId = await CreateJobAsync(client);

        // 1. Received -> Diagnosing
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.Diagnosing, "Step 1"));
        // 2. Diagnosing -> WaitingForParts
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.WaitingForParts, "Step 2"));
        // 3. WaitingForParts -> InRepair
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.InRepair, "Step 3"));
        // 4. InRepair -> ReadyForPickup
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.ReadyForPickup, "Step 4"));

        var history = await client.GetFromJsonAsync<IReadOnlyList<RepairStatusHistoryDto>>($"/api/repair-jobs/{jobId}/status-history", JsonOpts);
        history.Should().NotBeNull();
        history!.Should().HaveCount(4);

        history[0].FromStatus.Should().Be(RepairStatus.Received);
        history[0].ToStatus.Should().Be(RepairStatus.Diagnosing);
        history[0].Notes.Should().Be("Step 1");

        history[1].FromStatus.Should().Be(RepairStatus.Diagnosing);
        history[1].ToStatus.Should().Be(RepairStatus.WaitingForParts);
        history[1].Notes.Should().Be("Step 2");

        history[2].FromStatus.Should().Be(RepairStatus.WaitingForParts);
        history[2].ToStatus.Should().Be(RepairStatus.InRepair);
        history[2].Notes.Should().Be("Step 3");

        history[3].FromStatus.Should().Be(RepairStatus.InRepair);
        history[3].ToStatus.Should().Be(RepairStatus.ReadyForPickup);
        history[3].Notes.Should().Be("Step 4");
    }
}
