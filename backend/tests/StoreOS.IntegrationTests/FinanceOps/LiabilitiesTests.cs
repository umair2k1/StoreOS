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
using StoreOS.Application.FinanceOps.Liabilities;
using StoreOS.Application.FinanceOps.Liabilities.CreateLiability;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.FinanceOps;

[Collection("postgres")]
public class LiabilitiesTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branchId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public LiabilitiesTests(PostgresTestFixture fixture)
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

        var tenant = Tenant.Create("Liabilities Test Tenant", subdomain: "lia-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch = Branch.Create(_tenantId, "LI1", "Liabilities Branch");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch, _branchId);
        db.Branches.Add(branch);

        var role = Role.Create(_tenantId, "LiabilityManager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "liabilities:manage");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "liamgr@test.com", passwordHash, "Liability Mgr");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        var ub = UserBranch.Create(_userId, _branchId, isDefault: true);
        db.UserBranches.Add(ub);

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
            "DELETE FROM liability_payments WHERE tenant_id = @tId; " +
            "DELETE FROM liabilities WHERE tenant_id = @tId; " +
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

    private string IssueToken(string[]? permissions = null)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId,
            _tenantId,
            allBranches: true,
            Array.Empty<Guid>(),
            permissions ?? new[] { "liabilities:manage" },
            tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task LiabilityLifecycle_Create_Repayments_Update_AndExceedsBalance_Succeeds()
    {
        var token = IssueToken();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        // (a) create a liability with TotalPrincipal=100000 -> CurrentBalance=100000 initially
        var createCmd = new CreateLiabilityCommand(
            Title: "HDFC Business Loan",
            Type: LiabilityType.Loan,
            LenderName: "HDFC Bank",
            TotalPrincipal: 100000m,
            DueDate: new DateOnly(2027, 3, 31),
            Notes: "Initial business credit");

        var createResp = await client.PostAsJsonAsync("/api/liabilities", createCmd);
        createResp.StatusCode.Should().Be(HttpStatusCode.Created);

        var createResult = await createResp.Content.ReadFromJsonAsync<JsonElement>(JsonOpts);
        var liabilityId = createResult.GetProperty("id").GetGuid();

        // Verify GET by ID initial balance
        var getResp1 = await client.GetAsync($"/api/liabilities/{liabilityId}");
        getResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto1 = await getResp1.Content.ReadFromJsonAsync<LiabilityDto>(JsonOpts);
        dto1.Should().NotBeNull();
        dto1!.TotalPrincipal.Should().Be(100000m);
        dto1.CurrentBalance.Should().Be(100000m);
        dto1.Title.Should().Be("HDFC Business Loan");
        dto1.Type.Should().Be(LiabilityType.Loan);

        // Verify GET list shows the liability with 100000 balance
        var listResp1 = await client.GetAsync("/api/liabilities");
        listResp1.StatusCode.Should().Be(HttpStatusCode.OK);
        var paged1 = await listResp1.Content.ReadFromJsonAsync<PagedResult<LiabilityDto>>(JsonOpts);
        paged1.Should().NotBeNull();
        paged1!.Items.Should().Contain(l => l.Id == liabilityId && l.CurrentBalance == 100000m);

        // (b) record a repayment of 20000 -> CurrentBalance=80000
        var repay1Resp = await client.PostAsJsonAsync($"/api/liabilities/{liabilityId}/repayments", new
        {
            Amount = 20000m,
            PaymentMode = "BankTransfer",
            PaymentDate = new DateOnly(2026, 10, 7),
            Notes = "Installment 1"
        });
        repay1Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp2 = await client.GetAsync($"/api/liabilities/{liabilityId}");
        getResp2.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto2 = await getResp2.Content.ReadFromJsonAsync<LiabilityDto>(JsonOpts);
        dto2!.TotalPrincipal.Should().Be(100000m);
        dto2.CurrentBalance.Should().Be(80000m);

        // (c) record a SECOND repayment of 30000 -> CurrentBalance=50000, and confirm there are TWO SEPARATE rows
        var repay2Resp = await client.PostAsJsonAsync($"/api/liabilities/{liabilityId}/repayments", new
        {
            Amount = 30000m,
            PaymentMode = "BankTransfer",
            PaymentDate = new DateOnly(2026, 11, 7),
            Notes = "Installment 2"
        });
        repay2Resp.StatusCode.Should().Be(HttpStatusCode.OK);

        var getResp3 = await client.GetAsync($"/api/liabilities/{liabilityId}");
        getResp3.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto3 = await getResp3.Content.ReadFromJsonAsync<LiabilityDto>(JsonOpts);
        dto3!.TotalPrincipal.Should().Be(100000m);
        dto3.CurrentBalance.Should().Be(50000m);

        // Confirm TWO SEPARATE liability_payments rows via superuser query (never one merged/updated row)
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;
        await using (var db = new StoreOsDbContext(options, new NullAccessScope()))
        {
            var payments = await db.LiabilityPayments.IgnoreQueryFilters()
                .Where(p => p.LiabilityId == liabilityId && p.TenantId == _tenantId)
                .OrderBy(p => p.CreatedAt)
                .ToListAsync();

            payments.Should().HaveCount(2, "two distinct repayments must be preserved as separate immutable rows");
            payments[0].Amount.Should().Be(20000m);
            payments[1].Amount.Should().Be(30000m);
        }

        // (d) UpdateLiability changing title/lender does NOT affect TotalPrincipal or CurrentBalance
        var patchResp = await client.PatchAsJsonAsync($"/api/liabilities/{liabilityId}", new
        {
            Title = "HDFC Commercial Loan (Updated)",
            LenderName = "HDFC Bank Ltd",
            DueDate = new DateOnly(2027, 4, 30),
            Status = "Active",
            Notes = "Revised lender branch"
        });
        patchResp.StatusCode.Should().Be(HttpStatusCode.NoContent);

        var getResp4 = await client.GetAsync($"/api/liabilities/{liabilityId}");
        getResp4.StatusCode.Should().Be(HttpStatusCode.OK);
        var dto4 = await getResp4.Content.ReadFromJsonAsync<LiabilityDto>(JsonOpts);
        dto4!.Title.Should().Be("HDFC Commercial Loan (Updated)");
        dto4.LenderName.Should().Be("HDFC Bank Ltd");
        dto4.TotalPrincipal.Should().Be(100000m); // strictly unchanged
        dto4.CurrentBalance.Should().Be(50000m); // strictly unchanged

        // (e) a repayment exceeding the current balance (balance is 50000, attempt 50000.01) -> repayment_exceeds_balance
        var exceedResp = await client.PostAsJsonAsync($"/api/liabilities/{liabilityId}/repayments", new
        {
            Amount = 50000.01m,
            PaymentMode = "Cash",
            PaymentDate = new DateOnly(2026, 12, 1)
        });
        exceedResp.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        var exceedBody = await exceedResp.Content.ReadAsStringAsync();
        exceedBody.Should().Contain("repayment_exceeds_balance");

        // Verify balance is STILL 50000 after failed attempt
        var getResp5 = await client.GetAsync($"/api/liabilities/{liabilityId}");
        var dto5 = await getResp5.Content.ReadFromJsonAsync<LiabilityDto>(JsonOpts);
        dto5!.CurrentBalance.Should().Be(50000m);
    }

    [Fact]
    public async Task CallerWithoutPermission_Returns403_OnEveryRoute()
    {
        // (f) a caller without liabilities:manage -> 403 on every route
        var tokenWithoutPerm = IssueToken(permissions: new[] { "some_other:permission" });
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokenWithoutPerm);
        client.DefaultRequestHeaders.Add("X-Branch-Id", _branchId.ToString());

        var randomId = Guid.NewGuid();

        // 1. POST /api/liabilities
        var r1 = await client.PostAsJsonAsync("/api/liabilities", new CreateLiabilityCommand(
            "Test", LiabilityType.Loan, "Bank", 50000m, null, null));
        r1.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 2. GET /api/liabilities
        var r2 = await client.GetAsync("/api/liabilities");
        r2.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 3. GET /api/liabilities/{id}
        var r3 = await client.GetAsync($"/api/liabilities/{randomId}");
        r3.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 4. PATCH /api/liabilities/{id}
        var r4 = await client.PatchAsJsonAsync($"/api/liabilities/{randomId}", new
        {
            Title = "Updated"
        });
        r4.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        // 5. POST /api/liabilities/{id}/repayments
        var r5 = await client.PostAsJsonAsync($"/api/liabilities/{randomId}/repayments", new
        {
            Amount = 1000m,
            PaymentMode = "Cash",
            PaymentDate = new DateOnly(2026, 10, 7)
        });
        r5.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }
}
