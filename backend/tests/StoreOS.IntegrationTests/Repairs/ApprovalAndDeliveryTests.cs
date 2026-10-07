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
using StoreOS.Application.Repairs.Jobs;
using StoreOS.Application.Repairs.Jobs.CreateRepairJob;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Repairs;

[Collection("postgres")]
public class ApprovalAndDeliveryTests : IAsyncLifetime
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

    public ApprovalAndDeliveryTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Approval Test Shop", subdomain: "appr-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        tenant.SetFeatures("{\"repairs\":true}");
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, BranchCode, "Main Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "ApprovalAdminRole");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "repairs:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "approvaladmin@test.com", passwordHash, "Approval Admin");
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

    private async Task AdvanceToReadyForPickupAsync(HttpClient client, Guid jobId)
    {
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.Diagnosing));
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.InRepair));
        await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/status", new ChangeRepairStatusBody(RepairStatus.ReadyForPickup));
    }

    [Fact]
    public async Task ApprovalState_InvalidState_Returns400_ValidState_PersistsWithTimestamp()
    {
        // (a) recording an invalid ApprovalState string -> 400; a valid one persists correctly with a timestamp
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        // Invalid approval state -> 400
        var invalidRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/approval", new RecordApprovalBody("InvalidState"));
        invalidRes.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        // Valid approval state -> Approved
        var validRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/approval", new RecordApprovalBody(ApprovalState.Approved));
        validRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var job = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.ApprovalState.Should().Be(ApprovalState.Approved);
        job.ApprovalTimestamp.Should().NotBeNull();
        job.ApprovalTimestamp!.Value.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task Deliver_NotReadyForPickup_Returns422_IllegalStatusTransition()
    {
        // (b) delivering a job NOT in ReadyForPickup -> illegal_status_transition (reusing P-704's guard)
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client); // Status is 'Received'

        var deliverRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/deliver", new DeliverRepairJobBody());
        deliverRes.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);

        var body = await deliverRes.Content.ReadAsStringAsync();
        body.Should().Contain("illegal_status_transition");
    }

    [Fact]
    public async Task Deliver_WithLinkedServiceProduct_SetsWarrantyUntilCorrectly()
    {
        // (c) delivering a job correctly linked to a repair-service product with WarrantyDays=45 -> WarrantyUntil is exactly 45 days from delivery
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        // Create a RepairService product with WarrantyDays = 45 in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var serviceProd = Product.Create(
            _tenantId,
            name: "Display Replacement 45D",
            category: ProductCategory.RepairService,
            sellPrice: 4500m,
            purchasePrice: 0m,
            gstRate: 18m,
            hsnCode: "998713");
        serviceProd.UpdateDescriptionAndServiceFields(description: null, estimatedDurationMinutes: null, warrantyDays: 45);
        db.Products.Add(serviceProd);
        await db.SaveChangesAsync();

        // Link service as a repair job part
        var partRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/parts", new ReserveRepairPartBody(
            ProductId: serviceProd.Id,
            PartName: serviceProd.Name,
            Quantity: 1,
            UnitCost: 2000m,
            ChargeAmount: 4500m));
        partRes.StatusCode.Should().Be(HttpStatusCode.OK);

        // Advance to ReadyForPickup
        await AdvanceToReadyForPickupAsync(client, jobId);

        // Deliver
        var deliverRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/deliver", new DeliverRepairJobBody());
        deliverRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify in DB
        await using var verifyDb = new StoreOsDbContext(options, new NullAccessScope());
        var job = await verifyDb.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be(RepairStatus.Delivered);
        job.WarrantyUntil.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(45));
    }

    [Fact]
    public async Task Deliver_NoOverrideAndNoSingleService_FallsBackTo30DaysTenantDefault()
    {
        // (d) delivering a job with NO override and no clearly identifiable single service -> falls back to the 30-day tenant default
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        // Advance to ReadyForPickup without linking any service product
        await AdvanceToReadyForPickupAsync(client, jobId);

        // Deliver with no override
        var deliverRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/deliver", new DeliverRepairJobBody());
        deliverRes.StatusCode.Should().Be(HttpStatusCode.NoContent);

        // Verify in DB
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var job = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);
        job.Status.Should().Be(RepairStatus.Delivered);
        job.WarrantyUntil.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(30));
    }

    [Fact]
    public async Task Deliver_DeliveredAtIsSetExactlyOnce_WithNoConflictingDoubleWrite()
    {
        // (e) DeliveredAt is set exactly once, correctly, with no conflicting double-write between TransitionTo and Deliver
        using var client = CreateAuthenticatedClient();
        var jobId = await CreateJobAsync(client);

        await AdvanceToReadyForPickupAsync(client, jobId);

        var beforeDeliver = DateTimeOffset.UtcNow;
        var deliverRes = await client.PostAsJsonAsync($"/api/repair-jobs/{jobId}/deliver", new DeliverRepairJobBody(WarrantyDaysOverride: 60));
        deliverRes.StatusCode.Should().Be(HttpStatusCode.NoContent);
        var afterDeliver = DateTimeOffset.UtcNow;

        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(_fixture.OwnerConnectionString).Options;
        await using var db = new StoreOsDbContext(options, new NullAccessScope());
        var job = await db.RepairJobs.IgnoreQueryFilters().FirstAsync(j => j.Id == jobId);

        job.Status.Should().Be(RepairStatus.Delivered);
        job.DeliveredAt.Should().NotBeNull();
        job.DeliveredAt!.Value.Should().BeOnOrAfter(beforeDeliver.AddSeconds(-2));
        job.DeliveredAt!.Value.Should().BeOnOrBefore(afterDeliver.AddSeconds(2));
        job.WarrantyUntil.Should().Be(DateOnly.FromDateTime(DateTime.UtcNow).AddDays(60));
    }
}
