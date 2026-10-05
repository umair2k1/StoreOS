using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Identity.Login;

namespace StoreOS.IntegrationTests.RateLimiting;

public class RateLimitingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public RateLimitingTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task LoginRateLimiter_Allows5FailedAttempts_Rejects6thWith429()
    {
        // Use an isolated WebApplicationFactory to guarantee clean partition state
        using var isolatedFactory = new WebApplicationFactory<Program>();
        using var client = isolatedFactory.CreateClient();

        var badLogin = new LoginCommand("wrong@test.com", "BadPassword123!", Guid.NewGuid());

        // First 5 attempts should reach the endpoint and return 401 Unauthorized
        for (var i = 1; i <= 5; i++)
        {
            var response = await client.PostAsJsonAsync("/api/auth/login", badLogin);
            response.StatusCode.Should().Be(HttpStatusCode.Unauthorized, $"attempt {i} should be allowed through to auth");
        }

        // 6th attempt in the same window must be rate-limited with 429
        var rejectedResponse = await client.PostAsJsonAsync("/api/auth/login", badLogin);
        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var json = await rejectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetInt32().Should().Be(429);
        json.GetProperty("code").GetString().Should().Be("rate_limited");
    }

    [Fact]
    public async Task GlobalRateLimiter_Allows200AuthenticatedRequests_Rejects201stWith429()
    {
        using var isolatedFactory = new WebApplicationFactory<Program>();
        using var client = isolatedFactory.CreateClient();

        var userId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();

        string token;
        using (var scope = isolatedFactory.Services.CreateScope())
        {
            var jwtService = scope.ServiceProvider.GetRequiredService<IJwtTokenService>();
            (token, _) = jwtService.IssueAccessToken(
                userId, tenantId, allBranches: true, Array.Empty<Guid>(), Array.Empty<string>(), tokenVersion: 1);
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // First 200 requests within the 1-minute window should succeed (200 OK)
        for (var i = 1; i <= 200; i++)
        {
            var response = await client.GetAsync("/api/auth/me");
            response.StatusCode.Should().Be(HttpStatusCode.OK, $"request {i} should be permitted");
        }

        // 201st request as the same user should be rejected with 429 TooManyRequests
        var rejectedResponse = await client.GetAsync("/api/auth/me");
        rejectedResponse.StatusCode.Should().Be(HttpStatusCode.TooManyRequests);

        var json = await rejectedResponse.Content.ReadFromJsonAsync<JsonElement>();
        json.GetProperty("status").GetInt32().Should().Be(429);
        json.GetProperty("code").GetString().Should().Be("rate_limited");
    }
}
