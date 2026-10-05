using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace StoreOS.IntegrationTests.Security;

public class QueryFilterTests : IAsyncLifetime
{
    private readonly string _connectionString;
    private readonly DbContextOptions<StoreOS.Infrastructure.Persistence.StoreOsDbContext> _options;

    private Tenant _tenantA = default!;
    private Tenant _tenantB = default!;
    private Branch _branchA1 = default!;
    private Branch _branchA2 = default!;
    private Branch _branchB1 = default!;
    private Setting _settingA = default!;
    private Setting _settingB = default!;

    public QueryFilterTests()
    {
        _connectionString = Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
            ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password";

        _options = new DbContextOptionsBuilder<StoreOS.Infrastructure.Persistence.StoreOsDbContext>()
            .UseNpgsql(_connectionString)
            .Options;
    }

    private sealed class FakeAccessScope : IAccessScope
    {
        public FakeAccessScope(Guid tenantId)
        {
            TenantId = tenantId;
        }

        public Guid TenantId { get; private set; }
        public Guid UserId => Guid.NewGuid();
        public IReadOnlySet<Guid> BranchIds => new HashSet<Guid>();
        public bool AllBranches => false;
        public IReadOnlySet<string> Permissions => new HashSet<string>();
        public int TokenVersion => 1;
        public bool IsAuthenticated => true;
        public bool HasTenantContext => true;

        public void UseAnonymousTenant(Guid tenantId) => TenantId = tenantId;
        public void UseSystemContext(Guid tenantId) => TenantId = tenantId;
        public bool HasBranchAccess(Guid branchId) => false;
        public bool HasPermission(string permissionCode) => false;
    }

    public async Task InitializeAsync()
    {
        await using var seedContext = new StoreOsDbContext(_options, new NullAccessScope());

        var suffix = Guid.NewGuid().ToString("N")[..8];
        _tenantA = Tenant.Create("Tenant A " + suffix, "ta-" + suffix);
        _tenantB = Tenant.Create("Tenant B " + suffix, "tb-" + suffix);

        seedContext.Tenants.AddRange(_tenantA, _tenantB);
        await seedContext.SaveChangesAsync();

        _branchA1 = Branch.Create(_tenantA.Id, "A01", "Tenant A Branch 1");
        _branchA2 = Branch.Create(_tenantA.Id, "A02", "Tenant A Branch 2");
        _branchB1 = Branch.Create(_tenantB.Id, "B01", "Tenant B Branch 1");

        seedContext.Branches.AddRange(_branchA1, _branchA2, _branchB1);
        await seedContext.SaveChangesAsync();
        _settingA = Setting.Create(_tenantA.Id, _branchA1.Id, "shop_profile", "{\"name\":\"Tenant A\"}");
        _settingB = Setting.Create(_tenantB.Id, _branchB1.Id, "shop_profile", "{\"name\":\"Tenant B\"}");

        seedContext.Settings.AddRange(_settingA, _settingB);
        await seedContext.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await using var cleanupContext = new StoreOsDbContext(_options, new NullAccessScope());

        cleanupContext.Settings.RemoveRange(_settingA, _settingB);
        cleanupContext.Branches.RemoveRange(_branchA1, _branchA2, _branchB1);
        cleanupContext.Tenants.RemoveRange(_tenantA, _tenantB);

        await cleanupContext.SaveChangesAsync();
    }

    [Fact]
    public async Task QueryBranches_ScopedToTenantB_ReturnsOnlyTenantBRows()
    {
        await using var contextB = new StoreOsDbContext(_options, new FakeAccessScope(_tenantB.Id));

        var branches = await contextB.Branches.ToListAsync();

        branches.Should().ContainSingle(b => b.Id == _branchB1.Id);
        branches.Should().NotContain(b => b.Id == _branchA1.Id);
        branches.Should().NotContain(b => b.Id == _branchA2.Id);
        branches.Should().OnlyContain(b => b.TenantId == _tenantB.Id);
    }

    [Fact]
    public async Task QueryBranches_ScopedToTenantA_ReturnsOnlyTenantARows()
    {
        await using var contextA = new StoreOsDbContext(_options, new FakeAccessScope(_tenantA.Id));

        var branches = await contextA.Branches.ToListAsync();

        branches.Should().HaveCount(2);
        branches.Should().Contain(b => b.Id == _branchA1.Id);
        branches.Should().Contain(b => b.Id == _branchA2.Id);
        branches.Should().NotContain(b => b.Id == _branchB1.Id);
        branches.Should().OnlyContain(b => b.TenantId == _tenantA.Id);
    }

    [Fact]
    public async Task QuerySettings_ScopedToTenantB_ReturnsOnlyTenantBRows()
    {
        await using var contextB = new StoreOsDbContext(_options, new FakeAccessScope(_tenantB.Id));

        var settings = await contextB.Settings.ToListAsync();

        settings.Should().ContainSingle(s => s.Key == "shop_profile" && s.TenantId == _tenantB.Id);
        settings.Should().NotContain(s => s.TenantId == _tenantA.Id);
    }

    [Fact]
    public async Task IgnoreQueryFilters_ReturnsAllTenantsRows()
    {
        await using var contextB = new StoreOsDbContext(_options, new FakeAccessScope(_tenantB.Id));

        var allBranches = await contextB.Branches.IgnoreQueryFilters().ToListAsync();

        allBranches.Should().Contain(b => b.Id == _branchA1.Id);
        allBranches.Should().Contain(b => b.Id == _branchA2.Id);
        allBranches.Should().Contain(b => b.Id == _branchB1.Id);
    }
}
