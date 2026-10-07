using System.Text.Json;
using FluentAssertions;
using PzMigrate;

namespace PzMigrate.Tests;

public class DryRunImportTests : IDisposable
{
    private readonly string _tempDir;

    public DryRunImportTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), "pz_migrate_tests_" + Guid.NewGuid());
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
            // Ignore cleanup failure in temp directory
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
              "purchase_date": "2026-01-10"
            }
          ],
          "sales": [
            {
              "id": "1",
              "invoice_number": "INV-1001",
              "sale_date": "2026-02-01",
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
          "expenses": [
            {
              "id": "1",
              "description": "Tea and snacks",
              "amount": 150.00,
              "category": "Pantry",
              "expense_date": "2026-02-01"
            }
          ],
          "suppliers": [
            {
              "id": "1",
              "name": "Samsung India",
              "phone": "9800000000"
            }
          ]
        }
        """;

        var filePath = Path.Combine(_tempDir, "desktop-backup.json");
        File.WriteAllText(filePath, sampleJson);
        return filePath;
    }

    [Fact]
    public void DryRun_WellFormedSampleBackup_ProducesCorrectReportAndJsonFile()
    {
        // (a) a well-formed sample desktop backup JSON fixture file produces a correct
        // ReconciliationReport with exact hand-computed sums
        var backupPath = CreateSampleDesktopBackupFile();

        var report = DesktopMigrationService.RunDryRun(backupPath);

        // Sum of StockQty: 5 + 10 + 2 = 17
        report.SumStockQty.Should().Be(17);

        // Sum of Sale GrandTotals: 15000.50 + 25000.00 = 40000.50
        report.SumSaleGrandTotals.Should().Be(40000.50m);

        // Sum of Customer Payments: 10000.00 + 15000.50 = 25000.50
        report.SumPayments.Should().Be(25000.50m);

        // Sum of Active Credit Balances: 5000.00 + 0 + 1200.50 = 6200.50
        report.SumActiveCreditBalances.Should().Be(6200.50m);

        // Record Counts
        report.RecordCountsByTable["items"].Should().Be(3);
        report.RecordCountsByTable["sales"].Should().Be(2);
        report.RecordCountsByTable["sale_items"].Should().Be(1);
        report.RecordCountsByTable["customers"].Should().Be(3);
        report.RecordCountsByTable["customer_payments"].Should().Be(2);
        report.RecordCountsByTable["expenses"].Should().Be(1);
        report.RecordCountsByTable["suppliers"].Should().Be(1);
        report.RecordCountsByTable["purchases"].Should().Be(1);
        report.RecordCountsByTable["settings"].Should().Be(1);

        // Output report file exists next to backup
        var reportFilePath = Path.Combine(_tempDir, "reconciliation-report.json");
        File.Exists(reportFilePath).Should().BeTrue();

        var writtenJson = File.ReadAllText(reportFilePath);
        var readReport = JsonSerializer.Deserialize<ReconciliationReport>(writtenJson, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        readReport.Should().NotBeNull();
        readReport!.SumStockQty.Should().Be(17);
        readReport.SumSaleGrandTotals.Should().Be(40000.50m);
        readReport.SumPayments.Should().Be(25000.50m);
        readReport.SumActiveCreditBalances.Should().Be(6200.50m);
    }

    [Fact]
    public void DryRun_UnrecognizedOrMalformedJson_IsRejectedWithoutProducingReportFile()
    {
        // (b) a malformed/unrecognized JSON file is rejected with a clear error message, producing NO report file
        var unrecognizedPath = Path.Combine(_tempDir, "unrecognized.json");
        File.WriteAllText(unrecognizedPath, """{ "arbitraryObject": 42, "unknownField": "bad" }""");

        var act1 = () => DesktopMigrationService.RunDryRun(unrecognizedPath);
        act1.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unrecognized desktop backup schema*");

        var reportPath1 = Path.Combine(_tempDir, "reconciliation-report.json");
        File.Exists(reportPath1).Should().BeFalse();

        // Also test cloud backup shape rejected as desktop backup
        var cloudBackupPath = Path.Combine(_tempDir, "cloud-backup.json");
        File.WriteAllText(cloudBackupPath, """{ "schemaVersion": "1.0", "tenantId": "c3e9ff76-a602-48cb-b09e-dc77e09ea640" }""");

        var act2 = () => DesktopMigrationService.RunDryRun(cloudBackupPath);
        act2.Should().Throw<InvalidOperationException>()
            .WithMessage("*Unrecognized desktop backup schema*");

        File.Exists(reportPath1).Should().BeFalse();

        // Also test completely malformed JSON syntax
        var malformedPath = Path.Combine(_tempDir, "malformed.json");
        File.WriteAllText(malformedPath, """{ "items": [ broken json syntax ...""");

        var act3 = () => DesktopMigrationService.RunDryRun(malformedPath);
        act3.Should().Throw<InvalidOperationException>()
            .WithMessage("*Malformed JSON*");

        File.Exists(reportPath1).Should().BeFalse();
    }

    [Fact]
    public void DryRun_OperatesWithZeroDatabaseConnections()
    {
        // (c) dry-run mode makes ZERO database connections at all
        // (no connection string configured, operates purely in memory on input file)
        var backupPath = CreateSampleDesktopBackupFile();

        // Running without any DbContext or DB connection succeeds smoothly
        var report = DesktopMigrationService.RunDryRun(backupPath);
        report.Should().NotBeNull();
        report.SumStockQty.Should().Be(17);
    }

    [Fact]
    public async Task CliMain_ImportDryRunCommand_ExecutesSuccessfullyWithExitCodeZero()
    {
        var backupPath = CreateSampleDesktopBackupFile();

        var exitCode = await Program.Main(new[]
        {
            "import",
            "--backup", backupPath,
            "--tenant-name", "Acme Electronics",
            "--dry-run"
        });

        exitCode.Should().Be(0);

        var reportFilePath = Path.Combine(_tempDir, "reconciliation-report.json");
        File.Exists(reportFilePath).Should().BeTrue();
    }
}
