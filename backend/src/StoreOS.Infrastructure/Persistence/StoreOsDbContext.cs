using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Tenancy;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Catalog;
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
    public DbSet<Product> Products => Set<Product>();
    public DbSet<BranchInventory> BranchInventories => Set<BranchInventory>();
    public DbSet<InventoryImei> InventoryImeis => Set<InventoryImei>();
    public DbSet<InventoryMovement> InventoryMovements => Set<InventoryMovement>();
    public DbSet<Purchase> Purchases => Set<Purchase>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();
    public Task<int> ExecuteRawSqlAsync(string sql, object[] parameters, CancellationToken ct = default)
        => Database.ExecuteSqlRawAsync(sql, parameters, ct);
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        modelBuilder.HasDefaultSchema("public");
        // Entity configurations are added via IEntityTypeConfiguration<T> classes applied
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(StoreOsDbContext).Assembly);
        QueryFilters.ApplyAll(modelBuilder, () => _scope);
    }
}
