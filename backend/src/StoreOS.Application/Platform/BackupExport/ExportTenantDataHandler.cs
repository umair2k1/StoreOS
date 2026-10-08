using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;

namespace StoreOS.Application.Platform.BackupExport;

public class ExportTenantDataHandler : IRequestHandler<ExportTenantDataCommand, TenantBackupDto>
{
    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;
    private readonly IObjectStorageService _objectStorage;

    public ExportTenantDataHandler(
        IAppDbContext db,
        IAccessScope scope,
        IObjectStorageService objectStorage)
    {
        _db = db;
        _scope = scope;
        _objectStorage = objectStorage;
    }

    public async Task<TenantBackupDto> Handle(ExportTenantDataCommand request, CancellationToken ct)
    {
        var tenantId = _scope.TenantId;
        // Full tenant export must include all branches' data regardless of the exporting user's branch scope.
        // Lift the branch restriction at the database RLS level for this transaction while keeping tenant_id strictly pinned.
        await _db.ExecuteRawSqlAsync("SET LOCAL app.all_branches = 'true'", Array.Empty<object>(), ct);


        var tenantDto = await _db.Set<Tenant>().IgnoreQueryFilters()
            .Where(t => t.Id == tenantId)
            .AsNoTracking()
            .Select(t => new BackupTenantDto(
                t.Id, t.Name, t.Subdomain, t.IsActive, t.Timezone, t.FeaturesJson, t.CreatedAt
            ))
            .FirstOrDefaultAsync(ct);

        var branches = await _db.Set<Branch>().IgnoreQueryFilters()
            .Where(b => b.TenantId == tenantId)
            .AsNoTracking()
            .Select(b => new BackupBranchDto(
                b.Id, b.TenantId, b.Code, b.Name, b.Address, b.Phone, b.Email, b.Gstin, b.IsActive, b.CreatedAt
            ))
            .ToArrayAsync(ct);

        var settings = await _db.Set<Setting>().IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .AsNoTracking()
            .Select(s => new BackupSettingDto(
                s.TenantId, s.BranchId, s.Key, s.ValueJson
            ))
            .ToArrayAsync(ct);

        var users = await _db.Set<User>().IgnoreQueryFilters()
            .Where(u => u.TenantId == tenantId)
            .AsNoTracking()
            .Select(u => new BackupUserDto(
                u.Id, u.TenantId, u.Email, u.PasswordHash, u.DisplayName, u.TokenVersion, u.IsActive, u.CreatedAt
            ))
            .ToArrayAsync(ct);

        var roles = await _db.Set<Role>().IgnoreQueryFilters()
            .Where(r => r.TenantId == tenantId)
            .AsNoTracking()
            .Select(r => new BackupRoleDto(
                r.Id, r.TenantId, r.Name
            ))
            .ToArrayAsync(ct);

        var roleIds = roles.Select(r => r.Id).ToList();
        var rolePermissions = await _db.Set<RolePermission>().IgnoreQueryFilters()
            .Where(rp => roleIds.Contains(rp.RoleId))
            .AsNoTracking()
            .Select(rp => new BackupRolePermissionDto(
                rp.RoleId, rp.PermissionCode
            ))
            .ToArrayAsync(ct);

        var userIds = users.Select(u => u.Id).ToList();
        var userBranches = await _db.Set<UserBranch>().IgnoreQueryFilters()
            .Where(ub => userIds.Contains(ub.UserId))
            .AsNoTracking()
            .Select(ub => new BackupUserBranchDto(
                ub.UserId, ub.BranchId, ub.IsDefault
            ))
            .ToArrayAsync(ct);

        var userRoleAssignments = await (
            from ura in _db.Set<UserRoleAssignment>().IgnoreQueryFilters().AsNoTracking()
            where userIds.Contains(ura.UserId)
            join b in _db.Set<Branch>().IgnoreQueryFilters().AsNoTracking() on ura.BranchId equals b.Id into branchGroup
            from b in branchGroup.DefaultIfEmpty()
            select new BackupUserRoleAssignmentDto(
                ura.UserId, ura.RoleId, (Guid?)b.Id
            )
        ).ToArrayAsync(ct);

        var products = await _db.Set<Product>().IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .AsNoTracking()
            .Select(p => new BackupProductDto(
                p.Id, p.TenantId, p.Name, p.Category, p.SellPrice, p.PurchasePrice, p.GstRate,
                p.IsTaxable, p.IsMarginScheme, p.HsnCode, p.Description, p.EstimatedDurationMinutes,
                p.WarrantyDays, p.IsActive, p.CreatedAt
            ))
            .ToArrayAsync(ct);

        var branchInventories = await _db.Set<BranchInventory>().IgnoreQueryFilters()
            .Where(bi => bi.TenantId == tenantId)
            .AsNoTracking()
            .Select(bi => new BackupBranchInventoryDto(
                bi.Id, bi.TenantId, bi.BranchId, bi.ProductId, bi.StockQty, bi.CostPrice,
                bi.SellPriceOverride, bi.LowStockThreshold
            ))
            .ToArrayAsync(ct);

        var purchases = await _db.Set<Purchase>().IgnoreQueryFilters()
            .Where(p => p.TenantId == tenantId)
            .AsNoTracking()
            .Select(p => new BackupPurchaseDto(
                p.Id, p.TenantId, p.BranchId, p.ProductId, p.Qty, p.PurchaseRate, p.SupplierId,
                p.SupplierName, p.SupplierPhone, p.SupplierAddress, p.SellerName, p.SellerPhone,
                p.SellerAddress, p.SupplierInvoiceNo, p.ImportBatchId, p.PurchaseDate, p.CreatedBy, p.CreatedAt
            ))
            .ToArrayAsync(ct);

        var inventoryImeis = await _db.Set<InventoryImei>().IgnoreQueryFilters()
            .Where(i => i.TenantId == tenantId)
            .AsNoTracking()
            .Select(i => new BackupInventoryImeiDto(
                i.Id, i.TenantId, i.BranchId, i.ProductId, i.PurchaseId, i.Imei, i.Status,
                i.SaleId, i.SaleItemId, i.ReturnId, i.PurchaseDate, i.PurchasePrice, i.CreatedAt
            ))
            .ToArrayAsync(ct);

        var inventoryMovements = await _db.Set<InventoryMovement>().IgnoreQueryFilters()
            .Where(m => m.TenantId == tenantId)
            .AsNoTracking()
            .Select(m => new BackupInventoryMovementDto(
                m.Id, m.TenantId, m.BranchId, m.ProductId, m.ImeiId, m.MovementType, m.QtyDelta,
                m.UnitCost, m.RefType, m.RefId, m.CreatedAt, m.CreatedBy
            ))
            .ToArrayAsync(ct);

        var sales = await _db.Set<Sale>().IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .AsNoTracking()
            .Select(s => new BackupSaleDto(
                s.Id, s.TenantId, s.BranchId, s.InvoiceNumber, s.InvoiceType, s.SaleDate,
                s.CustomerId, s.CustomerName, s.CustomerGstin, s.TotalTaxable, s.TotalCgst,
                s.TotalSgst, s.GrandTotal, s.Discount, s.AmountPaid, s.Status, s.PaymentMode,
                s.FinanceProvider, s.FinancerName, s.DownPayment, s.EmiDetailsJson, s.GiftDetailsJson,
                s.Description, s.RepairJobId, s.Irn, s.IrnAckNo, s.IrnQrData, s.CreatedBy,
                s.CreatedAt, s.ConvertedFromEstimateId, s.LinkedTaxInvoiceId
            ))
            .ToArrayAsync(ct);

        var saleItems = await _db.Set<SaleItem>().IgnoreQueryFilters()
            .Where(si => si.TenantId == tenantId)
            .AsNoTracking()
            .Select(si => new BackupSaleItemDto(
                si.Id, si.TenantId, si.SaleId, si.ProductId, si.ItemName, si.Category,
                si.Qty, si.PricePerUnit, si.PurchasePriceSnapshot, si.HsnSnapshot,
                si.IsMarginScheme, si.IsGift, si.Taxable, si.Cgst, si.Sgst, si.Imei
            ))
            .ToArrayAsync(ct);

        var customerPayments = await _db.Set<CustomerPayment>().IgnoreQueryFilters()
            .Where(cp => cp.TenantId == tenantId)
            .AsNoTracking()
            .Select(cp => new BackupCustomerPaymentDto(
                cp.Id, cp.TenantId, cp.SaleId, cp.Amount, cp.PaymentMode, cp.PaymentDate,
                cp.ReceivedBy, cp.CreatedAt
            ))
            .ToArrayAsync(ct);

        var customers = await _db.Set<Customer>().IgnoreQueryFilters()
            .Where(c => c.TenantId == tenantId)
            .AsNoTracking()
            .Select(c => new BackupCustomerDto(
                c.Id, c.TenantId, c.Name, c.Phone, c.Gstin, c.TotalPurchases, c.CreditLimit, c.CreatedAt
            ))
            .ToArrayAsync(ct);

        var customerLedgerEntries = await _db.Set<CustomerLedgerEntry>().IgnoreQueryFilters()
            .Where(cle => cle.TenantId == tenantId)
            .AsNoTracking()
            .Select(cle => new BackupCustomerLedgerEntryDto(
                cle.Id, cle.TenantId, cle.CustomerId, cle.SaleId, cle.EntryType,
                cle.Debit, cle.Credit, cle.BranchId, cle.EntryDate, cle.RefId, cle.CreatedAt
            ))
            .ToArrayAsync(ct);

        var suppliers = await _db.Set<Supplier>().IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId)
            .AsNoTracking()
            .Select(s => new BackupSupplierDto(
                s.Id, s.TenantId, s.Name, s.Phone, s.Address, s.BrandOrCategory, s.Notes, s.CreatedAt
            ))
            .ToArrayAsync(ct);

        var supplierTransactions = await _db.Set<SupplierTransaction>().IgnoreQueryFilters()
            .Where(st => st.TenantId == tenantId)
            .AsNoTracking()
            .Select(st => new BackupSupplierTransactionDto(
                st.Id, st.TenantId, st.SupplierId, st.BranchId, st.Type, st.Amount,
                st.PaymentMode, st.ReferenceNo, st.TxnDate, st.Notes, st.CreatedBy, st.CreatedAt
            ))
            .ToArrayAsync(ct);

        var supplierLedgerEntries = await _db.Set<SupplierLedgerEntry>().IgnoreQueryFilters()
            .Where(sle => sle.TenantId == tenantId)
            .AsNoTracking()
            .Select(sle => new BackupSupplierLedgerEntryDto(
                sle.Id, sle.TenantId, sle.SupplierId, sle.BranchId, sle.EntryType,
                sle.Debit, sle.Credit, sle.EntryDate, sle.RefId, sle.CreatedAt
            ))
            .ToArrayAsync(ct);

        var supplierReturns = await _db.Set<SupplierReturn>().IgnoreQueryFilters()
            .Where(sr => sr.TenantId == tenantId)
            .AsNoTracking()
            .Select(sr => new BackupSupplierReturnDto(
                sr.Id, sr.TenantId, sr.BranchId, sr.ReturnNumber, sr.ReturnDate,
                sr.SupplierId, sr.SupplierPhone, sr.SupplierAddress, sr.TotalQty,
                sr.TotalAmount, sr.SettlementType, sr.Reason, sr.Notes, sr.CreatedBy, sr.CreatedAt
            ))
            .ToArrayAsync(ct);

        var supplierReturnItems = await _db.Set<SupplierReturnItem>().IgnoreQueryFilters()
            .Where(sri => sri.TenantId == tenantId)
            .AsNoTracking()
            .Select(sri => new BackupSupplierReturnItemDto(
                sri.Id, sri.TenantId, sri.ReturnId, sri.ProductId, sri.ItemName,
                sri.Qty, sri.PurchasePrice, sri.ReturnRate, sri.LineTotal, sri.ImeiNumbers
            ))
            .ToArrayAsync(ct);

        var expenses = await _db.Set<Expense>().IgnoreQueryFilters()
            .Where(e => e.TenantId == tenantId)
            .AsNoTracking()
            .Select(e => new BackupExpenseDto(
                e.Id, e.TenantId, e.BranchId, e.Description, e.Amount, e.Category,
                e.ExpenseDate, e.Notes, e.CreatedBy, e.CreatedAt
            ))
            .ToArrayAsync(ct);

        var liabilities = await _db.Set<Liability>().IgnoreQueryFilters()
            .Where(l => l.TenantId == tenantId)
            .AsNoTracking()
            .Select(l => new BackupLiabilityDto(
                l.Id, l.TenantId, l.Title, l.Type, l.LenderName, l.TotalPrincipal,
                l.DueDate, l.Status, l.Notes, l.CreatedAt
            ))
            .ToArrayAsync(ct);

        var liabilityPayments = await _db.Set<LiabilityPayment>().IgnoreQueryFilters()
            .Where(lp => lp.TenantId == tenantId)
            .AsNoTracking()
            .Select(lp => new BackupLiabilityPaymentDto(
                lp.Id, lp.TenantId, lp.LiabilityId, lp.Amount, lp.PaymentMode,
                lp.PaymentDate, lp.Notes, lp.CreatedAt
            ))
            .ToArrayAsync(ct);

        var repairJobs = await _db.Set<RepairJob>().IgnoreQueryFilters()
            .Where(rj => rj.TenantId == tenantId)
            .AsNoTracking()
            .Select(rj => new BackupRepairJobDto(
                rj.Id, rj.TenantId, rj.BranchId, rj.JobNumber, rj.CustomerId,
                rj.CustomerName, rj.CustomerPhone, rj.CustomerAddress, rj.DeviceBrand,
                rj.DeviceModel, rj.DeviceType, rj.SerialNumber, rj.Imei1, rj.Imei2,
                rj.DeviceCondition, rj.AccessoriesReceived, rj.ProblemDescription,
                rj.CustomerNotes, rj.Diagnosis, rj.TechnicianNotes, rj.InternalNotes,
                rj.Technician, rj.EstimatedCost, rj.ApprovedCost, rj.PartsCost,
                rj.LaborCharge, rj.Discount, rj.Tax, rj.FinalTotal, rj.AdvancePaid,
                rj.BalanceDue, rj.Status, rj.ReceivedAt, rj.ExpectedCompletion,
                rj.CompletedAt, rj.DeliveredAt, rj.WarrantyUntil, rj.ApprovalState,
                rj.ApprovalTimestamp, rj.CancellationReason, rj.InvoiceNumber,
                rj.SaleId, rj.CreatedAt, rj.UpdatedAt
            ))
            .ToArrayAsync(ct);

        var repairJobParts = await _db.Set<RepairJobPart>().IgnoreQueryFilters()
            .Where(rjp => rjp.TenantId == tenantId)
            .AsNoTracking()
            .Select(rjp => new BackupRepairJobPartDto(
                rjp.Id, rjp.TenantId, rjp.RepairJobId, rjp.ProductId, rjp.PartName,
                rjp.Quantity, rjp.UnitCost, rjp.ChargeAmount, rjp.Status, rjp.CreatedAt
            ))
            .ToArrayAsync(ct);

        var repairJobStatusHistory = await _db.Set<RepairJobStatusHistory>().IgnoreQueryFilters()
            .Where(h => h.TenantId == tenantId)
            .AsNoTracking()
            .Select(h => new BackupRepairJobStatusHistoryDto(
                h.Id, h.TenantId, h.RepairJobId, h.FromStatus, h.ToStatus,
                h.Notes, h.ChangedBy, h.ChangedAt
            ))
            .ToArrayAsync(ct);

        var dto = new TenantBackupDto(
            "1.0",
            DateTimeOffset.UtcNow,
            tenantId,
            tenantDto ?? (object)new { },
            branches.Cast<object>().ToArray(),
            settings.Cast<object>().ToArray(),
            users.Cast<object>().ToArray(),
            roles.Cast<object>().ToArray(),
            rolePermissions.Cast<object>().ToArray(),
            userBranches.Cast<object>().ToArray(),
            userRoleAssignments.Cast<object>().ToArray(),
            products.Cast<object>().ToArray(),
            branchInventories.Cast<object>().ToArray(),
            purchases.Cast<object>().ToArray(),
            inventoryImeis.Cast<object>().ToArray(),
            inventoryMovements.Cast<object>().ToArray(),
            sales.Cast<object>().ToArray(),
            saleItems.Cast<object>().ToArray(),
            customerPayments.Cast<object>().ToArray(),
            customers.Cast<object>().ToArray(),
            customerLedgerEntries.Cast<object>().ToArray(),
            suppliers.Cast<object>().ToArray(),
            supplierTransactions.Cast<object>().ToArray(),
            supplierLedgerEntries.Cast<object>().ToArray(),
            supplierReturns.Cast<object>().ToArray(),
            supplierReturnItems.Cast<object>().ToArray(),
            expenses.Cast<object>().ToArray(),
            liabilities.Cast<object>().ToArray(),
            liabilityPayments.Cast<object>().ToArray(),
            repairJobs.Cast<object>().ToArray(),
            repairJobParts.Cast<object>().ToArray(),
            repairJobStatusHistory.Cast<object>().ToArray()
        );

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(dto, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        var timestamp = DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmss");
        var key = $"{tenantId}/backup-{timestamp}.json";

        await _objectStorage.UploadAsync("storeos-backups", key, jsonBytes, "application/json", ct);

        return dto;
    }
}
