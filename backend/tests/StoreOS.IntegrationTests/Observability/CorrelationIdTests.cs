using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using StoreOS.Api.Middleware;

namespace StoreOS.IntegrationTests.Observability;

public class CorrelationIdTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public CorrelationIdTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.CreateClient();
    }

    [Fact]
    public async Task Request_WithNoCorrelationId_GeneratesFreshGuid()
    {
        var response = await _client.GetAsync("/health/live");

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).Should().BeTrue();
        var headerValue = values!.Single();

        Guid.TryParseExact(headerValue, "D", out var parsed).Should().BeTrue();
        parsed.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Request_WithValidCorrelationId_EchoesExactValue(bool uppercase)
    {
        var guidStr = Guid.NewGuid().ToString("D");
        var customId = uppercase ? guidStr.ToUpperInvariant() : guidStr.ToLowerInvariant();
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, customId);

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).Should().BeTrue();
        values!.Single().Should().Be(customId);
    }

    [Fact]
    public async Task Request_WithGarbageCorrelationId_GeneratesNewGuid()
    {
        const string garbage = "not-a-valid-guid-12345";
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, garbage);

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).Should().BeTrue();
        var headerValue = values!.Single();

        headerValue.Should().NotBe(garbage);
        Guid.TryParseExact(headerValue, "D", out var parsed).Should().BeTrue();
        parsed.Should().NotBeEmpty();
    }

    [Theory]
    [InlineData("N")]
    [InlineData("B")]
    [InlineData("P")]
    public async Task Request_WithNonDFormatCorrelationId_GeneratesNewDFormatGuid(string format)
    {
        var rawGuid = Guid.NewGuid().ToString(format);
        using var request = new HttpRequestMessage(HttpMethod.Get, "/health/live");
        request.Headers.Add(CorrelationIdMiddleware.HeaderName, rawGuid);

        var response = await _client.SendAsync(request);

        response.Headers.TryGetValues(CorrelationIdMiddleware.HeaderName, out var values).Should().BeTrue();
        var headerValue = values!.Single();

        headerValue.Should().NotBe(rawGuid);
        Guid.TryParseExact(headerValue, "D", out var parsed).Should().BeTrue();
        parsed.Should().NotBeEmpty();
    }
}
