using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using StoreOS.Domain.Catalog;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Exceptions;
using StoreOS.Domain.FinanceOps;
using StoreOS.Domain.Identity;
using StoreOS.Domain.Inventory;
using StoreOS.Domain.Platform;
using StoreOS.Domain.Purchasing;
using StoreOS.Domain.Repairs;
using StoreOS.Domain.Sales;
using StoreOS.Domain.Suppliers;
using StoreOS.Domain.Tenancy;
using StoreOS.Infrastructure.Identity;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Provisioning;

namespace StoreOS.Seed;

public static class SeedDemoTenant
{
    public static async Task<SeedResult> SeedAsync(
        StoreOsDbContext db,
        string tenantName = "Apex Mobiles Demo",
        string subdomain = "apex",
        bool force = false,
        CancellationToken ct = default)
    {
        // 1. Idempotency check: does tenant exist?
        var existing = await db.Tenants.IgnoreQueryFilters().AsNoTracking()
            .FirstOrDefaultAsync(t => t.Name == tenantName || t.Subdomain == subdomain, ct);

        if (existing != null)
        {
            if (!force)
            {
                return new SeedResult
                {
                    Success = true,
                    Skipped = true,
                    Message = $"Tenant '{existing.Name}' ({existing.Subdomain}) already exists. Pass --force to wipe and re-seed.",
                    TenantId = existing.Id
                };
            }

            // Wipe existing tenant: delete imeis & purchases first to satisfy fk_purchases_supplier, then delete_tenant_data_for_restore
            await db.ExecuteRawSqlAsync("DELETE FROM public.inventory_imeis WHERE tenant_id = {0}", new object[] { existing.Id }, ct);
            await db.ExecuteRawSqlAsync("DELETE FROM public.purchases WHERE tenant_id = {0}", new object[] { existing.Id }, ct);
            await db.ExecuteRawSqlAsync("DELETE FROM public.invoice_sequences WHERE tenant_id = {0}", new object[] { existing.Id }, ct);
            await db.ExecuteRawSqlAsync("SELECT public.delete_tenant_data_for_restore({0})", new object[] { existing.Id }, ct);
            await db.ExecuteRawSqlAsync("DELETE FROM public.tenants WHERE id = {0}", new object[] { existing.Id }, ct);
        }

        // 2. Provision Tenant, Branch B01, Roles, Owner via ProvisioningService (P-1206)
        var provService = new ProvisioningService(db);
        var provResult = await provService.ProvisionAsync(new ProvisionRequest(
            Name: tenantName,
            Subdomain: subdomain,
            OwnerEmail: "owner@apex.example",
            OwnerName: "Owner User",
            BranchCode: "B01",
            BranchName: "Main Branch",
            Gstin: "27AAAAA0000A1Z5",
            Timezone: "Asia/Kolkata",
            Repairs: true,
            OwnerPassword: "Demo@12345"
        ), ct);

        if (!provResult.Success)
        {
            throw new InvalidOperationException($"Provisioning failed: {provResult.Error}");
        }

        var tenantId = provResult.TenantId;
        var branchB01Id = provResult.BranchId;
        var ownerUserId = provResult.OwnerUserId;

        // 3. Create Branch B02 ("Mall Branch")
        var branchB02 = Branch.Create(tenantId, "B02", "Mall Branch");
        branchB02.UpdateProfile("Mall Branch", "Phoenix Mall, Lower Parel, Mumbai", "022-98765432", "mall@apex.example", "27AAAAA0000A1Z5");
        await db.Branches.AddAsync(branchB02, ct);
        await db.SaveChangesAsync(ct);
        var branchB02Id = branchB02.Id;

        // 4. Create Users per role with known password "Demo@12345"
        var roles = await db.Roles.IgnoreQueryFilters().Where(r => r.TenantId == tenantId).ToDictionaryAsync(r => r.Name, StringComparer.OrdinalIgnoreCase, ct);
        var managerRole = roles[StandardRoles.BranchManager];
        var cashierRole = roles[StandardRoles.Cashier];
        var techRole = roles[StandardRoles.Technician];

        var hasher = new Argon2PasswordHasher();
        var demoPasswordHash = hasher.Hash("Demo@12345");

        var managerUser = User.Create(tenantId, "manager@apex.example", demoPasswordHash, "Branch Manager");
        await db.Users.AddAsync(managerUser, ct);

        var cashierUser = User.Create(tenantId, "cashier@apex.example", demoPasswordHash, "Cashier User");
        await db.Users.AddAsync(cashierUser, ct);

        var techUser = User.Create(tenantId, "technician@apex.example", demoPasswordHash, "Technician User");
        await db.Users.AddAsync(techUser, ct);

        await db.SaveChangesAsync(ct);

        // Assign default branch
        await db.UserBranches.AddAsync(UserBranch.Create(managerUser.Id, branchB01Id, isDefault: true), ct);
        await db.UserBranches.AddAsync(UserBranch.Create(cashierUser.Id, branchB01Id, isDefault: true), ct);
        await db.UserBranches.AddAsync(UserBranch.Create(techUser.Id, branchB01Id, isDefault: true), ct);

        // Assign branch-scoped roles to B01
        await db.UserRoleAssignments.AddAsync(UserRoleAssignment.Create(managerUser.Id, managerRole.Id, branchB01Id), ct);
        await db.UserRoleAssignments.AddAsync(UserRoleAssignment.Create(cashierUser.Id, cashierRole.Id, branchB01Id), ct);
        await db.UserRoleAssignments.AddAsync(UserRoleAssignment.Create(techUser.Id, techRole.Id, branchB01Id), ct);

        await db.SaveChangesAsync(ct);

        // 5. Catalog Products
        var products = new List<Product>();

        // 5.1 New Phones (5 items)
        var newPhones = new List<Product>
        {
            Product.Create(tenantId, "Apple iPhone 15 Pro 128GB", ProductCategory.NewPhone, 134900m, 120000m, 18m, "85171300"),
            Product.Create(tenantId, "Samsung Galaxy S24 Ultra 256GB", ProductCategory.NewPhone, 129999m, 115000m, 18m, "85171300"),
            Product.Create(tenantId, "Google Pixel 8 Pro 128GB", ProductCategory.NewPhone, 106999m, 95000m, 18m, "85171300"),
            Product.Create(tenantId, "OnePlus 12 256GB", ProductCategory.NewPhone, 64999m, 58000m, 18m, "85171300"),
            Product.Create(tenantId, "Xiaomi 14 512GB", ProductCategory.NewPhone, 69999m, 62000m, 18m, "85171300")
        };
        products.AddRange(newPhones);

        // 5.2 Used Phones with Margin Scheme (5 items)
        var usedPhones = new List<Product>
        {
            Product.Create(tenantId, "Apple iPhone 13 128GB (Pre-owned)", ProductCategory.UsedPhone, 42000m, 35000m, 18m, "85171300"),
            Product.Create(tenantId, "Apple iPhone 14 128GB (Pre-owned)", ProductCategory.UsedPhone, 52000m, 44000m, 18m, "85171300"),
            Product.Create(tenantId, "Samsung Galaxy S22 128GB (Pre-owned)", ProductCategory.UsedPhone, 32000m, 26000m, 18m, "85171300"),
            Product.Create(tenantId, "Google Pixel 7 128GB (Pre-owned)", ProductCategory.UsedPhone, 34000m, 28000m, 18m, "85171300"),
            Product.Create(tenantId, "OnePlus 11 128GB (Pre-owned)", ProductCategory.UsedPhone, 36000m, 30000m, 18m, "85171300")
        };
        foreach (var up in usedPhones)
        {
            up.SetMarginScheme(true);
        }
        products.AddRange(usedPhones);

        // 5.3 Accessories (10 items)
        var accessories = new List<Product>
        {
            Product.Create(tenantId, "Apple 20W USB-C Power Adapter", ProductCategory.Accessory, 1900m, 1400m, 18m, "85044090"),
            Product.Create(tenantId, "Apple MagSafe Charger", ProductCategory.Accessory, 4500m, 3500m, 18m, "85044090"),
            Product.Create(tenantId, "Tempered Glass Screen Protector 9H", ProductCategory.Accessory, 499m, 150m, 18m, "70071900"),
            Product.Create(tenantId, "Silicone Case Clear with MagSafe", ProductCategory.Accessory, 999m, 350m, 18m, "39269099"),
            Product.Create(tenantId, "Braided USB-C to USB-C Cable 2M", ProductCategory.Accessory, 799m, 300m, 18m, "85444299"),
            Product.Create(tenantId, "Fast Car Charger Dual 30W", ProductCategory.Accessory, 1299m, 650m, 18m, "85044090"),
            Product.Create(tenantId, "Wireless Earbuds Pro", ProductCategory.Accessory, 3999m, 2500m, 18m, "85183000"),
            Product.Create(tenantId, "Power Bank 10000mAh 22.5W", ProductCategory.Accessory, 1999m, 1200m, 18m, "85076000"),
            Product.Create(tenantId, "Aluminum Desktop Phone Stand", ProductCategory.Accessory, 699m, 280m, 18m, "76169990"),
            Product.Create(tenantId, "Portable Bluetooth Speaker 5W", ProductCategory.Accessory, 1499m, 850m, 18m, "85182100")
        };
        products.AddRange(accessories);

        // 5.4 Repair Services (3 items)
        var repairServices = new List<Product>
        {
            Product.Create(tenantId, "Screen Replacement Service", ProductCategory.RepairService, 2500m, 0m, 18m, "998713"),
            Product.Create(tenantId, "Battery Replacement Service", ProductCategory.RepairService, 1500m, 0m, 18m, "998713"),
            Product.Create(tenantId, "Charging Port Repair Service", ProductCategory.RepairService, 1200m, 0m, 18m, "998713")
        };
        products.AddRange(repairServices);

        await db.Products.AddRangeAsync(products, ct);
        await db.SaveChangesAsync(ct);

        // 6. Suppliers (3 items)
        var s1 = Supplier.Create(tenantId, "TechSource Distributing Pvt Ltd", "9820011111", "Lamington Road, Mumbai", "New Phones & Accessories", "Primary distributor");
        var s2 = Supplier.Create(tenantId, "RenewMobiles Wholesale", "9820022222", "Fort, Mumbai", "Used Phones", "Pre-owned stock supplier");
        var s3 = Supplier.Create(tenantId, "QuickFix Parts & Spares", "9820033333", "Opera House, Mumbai", "Repair Parts", "OEM replacement parts");

        await db.Suppliers.AddRangeAsync(new[] { s1, s2, s3 }, ct);
        await db.SaveChangesAsync(ct);

        // Supplier Transactions & Ledgers
        var txnDate1 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-20));
        var txnDate2 = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-10));

        // Supplier 1: Bill 500,000, Payment 300,000 -> 200,000 payable
        var s1Bill = SupplierTransaction.Create(tenantId, s1.Id, branchB01Id, SupplierTransactionType.Bill, 500000m, null, "BILL-TS-001", txnDate1, "Initial shipment", ownerUserId);
        var s1BillLedger = SupplierLedgerEntry.CreateCredit(tenantId, s1.Id, branchB01Id, "Bill", 500000m, txnDate1, "BILL-TS-001");
        var s1Pay = SupplierTransaction.Create(tenantId, s1.Id, branchB01Id, SupplierTransactionType.Payment, 300000m, SupplierPaymentMode.BankTransfer, "PAY-TS-001", txnDate2, "Partial payment", ownerUserId);
        var s1PayLedger = SupplierLedgerEntry.CreateDebit(tenantId, s1.Id, branchB01Id, "Payment", 300000m, txnDate2, "PAY-TS-001");

        // Supplier 2: Bill 300,000, Payment 200,000 -> 100,000 payable
        var s2Bill = SupplierTransaction.Create(tenantId, s2.Id, branchB01Id, SupplierTransactionType.Bill, 300000m, null, "BILL-RM-001", txnDate1, "Used phones lot", ownerUserId);
        var s2BillLedger = SupplierLedgerEntry.CreateCredit(tenantId, s2.Id, branchB01Id, "Bill", 300000m, txnDate1, "BILL-RM-001");
        var s2Pay = SupplierTransaction.Create(tenantId, s2.Id, branchB01Id, SupplierTransactionType.Payment, 200000m, SupplierPaymentMode.BankTransfer, "PAY-RM-001", txnDate2, "Settlement", ownerUserId);
        var s2PayLedger = SupplierLedgerEntry.CreateDebit(tenantId, s2.Id, branchB01Id, "Payment", 200000m, txnDate2, "PAY-RM-001");

        // Supplier 3: Bill 100,000 -> 100,000 payable
        var s3Bill = SupplierTransaction.Create(tenantId, s3.Id, branchB01Id, SupplierTransactionType.Bill, 100000m, null, "BILL-QF-001", txnDate1, "Spares batch", ownerUserId);
        var s3BillLedger = SupplierLedgerEntry.CreateCredit(tenantId, s3.Id, branchB01Id, "Bill", 100000m, txnDate1, "BILL-QF-001");

        await db.SupplierTransactions.AddRangeAsync(new[] { s1Bill, s1Pay, s2Bill, s2Pay, s3Bill }, ct);
        await db.SupplierLedgerEntries.AddRangeAsync(new[] { s1BillLedger, s1PayLedger, s2BillLedger, s2PayLedger, s3BillLedger }, ct);

        // Supplier Return: Return to Supplier 1
        var sReturn = SupplierReturn.Create(
            tenantId, branchB01Id, "RET-B01-001", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)),
            s1.Id, s1.Phone, s1.Address, SettlementType.PendingCredit, "Defective power adapter", "Returned 1 adapter", ownerUserId);
        var sReturnItem = SupplierReturnItem.Create(tenantId, sReturn.Id, accessories[0].Id, accessories[0].Name, 1, 1400m, 1400m);
        sReturn.AddItem(sReturnItem);

        await db.SupplierReturns.AddAsync(sReturn, ct);
        await db.SupplierReturnItems.AddAsync(sReturnItem, ct);
        await db.SaveChangesAsync(ct);

        // 7. Stock: BranchInventory rows at BOTH branches for every physical product (20 products)
        var physicalProducts = newPhones.Concat(usedPhones).Concat(accessories).ToList();
        var branches = new[] { branchB01Id, branchB02Id };
        var branchInventories = new List<BranchInventory>();
        var totalImeis = new List<InventoryImei>();
        var totalPurchases = new List<Purchase>();

        for (int bIdx = 0; bIdx < branches.Length; bIdx++)
        {
            var branchId = branches[bIdx];
            var branchCode = bIdx == 0 ? "B01" : "B02";

            for (int pIdx = 0; pIdx < physicalProducts.Count; pIdx++)
            {
                var prod = physicalProducts[pIdx];
                var inv = BranchInventory.Create(tenantId, branchId, prod.Id);

                // Requirements:
                // - At least one item deliberately at/below low-stock threshold (pIdx == 0 at B01: stock 2, threshold 5)
                // - At least one item at exactly zero (pIdx == 1 at B02: stock 0, threshold 3)
                // - Rest have positive stock (10, 15, or 20)
                int stockQty;
                int threshold = 3;

                if (bIdx == 0 && pIdx == 0)
                {
                    stockQty = 2;
                    threshold = 5;
                }
                else if (bIdx == 1 && pIdx == 1)
                {
                    stockQty = 0;
                    threshold = 3;
                }
                else
                {
                    stockQty = 10 + (pIdx % 3) * 5;
                }

                if (stockQty > 0)
                {
                    inv.IncreaseStock(stockQty, prod.PurchasePrice);
                }
                inv.SetLowStockThreshold(threshold);
                branchInventories.Add(inv);

                // For Phone products (first 10 physical items), create real Purchase and at least 3 InStock IMEIs per branch
                if (pIdx < 10)
                {
                    var supplierId = pIdx < 5 ? s1.Id : s2.Id;
                    var supplierName = pIdx < 5 ? s1.Name : s2.Name;
                    var purchaseQty = Math.Max(stockQty, 3);
                    var purchaseDate = DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15));

                    var purchase = Purchase.Create(
                        tenantId, branchId, prod.Id, purchaseQty, prod.PurchasePrice,
                        supplierId, supplierName, "9820011111", "Mumbai", purchaseDate, ownerUserId,
                        $"INV-SUP-{branchCode}-{pIdx:D2}");
                    totalPurchases.Add(purchase);

                    // Create at least 3 InStock IMEIs
                    for (int k = 1; k <= 3; k++)
                    {
                        var imeiString = $"35{bIdx + 1}{pIdx:D2}{k:D2}{RandomNumberGenerator.GetInt32(1000000, 9999999)}";
                        var imei = InventoryImei.Create(
                            tenantId, branchId, prod.Id, purchase.Id, imeiString,
                            purchaseDate, prod.PurchasePrice);
                        totalImeis.Add(imei);
                    }
                }
            }
        }

        await db.BranchInventories.AddRangeAsync(branchInventories, ct);
        await db.Purchases.AddRangeAsync(totalPurchases, ct);
        await db.InventoryImeis.AddRangeAsync(totalImeis, ct);
        await db.SaveChangesAsync(ct);

        // 8. Customers (At least 5)
        var customers = new List<Customer>
        {
            Customer.Create(tenantId, "Rahul Dravid", "9845012345", "29ABCDE1234F1Z5"),
            Customer.Create(tenantId, "Sachin Tendulkar", "9820054321", null),
            Customer.Create(tenantId, "VVS Laxman", "9849011223", null),
            Customer.Create(tenantId, "Sourav Ganguly", "9830099887", null),
            Customer.Create(tenantId, "Anonymized Target", "9900011122", null)
        };

        // Customer 3: Credit limit set
        customers[2].SetCreditLimit(50000m);

        // Customer 5: Anonymized
        customers[4].Anonymize();

        await db.Customers.AddRangeAsync(customers, ct);
        await db.SaveChangesAsync(ct);

        // 9. Sales History
        // 9.1 Daily TaxInvoices spanning last 7 days (to populate revenue chart)
        var sales = new List<Sale>();
        var saleItems = new List<SaleItem>();

        for (int dayOffset = -6; dayOffset <= 0; dayOffset++)
        {
            var saleDate = DateTimeOffset.UtcNow.AddDays(dayOffset);
            var invNum = $"B01/26-27/0000{Math.Abs(dayOffset) + 1:D2}";
            var customer = customers[Math.Abs(dayOffset) % customers.Count];
            var accessory = accessories[Math.Abs(dayOffset) % accessories.Count];

            var taxable = accessory.SellPrice / 1.18m;
            var gstHalves = (accessory.SellPrice - taxable) / 2m;

            var sale = Sale.CreateTaxInvoice(
                tenantId, branchB01Id, invNum, saleDate, customer.Id, customer.Name, customer.Gstin,
                taxable, gstHalves, gstHalves, accessory.SellPrice, 0m, accessory.SellPrice,
                PaymentMode.UPI, null, null, null, null, null, "Daily Sale", null, ownerUserId);

            var item = SaleItem.Create(
                tenantId, sale.Id, accessory.Id, accessory.Name, accessory.Category, 1, accessory.SellPrice,
                accessory.PurchasePrice, accessory.HsnCode, false, false, taxable, gstHalves, gstHalves, null);

            sale.AddItem(item);
            sales.Add(sale);
            saleItems.Add(item);
        }

        // 9.2 Estimate
        var estimate = Sale.CreateEstimate(
            tenantId, branchB01Id, "B01/E/26-27/000001", DateTimeOffset.UtcNow.AddDays(-2),
            customers[0].Id, customers[0].Name, customers[0].Gstin,
            114322.03m, 10288.985m, 10288.985m, 134900m, 0m, 0m,
            null, null, null, null, null, null, "iPhone 15 Pro Quote", null, ownerUserId);
        var estItem = SaleItem.Create(
            tenantId, estimate.Id, newPhones[0].Id, newPhones[0].Name, newPhones[0].Category, 1, 134900m,
            newPhones[0].PurchasePrice, newPhones[0].HsnCode, false, false, 114322.03m, 10288.985m, 10288.985m, null);
        estimate.AddItem(estItem);
        sales.Add(estimate);
        saleItems.Add(estItem);

        // 9.3 Credit Sale with Customer Laxman & subsequent partial payment
        var laxman = customers[2];
        var creditSale = Sale.CreateTaxInvoice(
            tenantId, branchB01Id, "B01/26-27/000040", DateTimeOffset.UtcNow.AddDays(-4),
            laxman.Id, laxman.Name, laxman.Gstin,
            42372.88m, 3813.56m, 3813.56m, 50000m, 0m, 5000m,
            PaymentMode.Cash, null, null, null, null, null, "Credit Sale - Partial Paid", null, ownerUserId);
        var creditItem = SaleItem.Create(
            tenantId, creditSale.Id, usedPhones[0].Id, usedPhones[0].Name, usedPhones[0].Category, 1, 50000m,
            usedPhones[0].PurchasePrice, usedPhones[0].HsnCode, true, false, 42372.88m, 3813.56m, 3813.56m, null);
        creditSale.AddItem(creditItem);
        sales.Add(creditSale);
        saleItems.Add(creditItem);

        // Debit ledger entry for credit balance: 45,000 balance due
        var creditLedgerEntry = CustomerLedgerEntry.CreateDebit(
            tenantId, laxman.Id, creditSale.Id, CustomerLedgerEntryType.Invoice, 45000m, branchB01Id,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-4)), creditSale.InvoiceNumber);
        await db.CustomerLedgerEntries.AddAsync(creditLedgerEntry, ct);

        // Partial subsequent payment (5,000 paid 2 days later, leaving balance 40,000)
        var custPayment = CustomerPayment.Create(
            tenantId, creditSale.Id, 5000m, PaymentMode.UPI, DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), ownerUserId);
        await db.CustomerPayments.AddAsync(custPayment, ct);

        var custPayLedger = CustomerLedgerEntry.CreateCredit(
            tenantId, laxman.Id, creditSale.Id, "Payment", 5000m, branchB01Id,
            DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-2)), "RCPT-001");
        await db.CustomerLedgerEntries.AddAsync(custPayLedger, ct);

        // 9.4 Voided Sale
        var voidSale = Sale.CreateTaxInvoice(
            tenantId, branchB01Id, "B01/26-27/000050", DateTimeOffset.UtcNow.AddDays(-3),
            customers[1].Id, customers[1].Name, null,
            1610.17m, 144.915m, 144.915m, 1900m, 0m, 1900m,
            PaymentMode.Cash, null, null, null, null, null, "Voided Sale Test", null, ownerUserId);
        var voidItem = SaleItem.Create(
            tenantId, voidSale.Id, accessories[0].Id, accessories[0].Name, accessories[0].Category, 1, 1900m,
            accessories[0].PurchasePrice, accessories[0].HsnCode, false, false, 1610.17m, 144.915m, 144.915m, null);
        voidSale.AddItem(voidItem);
        voidSale.MarkVoided();
        sales.Add(voidSale);
        saleItems.Add(voidItem);

        // 9.5 Refunded Sale
        var refundSale = Sale.CreateTaxInvoice(
            tenantId, branchB01Id, "B01/26-27/000051", DateTimeOffset.UtcNow.AddDays(-2),
            customers[3].Id, customers[3].Name, null,
            3813.56m, 343.22m, 343.22m, 4500m, 0m, 4500m,
            PaymentMode.Card, null, null, null, null, null, "Refunded Sale Test", null, ownerUserId);
        var refundItem = SaleItem.Create(
            tenantId, refundSale.Id, accessories[1].Id, accessories[1].Name, accessories[1].Category, 1, 4500m,
            accessories[1].PurchasePrice, accessories[1].HsnCode, false, false, 3813.56m, 343.22m, 343.22m, null);
        refundSale.AddItem(refundItem);
        refundSale.MarkRefunded();
        sales.Add(refundSale);
        saleItems.Add(refundItem);

        // 9.6 Financed Sale
        var financeSale = Sale.CreateTaxInvoice(
            tenantId, branchB01Id, "B01/26-27/000060", DateTimeOffset.UtcNow.AddDays(-1),
            customers[0].Id, customers[0].Name, customers[0].Gstin,
            55083.90m, 4957.55m, 4957.55m, 64999m, 0m, 10000m,
            PaymentMode.Finance, "Bajaj Finserv", "Bajaj Finance Ltd", 10000m,
            "{\"scheme\":\"6_MONTH_ZERO_INT\",\"downPayment\":10000,\"emiAmount\":9166.50}", null,
            "Financed OnePlus 12", null, ownerUserId);
        var financeItem = SaleItem.Create(
            tenantId, financeSale.Id, newPhones[3].Id, newPhones[3].Name, newPhones[3].Category, 1, 64999m,
            newPhones[3].PurchasePrice, newPhones[3].HsnCode, false, false, 55083.90m, 4957.55m, 4957.55m, null);
        financeSale.AddItem(financeItem);
        sales.Add(financeSale);
        saleItems.Add(financeItem);

        await db.Sales.AddRangeAsync(sales, ct);
        await db.SaleItems.AddRangeAsync(saleItems, ct);
        await db.SaveChangesAsync(ct);

        // Initialize invoice sequences matching seeded sales
        var fyLabel = InvoiceNumberFormatter.FinancialYearLabel(DateOnly.FromDateTime(DateTime.UtcNow));
        var seqTax = InvoiceSequence.Create(tenantId, branchB01Id, $"FY{fyLabel}", 70);
        var seqEst = InvoiceSequence.Create(tenantId, branchB01Id, $"EST-FY{fyLabel}", 1);
        await db.InvoiceSequences.AddRangeAsync(new[] { seqTax, seqEst }, ct);
        await db.SaveChangesAsync(ct);

        // 10. Expenses
        var expenses = new List<Expense>
        {
            Expense.Create(tenantId, branchB01Id, "Store Rent - Main Branch", 45000m, "Rent", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15)), "Monthly lease", ownerUserId),
            Expense.Create(tenantId, branchB01Id, "Electricity Bill - Main Branch", 6500m, "Utilities", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-12)), "BESCOM power bill", ownerUserId),
            Expense.Create(tenantId, branchB01Id, "Staff Tea & Coffee", 1800m, "Office", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-5)), "Pantry supplies", ownerUserId),
            Expense.Create(tenantId, branchB02Id, "Store Rent - Mall Branch", 60000m, "Rent", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-15)), "Mall lease", ownerUserId),
            Expense.Create(tenantId, branchB02Id, "Fiber Broadband - Mall Branch", 2400m, "Utilities", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-8)), "Internet bill", ownerUserId)
        };
        await db.Expenses.AddRangeAsync(expenses, ct);

        // 11. Liabilities & Payment
        var loan = Liability.Create(
            tenantId, "Working Capital Business Loan", LiabilityType.Loan,
            "HDFC Bank", 500000m, DateOnly.FromDateTime(DateTime.UtcNow.AddMonths(12)), "3-year facility");
        await db.Liabilities.AddAsync(loan, ct);
        await db.SaveChangesAsync(ct);

        var loanPayment = LiabilityPayment.Create(
            tenantId, loan.Id, 50000m, "BankTransfer", DateOnly.FromDateTime(DateTime.UtcNow.AddDays(-7)), "Monthly principal installment");
        await db.LiabilityPayments.AddAsync(loanPayment, ct);
        await db.SaveChangesAsync(ct);

        // 12. Full Repair Job Lifecycle (Each status represented)
        var repairJobs = new List<RepairJob>();

        // 12.1 Received
        var r1 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0001", customers[0].Id, customers[0].Name, customers[0].Phone,
            null, "Apple", "iPhone 13", "Phone", "SN112233", "351111111111111", null, "Light scratches", "Phone only",
            "Screen shattered after drop", "Customer needs urgently", 2500m, 500m, DateTimeOffset.UtcNow.AddDays(2));
        repairJobs.Add(r1);

        // 12.2 Diagnosing
        var r2 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0002", customers[1].Id, customers[1].Name, customers[1].Phone,
            null, "Samsung", "Galaxy S22", "Phone", "SN223344", "352222222222222", null, "Good condition", "Phone + Charger",
            "Not charging via USB-C", null, 1200m, null, DateTimeOffset.UtcNow.AddDays(1));
        r2.TransitionTo(RepairStatus.Diagnosing);
        r2.RecordDiagnosis("Charging port daughterboard damaged", "Requires USB-C sub-board replacement", "Part in stock", "Technician User");
        repairJobs.Add(r2);

        // 12.3 WaitingForApproval
        var r3 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0003", customers[2].Id, customers[2].Name, customers[2].Phone,
            null, "Google", "Pixel 7", "Phone", "SN334455", "353333333333333", null, "Minor dents", "Phone only",
            "Water exposure, no sound", null, 3500m, null, DateTimeOffset.UtcNow.AddDays(3));
        r3.TransitionTo(RepairStatus.Diagnosing);
        r3.TransitionTo(RepairStatus.WaitingForApproval);
        repairJobs.Add(r3);

        // 12.4 WaitingForParts
        var r4 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0004", customers[3].Id, customers[3].Name, customers[3].Phone,
            null, "OnePlus", "OnePlus 11", "Phone", "SN445566", "354444444444444", null, "Camera glass cracked", "Phone only",
            "Rear main lens broken", null, 2800m, null, DateTimeOffset.UtcNow.AddDays(5));
        r4.TransitionTo(RepairStatus.Diagnosing);
        r4.TransitionTo(RepairStatus.WaitingForParts);
        repairJobs.Add(r4);

        // 12.5 InRepair (with Parts Consumed)
        var r5 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0005", customers[0].Id, customers[0].Name, customers[0].Phone,
            null, "Apple", "iPhone 14", "Phone", "SN556677", "355555555555555", null, "Pristine", "Phone only",
            "Battery health 71%, shuts down", null, 3200m, 1000m, DateTimeOffset.UtcNow.AddDays(1));
        r5.TransitionTo(RepairStatus.Diagnosing);
        r5.TransitionTo(RepairStatus.InRepair);
        repairJobs.Add(r5);

        // 12.6 ReadyForPickup
        var r6 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0006", customers[1].Id, customers[1].Name, customers[1].Phone,
            null, "Xiaomi", "Xiaomi 13", "Phone", "SN667788", "356666666666666", null, "Case attached", "Phone only",
            "Speaker distortion at high volume", null, 1500m, null, DateTimeOffset.UtcNow.AddDays(-1));
        r6.TransitionTo(RepairStatus.Diagnosing);
        r6.TransitionTo(RepairStatus.InRepair);
        r6.TransitionTo(RepairStatus.ReadyForPickup);
        repairJobs.Add(r6);

        // 12.7 Delivered & Billed (linked to a Sale)
        var r7 = RepairJob.Intake(
            tenantId, branchB01Id, "REP-B01-20261001-0007", customers[2].Id, customers[2].Name, customers[2].Phone,
            null, "Apple", "iPhone 12", "Phone", "SN778899", "357777777777777", null, "Normal wear", "Phone only",
            "Back glass shattered", null, 2500m, null, DateTimeOffset.UtcNow.AddDays(-2));
        r7.TransitionTo(RepairStatus.Diagnosing);
        r7.TransitionTo(RepairStatus.InRepair);
        r7.TransitionTo(RepairStatus.ReadyForPickup);
        r7.Deliver(DateTimeOffset.UtcNow.AddDays(-1), DateOnly.FromDateTime(DateTime.UtcNow.AddDays(29)));

        // Create sale and link r7 to sale
        var billedSale = Sale.CreateTaxInvoice(
            tenantId, branchB01Id, "B01/26-27/000070", DateTimeOffset.UtcNow.AddDays(-1),
            customers[2].Id, customers[2].Name, null,
            2118.64m, 190.68m, 190.68m, 2500m, 0m, 2500m,
            PaymentMode.UPI, null, null, null, null, null, "Back Glass Repair Bill", r7.Id, ownerUserId);
        var billedSaleItem = SaleItem.Create(
            tenantId, billedSale.Id, repairServices[0].Id, "iPhone 12 Back Glass Replacement", ProductCategory.RepairService,
            1, 2500m, 0m, "998713", false, false, 2118.64m, 190.68m, 190.68m, null);
        billedSale.AddItem(billedSaleItem);
        await db.Sales.AddAsync(billedSale, ct);
        await db.SaleItems.AddAsync(billedSaleItem, ct);

        r7.LinkToSale(billedSale.Id, billedSale.InvoiceNumber);
        repairJobs.Add(r7);

        await db.RepairJobs.AddRangeAsync(repairJobs, ct);
        await db.SaveChangesAsync(ct);

        // Part consumed for job r5
        var part = RepairJobPart.Reserve(
            tenantId, r5.Id, null, "OEM Replacement Battery iPhone 14", 1, 1200m, 2000m);
        part.MarkConsumed();
        await db.RepairJobParts.AddAsync(part, ct);
        await db.SaveChangesAsync(ct);

        return new SeedResult
        {
            Success = true,
            Skipped = false,
            Message = "Demo tenant seeded successfully.",
            TenantId = tenantId,
            BranchB01Id = branchB01Id,
            BranchB02Id = branchB02Id,
            ProductCount = products.Count,
            InventoryCount = branchInventories.Count,
            ImeiCount = totalImeis.Count,
            CustomerCount = customers.Count,
            SupplierCount = 3,
            SaleCount = sales.Count + 1, // sales + billedSale
            ExpenseCount = expenses.Count,
            LiabilityCount = 1,
            RepairJobCount = repairJobs.Count
        };
    }
}
