using System.Text.Json;

namespace PzMigrate;

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
}
