using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity.Login;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Provisioning;

namespace StoreOS.IntegrationTests.Identity;

[Collection("postgres")]
public class ProvisioningTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    public ProvisioningTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });

        _client = _factory.CreateClient();
    }

    public Task InitializeAsync() => Task.CompletedTask;

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    private StoreOsDbContext CreateOwnerDbContext()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        return new StoreOsDbContext(options, new NullAccessScope());
    }

    [Fact]
    public async Task Provisioning_CreatesExpectedEntities_AndOwnerCanLoginWithFullPermissions()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var subdomain = "apex-" + suffix;
        var email = $"owner-{suffix}@apexmobiles.example";

        var request = new ProvisionRequest(
            Name: "Apex Mobiles Test",
            Subdomain: subdomain,
            OwnerEmail: email,
            OwnerName: "Anil Kumar",
            BranchCode: "B01",
            BranchName: "Main Branch",
            Gstin: "27AAAAA0000A1Z5",
            Timezone: "Asia/Kolkata",
            Repairs: true
        );

        var service = new ProvisioningService(_fixture.OwnerConnectionString);

        // Act
        var result = await service.ProvisionAsync(request);

        // Assert: Result details
        result.Success.Should().BeTrue();
        result.ExitCode.Should().Be(0);
        result.TenantId.Should().NotBeEmpty();
        result.BranchId.Should().NotBeEmpty();
        result.OwnerUserId.Should().NotBeEmpty();
        result.Subdomain.Should().Be(subdomain);
        result.OwnerPassword.Should().NotBeNullOrWhiteSpace();
        result.OwnerPassword.Length.Should().Be(16);
        result.LoginUrl.Should().Be($"https://{subdomain}.pos.storeos.io");

        // Assert: Database state
        await using var db = CreateOwnerDbContext();

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == result.TenantId);
        tenant.Should().NotBeNull();
        tenant!.Name.Should().Be("Apex Mobiles Test");
        tenant.Subdomain.Should().Be(subdomain);
        tenant.IsActive.Should().BeTrue();
        tenant.Timezone.Should().Be("Asia/Kolkata");
        using var doc = JsonDocument.Parse(tenant.FeaturesJson);
        doc.RootElement.GetProperty("repairs").GetBoolean().Should().BeTrue();

        var branch = await db.Branches.IgnoreQueryFilters().FirstOrDefaultAsync(b => b.TenantId == result.TenantId);
        branch.Should().NotBeNull();
        branch!.Id.Should().Be(result.BranchId);
        branch.Code.Should().Be("B01");
        branch.Name.Should().Be("Main Branch");
        branch.Gstin.Should().Be("27AAAAA0000A1Z5");
        branch.IsActive.Should().BeTrue();

        var roles = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == result.TenantId).ToListAsync();
        roles.Should().HaveCount(5);
        roles.Select(r => r.Name).Should().BeEquivalentTo(StandardRoles.All);

        var user = await db.Users.IgnoreQueryFilters().FirstOrDefaultAsync(u => u.TenantId == result.TenantId);
        user.Should().NotBeNull();
        user!.Id.Should().Be(result.OwnerUserId);
        user.Email.Should().Be(email.ToLowerInvariant());
        user.DisplayName.Should().Be("Anil Kumar");
        user.IsActive.Should().BeTrue();

        var userBranch = await db.UserBranches.IgnoreQueryFilters().FirstOrDefaultAsync(ub => ub.UserId == user.Id);
        userBranch.Should().NotBeNull();
        userBranch!.BranchId.Should().Be(branch.Id);
        userBranch.IsDefault.Should().BeTrue();

        var ownerRole = roles.First(r => r.Name == StandardRoles.Owner);
        Guid assignedRoleId;
        bool isBranchNull;
        await using (var cmd = db.Database.GetDbConnection().CreateCommand())
        {
            cmd.CommandText = "SELECT role_id, branch_id FROM user_role_assignments WHERE user_id = @u";
            var p = cmd.CreateParameter();
            p.ParameterName = "u";
            p.Value = user.Id;
            cmd.Parameters.Add(p);
            if (cmd.Connection!.State != System.Data.ConnectionState.Open)
                await cmd.Connection.OpenAsync();
            await using var reader = await cmd.ExecuteReaderAsync();
            reader.Read().Should().BeTrue();
            assignedRoleId = reader.GetGuid(0);
            isBranchNull = reader.IsDBNull(1);
            await reader.CloseAsync();
        }
        assignedRoleId.Should().Be(ownerRole.Id);
        isBranchNull.Should().BeTrue(); // tenant-wide

        var auditLog = await db.AuditLogEntries.IgnoreQueryFilters().FirstOrDefaultAsync(a => a.TenantId == result.TenantId && a.Action == "tenant.provisioned");
        auditLog.Should().NotBeNull();
        auditLog!.EntityType.Should().Be("Tenant");
        auditLog.EntityId.Should().Be(tenant.Id.ToString());
        auditLog.UserId.Should().Be(user.Id);

        // Assert: Login via HTTP endpoint
        var loginCmd = new LoginCommand(email, result.OwnerPassword, result.TenantId);
        var response = await _client.PostAsJsonAsync("/api/auth/login", loginCmd);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var loginResult = await response.Content.ReadFromJsonAsync<LoginResult>();
        loginResult.Should().NotBeNull();
        loginResult!.UserId.Should().Be(result.OwnerUserId);
        loginResult.TenantId.Should().Be(result.TenantId);
        loginResult.AllBranches.Should().BeTrue();
        loginResult.Permissions.Should().BeEquivalentTo(PermissionCatalog.All);
    }

    [Fact]
    public async Task DuplicateSubdomain_FailsWithExitCode2_AndCreatesNothing()
    {
        // Arrange
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var subdomain = "dup-" + suffix;
        var service = new ProvisioningService(_fixture.OwnerConnectionString);

        var request1 = new ProvisionRequest(
            Name: "First Tenant",
            Subdomain: subdomain,
            OwnerEmail: $"first-{suffix}@test.example",
            OwnerName: "First User"
        );

        var firstResult = await service.ProvisionAsync(request1);
        firstResult.Success.Should().BeTrue();

        await using var db = CreateOwnerDbContext();
        var tenantCountBefore = await db.Tenants.IgnoreQueryFilters().CountAsync();
        var branchCountBefore = await db.Branches.IgnoreQueryFilters().CountAsync();
        var roleCountBefore = await db.Roles.IgnoreQueryFilters().CountAsync();
        var userCountBefore = await db.Users.IgnoreQueryFilters().CountAsync();

        // Act: Second provision with exact same subdomain
        var request2 = new ProvisionRequest(
            Name: "Second Tenant",
            Subdomain: subdomain,
            OwnerEmail: $"second-{suffix}@test.example",
            OwnerName: "Second User"
        );

        var secondResult = await service.ProvisionAsync(request2);

        // Assert: Fails with exit code 2
        secondResult.Success.Should().BeFalse();
        secondResult.ExitCode.Should().Be(2);
        secondResult.Error.Should().Contain(subdomain);

        // Assert: No new records created
        var tenantCountAfter = await db.Tenants.IgnoreQueryFilters().CountAsync();
        var branchCountAfter = await db.Branches.IgnoreQueryFilters().CountAsync();
        var roleCountAfter = await db.Roles.IgnoreQueryFilters().CountAsync();
        var userCountAfter = await db.Users.IgnoreQueryFilters().CountAsync();

        tenantCountAfter.Should().Be(tenantCountBefore);
        branchCountAfter.Should().Be(branchCountBefore);
        roleCountAfter.Should().Be(roleCountBefore);
        userCountAfter.Should().Be(userCountBefore);
    }

    [Fact]
    public async Task InvalidSubdomainOrBranchCode_RejectedBeforeAnyWrite()
    {
        var service = new ProvisioningService(_fixture.OwnerConnectionString);
        await using var db = CreateOwnerDbContext();

        var tenantCountBefore = await db.Tenants.IgnoreQueryFilters().CountAsync();
        var branchCountBefore = await db.Branches.IgnoreQueryFilters().CountAsync();

        // Invalid subdomain (uppercase and special characters)
        var invalidSubdomainReq = new ProvisionRequest(
            Name: "Invalid Subdomain Test",
            Subdomain: "INVALID_SUB!",
            OwnerEmail: "owner@test.com",
            OwnerName: "Owner"
        );

        var actSubdomain = () => service.ProvisionAsync(invalidSubdomainReq);
        await actSubdomain.Should().ThrowAsync<ArgumentException>();

        // Invalid branch code (starts with 0, must be [A-Z1-9][A-Z0-9]{0,2})
        var invalidBranchReq = new ProvisionRequest(
            Name: "Invalid Branch Test",
            Subdomain: "valid-sub-" + Guid.NewGuid().ToString("N")[..6],
            OwnerEmail: "owner@test.com",
            OwnerName: "Owner",
            BranchCode: "01"
        );

        var actBranch = () => service.ProvisionAsync(invalidBranchReq);
        await actBranch.Should().ThrowAsync<ArgumentException>();

        // Ensure nothing was written
        var tenantCountAfter = await db.Tenants.IgnoreQueryFilters().CountAsync();
        var branchCountAfter = await db.Branches.IgnoreQueryFilters().CountAsync();

        tenantCountAfter.Should().Be(tenantCountBefore);
        branchCountAfter.Should().Be(branchCountBefore);
    }

    [Fact]
    public async Task TwoTenants_ProvisionedBackToBack_CannotSeeEachOthersRoles_ThroughAppRole()
    {
        // Arrange
        var service = new ProvisioningService(_fixture.OwnerConnectionString);

        var suffix1 = Guid.NewGuid().ToString("N")[..8];
        var tenant1Result = await service.ProvisionAsync(new ProvisionRequest(
            Name: "Tenant One",
            Subdomain: "t1-" + suffix1,
            OwnerEmail: $"t1-{suffix1}@test.com",
            OwnerName: "Owner One"
        ));
        tenant1Result.Success.Should().BeTrue();

        var suffix2 = Guid.NewGuid().ToString("N")[..8];
        var tenant2Result = await service.ProvisionAsync(new ProvisionRequest(
            Name: "Tenant Two",
            Subdomain: "t2-" + suffix2,
            OwnerEmail: $"t2-{suffix2}@test.com",
            OwnerName: "Owner Two"
        ));
        tenant2Result.Success.Should().BeTrue();

        // Act & Assert: Query under storeos_app using Tenant 1 scope
        var scope1 = new TestAccessScope { TenantId = tenant1Result.TenantId };
        var options1 = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope1))
            .Options;

        await using (var db1 = new StoreOsDbContext(options1, scope1))
        {
            await db1.Database.BeginTransactionAsync();
            var rolesTenant1 = await db1.Roles.ToListAsync();

            rolesTenant1.Should().HaveCount(5);
            rolesTenant1.Should().OnlyContain(r => r.TenantId == tenant1Result.TenantId);
            rolesTenant1.Should().NotContain(r => r.TenantId == tenant2Result.TenantId);
        }

        // Act & Assert: Query under storeos_app using Tenant 2 scope
        var scope2 = new TestAccessScope { TenantId = tenant2Result.TenantId };
        var options2 = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.AppConnectionString)
            .AddInterceptors(new RlsSessionInterceptor(scope2))
            .Options;

        await using (var db2 = new StoreOsDbContext(options2, scope2))
        {
            await db2.Database.BeginTransactionAsync();
            var rolesTenant2 = await db2.Roles.ToListAsync();

            rolesTenant2.Should().HaveCount(5);
            rolesTenant2.Should().OnlyContain(r => r.TenantId == tenant2Result.TenantId);
            rolesTenant2.Should().NotContain(r => r.TenantId == tenant1Result.TenantId);
        }
    }

    private class TestAccessScope : IAccessScope
    {
        public Guid TenantId { get; init; }
        public bool HasTenantContext => TenantId != Guid.Empty;
        public Guid UserId { get; init; } = Guid.NewGuid();
        public IReadOnlySet<Guid> BranchIds { get; init; } = new HashSet<Guid>();
        public bool AllBranches => true;
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;

        public bool HasBranchAccess(Guid branchId) => true;
        public bool HasPermission(string permissionCode) => true;
        public void UseAnonymousTenant(Guid tenantId) { }
        public void UseSystemContext(Guid tenantId) { }
    }
}
