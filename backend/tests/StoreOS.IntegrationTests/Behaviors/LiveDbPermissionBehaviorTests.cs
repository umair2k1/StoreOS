using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Behaviors;

#region Test Command & Controller for LiveDbPermissionBehavior

public record VoidSaleProbeCommand(string SaleId) : IRequest<string>, IHighImpactOperation
{
    public string RequiredPermission => "sales:void";
}

public class VoidSaleProbeHandler : IRequestHandler<VoidSaleProbeCommand, string>
{
    public Task<string> Handle(VoidSaleProbeCommand request, CancellationToken ct)
        => Task.FromResult($"voided:{request.SaleId}");
}

[ApiController]
[Route("test/high-impact")]
public class TestHighImpactController : ControllerBase
{
    private readonly IMediator _mediator;
    public TestHighImpactController(IMediator mediator) => _mediator = mediator;

    [HttpPost("void")]
    public async Task<IActionResult> Void([FromBody] VoidSaleProbeCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(new { message = result });
    }
}

#endregion

[Collection("postgres")]
public class LiveDbPermissionBehaviorTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly Argon2PasswordHasher _hasher = new();

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();
    private readonly Guid _roleId = Guid.NewGuid();
    private const string Password = "Password123!";

    public LiveDbPermissionBehaviorTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TestHighImpactController).Assembly);
                services.AddTransient<IRequestHandler<VoidSaleProbeCommand, string>, VoidSaleProbeHandler>();
            });
        });

        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var passwordHash = _hasher.Hash(Password);

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("High Impact Tenant", subdomain: "hi-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var role = Role.Create(_tenantId, "Manager");
        typeof(Role).GetProperty(nameof(Role.Id))!.SetValue(role, _roleId);
        db.Roles.Add(role);

        var perm = RolePermission.Create(_roleId, "sales:void");
        db.RolePermissions.Add(perm);

        var user = User.Create(_tenantId, "manager@test.com", passwordHash, "Manager User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        await db.SaveChangesAsync();

        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand(
            "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES (@uId, @rId, NULL);", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        await cmd.ExecuteNonQueryAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM user_role_assignments WHERE user_id = @uId; " +
            "DELETE FROM role_permissions WHERE role_id = @rId; " +
            "DELETE FROM roles WHERE tenant_id = @tId; " +
            "DELETE FROM users WHERE tenant_id = @tId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("rId", _roleId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(IEnumerable<string> permissions)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), permissions, tokenVersion: 1);
        return token;
    }

    [Fact]
    public async Task UserWithSalesVoidPermission_Succeeds()
    {
        var token = IssueToken(new[] { "sales:void" });

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/high-impact/void")
        {
            Content = JsonContent.Create(new VoidSaleProbeCommand("sale-001"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("message").GetString().Should().Be("voided:sale-001");
    }

    [Fact]
    public async Task UserWithoutSalesVoidPermission_Returns403Forbidden()
    {
        // Issue token WITHOUT sales:void
        var token = IssueToken(new[] { "pos:checkout" });

        // Temporarily delete sales:void from DB as well
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM role_permissions WHERE role_id = @rId AND permission_code = 'sales:void';", conn))
        {
            cmd.Parameters.AddWithValue("rId", _roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/high-impact/void")
        {
            Content = JsonContent.Create(new VoidSaleProbeCommand("sale-002"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Fact]
    public async Task RevokedPermissionInDb_WithStaleJwtCarryingClaim_RejectsImmediatelyWith403Forbidden()
    {
        // 1. Issue JWT with sales:void while DB still has it
        var tokenWithVoidClaim = IssueToken(new[] { "sales:void" });

        // 2. Revoke permission directly in the database WITHOUT issuing a new JWT
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "DELETE FROM role_permissions WHERE role_id = @rId AND permission_code = 'sales:void';", conn))
        {
            cmd.Parameters.AddWithValue("rId", _roleId);
            await cmd.ExecuteNonQueryAsync();
        }

        // 3. Immediately retry high-impact endpoint using the still-valid JWT that contains sales:void in claims
        var request = new HttpRequestMessage(HttpMethod.Post, "/test/high-impact/void")
        {
            Content = JsonContent.Create(new VoidSaleProbeCommand("sale-003"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", tokenWithVoidClaim);

        var response = await _client.SendAsync(request);

        // Must reject IMMEDIATELY (zero latency, bypassing JWT claims and 30s cache)
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Fact]
    public async Task DeactivatedUserInDb_WithStaleValidJwt_RejectsImmediatelyWith403Forbidden()
    {
        var token = IssueToken(new[] { "sales:void" });

        // Deactivate user in DB mid-session
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using (var cmd = new NpgsqlCommand(
            "UPDATE users SET is_active = false WHERE id = @uId;", conn))
        {
            cmd.Parameters.AddWithValue("uId", _userId);
            await cmd.ExecuteNonQueryAsync();
        }

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/high-impact/void")
        {
            Content = JsonContent.Create(new VoidSaleProbeCommand("sale-004"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("forbidden");
    }
}
