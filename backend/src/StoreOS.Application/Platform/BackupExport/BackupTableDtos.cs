using System.Text.Json.Serialization;

namespace StoreOS.Application.Platform.BackupExport;

public record BackupTenantDto(
    Guid Id,
    string Name,
    string Subdomain,
    bool IsActive,
    string Timezone,
    string FeaturesJson,
    DateTimeOffset CreatedAt
);

public record BackupBranchDto(
    Guid Id,
    Guid TenantId,
    string Code,
    string Name,
    string? Address,
    string? Phone,
    string? Email,
    string? Gstin,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record BackupSettingDto(
    Guid TenantId,
    Guid? BranchId,
    string Key,
    string ValueJson
);

public record BackupUserDto(
    Guid Id,
    Guid TenantId,
    string Email,
    [property: JsonPropertyName("password_hash")]
    string PasswordHash,
    string DisplayName,
    int TokenVersion,
    bool IsActive,
    DateTimeOffset CreatedAt
)
{
    [JsonPropertyName("passwordHash")]
    public string PasswordHashAlias => PasswordHash;
}

public record BackupRoleDto(
    Guid Id,
    Guid TenantId,
    string Name
);

public record BackupRolePermissionDto(
    Guid RoleId,
    string PermissionCode
);

public record BackupUserBranchDto(
    Guid UserId,
    Guid BranchId,
    bool IsDefault
);

public record BackupUserRoleAssignmentDto(
    Guid UserId,
    Guid RoleId,
    Guid? BranchId
);

public record BackupProductDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string Category,
    decimal SellPrice,
    decimal PurchasePrice,
    decimal GstRate,
    bool IsTaxable,
    bool IsMarginScheme,
    string? HsnCode,
    string? Description,
    int? EstimatedDurationMinutes,
    int? WarrantyDays,
    bool IsActive,
    DateTimeOffset CreatedAt
);

public record BackupBranchInventoryDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid ProductId,
    int StockQty,
    decimal CostPrice,
    decimal? SellPriceOverride,
    int LowStockThreshold
);

public record BackupPurchaseDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid ProductId,
    int Qty,
    decimal PurchaseRate,
    Guid? SupplierId,
    string? SupplierName,
    string? SupplierPhone,
    string? SupplierAddress,
    string? SellerName,
    string? SellerPhone,
    string? SellerAddress,
    string? SupplierInvoiceNo,
    Guid? ImportBatchId,
    DateOnly PurchaseDate,
    Guid CreatedBy,
    DateTimeOffset CreatedAt
);

public record BackupInventoryImeiDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    Guid ProductId,
    Guid? PurchaseId,
    string Imei,
    string Status,
    Guid? SaleId,
    Guid? SaleItemId,
    Guid? ReturnId,
    DateOnly? PurchaseDate,
    decimal? PurchasePrice,
    DateTimeOffset CreatedAt
);

public record BackupInventoryMovementDto(
    long Id,
    Guid TenantId,
    Guid BranchId,
    Guid ProductId,
    Guid? ImeiId,
    string MovementType,
    int QtyDelta,
    decimal UnitCost,
    string RefType,
    string RefId,
    DateTimeOffset CreatedAt,
    Guid? CreatedBy
);

public record BackupSaleDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string InvoiceNumber,
    string InvoiceType,
    DateTimeOffset SaleDate,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerGstin,
    decimal TotalTaxable,
    decimal TotalCgst,
    decimal TotalSgst,
    decimal GrandTotal,
    decimal Discount,
    decimal AmountPaid,
    string Status,
    string? PaymentMode,
    string? FinanceProvider,
    string? FinancerName,
    decimal? DownPayment,
    string? EmiDetailsJson,
    string? GiftDetailsJson,
    string? Description,
    Guid? RepairJobId,
    string? Irn,
    string? IrnAckNo,
    string? IrnQrData,
    Guid CreatedBy,
    DateTimeOffset CreatedAt,
    Guid? ConvertedFromEstimateId,
    Guid? LinkedTaxInvoiceId
);

public record BackupSaleItemDto(
    Guid Id,
    Guid TenantId,
    Guid SaleId,
    Guid? ProductId,
    string ItemName,
    string Category,
    int Qty,
    decimal PricePerUnit,
    decimal? PurchasePriceSnapshot,
    string? HsnSnapshot,
    bool IsMarginScheme,
    bool IsGift,
    decimal Taxable,
    decimal Cgst,
    decimal Sgst,
    string? Imei
);

public record BackupCustomerPaymentDto(
    Guid Id,
    Guid TenantId,
    Guid SaleId,
    decimal Amount,
    string PaymentMode,
    DateOnly PaymentDate,
    Guid ReceivedBy,
    DateTimeOffset CreatedAt
);

public record BackupCustomerDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Phone,
    string? Gstin,
    decimal TotalPurchases,
    decimal? CreditLimit,
    DateTimeOffset CreatedAt
);

public record BackupCustomerLedgerEntryDto(
    long Id,
    Guid TenantId,
    Guid CustomerId,
    Guid? SaleId,
    string EntryType,
    decimal Debit,
    decimal Credit,
    Guid? BranchId,
    DateOnly EntryDate,
    string? RefId,
    DateTimeOffset CreatedAt
);

public record BackupSupplierDto(
    Guid Id,
    Guid TenantId,
    string Name,
    string? Phone,
    string? Address,
    string? BrandOrCategory,
    string? Notes,
    DateTimeOffset CreatedAt
);

public record BackupSupplierTransactionDto(
    Guid Id,
    Guid TenantId,
    Guid SupplierId,
    Guid? BranchId,
    string Type,
    decimal Amount,
    string? PaymentMode,
    string? ReferenceNo,
    DateOnly TxnDate,
    string? Notes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt
);

public record BackupSupplierLedgerEntryDto(
    long Id,
    Guid TenantId,
    Guid SupplierId,
    Guid? BranchId,
    string EntryType,
    decimal Debit,
    decimal Credit,
    DateOnly EntryDate,
    string? RefId,
    DateTimeOffset CreatedAt
);

public record BackupSupplierReturnDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string ReturnNumber,
    DateOnly ReturnDate,
    Guid SupplierId,
    string? SupplierPhone,
    string? SupplierAddress,
    int TotalQty,
    decimal TotalAmount,
    string SettlementType,
    string? Reason,
    string? Notes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt
);

public record BackupSupplierReturnItemDto(
    Guid Id,
    Guid TenantId,
    Guid ReturnId,
    Guid? ProductId,
    string ItemName,
    int Qty,
    decimal PurchasePrice,
    decimal ReturnRate,
    decimal LineTotal,
    string[]? ImeiNumbers
);

public record BackupExpenseDto(
    Guid Id,
    Guid TenantId,
    Guid? BranchId,
    string Description,
    decimal Amount,
    string Category,
    DateOnly ExpenseDate,
    string? Notes,
    Guid CreatedBy,
    DateTimeOffset CreatedAt
);

public record BackupLiabilityDto(
    Guid Id,
    Guid TenantId,
    string Title,
    string Type,
    string? LenderName,
    decimal TotalPrincipal,
    DateOnly? DueDate,
    string? Status,
    string? Notes,
    DateTimeOffset CreatedAt
);

public record BackupLiabilityPaymentDto(
    Guid Id,
    Guid TenantId,
    Guid LiabilityId,
    decimal Amount,
    string PaymentMode,
    DateOnly PaymentDate,
    string? Notes,
    DateTimeOffset CreatedAt
);

public record BackupRepairJobDto(
    Guid Id,
    Guid TenantId,
    Guid BranchId,
    string JobNumber,
    Guid? CustomerId,
    string? CustomerName,
    string? CustomerPhone,
    string? CustomerAddress,
    string? DeviceBrand,
    string? DeviceModel,
    string? DeviceType,
    string? SerialNumber,
    string? Imei1,
    string? Imei2,
    string? DeviceCondition,
    string? AccessoriesReceived,
    string? ProblemDescription,
    string? CustomerNotes,
    string? Diagnosis,
    string? TechnicianNotes,
    string? InternalNotes,
    string? Technician,
    decimal? EstimatedCost,
    decimal? ApprovedCost,
    decimal? PartsCost,
    decimal? LaborCharge,
    decimal? Discount,
    decimal? Tax,
    decimal? FinalTotal,
    decimal? AdvancePaid,
    decimal? BalanceDue,
    string Status,
    DateTimeOffset? ReceivedAt,
    DateTimeOffset? ExpectedCompletion,
    DateTimeOffset? CompletedAt,
    DateTimeOffset? DeliveredAt,
    DateOnly? WarrantyUntil,
    string? ApprovalState,
    DateTimeOffset? ApprovalTimestamp,
    string? CancellationReason,
    string? InvoiceNumber,
    Guid? SaleId,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);

public record BackupRepairJobPartDto(
    Guid Id,
    Guid TenantId,
    Guid RepairJobId,
    Guid? ProductId,
    string PartName,
    int Quantity,
    decimal UnitCost,
    decimal ChargeAmount,
    string Status,
    DateTimeOffset CreatedAt
);

public record BackupRepairJobStatusHistoryDto(
    long Id,
    Guid TenantId,
    Guid RepairJobId,
    string? FromStatus,
    string ToStatus,
    string? Notes,
    Guid? ChangedBy,
    DateTimeOffset ChangedAt
);
