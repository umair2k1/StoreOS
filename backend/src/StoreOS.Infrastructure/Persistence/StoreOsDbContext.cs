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
    public DbSet<InventoryTransfer> InventoryTransfers => Set<InventoryTransfer>();
    public DbSet<StoreOS.Domain.Sales.InvoiceSequence> InvoiceSequences => Set<StoreOS.Domain.Sales.InvoiceSequence>();
    public DbSet<StoreOS.Domain.Sales.Sale> Sales => Set<StoreOS.Domain.Sales.Sale>();
    public DbSet<StoreOS.Domain.Sales.SaleItem> SaleItems => Set<StoreOS.Domain.Sales.SaleItem>();
    public DbSet<StoreOS.Domain.Customers.Customer> Customers => Set<StoreOS.Domain.Customers.Customer>();
    public DbSet<StoreOS.Domain.Customers.CustomerLedgerEntry> CustomerLedgerEntries => Set<StoreOS.Domain.Customers.CustomerLedgerEntry>();
    public DbSet<StoreOS.Domain.Sales.CustomerPayment> CustomerPayments => Set<StoreOS.Domain.Sales.CustomerPayment>();
    public DbSet<StoreOS.Domain.Suppliers.SupplierTransaction> SupplierTransactions => Set<StoreOS.Domain.Suppliers.SupplierTransaction>();
    public DbSet<StoreOS.Domain.Suppliers.SupplierLedgerEntry> SupplierLedgerEntries => Set<StoreOS.Domain.Suppliers.SupplierLedgerEntry>();
    public DbSet<StoreOS.Domain.Suppliers.SupplierReturn> SupplierReturns => Set<StoreOS.Domain.Suppliers.SupplierReturn>();
    public DbSet<StoreOS.Domain.Suppliers.SupplierReturnItem> SupplierReturnItems => Set<StoreOS.Domain.Suppliers.SupplierReturnItem>();
    public DbSet<StoreOS.Domain.FinanceOps.Expense> Expenses => Set<StoreOS.Domain.FinanceOps.Expense>();
    public DbSet<StoreOS.Domain.FinanceOps.Liability> Liabilities => Set<StoreOS.Domain.FinanceOps.Liability>();
    public DbSet<StoreOS.Domain.FinanceOps.LiabilityPayment> LiabilityPayments => Set<StoreOS.Domain.FinanceOps.LiabilityPayment>();
    public DbSet<StoreOS.Domain.Repairs.RepairJob> RepairJobs => Set<StoreOS.Domain.Repairs.RepairJob>();
    public DbSet<StoreOS.Domain.Repairs.RepairJobStatusHistory> RepairJobStatusHistories => Set<StoreOS.Domain.Repairs.RepairJobStatusHistory>();
    public DbSet<StoreOS.Domain.Repairs.RepairJobPart> RepairJobParts => Set<StoreOS.Domain.Repairs.RepairJobPart>();
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
