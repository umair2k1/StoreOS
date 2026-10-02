using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace StoreOS.Infrastructure.Persistence;

/// <summary>
/// Used ONLY by `dotnet ef` (design time). EF prefers a design-time factory over the host's service
/// provider, so every `dotnet ef migrations/database update` in this build automatically runs as the
/// OWNER role. Override with env var STOREOS_MIGRATIONS_CONNECTION (CI, staging, production).
/// </summary>
public sealed class StoreOsDbContextFactory : IDesignTimeDbContextFactory<StoreOsDbContext>
{
    public StoreOsDbContext CreateDbContext(string[] args)
    {
        var cs = Environment.GetEnvironmentVariable("STOREOS_MIGRATIONS_CONNECTION")
                 ?? "Host=localhost;Port=5432;Database=storeos;Username=storeos;Password=storeos_dev_password";
        var options = new DbContextOptionsBuilder<StoreOsDbContext>().UseNpgsql(cs).Options;
        return new StoreOsDbContext(options);
    }
}
