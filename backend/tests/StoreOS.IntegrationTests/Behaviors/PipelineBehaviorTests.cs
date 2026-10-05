using System.Net;
using Microsoft.AspNetCore.Authorization;
using System.Net.Http.Json;
using System.Text.Json;
using FluentAssertions;
using FluentValidation;
using MediatR;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using StoreOS.Application.Abstractions;
using StoreOS.IntegrationTests.Fixtures;
using StoreOS.IntegrationTests.Security;

namespace StoreOS.IntegrationTests.Behaviors;

public record TestCommand(string Name) : IRequest<string>, IRequiresPermission
{
    public string RequiredPermission => "test:permission";
}

public class TestCommandValidator : AbstractValidator<TestCommand>
{
    public TestCommandValidator()
    {
        RuleFor(x => x.Name).NotEmpty().WithMessage("Name is required.");
    }
}

public class TestCommandHandler : IRequestHandler<TestCommand, string>
{
    public Task<string> Handle(TestCommand request, CancellationToken cancellationToken)
    {
        return Task.FromResult($"Hello, {request.Name}!");
    }
}

[ApiController]
[Route("test/pipeline")]
[AllowAnonymous]
public class TestPipelineController : ControllerBase
{
    private readonly IMediator _mediator;

    public TestPipelineController(IMediator mediator) => _mediator = mediator;

    [HttpPost]
    public async Task<IActionResult> Execute([FromBody] TestCommand command)
    {
        var result = await _mediator.Send(command);
        return Ok(new { message = result });
    }
}

public class PipelineBehaviorTests : IClassFixture<StoreOsWebApplicationFactory>
{
    private readonly StoreOsWebApplicationFactory _factory;
    private readonly HttpClient _client;

    public PipelineBehaviorTests(StoreOsWebApplicationFactory factory)
    {
        _factory = factory;
        _client = factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureTestServices(services =>
            {
                services.AddControllers()
                    .AddApplicationPart(typeof(TestPipelineController).Assembly);
                services.AddValidatorsFromAssemblyContaining<TestCommandValidator>();
                services.AddTransient<IRequestHandler<TestCommand, string>, TestCommandHandler>();
            });
        }).CreateClient();
    }

    [Fact]
    public async Task Unauthenticated_request_returns_401_unauthenticated()
    {
        TestAuthHandler.CurrentPrincipal.Value = null;

        var response = await _client.PostAsJsonAsync("/test/pipeline", new TestCommand("valid-name"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("code").GetString().Should().Be("unauthenticated");
    }

    [Fact]
    public async Task Authenticated_without_required_permission_returns_403_forbidden()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/pipeline")
        {
            Content = JsonContent.Create(new TestCommand("valid-name"))
        };
        request.Headers.Add("X-Test-Auth", "true");
        request.Headers.Add("X-Test-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Perms", "different:permission");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("code").GetString().Should().Be("forbidden");
    }

    [Fact]
    public async Task Authenticated_with_permission_but_invalid_payload_returns_400_validation_error()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/pipeline")
        {
            Content = JsonContent.Create(new TestCommand(""))
        };
        request.Headers.Add("X-Test-Auth", "true");
        request.Headers.Add("X-Test-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Perms", "test:permission");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        response.Content.Headers.ContentType!.MediaType.Should().Be("application/problem+json");

        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("code").GetString().Should().Be("validation_error");
        root.GetProperty("errors").TryGetProperty("Name", out _).Should().BeTrue();
    }

    [Fact]
    public async Task Authenticated_permitted_and_valid_returns_200_success()
    {
        var tenantId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var request = new HttpRequestMessage(HttpMethod.Post, "/test/pipeline")
        {
            Content = JsonContent.Create(new TestCommand("valid-name"))
        };
        request.Headers.Add("X-Test-Auth", "true");
        request.Headers.Add("X-Test-Tenant-Id", tenantId.ToString());
        request.Headers.Add("X-Test-User-Id", userId.ToString());
        request.Headers.Add("X-Test-Perms", "test:permission");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("message").GetString().Should().Be("Hello, valid-name!");
    }
}
