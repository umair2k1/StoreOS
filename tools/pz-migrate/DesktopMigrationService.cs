using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Persistence;

namespace PzMigrate;

public record MigrationConflict(
    string DesktopId,
    string CustomerName,
    string DesktopPhone,
    string ConflictingExistingCustomerName,
    string Reason
);

public record ImportSummary(
    Guid TenantId,
    Guid DefaultBranchId,
    int InsertedProducts,
    int InsertedCustomers,
    int UpdatedCustomers,
    int InsertedSales,
    int InsertedPurchases,
    int InsertedImeis,
    int SkippedDuplicates,
    int ConflictCount
);

public static class DesktopMigrationService
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public static ReconciliationReport RunDryRun(string backupPath)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Desktop backup file not found at path: {backupPath}");
        }

        var json = File.ReadAllText(backupPath);
        DetectSchemaOrThrow(json);

        var backup = JsonSerializer.Deserialize<DesktopBackup>(json, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize desktop backup file.");

        var recordCounts = new Dictionary<string, int>
        {
            ["settings"] = backup.Settings?.Count ?? 0,
            ["items"] = backup.Items?.Count ?? 0,
            ["purchases"] = backup.Purchases?.Count ?? 0,
            ["sales"] = backup.Sales?.Count ?? 0,
            ["sale_items"] = backup.SaleItems?.Count ?? 0,
            ["customers"] = backup.Customers?.Count ?? 0,
            ["customer_payments"] = backup.CustomerPayments?.Count ?? 0,
            ["expenses"] = backup.Expenses?.Count ?? 0,
            ["suppliers"] = backup.Suppliers?.Count ?? 0,
            ["supplier_transactions"] = backup.SupplierTransactions?.Count ?? 0,
            ["liabilities"] = backup.Liabilities?.Count ?? 0,
            ["liability_payments"] = backup.LiabilityPayments?.Count ?? 0,
            ["inventory_imeis"] = backup.InventoryImeis?.Count ?? 0,
            ["supplier_returns"] = backup.SupplierReturns?.Count ?? 0,
            ["supplier_return_items"] = backup.SupplierReturnItems?.Count ?? 0,
            ["repair_services"] = backup.RepairServices?.Count ?? 0,
            ["repair_jobs"] = backup.RepairJobs?.Count ?? 0,
            ["repair_job_parts"] = backup.RepairJobParts?.Count ?? 0,
            ["repair_job_status_history"] = backup.RepairJobStatusHistory?.Count ?? 0
        };

        var sumSaleGrandTotals = backup.Sales?.Sum(s => s.GrandTotal) ?? 0m;
        var sumPayments = backup.CustomerPayments?.Sum(p => p.Amount) ?? 0m;
        var sumStockQty = backup.Items?.Sum(i => i.StockQty) ?? 0;
        var sumActiveCreditBalances = backup.Customers?.Where(c => c.CreditBalance > 0).Sum(c => c.CreditBalance) ?? 0m;

        var report = new ReconciliationReport(
            recordCounts,
            sumSaleGrandTotals,
            sumPayments,
            sumStockQty,
            sumActiveCreditBalances
        );

        var dir = Path.GetDirectoryName(Path.GetFullPath(backupPath)) ?? Directory.GetCurrentDirectory();
        var reportPath = Path.Combine(dir, "reconciliation-report.json");
        var reportJson = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(reportPath, reportJson);

        return report;
    }

    public static void DetectSchemaOrThrow(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            if (root.ValueKind != JsonValueKind.Object)
            {
                throw new InvalidOperationException("Unrecognized desktop backup schema: Root must be a JSON object.");
            }

            // If it has cloud backup schema markers like schemaVersion, reject
            if (root.TryGetProperty("schemaVersion", out _) || root.TryGetProperty("SchemaVersion", out _))
            {
                throw new InvalidOperationException("Unrecognized desktop backup schema: File appears to be a StoreOS cloud backup, not a desktop SQLite backup.");
            }

            // Must contain at least recognized desktop keys: items, sales, or customers
            bool hasItems = root.TryGetProperty("items", out _) || root.TryGetProperty("Items", out _);
            bool hasSales = root.TryGetProperty("sales", out _) || root.TryGetProperty("Sales", out _);
            bool hasCustomers = root.TryGetProperty("customers", out _) || root.TryGetProperty("Customers", out _);

            if (!hasItems && !hasSales && !hasCustomers)
            {
                throw new InvalidOperationException("Unrecognized desktop backup schema: Missing expected top-level keys ('items', 'sales', 'customers').");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"Malformed JSON: {ex.Message}", ex);
        }
    }

    public static async Task<ImportSummary> RunImportAsync(
        string backupPath,
        string tenantName,
        StoreOsDbContext db,
        CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(backupPath) || !File.Exists(backupPath))
        {
            throw new FileNotFoundException($"Desktop backup file not found at path: {backupPath}");
        }

        var json = File.ReadAllText(backupPath);
        DetectSchemaOrThrow(json);

        var backup = JsonSerializer.Deserialize<DesktopBackup>(json, JsonOpts)
            ?? throw new InvalidOperationException("Failed to deserialize desktop backup file.");

        // 1. Tenant & Default Branch
        var tenant = await db.Tenants.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.Name == tenantName, ct);
        if (tenant == null)
        {
            tenant = Tenant.Create(tenantName);
            await db.Tenants.AddAsync(tenant, ct);
            await db.SaveChangesAsync(ct);
        }

        var tenantId = tenant.Id;

        var branch = await db.Branches.IgnoreQueryFilters()
            .FirstOrDefaultAsync(b => b.TenantId == tenantId && b.Code == "B01", ct);
        if (branch == null)
        {
            branch = Branch.Create(tenantId, "B01", "Main Branch");
            await db.Branches.AddAsync(branch, ct);
            await db.SaveChangesAsync(ct);
        }

        var branchId = branch.Id;

        // In-memory ID remapping dictionaries
        var itemMap = new Dictionary<string, Guid>();
        var customerMap = new Dictionary<string, Guid>();
        var supplierMap = new Dictionary<string, Guid>();
        var purchaseMap = new Dictionary<string, Guid>();
        var saleMap = new Dictionary<string, Guid>();

        var conflicts = new List<MigrationConflict>();
        int insertedProducts = 0;
        int insertedCustomers = 0;
        int updatedCustomers = 0;
        int insertedSales = 0;
        int insertedPurchases = 0;
        int insertedImeis = 0;
        int skippedDuplicates = 0;

        // 2. Products & BranchInventory
        if (backup.Items != null)
        {
            foreach (var item in backup.Items)
            {
                var existingProduct = await db.Products.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId && p.Name == item.Name, ct);

                if (existingProduct != null)
                {
                    itemMap[item.Id] = existingProduct.Id;
                    skippedDuplicates++;
                }
                else
                {
                    var category = NormalizeCategory(item.Category);
                    var product = Product.Create(
                        tenantId, item.Name, category,
                        item.SellPrice, item.PurchasePrice, item.GstRate, item.HsnCode);

                    if (item.IsMarginScheme && category == ProductCategory.UsedPhone)
                    {
                        product.SetMarginScheme(true);
                    }

                    await db.Products.AddAsync(product, ct);
                    itemMap[item.Id] = product.Id;
                    insertedProducts++;

                    var inv = BranchInventory.Create(tenantId, branchId, product.Id);
                    if (item.StockQty > 0)
                    {
                        inv.IncreaseStock(item.StockQty, item.PurchasePrice);
                    }
                    await db.BranchInventories.AddAsync(inv, ct);
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // 3. Customers (Two-Tier Dedup & Conflict Logging)
        if (backup.Customers != null)
        {
            foreach (var dc in backup.Customers)
            {
                // Primary match on (tenant_id, desktop_id)
                var matchByDesktopId = await db.Customers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.DesktopId == dc.Id, ct);

                if (matchByDesktopId != null)
                {
                    // Check if phone changed
                    if (!string.IsNullOrWhiteSpace(dc.Phone) && matchByDesktopId.Phone != dc.Phone)
                    {
                        // Check if new phone collides with a DIFFERENT existing customer
                        var phoneCollision = await db.Customers.IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Id != matchByDesktopId.Id && c.Phone == dc.Phone, ct);

                        if (phoneCollision != null)
                        {
                            conflicts.Add(new MigrationConflict(
                                dc.Id,
                                dc.Name,
                                dc.Phone,
                                phoneCollision.Name,
                                $"Phone '{dc.Phone}' collides with existing customer '{phoneCollision.Name}' (ID {phoneCollision.Id})."
                            ));
                            customerMap[dc.Id] = matchByDesktopId.Id;
                            skippedDuplicates++;
                        }
                        else
                        {
                            matchByDesktopId.UpdateProfile(matchByDesktopId.Name, dc.Phone, matchByDesktopId.Gstin);
                            customerMap[dc.Id] = matchByDesktopId.Id;
                            updatedCustomers++;
                        }
                    }
                    else
                    {
                        customerMap[dc.Id] = matchByDesktopId.Id;
                        skippedDuplicates++;
                    }
                }
                else
                {
                    // Secondary fallback match on (tenant_id, phone)
                    Customer? matchByPhone = null;
                    if (!string.IsNullOrWhiteSpace(dc.Phone))
                    {
                        matchByPhone = await db.Customers.IgnoreQueryFilters()
                            .FirstOrDefaultAsync(c => c.TenantId == tenantId && c.Phone == dc.Phone, ct);
                    }

                    if (matchByPhone != null)
                    {
                        matchByPhone.SetDesktopId(dc.Id);
                        customerMap[dc.Id] = matchByPhone.Id;
                        skippedDuplicates++;
                    }
                    else
                    {
                        var newCustomer = Customer.Create(tenantId, dc.Name, dc.Phone, dc.Gstin);
                        newCustomer.SetDesktopId(dc.Id);
                        if (dc.TotalPurchases > 0) newCustomer.AddToTotalPurchases(dc.TotalPurchases);
                        if (dc.CreditLimit.HasValue) newCustomer.SetCreditLimit(dc.CreditLimit);

                        await db.Customers.AddAsync(newCustomer, ct);
                        customerMap[dc.Id] = newCustomer.Id;
                        insertedCustomers++;
                    }
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // 4. Suppliers
        if (backup.Suppliers != null)
        {
            foreach (var ds in backup.Suppliers)
            {
                var existingSupplier = await db.Suppliers.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.Name == ds.Name, ct);

                if (existingSupplier != null)
                {
                    supplierMap[ds.Id] = existingSupplier.Id;
                    skippedDuplicates++;
                }
                else
                {
                    var supplier = Supplier.Create(tenantId, ds.Name, ds.Phone, ds.Address, ds.BrandOrCategory, ds.Notes);
                    await db.Suppliers.AddAsync(supplier, ct);
                    supplierMap[ds.Id] = supplier.Id;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // 5. Purchases (Dedup on tenant_id, supplier_invoice_no, supplier_id, purchase_date)
        if (backup.Purchases != null)
        {
            foreach (var dp in backup.Purchases)
            {
                if (!itemMap.TryGetValue(dp.ItemId, out var productId)) continue;
                var supplierId = dp.SupplierId != null && supplierMap.TryGetValue(dp.SupplierId, out var sId) ? sId : (Guid?)null;

                DateOnly pDate = DateOnly.TryParse(dp.PurchaseDate, out var parsedDate) ? parsedDate : DateOnly.FromDateTime(DateTime.UtcNow);

                var existingPurchase = await db.Purchases.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(p => p.TenantId == tenantId
                        && p.SupplierInvoiceNo == dp.SupplierInvoiceNo
                        && p.SupplierId == supplierId
                        && p.PurchaseDate == pDate, ct);

                if (existingPurchase != null)
                {
                    purchaseMap[dp.Id] = existingPurchase.Id;
                    skippedDuplicates++;
                }
                else
                {
                    var purchase = Purchase.Create(
                        tenantId, branchId, productId, dp.Qty, dp.PurchaseRate,
                        supplierId, dp.SupplierName, dp.SupplierPhone, dp.SupplierAddress,
                        pDate, Guid.Empty, dp.SupplierInvoiceNo, null,
                        dp.SellerName, dp.SellerPhone, dp.SellerAddress);

                    await db.Purchases.AddAsync(purchase, ct);
                    purchaseMap[dp.Id] = purchase.Id;
                    insertedPurchases++;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // 6. Sales (Dedup on tenant_id, original_invoice_number)
        if (backup.Sales != null)
        {
            foreach (var ds in backup.Sales)
            {
                var existingSale = await db.Sales.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(s => s.TenantId == tenantId && s.OriginalInvoiceNumber == ds.InvoiceNumber, ct);

                if (existingSale != null)
                {
                    saleMap[ds.Id] = existingSale.Id;
                    skippedDuplicates++;
                }
                else
                {
                    var customerId = ds.CustomerId != null && customerMap.TryGetValue(ds.CustomerId, out var cId) ? cId : (Guid?)null;
                    DateTimeOffset sDate = DateTimeOffset.TryParse(ds.SaleDate, out var parsedDate)
                        ? parsedDate.ToUniversalTime()
                        : DateTimeOffset.UtcNow;
                    var cloudInvoiceNumber = $"INV-{DateTime.UtcNow:yyyyMMdd}-{ds.InvoiceNumber}";

                    var sale = Sale.CreateTaxInvoice(
                        tenantId, branchId, cloudInvoiceNumber,
                        sDate,
                        customerId, ds.CustomerName, ds.CustomerGstin,
                        ds.TotalTaxable, ds.TotalCgst, ds.TotalSgst, ds.GrandTotal,
                        ds.Discount, ds.AmountPaid, ds.PaymentMode,
                        null, null, null, null, null, null, null, Guid.Empty);

                    sale.SetOriginalInvoiceNumber(ds.InvoiceNumber);

                    // Add items for this sale
                    if (backup.SaleItems != null)
                    {
                        var saleItems = backup.SaleItems.Where(si => si.SaleId == ds.Id).ToList();
                        foreach (var dsi in saleItems)
                        {
                            var prodId = dsi.ItemId != null && itemMap.TryGetValue(dsi.ItemId, out var pi) ? pi : (Guid?)null;
                            var item = SaleItem.Create(
                                tenantId, sale.Id, prodId, dsi.ItemName, dsi.Category ?? "General",
                                dsi.Qty, dsi.PricePerUnit, null, null, dsi.IsMarginScheme,
                                false, dsi.Taxable, dsi.Cgst, dsi.Sgst, dsi.Imei);
                            sale.AddItem(item);
                        }
                    }

                    await db.Sales.AddAsync(sale, ct);
                    saleMap[ds.Id] = sale.Id;
                    insertedSales++;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // 7. IMEIs (Dedup on tenant_id, imei)
        if (backup.InventoryImeis != null)
        {
            foreach (var di in backup.InventoryImeis)
            {
                var existingImei = await db.InventoryImeis.IgnoreQueryFilters()
                    .FirstOrDefaultAsync(i => i.TenantId == tenantId && i.Imei == di.Imei, ct);

                if (existingImei != null)
                {
                    skippedDuplicates++;
                }
                else
                {
                    if (!itemMap.TryGetValue(di.ItemId, out var prodId)) continue;
                    var purchId = di.PurchaseId != null && purchaseMap.TryGetValue(di.PurchaseId, out var purId) ? purId : (Guid?)null;
                    DateOnly.TryParse(di.PurchaseDate, out var pDate);

                    var imei = InventoryImei.Create(
                        tenantId, branchId, prodId, purchId, di.Imei,
                        pDate == default ? DateOnly.FromDateTime(DateTime.UtcNow) : pDate,
                        di.PurchasePrice ?? 1m);

                    if (di.Status == "Sold" && di.SaleId != null && saleMap.TryGetValue(di.SaleId, out var sId))
                    {
                        imei.MarkSold(sId, Guid.Empty);
                    }

                    await db.InventoryImeis.AddAsync(imei, ct);
                    insertedImeis++;
                }
            }
            await db.SaveChangesAsync(ct);
        }

        // Write conflicts if any
        var dir = Path.GetDirectoryName(Path.GetFullPath(backupPath)) ?? Directory.GetCurrentDirectory();
        var conflictFile = Path.Combine(dir, "migration-conflicts.json");
        if (conflicts.Count > 0)
        {
            var conflictJson = JsonSerializer.Serialize(conflicts, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(conflictFile, conflictJson);
        }

        return new ImportSummary(
            tenantId,
            branchId,
            insertedProducts,
            insertedCustomers,
            updatedCustomers,
            insertedSales,
            insertedPurchases,
            insertedImeis,
            skippedDuplicates,
            conflicts.Count
        );
    }

    public static async Task MergeCustomersAsync(
        StoreOsDbContext db,
        Guid survivingCustomerId,
        Guid duplicateCustomerId,
        CancellationToken ct = default)
    {
        // 1. Re-point sales to surviving customer
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE sales SET customer_id = {survivingCustomerId} WHERE customer_id = {duplicateCustomerId}", ct);

        // 2. Re-point customer ledger entries to surviving customer
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE customer_ledger_entries SET customer_id = {survivingCustomerId} WHERE customer_id = {duplicateCustomerId}", ct);

        // 3. Delete the duplicate customer
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"DELETE FROM customers WHERE id = {duplicateCustomerId}", ct);

        // Clear EF change tracker so subsequent queries reload updated rows from DB
        db.ChangeTracker.Clear();
    }

    private static string NormalizeCategory(string? rawCategory)
    {
        if (string.IsNullOrWhiteSpace(rawCategory)) return ProductCategory.Accessory;
        var clean = rawCategory.Replace(" ", "").Trim();
        if (clean.Equals("NewPhone", StringComparison.OrdinalIgnoreCase)) return ProductCategory.NewPhone;
        if (clean.Equals("UsedPhone", StringComparison.OrdinalIgnoreCase) || clean.Equals("Used", StringComparison.OrdinalIgnoreCase)) return ProductCategory.UsedPhone;
        if (clean.Equals("RepairService", StringComparison.OrdinalIgnoreCase) || clean.Equals("Repair", StringComparison.OrdinalIgnoreCase) || clean.Equals("Service", StringComparison.OrdinalIgnoreCase)) return ProductCategory.RepairService;
        return ProductCategory.Accessory;
    }
}
