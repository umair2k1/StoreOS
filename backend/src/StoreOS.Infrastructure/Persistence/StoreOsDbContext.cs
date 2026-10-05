using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Platform;
namespace StoreOS.Infrastructure.Persistence;

public class StoreOsDbContext : DbContext, StoreOS.Application.Abstractions.IAppDbContext
{
    private readonly IAccessScope _scope;

    public StoreOsDbContext(DbContextOptions<StoreOsDbContext> options, IAccessScope scope) : base(options)
    {
        _scope = scope;
    }

    public DbSet<Tenant> Tenants => Set<Tenant>();
    public DbSet<Branch> Branches => Set<Branch>();
    public DbSet<Setting> Settings => Set<Setting>();
    public DbSet<User> Users => Set<User>();
    public DbSet<Role> Roles => Set<Role>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<UserBranch> UserBranches => Set<UserBranch>();
    public DbSet<UserRoleAssignment> UserRoleAssignments => Set<UserRoleAssignment>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<AuditLogEntry> AuditLogEntries => Set<AuditLogEntry>();
    public DbSet<IdempotencyRecord> IdempotencyRecords => Set<IdempotencyRecord>();
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        // Entity configurations are added via IEntityTypeConfiguration<T> classes applied
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);

        // Tenant-only filter archetype (Plan §9.1)
        modelBuilder.Entity<Branch>().HasQueryFilter(b => b.TenantId == _scope.TenantId);
        modelBuilder.Entity<Setting>().HasQueryFilter(s => s.TenantId == _scope.TenantId);
        modelBuilder.Entity<AuditLogEntry>().HasQueryFilter(a => a.TenantId == _scope.TenantId && (a.BranchId == null || _scope.AllBranches || _scope.BranchIds.Contains(a.BranchId.Value)));
        modelBuilder.Entity<IdempotencyRecord>().HasQueryFilter(i => i.TenantId == _scope.TenantId);
    }
}
