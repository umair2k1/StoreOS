using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Tenancy;
using StoreOS.Domain.Identity;

namespace StoreOS.Infrastructure.Persistence;

public class StoreOsDbContext : DbContext, StoreOS.Application.Abstractions.IAppDbContext
{
    public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options) : base(options) { }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        // Entity configurations are added via IEntityTypeConfiguration<T> classes applied
        // from each module as entities are introduced in later prompts:
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);
    }
}
