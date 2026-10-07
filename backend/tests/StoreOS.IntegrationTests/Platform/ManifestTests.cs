using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Platform;

[Collection("postgres")]
public class ManifestTests : IAsyncLifetime
{
    private readonly PostgresTestFixture _fixture;
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;

    private readonly Guid _tenantId = Guid.NewGuid();
    private readonly string _subdomain;

    public ManifestTests(PostgresTestFixture fixture)
    {
        _fixture = fixture;
        _subdomain = "apex-" + _tenantId.ToString("N")[..8];
        _factory = new WebApplicationFactory<Program>().WithWebHostBuilder(builder =>
        {
            builder.UseSetting("ConnectionStrings:Default", _fixture.AppConnectionString);
            builder.UseSetting("ConnectionStrings:Reporting", _fixture.AppConnectionString);
        });
        _client = _factory.CreateClient();
    }

    public async Task InitializeAsync()
    {
        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(_fixture.OwnerConnectionString)
            .Options;

        await using var db = new StoreOsDbContext(options, new NullAccessScope());

        var tenant = Tenant.Create("Apex Electronics", subdomain: _subdomain);
        typeof(Tenant).GetProperty(nameof(Tenant.Id))!.SetValue(tenant, _tenantId);
        db.Tenants.Add(tenant);

        await db.SaveChangesAsync();
        await _fixture.ApplyPostMigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var conn = new NpgsqlConnection(_fixture.OwnerConnectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM tenants WHERE id = @t;";
        cmd.Parameters.AddWithValue("t", _tenantId);
        await cmd.ExecuteNonQueryAsync();

        _client.Dispose();
        _factory.Dispose();
    }

    [Fact]
    public async Task GetManifest_WithValidTenantId_ReturnsCorrectManifestWithoutStoreOS()
    {
        var response = await _client.GetAsync($"/api/tenant/manifest.webmanifest?tenantId={_tenantId}");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/manifest+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Apex Electronics");

        // White-labeling constraint: must NEVER contain "StoreOS" for a named tenant
        body.Should().NotContain("StoreOS");

        using var doc = JsonDocument.Parse(body);
        var root = doc.RootElement;
        root.GetProperty("name").GetString().Should().Be("Apex Electronics");
        root.GetProperty("short_name").GetString().Should().Be("Apex Electro");
        root.GetProperty("display").GetString().Should().Be("standalone");
    }

    [Fact]
    public async Task GetManifest_WithInvalidTenantId_ReturnsNotFound()
    {
        var response = await _client.GetAsync($"/api/tenant/manifest.webmanifest?tenantId={Guid.NewGuid()}");
        response.StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task GetManifest_WithHostHeader_ResolvesTenantFromSubdomain()
    {
        var request = new HttpRequestMessage(HttpMethod.Get, $"http://{_subdomain}.localhost/api/tenant/manifest.webmanifest");

        var response = await _client.SendAsync(request);

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        response.Content.Headers.ContentType?.MediaType.Should().Be("application/manifest+json");

        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Apex Electronics");
        body.Should().NotContain("StoreOS");
    }
}
