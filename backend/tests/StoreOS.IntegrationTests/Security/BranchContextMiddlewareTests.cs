using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Api.Middleware;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Security;

#region Test Controller for BranchContextMiddleware

[ApiController]
[Route("test/branch-context")]
public class TestBranchContextController : ControllerBase
{
    private readonly IBranchContext _branchContext;
    public TestBranchContextController(IBranchContext branchContext) => _branchContext = branchContext;

    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new
        {
            hasBranch = _branchContext.HasBranchContext,
            branchId = _branchContext.CurrentBranchId
        });
    }

    [HttpPost]
    public IActionResult Post()
    {
        return Ok(new
        {
            hasBranch = _branchContext.HasBranchContext,
            branchId = _branchContext.CurrentBranchId
        });
    }
}

#endregion

[Collection("postgres")]
public class BranchContextMiddlewareTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _branch1Id = Guid.NewGuid();
    private readonly Guid _branch2Id = Guid.NewGuid();

    private readonly Guid _userWithoutBranchesId = Guid.NewGuid();
    private readonly Guid _userWithSingleBranchId = Guid.NewGuid();

    public BranchContextMiddlewareTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TestBranchContextController).Assembly);
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Branch Context Tenant", subdomain: "bc-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var branch1 = Branch.Create(_tenantId, "B01", "Branch One");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch1, _branch1Id);
        db.Branches.Add(branch1);

        var branch2 = Branch.Create(_tenantId, "B02", "Branch Two");
        typeof(Branch).GetProperty(nameof(Branch.Id))!.SetValue(branch2, _branch2Id);
        db.Branches.Add(branch2);

        var userNoBranch = User.Create(_tenantId, "no-branch@test.com", "hash", "No Branch User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userNoBranch, _userWithoutBranchesId);
        db.Users.Add(userNoBranch);

        var userSingleBranch = User.Create(_tenantId, "single-branch@test.com", "hash", "Single Branch User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(userSingleBranch, _userWithSingleBranchId);
        db.Users.Add(userSingleBranch);

        var ubSingle = UserBranch.Create(_userWithSingleBranchId, _branch1Id, isDefault: true);
        db.UserBranches.Add(ubSingle);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_branches WHERE user_id IN (@u1, @u2); " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM branches WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("u1", _userWithoutBranchesId);
        cmd.Parameters.AddWithValue("u2", _userWithSingleBranchId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(Guid userId, bool allBranches, IEnumerable<Guid> branchIds)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            userId, _tenantId, allBranches, branchIds, Array.Empty<string>(), tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task Get_WithoutHeader_AndWithoutDefaultBranch_SucceedsWithNullBranchId()
    {
        var token = IssueToken(_userWithoutBranchesId, allBranches: false, Array.Empty<Guid>());

        var request = new HttpRequestMessage(HttpMethod.Get, "/test/branch-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("hasBranch").GetBoolean().Should().BeFalse();
        json.GetProperty("branchId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Post_WithoutHeader_AndWithoutDefaultBranch_Returns400BranchContextRequired()
    {
        var token = IssueToken(_userWithoutBranchesId, allBranches: false, Array.Empty<Guid>());

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/branch-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("branch_context_required");
    }

    [Fact]
    public async Task Post_WithValidHeader_UserHasAccess_SucceedsWithBranchId()
    {
        var token = IssueToken(_userWithSingleBranchId, allBranches: false, new[] { _branch1Id });

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/branch-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branch1Id.ToString());
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("hasBranch").GetBoolean().Should().BeTrue();
        json.GetProperty("branchId").GetGuid().Should().Be(_branch1Id);
    }

    [Fact]
    public async Task Post_WithBranchHeader_UserLacksAccess_Returns403Forbidden()
    {
        var token = IssueToken(_userWithSingleBranchId, allBranches: false, new[] { _branch1Id });

        // User only has access to branch 1, attempts branch 2
        var request = new HttpRequestMessage(HttpMethod.Post, "/test/branch-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Headers.Add(BranchContextMiddleware.HeaderName, _branch2Id.ToString());
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Fact]
    public async Task Post_WithoutHeader_SingleUserBranch_AutoDefaultsEvenForMutation()
    {
        var token = IssueToken(_userWithSingleBranchId, allBranches: false, new[] { _branch1Id });

        // No X-Branch-Id header provided
        var request = new HttpRequestMessage(HttpMethod.Post, "/test/branch-context");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new { });

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("hasBranch").GetBoolean().Should().BeTrue();
        json.GetProperty("branchId").GetGuid().Should().Be(_branch1Id);
    }
}
