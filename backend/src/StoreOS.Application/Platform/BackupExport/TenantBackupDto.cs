namespace StoreOS.Application.Platform.BackupExport;

public record TenantBackupDto(
    string SchemaVersion, // "1.0" - bump this any time the backup shape changes incompatibly
    DateTimeOffset ExportedAt,
    Guid TenantId,
    object Tenant, object[] Branches, object[] Settings,
    object[] Users, object[] Roles, object[] RolePermissions, object[] UserBranches, object[] UserRoleAssignments,
    object[] Products, object[] BranchInventories, object[] Purchases, object[] InventoryImeis, object[] InventoryMovements,
    object[] Sales, object[] SaleItems, object[] CustomerPayments,
    object[] Customers, object[] CustomerLedgerEntries,
    object[] Suppliers, object[] SupplierTransactions, object[] SupplierLedgerEntries,
    object[] SupplierReturns, object[] SupplierReturnItems,
    object[] Expenses,
    object[] Liabilities, object[] LiabilityPayments,
    object[] RepairJobs, object[] RepairJobParts, object[] RepairJobStatusHistory
);
