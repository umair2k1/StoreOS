using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Infrastructure.Tenancy;

namespace StoreOS.IntegrationTests.Tenancy;

public class TenantResolverTests : IAsyncLifetime
{
    private readonly StoreOsDbContext _dbContext;
    private readonly TenantResolver _resolver;
    private Guid _apexTenantId;

    public TenantResolverTests()
    {
        var cs = Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password";

        var options = new DbContextOptionsBuilder<StoreOsDbContext>()
            .UseNpgsql(cs)
            .Options;

        _dbContext = new StoreOsDbContext(options);

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Tenancy:BaseDomains:0"] = "pos.storeos.io",
                ["Tenancy:BaseDomains:1"] = "localhost"
            })
            .Build();

        _resolver = new TenantResolver(_dbContext, config);
    }

    public async Task InitializeAsync()
    {
        var existing = await _dbContext.Tenants.FirstOrDefaultAsync(t => t.Subdomain == "apex");
        if (existing is null)
        {
            var tenant = Tenant.Create("Apex Retail", subdomain: "apex");
            _dbContext.Tenants.Add(tenant);
            await _dbContext.SaveChangesAsync();
            _apexTenantId = tenant.Id;
        }
        else
        {
            _apexTenantId = existing.Id;
        }
    }

    public async Task DisposeAsync()
    {
        await _dbContext.DisposeAsync();
    }

    [Theory]
    [InlineData("apex.pos.storeos.io")]
    [InlineData("apex.localhost:4200")]
    public async Task ResolveFromHostAsync_ValidHost_ReturnsTenantProfile(string host)
    {
        var profile = await _resolver.ResolveFromHostAsync(host);

        profile.Should().NotBeNull();
        profile!.TenantId.Should().Be(_apexTenantId);
        profile.Subdomain.Should().Be("apex");
        profile.Name.Should().Be("Apex Retail");
        profile.IsActive.Should().BeTrue();
    }

    [Theory]
    [InlineData("unknown.pos.storeos.io")]
    [InlineData("pos.storeos.io")]
    [InlineData("localhost")]
    [InlineData("evil.com")]
    [InlineData("")]
    [InlineData(null)]
    public async Task ResolveFromHostAsync_InvalidOrUnknownHost_ReturnsNull(string? host)
    {
        var profile = await _resolver.ResolveFromHostAsync(host!);
        profile.Should().BeNull();
    }

    [Fact]
    public async Task ResolveByIdAsync_ExistingId_ReturnsProfile()
    {
        var profile = await _resolver.ResolveByIdAsync(_apexTenantId);

        profile.Should().NotBeNull();
        profile!.Subdomain.Should().Be("apex");
        profile.TenantId.Should().Be(_apexTenantId);
    }

    [Fact]
    public async Task ResolveByIdAsync_NonExistentId_ReturnsNull()
    {
        var profile = await _resolver.ResolveByIdAsync(Guid.NewGuid());
        profile.Should().BeNull();
    }
}
