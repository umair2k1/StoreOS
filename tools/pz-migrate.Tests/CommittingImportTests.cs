using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using PzMigrate;
using StoreOS.Domain.Customers;
using StoreOS.Domain.Sales;
using StoreOS.Infrastructure.Persistence;
using StoreOS.Domain.Tenancy;

namespace PzMigrate.Tests;

[Collection("pz-migrate-postgres")]
public class CommittingImportTests : IDisposable
{
    private readonly MigrationPostgresFixture _fixture;
    private readonly string _tempDir;

    public CommittingImportTests(MigrationPostgresFixture fixture)
    {
        _fixture = fixture;
        _tempDir = Path.Combine(Path.GetTempPath(), "pz_commit_tests_" + Guid.NewGuid());
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
            {
                Directory.Delete(_tempDir, true);
            }
        }
        catch
        {
            // Ignore temp dir cleanup failure
        }
    }

    private string CreateSampleDesktopBackupFile()
    {
        var sampleJson = """
        {
          "settings": [
            { "key": "shop.name", "value": "Desktop Phone Zone" }
          ],
          "items": [
            {
              "id": "1",
              "name": "Samsung Galaxy S23",
              "category": "New Phone",
              "stock_qty": 5,
              "purchase_price": 50000.00,
              "sell_price": 65000.00,
              "gst_rate": 18.00,
              "is_margin_scheme": false,
              "hsn_code": "85171200",
              "supplier_name": "Samsung India"
            },
            {
              "id": "2",
              "name": "Apple iPhone 14",
              "category": "New Phone",
              "stock_qty": 10,
              "purchase_price": 60000.00,
              "sell_price": 75000.00,
              "gst_rate": 18.00,
              "is_margin_scheme": false,
              "hsn_code": "85171200",
              "supplier_name": "Apple Distributors"
            },
            {
              "id": "3",
              "name": "Screen Protector Glass",
              "category": "Accessory",
              "stock_qty": 2,
              "purchase_price": 100.00,
              "sell_price": 250.00,
              "gst_rate": 18.00,
              "is_margin_scheme": false,
              "hsn_code": "39269099"
            }
          ],
          "purchases": [
            {
              "id": "1",
              "item_id": "1",
              "qty": 5,
              "purchase_rate": 50000.00,
              "supplier_name": "Samsung India",
              "supplier_invoice_no": "PUR-101",
              "purchase_date": "2026-01-10"
            }
          ],
          "sales": [
            {
              "id": "1",
              "invoice_number": "INV-1001",
              "sale_date": "2026-02-01",
              "customer_id": "1",
              "customer_name": "Alice Sharma",
              "total_taxable": 12711.86,
              "total_cgst": 1144.07,
              "total_sgst": 1144.07,
              "grand_total": 15000.50,
              "discount": 0.00,
              "amount_paid": 15000.50,
              "status": "Completed",
              "payment_mode": "Cash"
            },
            {
              "id": "2",
              "invoice_number": "INV-1002",
              "sale_date": "2026-02-02",
              "customer_id": "2",
              "customer_name": "Bob Patel",
              "total_taxable": 21186.44,
              "total_cgst": 1906.78,
              "total_sgst": 1906.78,
              "grand_total": 25000.00,
              "discount": 0.00,
              "amount_paid": 20000.00,
              "status": "Completed",
              "payment_mode": "UPI"
            }
          ],
          "sale_items": [
            {
              "id": "1",
              "sale_id": "1",
              "item_id": "1",
              "item_name": "Samsung Galaxy S23",
              "category": "New Phone",
              "qty": 1,
              "price_per_unit": 15000.50,
              "taxable": 12711.86,
              "cgst": 1144.07,
              "sgst": 1144.07
            }
          ],
          "customers": [
            {
              "id": "1",
              "name": "Alice Sharma",
              "phone": "9876543210",
              "total_purchases": 15000.50,
              "credit_balance": 5000.00
            },
            {
              "id": "2",
              "name": "Bob Patel",
              "phone": "9876543211",
              "total_purchases": 25000.00,
              "credit_balance": 0.00
            },
            {
              "id": "3",
              "name": "Charlie Verma",
              "phone": "9876543212",
              "total_purchases": 0.00,
              "credit_balance": 1200.50
            }
          ],
          "customer_payments": [
            {
              "id": "1",
              "sale_id": "1",
              "amount": 10000.00,
              "payment_mode": "Cash",
              "payment_date": "2026-02-01"
            },
            {
              "id": "2",
              "sale_id": "2",
              "amount": 15000.50,
              "payment_mode": "UPI",
              "payment_date": "2026-02-02"
            }
          ],
          "inventory_imeis": [
            {
              "id": "1",
              "item_id": "1",
              "purchase_id": "1",
              "imei": "358000000000001",
              "status": "InStock",
              "purchase_date": "2026-01-10",
              "purchase_price": 50000.00
            }
          ]
        }
        """;

        var filePath = Path.Combine(_tempDir, "desktop-backup.json");
        File.WriteAllText(filePath, sampleJson);
        return filePath;
    }

    [Fact]
    public async Task Import_FirstRunAndIdempotentSecondRun_PreservesCountsAndCreatesZeroDuplicates()
    {
        var backupPath = CreateSampleDesktopBackupFile();
        var tenantName = "Test Idempotent Tenant " + Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();

        // 1. First Committing Import (Task 5a)
        var summary1 = await DesktopMigrationService.RunImportAsync(backupPath, tenantName, db);
        summary1.InsertedProducts.Should().Be(3);
        summary1.InsertedCustomers.Should().Be(3);
        summary1.InsertedSales.Should().Be(2);
        summary1.InsertedPurchases.Should().Be(1);
        summary1.InsertedImeis.Should().Be(1);

        // Verify counts directly from DB
        var tenantId = summary1.TenantId;
        var productCount1 = await db.Products.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId);
        var customerCount1 = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId);
        var salesCount1 = await db.Sales.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId);
        var purchaseCount1 = await db.Purchases.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId);
        var imeiCount1 = await db.InventoryImeis.IgnoreQueryFilters().CountAsync(i => i.TenantId == tenantId);

        productCount1.Should().Be(3);
        customerCount1.Should().Be(3);
        salesCount1.Should().Be(2);
        purchaseCount1.Should().Be(1);
        imeiCount1.Should().Be(1);

        // 2. Second Committing Import with the SAME UNCHANGED backup (Task 5b)
        var summary2 = await DesktopMigrationService.RunImportAsync(backupPath, tenantName, db);
        summary2.InsertedProducts.Should().Be(0);
        summary2.InsertedCustomers.Should().Be(0);
        summary2.InsertedSales.Should().Be(0);
        summary2.InsertedPurchases.Should().Be(0);
        summary2.InsertedImeis.Should().Be(0);
        summary2.SkippedDuplicates.Should().BeGreaterThan(0);

        // Assert zero new rows in database
        var productCount2 = await db.Products.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId);
        var customerCount2 = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId);
        var salesCount2 = await db.Sales.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId);
        var purchaseCount2 = await db.Purchases.IgnoreQueryFilters().CountAsync(p => p.TenantId == tenantId);
        var imeiCount2 = await db.InventoryImeis.IgnoreQueryFilters().CountAsync(i => i.TenantId == tenantId);

        productCount2.Should().Be(productCount1);
        customerCount2.Should().Be(customerCount1);
        salesCount2.Should().Be(salesCount1);
        purchaseCount2.Should().Be(purchaseCount1);
        imeiCount2.Should().Be(imeiCount1);
    }

    [Fact]
    public async Task Import_UpdatedBackup_ImportsNewSaleAndUpdatesCustomerPhoneWithoutDuplicate()
    {
        // (c) re-running against an UPDATED backup (same fixture with one NEW sale added
        // and one EXISTING customer's phone number changed) correctly imports ONLY the new sale
        // and UPDATES the existing customer's phone (matched via desktop_id) rather than creating a duplicate customer
        var backupPath = CreateSampleDesktopBackupFile();
        var tenantName = "Test Update Tenant " + Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();

        // Initial import
        var summary1 = await DesktopMigrationService.RunImportAsync(backupPath, tenantName, db);
        summary1.InsertedCustomers.Should().Be(3);
        summary1.InsertedSales.Should().Be(2);
        var tenantId = summary1.TenantId;

        // Verify customer 1 initial phone
        var initialCustomer1 = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.TenantId == tenantId && c.DesktopId == "1");
        initialCustomer1.Phone.Should().Be("9876543210");

        // Read and update the backup JSON:
        // 1. Change Customer 1 phone from "9876543210" to "9876543299"
        // 2. Add Sale 3 ("INV-1003")
        var json = File.ReadAllText(backupPath);
        var backup = JsonSerializer.Deserialize<DesktopBackup>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        backup.Customers.First(c => c.Id == "1").Phone = "9876543299";

        backup.Sales.Add(new DesktopSale
        {
            Id = "3",
            InvoiceNumber = "INV-1003",
            SaleDate = "2026-02-03",
            CustomerId = "1",
            CustomerName = "Alice Sharma",
            TotalTaxable = 1000m,
            TotalCgst = 90m,
            TotalSgst = 90m,
            GrandTotal = 1180m,
            AmountPaid = 1180m,
            Status = "Completed",
            PaymentMode = "Card"
        });

        var updatedBackupPath = Path.Combine(_tempDir, "updated-backup.json");
        File.WriteAllText(updatedBackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

        // Run second import on updated backup
        var summary2 = await DesktopMigrationService.RunImportAsync(updatedBackupPath, tenantName, db);

        // ONLY the new sale was inserted
        summary2.InsertedSales.Should().Be(1);
        summary2.InsertedCustomers.Should().Be(0);
        summary2.UpdatedCustomers.Should().Be(1);

        // Total sales in DB is now 3
        var totalSales = await db.Sales.IgnoreQueryFilters().CountAsync(s => s.TenantId == tenantId);
        totalSales.Should().Be(3);

        // Total customers is STILL 3 (no duplicate was created!)
        var totalCustomers = await db.Customers.IgnoreQueryFilters().CountAsync(c => c.TenantId == tenantId);
        totalCustomers.Should().Be(3);

        // Customer 1's phone was updated
        var updatedCustomer1 = await db.Customers.IgnoreQueryFilters().FirstAsync(c => c.TenantId == tenantId && c.DesktopId == "1");
        updatedCustomer1.Phone.Should().Be("9876543299");
    }

    [Fact]
    public async Task Import_PhoneCollisionEdgeCase_LogsConflictAndSkipsWithoutAbortingBatch()
    {
        // (d) a contrived phone-collision edge case is logged to migration-conflicts.json
        // and skipped without aborting the rest of the batch
        var backupPath = CreateSampleDesktopBackupFile();
        var tenantName = "Test Conflict Tenant " + Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();

        // Initial import (Customer 1 has phone "9876543210", Customer 2 has phone "9876543211")
        var summary1 = await DesktopMigrationService.RunImportAsync(backupPath, tenantName, db);
        var tenantId = summary1.TenantId;

        // In the updated backup:
        // Customer 1's phone is modified to "9876543211" (which already belongs to Customer 2!)
        // Plus a new sale 4 ("INV-1004") is added to prove the batch didn't abort
        var json = File.ReadAllText(backupPath);
        var backup = JsonSerializer.Deserialize<DesktopBackup>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;

        backup.Customers.First(c => c.Id == "1").Phone = "9876543211"; // Collides with Customer 2

        backup.Sales.Add(new DesktopSale
        {
            Id = "4",
            InvoiceNumber = "INV-1004",
            SaleDate = "2026-02-04",
            CustomerName = "Bob Patel",
            TotalTaxable = 500m,
            TotalCgst = 45m,
            TotalSgst = 45m,
            GrandTotal = 590m,
            AmountPaid = 590m,
            Status = "Completed",
            PaymentMode = "Cash"
        });

        var conflictBackupPath = Path.Combine(_tempDir, "conflict-backup.json");
        File.WriteAllText(conflictBackupPath, JsonSerializer.Serialize(backup, new JsonSerializerOptions { WriteIndented = true }));

        var summary2 = await DesktopMigrationService.RunImportAsync(conflictBackupPath, tenantName, db);

        // 1 conflict was detected
        summary2.ConflictCount.Should().Be(1);

        // Batch did NOT abort: the new sale was imported!
        summary2.InsertedSales.Should().Be(1);

        // migration-conflicts.json was created
        var conflictFile = Path.Combine(_tempDir, "migration-conflicts.json");
        File.Exists(conflictFile).Should().BeTrue();

        var conflictJson = File.ReadAllText(conflictFile);
        conflictJson.Should().Contain("9876543211");
        conflictJson.Should().Contain("collides with existing customer");
    }

    [Fact]
    public async Task MergeCustomers_RePointsSalesAndLedgerEntriesAndDeletesDuplicate()
    {
        // (6) merge correctly re-points customer_ledger_entries/sales.customer_id
        // from duplicate onto surviving customer and deletes duplicate
        await using var db = _fixture.CreateDbContext();

        var tenant = Tenant.Create("Merge Test Tenant " + Guid.NewGuid());
        await db.Tenants.AddAsync(tenant);
        var branch = Branch.Create(tenant.Id, "B01", "Main");
        await db.Branches.AddAsync(branch);

        var survivingCustomer = Customer.Create(tenant.Id, "Alice Surviving", "9900112233", null);
        var duplicateCustomer = Customer.Create(tenant.Id, "Alice Duplicate", "9900112233", null);
        await db.Customers.AddRangeAsync(survivingCustomer, duplicateCustomer);

        // Create sale associated with duplicate customer
        var sale = Sale.CreateTaxInvoice(
            tenant.Id, branch.Id, "INV-MERGE-01", DateTimeOffset.UtcNow,
            duplicateCustomer.Id, duplicateCustomer.Name, null,
            1000m, 90m, 90m, 1180m, 0m, 1180m, "Cash",
            null, null, null, null, null, null, null, Guid.Empty);
        await db.Sales.AddAsync(sale);

        // Create ledger entry associated with duplicate customer
        var ledger = CustomerLedgerEntry.CreateDebit(
            tenant.Id, duplicateCustomer.Id, sale.Id, "Invoice", 1180m, branch.Id,
            DateOnly.FromDateTime(DateTime.UtcNow), "INV-MERGE-01");
        await db.CustomerLedgerEntries.AddAsync(ledger);

        await db.SaveChangesAsync();

        // Perform merge
        await DesktopMigrationService.MergeCustomersAsync(db, survivingCustomer.Id, duplicateCustomer.Id);

        // 1. Verify duplicate customer was deleted
        var dupExists = await db.Customers.IgnoreQueryFilters().AnyAsync(c => c.Id == duplicateCustomer.Id);
        dupExists.Should().BeFalse();

        // 2. Verify sale was re-pointed to surviving customer
        var rePointedSale = await db.Sales.IgnoreQueryFilters().FirstAsync(s => s.Id == sale.Id);
        rePointedSale.CustomerId.Should().Be(survivingCustomer.Id);

        // 3. Verify ledger entry was re-pointed to surviving customer
        var rePointedLedger = await db.CustomerLedgerEntries.IgnoreQueryFilters().FirstAsync(e => e.Id == ledger.Id);
        rePointedLedger.CustomerId.Should().Be(survivingCustomer.Id);
    }

    [Fact]
    public async Task Cli_ImportAndReconcileCommands_ExecuteCleanly()
    {
        var backupPath = CreateSampleDesktopBackupFile();
        var tenantName = "Test CLI Tenant " + Guid.NewGuid();

        await using var db = _fixture.CreateDbContext();

        // 1. Committing import via CLI
        var exitCode = await Program.HandleImportAsync(new[]
        {
            "--backup", backupPath,
            "--tenant-name", tenantName,
            "--connection", _fixture.ConnectionString
        }, db);
        exitCode.Should().Be(0);

        var tenant = await db.Tenants.IgnoreQueryFilters().FirstAsync(t => t.Name == tenantName);

        // 2. Reconcile customers dry-run via CLI
        var reconcileExitCode = await Program.HandleReconcileCustomersAsync(new[]
        {
            "--tenant", tenant.Id.ToString(),
            "--dry-run",
            "--connection", _fixture.ConnectionString
        }, db);
        reconcileExitCode.Should().Be(0);
    }
}
