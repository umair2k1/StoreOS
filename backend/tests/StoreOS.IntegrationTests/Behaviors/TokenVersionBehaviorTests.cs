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
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Behaviors;

#region Test Controller & Command for MediatR Pipeline Testing

public record VersionProbeCommand(string Message) : IRequest<string>;

public class VersionProbeHandler : IRequestHandler<VersionProbeCommand, string>
{
    public Task<string> Handle(VersionProbeCommand request, CancellationToken ct)
        => Task.FromResult($"echo:{request.Message}");
}

[ApiController]
[Route("test/token-version")]
public class TestTokenVersionController : ControllerBase
{
    private readonly IMediator _mediator;
    public TestTokenVersionController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Execute([FromBody] VersionProbeCommand command, CancellationToken ct)
    {
        var result = await _mediator.Send(command, ct);
        return Ok(new { message = result });
    }
}

#endregion

[Collection("postgres")]
public class TokenVersionBehaviorTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly Guid _userId = Guid.NewGuid();

    public TokenVersionBehaviorTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;

        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers().AddApplicationPart(typeof(TestTokenVersionController).Assembly);
                services.AddTransient<IRequestHandler<VersionProbeCommand, string>, VersionProbeHandler>();
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

        var tenant = Tenant.Create("TV Tenant", subdomain: "tv-" + _tenantId.ToString("N")[..8]);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        var user = User.Create(_tenantId, "tv-user@test.com", "hash", "TV User");
        typeof(User).GetProperty(nameof(User.Id))!.SetValue(user, _userId);
        db.Users.Add(user);

        await db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();

        await using var cmd = new NpgsqlCommand(
            "DELETE FROM users WHERE id = @uId; " +
            "DELETE FROM tenants WHERE id = @tId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        cmd.Parameters.AddWithValue("tId", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    private string IssueToken(int tokenVersion)
    {
        using var scope = _factory.Services.CreateScope();
        var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
        var (token, _) = jwtService.IssueAccessToken(
            _userId, _tenantId, allBranches: true, Array.Empty<Guid>(), Array.Empty<string>(), tokenVersion);
        return token;
    }

    [Fact]
    public async Task ValidToken_MatchesDbTokenVersion_PassesThroughPipeline()
    {
        var token = IssueToken(tokenVersion: 1);

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/token-version")
        {
            Content = JsonContent.Create(new VersionProbeCommand("hello"))
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        response.StatusCode.Should().Be(HttpStatusCode.OK);

        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("message").GetString().Should().Be("echo:hello");
    }

    [Fact]
    public async Task BumpTokenVersionInDb_WhileCacheValid_ContinuesToSucceed_Within30sWindow()
    {
        // 1. Initial request populates the 30-second cache with token_version = 1
        var token = IssueToken(tokenVersion: 1);

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/test/token-version")
        {
            Content = JsonContent.Create(new VersionProbeCommand("step1"))
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response1 = await _client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Directly bump user token_version in DB to 2 (simulating out-of-band revocation or deactivation)
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE users SET token_version = 2 WHERE id = @uId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        await cmd.ExecuteNonQueryAsync();

        // 3. Immediately retry: cache still holds version 1 (within the Plan §8.3 30-second window tradeoff),
        // so this call still succeeds until cache TTL expires.
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/test/token-version")
        {
            Content = JsonContent.Create(new VersionProbeCommand("step2"))
        };
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response2 = await _client.SendAsync(request2);
        response2.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task BumpTokenVersionInDb_AfterCacheEviction_ThrowsTokenVersionStale_Returns401()
    {
        // 1. Initial request populates the 30-second cache
        var token = IssueToken(tokenVersion: 1);

        var request1 = new HttpRequestMessage(HttpMethod.Post, "/test/token-version")
        {
            Content = JsonContent.Create(new VersionProbeCommand("step1"))
        };
        request1.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response1 = await _client.SendAsync(request1);
        response1.StatusCode.Should().Be(HttpStatusCode.OK);

        // 2. Directly bump user token_version in DB to 2
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand("UPDATE users SET token_version = 2 WHERE id = @uId;", conn);
        cmd.Parameters.AddWithValue("uId", _userId);
        await cmd.ExecuteNonQueryAsync();

        // 3. Directly evict the cache key to simulate the 30-second TTL expiration deterministically
        var cache = _factory.Services.GetRequiredService<IMemoryCache>();
        cache.Remove($"token_version:{_userId}");

        // 4. Retry with stale token: TokenVersionBehavior re-queries DB, detects mismatch, throws token_version_stale
        var request2 = new HttpRequestMessage(HttpMethod.Post, "/test/token-version")
        {
            Content = JsonContent.Create(new VersionProbeCommand("step2"))
        };
        request2.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var response2 = await _client.SendAsync(request2);

        response2.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var json = await response2.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("code").GetString().Should().Be("token_version_stale");
    }
}
