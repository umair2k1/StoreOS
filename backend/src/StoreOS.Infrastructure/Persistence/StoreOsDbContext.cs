using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Infrastructure.Persistence;

public class StoreOsDbContext : DbContext, StoreOS.Application.Abstractions.IAppDbContext
{
    public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        // Entity configurations are added via IEntityTypeConfiguration<T> classes applied
        // from each module as entities are introduced in later prompts:
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);
    }
}
