using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using FluentValidation.Results;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Domain.Exceptions;

namespace StoreOS.IntegrationTests.Errors;

[ApiController]
[Route("test/errors")]
[AllowAnonymous]
public class TestErrorsController : ControllerBase
{
    [HttpGet("validation")]
    public IActionResult ThrowValidation()
    {
        var failures = new List<ValidationFailure>
        {
            new("Email", "Email is required."),
            new("Email", "Email format is invalid."),
            new("Password", "Password too short.")
        };
        throw new ValidationException(failures);
    }

    [HttpGet("domain")]
    public IActionResult ThrowDomain()
    {
        throw new DomainException("test_code", "test message");
    }

    [HttpGet("not-found")]
    public IActionResult ThrowNotFound()
    {
        throw new DomainException("order_not_found", "Order was not found");
    }

    [HttpGet("conflict")]
    public IActionResult ThrowConflict()
    {
        throw new DomainException("serialization_conflict", "Transaction serialization failure");
    }

    [HttpGet("generic")]
    public IActionResult ThrowGeneric()
    {
        throw new InvalidOperationException("Sensitive internal database connection failed: secret_user:secret_password");
    }
}

public class ExceptionHandlingTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly HttpClient _client;

    public ExceptionHandlingTests(WebApplicationFactory<Program> factory)
    {
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(TestErrorsController).Assembly);
            });
        }).CreateClient();
    }

    [Fact]
    public async Task ValidationException_Returns400_WithValidationProblemDetails()
    {
        var response = await _client.GetAsync("/test/errors/validation");

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetInt32().Should().Be(400);
        body.GetProperty("title").GetString().Should().Be("One or more fields are invalid.");
        body.GetProperty("code").GetString().Should().Be("validation_error");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();

        var errors = body.GetProperty("errors");
        errors.TryGetProperty("Email", out var emailErrors).Should().BeTrue();
        emailErrors.GetArrayLength().Should().Be(2);

        errors.TryGetProperty("Password", out var passwordErrors).Should().BeTrue();
        passwordErrors.GetArrayLength().Should().Be(1);
    }

    [Fact]
    public async Task DomainException_Returns422_WithCodeAndCorrelationId()
    {
        var response = await _client.GetAsync("/test/errors/domain");

        response.StatusCode.Should().Be(HttpStatusCode.UnprocessableEntity);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetInt32().Should().Be(422);
        body.GetProperty("title").GetString().Should().Be("test message");
        body.GetProperty("code").GetString().Should().Be("test_code");
        body.GetProperty("type").GetString().Should().Be("https://storeos.io/errors/test_code");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task NotFoundDomainException_Returns404_PerErrorCatalog()
    {
        var response = await _client.GetAsync("/test/errors/not-found");

        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();

        body.GetProperty("status").GetInt32().Should().Be(404);
        body.GetProperty("code").GetString().Should().Be("order_not_found");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public async Task SerializationConflictDomainException_Returns409_WithRetryAfterHeader()
    {
        var response = await _client.GetAsync("/test/errors/conflict");

        response.StatusCode.Should().Be(HttpStatusCode.Conflict);
        response.Headers.Contains("Retry-After").Should().BeTrue();
        response.Headers.GetValues("Retry-After").Single().Should().Be("1");

        var body = await response.Content.ReadFromJsonAsync<JsonElement>();
        body.GetProperty("status").GetInt32().Should().Be(409);
        body.GetProperty("code").GetString().Should().Be("serialization_conflict");
    }

    [Fact]
    public async Task GenericException_Returns500_WithoutLeakingMessageOrStackTrace()
    {
        var response = await _client.GetAsync("/test/errors/generic");

        response.StatusCode.Should().Be(HttpStatusCode.InternalServerError);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var rawBody = await response.Content.ReadAsStringAsync();
        rawBody.Should().NotContain("secret_user");
        rawBody.Should().NotContain("secret_password");
        rawBody.Should().NotContain("StackTrace");
        rawBody.Should().NotContain("InvalidOperationException");

        var body = JsonSerializer.Deserialize<JsonElement>(rawBody);
        body.GetProperty("status").GetInt32().Should().Be(500);
        body.GetProperty("title").GetString().Should().Be("An unexpected error occurred.");
        body.GetProperty("code").GetString().Should().Be("internal_error");
        body.GetProperty("correlationId").GetString().Should().NotBeNullOrWhiteSpace();
    }
}
