using System.Text.Json;
using MediatR;
using Microsoft.EntityFrameworkCore;
using StoreOS.Application.Abstractions;
using StoreOS.Application.Platform.BackupExport;
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

namespace StoreOS.Application.Platform.BackupImport;

public class ImportTenantDataHandler : IRequestHandler<ImportTenantDataCommand, ImportResultDto>
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IAppDbContext _db;
    private readonly IAccessScope _scope;

    public ImportTenantDataHandler(IAppDbContext db, IAccessScope scope)
    {
        _db = db;
        _scope = scope;
    }

    public async Task<ImportResultDto> Handle(ImportTenantDataCommand request, CancellationToken ct)
    {
        // 1. Structural validation before touching database
        if (request.Backup == null || request.Backup.SchemaVersion != "1.0")
        {
            return new ImportResultDto(false, new(), "unsupported_schema_version: Backup schema version must be 1.0");
        }
        var tenantId = _scope.TenantId;

        // 2. Lift RLS branch restriction for this transaction while keeping tenant_id strictly pinned
        await _db.ExecuteRawSqlAsync("SET LOCAL app.all_branches = 'true'", Array.Empty<object>(), ct);

        // 3. Reverse dependency deletion via sanctioned SECURITY DEFINER function
        // (preserves append-only table invariant while allowing authorized tenant disaster recovery)
        await _db.ExecuteRawSqlAsync("SELECT public.delete_tenant_data_for_restore({0})", new object[] { tenantId }, ct);

        var counts = new Dictionary<string, int>();

        // 4. Update or restore Tenant
        var tenantDto = DeserializeSingle<BackupTenantDto>(request.Backup.Tenant);
        if (tenantDto != null)
        {
            var existingTenant = await _db.Set<Tenant>().IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Id == tenantId, ct);
            if (existingTenant != null)
            {
                existingTenant.Rename(tenantDto.Name);
                existingTenant.SetSubdomain(tenantDto.Subdomain);
                existingTenant.SetFeatures(tenantDto.FeaturesJson);
                if (tenantDto.IsActive) existingTenant.Activate(); else existingTenant.Deactivate();
            }
            else
            {
                var newTenant = Tenant.Rehydrate(tenantDto.Id, tenantDto.Name, tenantDto.Subdomain, tenantDto.IsActive, tenantDto.Timezone, tenantDto.FeaturesJson, tenantDto.CreatedAt);
                await _db.Set<Tenant>().AddAsync(newTenant, ct);
            }
            await _db.SaveChangesAsync(ct);
            counts["tenants"] = 1;
        }

        // 5. Forward dependency insertion

        // Branches
        var branchDtos = DeserializeTable<BackupBranchDto>(request.Backup.Branches);
        if (branchDtos.Count > 0)
        {
            var branchEntities = branchDtos.Select(b => Branch.Rehydrate(
                b.Id, b.TenantId, b.Code, b.Name, b.Address, b.Phone, b.Email, b.Gstin, b.IsActive, b.CreatedAt
            )).ToList();
            await _db.Set<Branch>().AddRangeAsync(branchEntities, ct);
            counts["branches"] = branchEntities.Count;
        }

        // Settings
        var settingDtos = DeserializeTable<BackupSettingDto>(request.Backup.Settings);
        if (settingDtos.Count > 0)
        {
            var settingEntities = settingDtos.Select(s => Setting.Create(
                s.TenantId, s.BranchId, s.Key, s.ValueJson
            )).ToList();
            await _db.Set<Setting>().AddRangeAsync(settingEntities, ct);
            counts["settings"] = settingEntities.Count;
        }

        // Roles
        var roleDtos = DeserializeTable<BackupRoleDto>(request.Backup.Roles);
        if (roleDtos.Count > 0)
        {
            var roleEntities = roleDtos.Select(r => Role.Rehydrate(
                r.Id, r.TenantId, r.Name
            )).ToList();
            await _db.Set<Role>().AddRangeAsync(roleEntities, ct);
            counts["roles"] = roleEntities.Count;
        }

        // RolePermissions
        var rolePermissionDtos = DeserializeTable<BackupRolePermissionDto>(request.Backup.RolePermissions);
        if (rolePermissionDtos.Count > 0)
        {
            var rolePermEntities = rolePermissionDtos.Select(rp => RolePermission.Create(
                rp.RoleId, rp.PermissionCode
            )).ToList();
            await _db.Set<RolePermission>().AddRangeAsync(rolePermEntities, ct);
            counts["role_permissions"] = rolePermEntities.Count;
        }

        // Users
        var userDtos = DeserializeTable<BackupUserDto>(request.Backup.Users);
        if (userDtos.Count > 0)
        {
            var userEntities = userDtos.Select(u => User.Rehydrate(
                u.Id, u.TenantId, u.Email, u.PasswordHash, u.DisplayName, u.TokenVersion, u.IsActive, u.CreatedAt
            )).ToList();
            await _db.Set<User>().AddRangeAsync(userEntities, ct);
            counts["users"] = userEntities.Count;
        }

        // UserBranches
        var userBranchDtos = DeserializeTable<BackupUserBranchDto>(request.Backup.UserBranches);
        if (userBranchDtos.Count > 0)
        {
            var userBranchEntities = userBranchDtos.Select(ub => UserBranch.Create(
                ub.UserId, ub.BranchId, ub.IsDefault
            )).ToList();
            await _db.Set<UserBranch>().AddRangeAsync(userBranchEntities, ct);
            counts["user_branches"] = userBranchEntities.Count;
        }

        // UserRoleAssignments
        var userRoleDtos = DeserializeTable<BackupUserRoleAssignmentDto>(request.Backup.UserRoleAssignments);
        if (userRoleDtos.Count > 0)
        {
            // Flush staged Roles, Users, Branches to DB so foreign keys exist for raw SQL inserts
            await _db.SaveChangesAsync(ct);
            var branchSpecific = userRoleDtos.Where(ura => ura.BranchId.HasValue).Select(ura => UserRoleAssignment.Create(
                ura.UserId, ura.RoleId, ura.BranchId
            )).ToList();
            if (branchSpecific.Count > 0)
            {
                await _db.Set<UserRoleAssignment>().AddRangeAsync(branchSpecific, ct);
            }

            foreach (var ura in userRoleDtos.Where(ura => !ura.BranchId.HasValue))
            {
                await _db.ExecuteRawSqlAsync(
                    "INSERT INTO user_role_assignments (user_id, role_id, branch_id) VALUES ({0}, {1}, NULL)",
                    new object[] { ura.UserId, ura.RoleId }, ct);
            }
            counts["user_role_assignments"] = userRoleDtos.Count;
        }

        // Products
        var productDtos = DeserializeTable<BackupProductDto>(request.Backup.Products);
        if (productDtos.Count > 0)
        {
            var productEntities = productDtos.Select(p => Product.Rehydrate(
                p.Id, p.TenantId, p.Name, p.Category, p.SellPrice, p.PurchasePrice, p.GstRate,
                p.IsTaxable, p.IsMarginScheme, p.HsnCode, p.Description, p.EstimatedDurationMinutes,
                p.WarrantyDays, p.IsActive, p.CreatedAt
            )).ToList();
            await _db.Set<Product>().AddRangeAsync(productEntities, ct);
            counts["products"] = productEntities.Count;
        }

        // BranchInventory
        var branchInvDtos = DeserializeTable<BackupBranchInventoryDto>(request.Backup.BranchInventories);
        if (branchInvDtos.Count > 0)
        {
            var branchInvEntities = branchInvDtos.Select(bi => BranchInventory.Rehydrate(
                bi.Id, bi.TenantId, bi.BranchId, bi.ProductId, bi.StockQty, bi.CostPrice,
                bi.SellPriceOverride, bi.LowStockThreshold
            )).ToList();
            await _db.Set<BranchInventory>().AddRangeAsync(branchInvEntities, ct);
            counts["branch_inventory"] = branchInvEntities.Count;
        }

        // Purchases
        var purchaseDtos = DeserializeTable<BackupPurchaseDto>(request.Backup.Purchases);
        if (purchaseDtos.Count > 0)
        {
            var purchaseEntities = purchaseDtos.Select(p => Purchase.Rehydrate(
                p.Id, p.TenantId, p.BranchId, p.ProductId, p.Qty, p.PurchaseRate, p.SupplierId,
                p.SupplierName, p.SupplierPhone, p.SupplierAddress, p.SellerName, p.SellerPhone,
                p.SellerAddress, p.SupplierInvoiceNo, p.ImportBatchId, p.PurchaseDate, p.CreatedBy, p.CreatedAt
            )).ToList();
            await _db.Set<Purchase>().AddRangeAsync(purchaseEntities, ct);
            counts["purchases"] = purchaseEntities.Count;
        }

        // InventoryImeis
        var imeiDtos = DeserializeTable<BackupInventoryImeiDto>(request.Backup.InventoryImeis);
        if (imeiDtos.Count > 0)
        {
            var imeiEntities = imeiDtos.Select(i => InventoryImei.Rehydrate(
                i.Id, i.TenantId, i.BranchId, i.ProductId, i.PurchaseId, i.Imei, i.Status,
                i.SaleId, i.SaleItemId, i.ReturnId, i.PurchaseDate, i.PurchasePrice, i.CreatedAt
            )).ToList();
            await _db.Set<InventoryImei>().AddRangeAsync(imeiEntities, ct);
            counts["inventory_imeis"] = imeiEntities.Count;
        }

        // InventoryMovements
        var movementDtos = DeserializeTable<BackupInventoryMovementDto>(request.Backup.InventoryMovements);
        if (movementDtos.Count > 0)
        {
            var movementEntities = movementDtos.Select(m => InventoryMovement.Rehydrate(
                m.Id, m.TenantId, m.BranchId, m.ProductId, m.ImeiId, m.MovementType, m.QtyDelta,
                m.UnitCost, m.RefType, m.RefId, m.CreatedAt, m.CreatedBy
            )).ToList();
            await _db.Set<InventoryMovement>().AddRangeAsync(movementEntities, ct);
            counts["inventory_movements"] = movementEntities.Count;
        }

        // Customers
        var customerDtos = DeserializeTable<BackupCustomerDto>(request.Backup.Customers);
        if (customerDtos.Count > 0)
        {
            var customerEntities = customerDtos.Select(c => Customer.Rehydrate(
                c.Id, c.TenantId, c.Name, c.Phone, c.Gstin, c.TotalPurchases, c.CreditLimit, c.CreatedAt
            )).ToList();
            await _db.Set<Customer>().AddRangeAsync(customerEntities, ct);
            counts["customers"] = customerEntities.Count;
        }

        // Sales
        var saleDtos = DeserializeTable<BackupSaleDto>(request.Backup.Sales);
        if (saleDtos.Count > 0)
        {
            var saleEntities = saleDtos.Select(s => Sale.Rehydrate(
                s.Id, s.TenantId, s.BranchId, s.InvoiceNumber, s.InvoiceType, s.SaleDate,
                s.CustomerId, s.CustomerName, s.CustomerGstin, s.TotalTaxable, s.TotalCgst,
                s.TotalSgst, s.GrandTotal, s.Discount, s.AmountPaid, s.Status, s.PaymentMode,
                s.FinanceProvider, s.FinancerName, s.DownPayment, s.EmiDetailsJson, s.GiftDetailsJson,
                s.Description, s.RepairJobId, s.Irn, s.IrnAckNo, s.IrnQrData, s.CreatedBy,
                s.CreatedAt, s.ConvertedFromEstimateId, s.LinkedTaxInvoiceId
            )).ToList();
            await _db.Set<Sale>().AddRangeAsync(saleEntities, ct);
            counts["sales"] = saleEntities.Count;
        }

        // SaleItems
        var saleItemDtos = DeserializeTable<BackupSaleItemDto>(request.Backup.SaleItems);
        if (saleItemDtos.Count > 0)
        {
            var saleItemEntities = saleItemDtos.Select(si => SaleItem.Rehydrate(
                si.Id, si.TenantId, si.SaleId, si.ProductId, si.ItemName, si.Category,
                si.Qty, si.PricePerUnit, si.PurchasePriceSnapshot, si.HsnSnapshot,
                si.IsMarginScheme, si.IsGift, si.Taxable, si.Cgst, si.Sgst, si.Imei
            )).ToList();
            await _db.Set<SaleItem>().AddRangeAsync(saleItemEntities, ct);
            counts["sale_items"] = saleItemEntities.Count;
        }

        // CustomerPayments
        var custPaymentDtos = DeserializeTable<BackupCustomerPaymentDto>(request.Backup.CustomerPayments);
        if (custPaymentDtos.Count > 0)
        {
            var custPaymentEntities = custPaymentDtos.Select(cp => CustomerPayment.Rehydrate(
                cp.Id, cp.TenantId, cp.SaleId, cp.Amount, cp.PaymentMode, cp.PaymentDate, cp.ReceivedBy, cp.CreatedAt
            )).ToList();
            await _db.Set<CustomerPayment>().AddRangeAsync(custPaymentEntities, ct);
            counts["customer_payments"] = custPaymentEntities.Count;
        }

        // CustomerLedgerEntries
        var custLedgerDtos = DeserializeTable<BackupCustomerLedgerEntryDto>(request.Backup.CustomerLedgerEntries);
        if (custLedgerDtos.Count > 0)
        {
            var custLedgerEntities = custLedgerDtos.Select(cle => CustomerLedgerEntry.Rehydrate(
                0, cle.TenantId, cle.CustomerId, cle.SaleId, cle.EntryType,
                cle.Debit, cle.Credit, cle.BranchId, cle.EntryDate, cle.RefId, cle.CreatedAt
            )).ToList();
            await _db.Set<CustomerLedgerEntry>().AddRangeAsync(custLedgerEntities, ct);
            counts["customer_ledger_entries"] = custLedgerEntities.Count;
        }

        // Suppliers
        var supplierDtos = DeserializeTable<BackupSupplierDto>(request.Backup.Suppliers);
        if (supplierDtos.Count > 0)
        {
            var supplierEntities = supplierDtos.Select(s => Supplier.Rehydrate(
                s.Id, s.TenantId, s.Name, s.Phone, s.Address, s.BrandOrCategory, s.Notes, s.CreatedAt
            )).ToList();
            await _db.Set<Supplier>().AddRangeAsync(supplierEntities, ct);
            counts["suppliers"] = supplierEntities.Count;
        }

        // SupplierTransactions
        var supplierTxnDtos = DeserializeTable<BackupSupplierTransactionDto>(request.Backup.SupplierTransactions);
        if (supplierTxnDtos.Count > 0)
        {
            var supplierTxnEntities = supplierTxnDtos.Select(st => SupplierTransaction.Rehydrate(
                st.Id, st.TenantId, st.SupplierId, st.BranchId, st.Type, st.Amount,
                st.PaymentMode, st.ReferenceNo, st.TxnDate, st.Notes, st.CreatedBy, st.CreatedAt
            )).ToList();
            await _db.Set<SupplierTransaction>().AddRangeAsync(supplierTxnEntities, ct);
            counts["supplier_transactions"] = supplierTxnEntities.Count;
        }

        // SupplierLedgerEntries
        var supplierLedgerDtos = DeserializeTable<BackupSupplierLedgerEntryDto>(request.Backup.SupplierLedgerEntries);
        if (supplierLedgerDtos.Count > 0)
        {
            var supplierLedgerEntities = supplierLedgerDtos.Select(sle => SupplierLedgerEntry.Rehydrate(
                0, sle.TenantId, sle.SupplierId, sle.BranchId, sle.EntryType,
                sle.Debit, sle.Credit, sle.EntryDate, sle.RefId, sle.CreatedAt
            )).ToList();
            await _db.Set<SupplierLedgerEntry>().AddRangeAsync(supplierLedgerEntities, ct);
            counts["supplier_ledger_entries"] = supplierLedgerEntities.Count;
        }

        // SupplierReturns
        var supplierReturnDtos = DeserializeTable<BackupSupplierReturnDto>(request.Backup.SupplierReturns);
        if (supplierReturnDtos.Count > 0)
        {
            var supplierReturnEntities = supplierReturnDtos.Select(sr => SupplierReturn.Rehydrate(
                sr.Id, sr.TenantId, sr.BranchId, sr.ReturnNumber, sr.ReturnDate,
                sr.SupplierId, sr.SupplierPhone, sr.SupplierAddress, sr.TotalQty,
                sr.TotalAmount, sr.SettlementType, sr.Reason, sr.Notes, sr.CreatedBy, sr.CreatedAt
            )).ToList();
            await _db.Set<SupplierReturn>().AddRangeAsync(supplierReturnEntities, ct);
            counts["supplier_returns"] = supplierReturnEntities.Count;
        }

        // SupplierReturnItems
        var supplierReturnItemDtos = DeserializeTable<BackupSupplierReturnItemDto>(request.Backup.SupplierReturnItems);
        if (supplierReturnItemDtos.Count > 0)
        {
            var supplierReturnItemEntities = supplierReturnItemDtos.Select(sri => SupplierReturnItem.Rehydrate(
                sri.Id, sri.TenantId, sri.ReturnId, sri.ProductId, sri.ItemName,
                sri.Qty, sri.PurchasePrice, sri.ReturnRate, sri.LineTotal, sri.ImeiNumbers
            )).ToList();
            await _db.Set<SupplierReturnItem>().AddRangeAsync(supplierReturnItemEntities, ct);
            counts["supplier_return_items"] = supplierReturnItemEntities.Count;
        }

        // Expenses
        var expenseDtos = DeserializeTable<BackupExpenseDto>(request.Backup.Expenses);
        if (expenseDtos.Count > 0)
        {
            var expenseEntities = expenseDtos.Select(e => Expense.Rehydrate(
                e.Id, e.TenantId, e.BranchId, e.Description, e.Amount, e.Category,
                e.ExpenseDate, e.Notes, e.CreatedBy, e.CreatedAt
            )).ToList();
            await _db.Set<Expense>().AddRangeAsync(expenseEntities, ct);
            counts["expenses"] = expenseEntities.Count;
        }

        // Liabilities
        var liabilityDtos = DeserializeTable<BackupLiabilityDto>(request.Backup.Liabilities);
        if (liabilityDtos.Count > 0)
        {
            var liabilityEntities = liabilityDtos.Select(l => Liability.Rehydrate(
                l.Id, l.TenantId, l.Title, l.Type, l.LenderName, l.TotalPrincipal,
                l.DueDate, l.Status, l.Notes, l.CreatedAt
            )).ToList();
            await _db.Set<Liability>().AddRangeAsync(liabilityEntities, ct);
            counts["liabilities"] = liabilityEntities.Count;
        }

        // LiabilityPayments
        var liabilityPaymentDtos = DeserializeTable<BackupLiabilityPaymentDto>(request.Backup.LiabilityPayments);
        if (liabilityPaymentDtos.Count > 0)
        {
            var liabilityPaymentEntities = liabilityPaymentDtos.Select(lp => LiabilityPayment.Rehydrate(
                lp.Id, lp.TenantId, lp.LiabilityId, lp.Amount, lp.PaymentMode,
                lp.PaymentDate, lp.Notes, lp.CreatedAt
            )).ToList();
            await _db.Set<LiabilityPayment>().AddRangeAsync(liabilityPaymentEntities, ct);
            counts["liability_payments"] = liabilityPaymentEntities.Count;
        }

        // RepairJobs
        var repairJobDtos = DeserializeTable<BackupRepairJobDto>(request.Backup.RepairJobs);
        if (repairJobDtos.Count > 0)
        {
            var repairJobEntities = repairJobDtos.Select(rj => RepairJob.Rehydrate(
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
            )).ToList();
            await _db.Set<RepairJob>().AddRangeAsync(repairJobEntities, ct);
            counts["repair_jobs"] = repairJobEntities.Count;
        }

        // RepairJobParts
        var repairJobPartDtos = DeserializeTable<BackupRepairJobPartDto>(request.Backup.RepairJobParts);
        if (repairJobPartDtos.Count > 0)
        {
            var repairJobPartEntities = repairJobPartDtos.Select(rjp => RepairJobPart.Rehydrate(
                rjp.Id, rjp.TenantId, rjp.RepairJobId, rjp.ProductId, rjp.PartName,
                rjp.Quantity, rjp.UnitCost, rjp.ChargeAmount, rjp.Status, rjp.CreatedAt
            )).ToList();
            await _db.Set<RepairJobPart>().AddRangeAsync(repairJobPartEntities, ct);
            counts["repair_job_parts"] = repairJobPartEntities.Count;
        }

        // RepairJobStatusHistory
        var statusHistoryDtos = DeserializeTable<BackupRepairJobStatusHistoryDto>(request.Backup.RepairJobStatusHistory);
        if (statusHistoryDtos.Count > 0)
        {
            var statusHistoryEntities = statusHistoryDtos.Select(h => RepairJobStatusHistory.Rehydrate(
                h.Id, h.TenantId, h.RepairJobId, h.FromStatus, h.ToStatus,
                h.Notes, h.ChangedBy, h.ChangedAt
            )).ToList();
            await _db.Set<RepairJobStatusHistory>().AddRangeAsync(statusHistoryEntities, ct);
            counts["repair_job_status_history"] = statusHistoryEntities.Count;
        }

        await _db.SaveChangesAsync(ct);
        return new ImportResultDto(true, counts, null);
    }

    private static List<T> DeserializeTable<T>(object? rawObjectOrArray)
    {
        if (rawObjectOrArray == null) return new List<T>();
        if (rawObjectOrArray is IEnumerable<T> typedList) return typedList.ToList();
        var json = JsonSerializer.Serialize(rawObjectOrArray);
        return JsonSerializer.Deserialize<List<T>>(json, JsonOpts) ?? new List<T>();
    }

    private static T? DeserializeSingle<T>(object? rawObject)
    {
        if (rawObject == null) return default;
        if (rawObject is T typed) return typed;
        var json = JsonSerializer.Serialize(rawObject);
        return JsonSerializer.Deserialize<T>(json, JsonOpts);
    }
}
